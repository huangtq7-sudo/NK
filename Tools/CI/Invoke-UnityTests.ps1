[CmdletBinding()]
param(
    [string]$UnityEditorPath,
    [ValidateSet("EditMode", "PlayMode", "All")]
    [string]$TestPlatform = "All",
    [string]$ProjectPath,
    [string]$ResultsDirectory,
    [ValidateRange(1, 60)]
    [int]$TimeoutMinutes = 20,
    [ValidateRange(5, 300)]
    [int]$ExitGraceSeconds = 30
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
if ([string]::IsNullOrWhiteSpace($ProjectPath)) {
    $ProjectPath = Join-Path $repositoryRoot "NK"
}

if ([string]::IsNullOrWhiteSpace($ResultsDirectory)) {
    $ResultsDirectory = Join-Path $repositoryRoot "artifacts\ci\unity"
}

if ([string]::IsNullOrWhiteSpace($UnityEditorPath)) {
    if (-not [string]::IsNullOrWhiteSpace($env:UNITY_EDITOR_PATH)) {
        $UnityEditorPath = $env:UNITY_EDITOR_PATH
    }
    else {
        $localEditor = "E:\2021.3.45f2c1\Editor\Unity.exe"
        if (Test-Path -LiteralPath $localEditor -PathType Leaf) {
            $UnityEditorPath = $localEditor
        }
    }
}

if ([string]::IsNullOrWhiteSpace($UnityEditorPath) -or
    -not (Test-Path -LiteralPath $UnityEditorPath -PathType Leaf)) {
    throw "Unity.exe was not found. Set UNITY_EDITOR_PATH or pass -UnityEditorPath."
}

$projectVersionPath = Join-Path $ProjectPath "ProjectSettings\ProjectVersion.txt"
if (-not (Test-Path -LiteralPath $projectVersionPath -PathType Leaf)) {
    throw "Unity ProjectVersion.txt was not found under $ProjectPath."
}

$versionMatch = [regex]::Match(
    (Get-Content -Raw -Encoding UTF8 -LiteralPath $projectVersionPath),
    '(?m)^m_EditorVersion:\s*(?<version>\S+)\s*$'
)
if (-not $versionMatch.Success) {
    throw "Unable to read the required Unity version from $projectVersionPath."
}

$requiredVersion = $versionMatch.Groups["version"].Value
New-Item -ItemType Directory -Force -Path $ResultsDirectory | Out-Null

$versionStandardOutput = Join-Path $ResultsDirectory "editor-version.stdout.txt"
$versionStandardError = Join-Path $ResultsDirectory "editor-version.stderr.txt"
$versionProcess = Start-Process -FilePath $UnityEditorPath `
    -ArgumentList "-version" `
    -Wait `
    -PassThru `
    -RedirectStandardOutput $versionStandardOutput `
    -RedirectStandardError $versionStandardError
$actualVersion = @(
    Get-Content -Raw -Encoding UTF8 -LiteralPath $versionStandardOutput
    Get-Content -Raw -Encoding UTF8 -LiteralPath $versionStandardError
) -join "`n"
$actualVersion = $actualVersion.Trim()
if ($versionProcess.ExitCode -ne 0) {
    throw "Unity -version failed with exit code $($versionProcess.ExitCode)."
}

if ($actualVersion -notmatch [regex]::Escape($requiredVersion)) {
    throw "The project requires Unity $requiredVersion, but the selected editor reported '$actualVersion'."
}

$platforms = if ($TestPlatform -eq "All") {
    @("EditMode", "PlayMode")
}
else {
    @($TestPlatform)
}

# Baseline CI never opts into the real Host/MySQL smoke test.
$env:NARAKA_RUN_LEGACY_CLIENT_SMOKE = $null

$warmupLogFile = Join-Path $ResultsDirectory "warmup.log"
if (Test-Path -LiteralPath $warmupLogFile -PathType Leaf) {
    Remove-Item -Force -LiteralPath $warmupLogFile
}

Write-Host "Importing and compiling the Unity project before test discovery."
$warmupArguments = @(
    "-batchmode",
    "-nographics",
    "-quit",
    "-projectPath", "`"$ProjectPath`"",
    "-logFile", "`"$warmupLogFile`""
)
$warmupProcess = Start-Process -FilePath $UnityEditorPath `
    -ArgumentList $warmupArguments `
    -PassThru
$warmupDeadline = [DateTime]::UtcNow.AddMinutes($TimeoutMinutes)

while (-not $warmupProcess.HasExited) {
    if ([DateTime]::UtcNow -ge $warmupDeadline) {
        Stop-Process -Id $warmupProcess.Id -Force
        $warmupProcess.WaitForExit()
        throw "Unity project import exceeded the $TimeoutMinutes minute timeout. See $warmupLogFile."
    }

    Start-Sleep -Seconds 1
    $warmupProcess.Refresh()
}

if ($warmupProcess.ExitCode -ne 0) {
    throw "Unity project import failed with exit code $($warmupProcess.ExitCode). See $warmupLogFile."
}

if (-not (Test-Path -LiteralPath $warmupLogFile -PathType Leaf)) {
    throw "Unity project import did not produce $warmupLogFile."
}

$warmupLog = Get-Content -Raw -Encoding UTF8 -LiteralPath $warmupLogFile
$compilationErrorPatterns = @(
    '(?im)^[^\r\n]*\(\d+,\d+\):\s+error\s+CS\d+\b',
    '(?im)^\s*error\s+CS\d+\b',
    '(?im)^\s*Scripts have compiler errors\.?\s*$',
    '(?im)^\s*Compilation failed\b'
)
foreach ($compilationErrorPattern in $compilationErrorPatterns) {
    if ($warmupLog -match $compilationErrorPattern) {
        throw "Unity project import reported C# compilation errors. See $warmupLogFile."
    }
}

Write-Host "Unity project import passed: exit=0, no C# compilation errors found."

foreach ($platform in $platforms) {
    $filePrefix = $platform.ToLowerInvariant()
    $resultFile = Join-Path $ResultsDirectory "$filePrefix-results.xml"
    $logFile = Join-Path $ResultsDirectory "$filePrefix.log"

    if (Test-Path -LiteralPath $resultFile -PathType Leaf) {
        Remove-Item -Force -LiteralPath $resultFile
    }
    if (Test-Path -LiteralPath $logFile -PathType Leaf) {
        Remove-Item -Force -LiteralPath $logFile
    }

    Write-Host "Running Unity $platform tests with $requiredVersion."
    $unityArguments = @(
        "-batchmode",
        "-nographics",
        "-projectPath", "`"$ProjectPath`"",
        "-runTests",
        "-testPlatform", $platform,
        "-testResults", "`"$resultFile`"",
        "-logFile", "`"$logFile`""
    )
    $unityProcess = Start-Process -FilePath $UnityEditorPath `
        -ArgumentList $unityArguments `
        -PassThru
    $deadline = [DateTime]::UtcNow.AddMinutes($TimeoutMinutes)
    $resultCompletedAt = $null
    $terminatedAfterResults = $false

    while (-not $unityProcess.HasExited) {
        if ($null -eq $resultCompletedAt -and
            (Test-Path -LiteralPath $resultFile -PathType Leaf)) {
            try {
                [xml]$candidateResult = Get-Content -Raw -Encoding UTF8 -LiteralPath $resultFile
                if ($null -ne $candidateResult.'test-run' -and
                    -not [string]::IsNullOrWhiteSpace($candidateResult.'test-run'.'end-time')) {
                    $resultCompletedAt = [DateTime]::UtcNow
                }
            }
            catch {
                # The test runner may still be flushing the XML file.
            }
        }

        if ($null -ne $resultCompletedAt -and
            ([DateTime]::UtcNow - $resultCompletedAt).TotalSeconds -ge $ExitGraceSeconds) {
            Write-Warning "Unity wrote complete $platform results but did not exit within $ExitGraceSeconds seconds; stopping process $($unityProcess.Id)."
            Stop-Process -Id $unityProcess.Id -Force
            $unityProcess.WaitForExit()
            $terminatedAfterResults = $true
            break
        }

        if ([DateTime]::UtcNow -ge $deadline) {
            Stop-Process -Id $unityProcess.Id -Force
            $unityProcess.WaitForExit()
            throw "Unity $platform exceeded the $TimeoutMinutes minute timeout. See $logFile."
        }

        Start-Sleep -Seconds 1
        $unityProcess.Refresh()
    }

    $unityExitCode = if ($terminatedAfterResults) { 0 } else { $unityProcess.ExitCode }

    if (-not (Test-Path -LiteralPath $resultFile -PathType Leaf)) {
        throw "Unity $platform did not produce $resultFile. See $logFile."
    }

    [xml]$testRun = Get-Content -Raw -Encoding UTF8 -LiteralPath $resultFile
    $root = $testRun.'test-run'
    $total = [int]$root.total
    $passed = [int]$root.passed
    $failed = [int]$root.failed
    $inconclusive = [int]$root.inconclusive
    $skipped = [int]$root.skipped

    if ($unityExitCode -ne 0 -or $total -eq 0 -or $failed -ne 0 -or $inconclusive -ne 0) {
        throw "Unity $platform failed: exit=$unityExitCode, total=$total, passed=$passed, failed=$failed, inconclusive=$inconclusive, skipped=$skipped. See $logFile."
    }

    Write-Host "Unity $platform passed: total=$total, passed=$passed, failed=$failed, inconclusive=$inconclusive, skipped=$skipped."
}
