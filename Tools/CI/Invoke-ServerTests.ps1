[CmdletBinding()]
param(
    [string]$DotNetPath,
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$ResultsDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$solutionPath = Join-Path $repositoryRoot "Server\Naraka.Server.slnx"
$nugetConfigPath = Join-Path $repositoryRoot "Server\NuGet.Config"
$globalJsonPath = Join-Path $repositoryRoot "global.json"

if ([string]::IsNullOrWhiteSpace($ResultsDirectory)) {
    $ResultsDirectory = Join-Path $repositoryRoot "artifacts\ci\server"
}

$dotnetCliHome = Join-Path $repositoryRoot "artifacts\ci\dotnet-home"
New-Item -ItemType Directory -Force -Path $dotnetCliHome | Out-Null
$env:DOTNET_CLI_HOME = $dotnetCliHome
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = "0"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:DOTNET_NOLOGO = "1"
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"

if ([string]::IsNullOrWhiteSpace($DotNetPath)) {
    if (-not [string]::IsNullOrWhiteSpace($env:DOTNET_EXE)) {
        $DotNetPath = $env:DOTNET_EXE
    }
    else {
        $dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
        if ($null -ne $dotnetCommand) {
            $DotNetPath = $dotnetCommand.Source
        }
        else {
            $repositoryDotNet = Join-Path $repositoryRoot ".tools\dotnet\dotnet.exe"
            if (Test-Path -LiteralPath $repositoryDotNet -PathType Leaf) {
                $DotNetPath = $repositoryDotNet
            }
        }
    }
}

if ([string]::IsNullOrWhiteSpace($DotNetPath) -or
    -not (Test-Path -LiteralPath $DotNetPath -PathType Leaf)) {
    throw "A dotnet executable was not found. Install the SDK from global.json or pass -DotNetPath."
}

$expectedSdkVersion = (Get-Content -Raw -Encoding UTF8 -LiteralPath $globalJsonPath |
    ConvertFrom-Json).sdk.version
$actualSdkVersion = (& $DotNetPath --version).Trim()
if ($LASTEXITCODE -ne 0) {
    throw "dotnet --version failed with exit code $LASTEXITCODE."
}

if ($actualSdkVersion -ne $expectedSdkVersion) {
    throw "The repository requires .NET SDK $expectedSdkVersion, but $actualSdkVersion was selected."
}

New-Item -ItemType Directory -Force -Path $ResultsDirectory | Out-Null

function Invoke-DotNet {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    & $DotNetPath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments[0]) failed with exit code $LASTEXITCODE."
    }
}

Write-Host "Restoring the server solution with NuGet vulnerability auditing enabled."
Invoke-DotNet -Arguments @(
    "restore",
    $solutionPath,
    "--configfile", $nugetConfigPath,
    "--nologo",
    "-p:NuGetAudit=true",
    "-p:NuGetAuditMode=all",
    "-p:WarningsAsErrors=NU1901%3BNU1902%3BNU1903%3BNU1904"
)

Write-Host "Building the server solution in $Configuration."
Invoke-DotNet -Arguments @(
    "build",
    $solutionPath,
    "--configuration", $Configuration,
    "--no-restore",
    "--nologo"
)

$testProjects = @(
    "Server\tests\Naraka.Server.ArchitectureTests\Naraka.Server.ArchitectureTests.csproj",
    "Server\tests\Naraka.Server.Application.Tests\Naraka.Server.Application.Tests.csproj",
    "Server\tests\Naraka.Server.LegacyNetworkV1.Tests\Naraka.Server.LegacyNetworkV1.Tests.csproj",
    "Server\tests\Naraka.Server.Infrastructure.Tests\Naraka.Server.Infrastructure.Tests.csproj"
)

$resultFiles = @()
foreach ($testProject in $testProjects) {
    $projectPath = Join-Path $repositoryRoot $testProject
    $projectName = [System.IO.Path]::GetFileNameWithoutExtension($projectPath)
    $resultFile = Join-Path $ResultsDirectory "$projectName.trx"
    $resultFiles += $resultFile

    Write-Host "Running $projectName."
    Invoke-DotNet -Arguments @(
        "test",
        $projectPath,
        "--configuration", $Configuration,
        "--no-build",
        "--no-restore",
        "--nologo",
        "--logger", "trx;LogFileName=$([System.IO.Path]::GetFileName($resultFile))",
        "--results-directory", $ResultsDirectory,
        "--blame-hang-timeout", "5m"
    )
}

$total = 0
$executed = 0
$passed = 0
$failed = 0
foreach ($resultFile in $resultFiles) {
    if (-not (Test-Path -LiteralPath $resultFile -PathType Leaf)) {
        throw "Expected test result was not produced: $resultFile"
    }

    [xml]$testRun = Get-Content -Raw -Encoding UTF8 -LiteralPath $resultFile
    $counters = $testRun.TestRun.ResultSummary.Counters
    $total += [int]$counters.total
    $executed += [int]$counters.executed
    $passed += [int]$counters.passed
    $failed += [int]$counters.failed
}

if ($total -eq 0 -or $executed -eq 0 -or $failed -ne 0) {
    throw "Server test summary is invalid: total=$total, executed=$executed, passed=$passed, failed=$failed."
}

Write-Host "Server CI passed: total=$total, executed=$executed, passed=$passed, failed=$failed."
