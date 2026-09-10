[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$')]
    [string]$ReleaseId,

    [string]$DotNetPath,
    [string]$UnityEditorPath,
    [switch]$AllowDirtyCandidate,
    [switch]$SkipUnityTestsCandidate
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$serverCiScript = Join-Path $repositoryRoot 'Tools\CI\Invoke-ServerTests.ps1'
$unityCiScript = Join-Path $repositoryRoot 'Tools\CI\Invoke-UnityTests.ps1'
$nugetConfig = Join-Path $repositoryRoot 'Server\NuGet.Config'
$hostProject = Join-Path $repositoryRoot 'Server\src\Naraka.Server.Host\Naraka.Server.Host.csproj'
$migratorProject = Join-Path $repositoryRoot 'Server\tools\Naraka.Server.DatabaseMigrator\Naraka.Server.DatabaseMigrator.csproj'
$appSettingsPath = Join-Path $repositoryRoot 'Server\src\Naraka.Server.Host\appsettings.json'
$gameConfigManifestPath = Join-Path $repositoryRoot 'Shared\Generated\Config\manifest.json'
$releaseRoot = Join-Path $repositoryRoot "artifacts\releases\$ReleaseId"

if (Test-Path -LiteralPath $releaseRoot) {
    throw "Release output already exists: $releaseRoot"
}

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
            $DotNetPath = Join-Path $repositoryRoot '.tools\dotnet\dotnet.exe'
        }
    }
}

if (-not (Test-Path -LiteralPath $DotNetPath -PathType Leaf)) {
    throw 'The required .NET SDK executable was not found.'
}

$gitStatus = @(& git -C $repositoryRoot status --porcelain=v1 --untracked-files=normal)
if ($LASTEXITCODE -ne 0) {
    throw 'Unable to read Git status.'
}

$gitDirty = $gitStatus.Count -gt 0
if ($gitDirty -and -not $AllowDirtyCandidate) {
    throw 'A deployable release requires a clean Git worktree. Use -AllowDirtyCandidate only for a non-deployable local candidate.'
}

$gitCommit = (& git -C $repositoryRoot rev-parse HEAD).Trim()
$gitTitle = (& git -C $repositoryRoot log -1 --pretty=format:%s).Trim()
if ($LASTEXITCODE -ne 0) {
    throw 'Unable to read the Git revision.'
}

$serverResults = Join-Path $releaseRoot 'test-results\server'
$unityResults = Join-Path $releaseRoot 'test-results\unity'
$stagingRoot = Join-Path $releaseRoot '_staging'
$hostOutput = Join-Path $stagingRoot 'host'
$migratorOutput = Join-Path $stagingRoot 'migrator'

New-Item -ItemType Directory -Path $hostOutput,$migratorOutput -Force | Out-Null

& $serverCiScript `
    -DotNetPath $DotNetPath `
    -Configuration Release `
    -ResultsDirectory $serverResults
if ($LASTEXITCODE -ne 0) {
    throw "Server CI failed with exit code $LASTEXITCODE."
}

$unityTestsRun = -not $SkipUnityTestsCandidate
if ($unityTestsRun) {
    & $unityCiScript `
        -UnityEditorPath $UnityEditorPath `
        -ProjectPath (Join-Path $repositoryRoot 'NK') `
        -ResultsDirectory $unityResults `
        -TestPlatform All
    if ($LASTEXITCODE -ne 0) {
        throw "Unity CI failed with exit code $LASTEXITCODE."
    }
}

$dotnetCliHome = Join-Path $stagingRoot 'dotnet-home'
New-Item -ItemType Directory -Path $dotnetCliHome -Force | Out-Null
$env:DOTNET_CLI_HOME = $dotnetCliHome
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = '0'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'

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

foreach ($project in @($hostProject,$migratorProject)) {
    Invoke-DotNet -Arguments @(
        'restore',
        $project,
        '--runtime', 'win-x64',
        '--configfile', $nugetConfig,
        '--nologo',
        '-p:NuGetAudit=true',
        '-p:NuGetAuditMode=all',
        '-p:WarningsAsErrors=NU1901%3BNU1902%3BNU1903%3BNU1904'
    )
}

Invoke-DotNet -Arguments @(
    'publish',
    $hostProject,
    '--configuration', 'Release',
    '--runtime', 'win-x64',
    '--self-contained', 'true',
    '--no-restore',
    '--output', $hostOutput,
    '--nologo',
    '-p:PublishSingleFile=false'
)

Invoke-DotNet -Arguments @(
    'publish',
    $migratorProject,
    '--configuration', 'Release',
    '--runtime', 'win-x64',
    '--self-contained', 'true',
    '--no-restore',
    '--output', $migratorOutput,
    '--nologo',
    '-p:PublishSingleFile=false'
)

$hostExe = Join-Path $hostOutput 'Naraka.Server.Host.exe'
$migratorExe = Join-Path $migratorOutput 'Naraka.Server.DatabaseMigrator.exe'
if (-not (Test-Path -LiteralPath $hostExe -PathType Leaf)) {
    throw 'Published Host executable is missing.'
}
if (-not (Test-Path -LiteralPath $migratorExe -PathType Leaf)) {
    throw 'Published migrator executable is missing.'
}

Push-Location -LiteralPath $migratorOutput
try {
    & $migratorExe --dry-run
    if ($LASTEXITCODE -ne 0) {
        throw "Migration dry-run failed with exit code $LASTEXITCODE."
    }
}
finally {
    Pop-Location
}

$hostArchiveName = "Naraka.Server.Host-win-x64-$ReleaseId.zip"
$migratorArchiveName = "Naraka.Server.DatabaseMigrator-win-x64-$ReleaseId.zip"
$hostArchive = Join-Path $releaseRoot $hostArchiveName
$migratorArchive = Join-Path $releaseRoot $migratorArchiveName

Compress-Archive -Path (Join-Path $hostOutput '*') -DestinationPath $hostArchive
Compress-Archive -Path (Join-Path $migratorOutput '*') -DestinationPath $migratorArchive

$hostHash = (Get-FileHash -LiteralPath $hostArchive -Algorithm SHA256).Hash
$migratorHash = (Get-FileHash -LiteralPath $migratorArchive -Algorithm SHA256).Hash
$appSettings = Get-Content -Raw -Encoding UTF8 -LiteralPath $appSettingsPath | ConvertFrom-Json
$gameConfigManifest = Get-Content -Raw -Encoding UTF8 -LiteralPath $gameConfigManifestPath | ConvertFrom-Json
$migrationFiles = @(Get-ChildItem -LiteralPath (Join-Path $migratorOutput 'Migrations') -Filter '*.sql' | Sort-Object Name)
$migrationManifest = @(
    foreach ($migrationFile in $migrationFiles) {
        [ordered]@{
            FileName = $migrationFile.Name
            SHA256 = (Get-FileHash -LiteralPath $migrationFile.FullName -Algorithm SHA256).Hash
        }
    }
)

$deployable = -not $gitDirty -and $unityTestsRun
$manifest = [ordered]@{
    SchemaVersion = 1
    ReleaseId = $ReleaseId
    CreatedAtUtc = [DateTime]::UtcNow.ToString('o')
    GitCommit = $gitCommit
    GitCommitTitle = $gitTitle
    GitDirty = $gitDirty
    Deployable = $deployable
    DotNetSdk = (& $DotNetPath --version).Trim()
    Runtime = 'win-x64'
    SelfContained = $true
    ServerTestsRun = $true
    UnityTestsRun = $unityTestsRun
    Compatibility = [ordered]@{
        ConfigVersion = $appSettings.Naraka.Bootstrap.ConfigVersion
        MinimumClientVersion = $appSettings.Naraka.Bootstrap.MinimumClientVersion
        MaximumClientVersion = $appSettings.Naraka.Bootstrap.MaximumClientVersion
        ProtocolVersion = $appSettings.Naraka.Bootstrap.ProtocolVersion
    }
    GameConfig = [ordered]@{
        ConfigVersion = $gameConfigManifest.ConfigVersion
        SchemaVersion = $gameConfigManifest.SchemaVersion
        CatalogSHA256 = $gameConfigManifest.CatalogSha256
    }
    ExpectedServerCapabilityCount = 12
    HostPackage = [ordered]@{
        FileName = $hostArchiveName
        Length = (Get-Item -LiteralPath $hostArchive).Length
        SHA256 = $hostHash
    }
    MigratorPackage = [ordered]@{
        FileName = $migratorArchiveName
        Length = (Get-Item -LiteralPath $migratorArchive).Length
        SHA256 = $migratorHash
    }
    Migrations = $migrationManifest
}

$manifestPath = Join-Path $releaseRoot 'release-manifest.json'
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPath -Encoding UTF8

Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Cloud\Deploy-NarakaServerRelease.ps1') -Destination $releaseRoot
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Cloud\Start-NarakaServerSupervised.ps1') -Destination $releaseRoot

$resolvedStaging = (Resolve-Path -LiteralPath $stagingRoot).Path
$resolvedRelease = (Resolve-Path -LiteralPath $releaseRoot).Path
if (-not $resolvedStaging.StartsWith($resolvedRelease, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'Refusing to remove a staging directory outside the release output.'
}
Remove-Item -LiteralPath $resolvedStaging -Recurse -Force

[PSCustomObject]@{
    ReleaseDirectory = $releaseRoot
    ReleaseId = $ReleaseId
    GitCommit = $gitCommit
    Deployable = $deployable
    HostSHA256 = $hostHash
    MigratorSHA256 = $migratorHash
    ConfigVersion = $appSettings.Naraka.Bootstrap.ConfigVersion
    ProtocolVersion = $appSettings.Naraka.Bootstrap.ProtocolVersion
} | Format-List
