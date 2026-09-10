[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ReleaseDirectory,
    [string]$ActiveHostDirectory = 'C:\NarakaDeploy\cloud-82c02c7\host',
    [string]$TaskName = 'NarakaServerHost',
    [ValidateRange(256,1024)]
    [int]$MinimumAvailableMemoryMiB = 400
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$allowedDeploymentRoot = [System.IO.Path]::GetFullPath('C:\NarakaDeploy\')
$resolvedActiveHost = [System.IO.Path]::GetFullPath($ActiveHostDirectory)
if (-not $resolvedActiveHost.StartsWith($allowedDeploymentRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Active Host directory is outside C:\NarakaDeploy: $resolvedActiveHost"
}

$resolvedReleaseDirectory = (Resolve-Path -LiteralPath $ReleaseDirectory).Path
$manifestPath = Join-Path $resolvedReleaseDirectory 'release-manifest.json'
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "Release manifest is missing: $manifestPath"
}

$manifest = Get-Content -Raw -Encoding UTF8 -LiteralPath $manifestPath | ConvertFrom-Json
if ($manifest.SchemaVersion -ne 1) {
    throw "Unsupported release manifest schema: $($manifest.SchemaVersion)"
}
if ($manifest.ReleaseId -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$') {
    throw 'ReleaseId contains unsupported characters.'
}
if (-not $manifest.Deployable -or $manifest.GitDirty) {
    throw 'Cloud deployment refuses a dirty or non-deployable candidate release.'
}

$hostArchive = Join-Path $resolvedReleaseDirectory $manifest.HostPackage.FileName
$migratorArchive = Join-Path $resolvedReleaseDirectory $manifest.MigratorPackage.FileName
foreach ($archivePath in @($hostArchive,$migratorArchive)) {
    if (-not (Test-Path -LiteralPath $archivePath -PathType Leaf)) {
        throw "Release package is missing: $archivePath"
    }
}

$actualHostHash = (Get-FileHash -LiteralPath $hostArchive -Algorithm SHA256).Hash
$actualMigratorHash = (Get-FileHash -LiteralPath $migratorArchive -Algorithm SHA256).Hash
if ($actualHostHash -ne $manifest.HostPackage.SHA256) {
    throw 'Host package SHA-256 does not match the release manifest.'
}
if ($actualMigratorHash -ne $manifest.MigratorPackage.SHA256) {
    throw 'Migrator package SHA-256 does not match the release manifest.'
}

$guiProcesses = @(Get-Process -Name 'heidisql','powershell_ise' -ErrorAction SilentlyContinue)
if ($guiProcesses.Count -gt 0) {
    throw 'Close HeidiSQL and PowerShell ISE before deployment.'
}

$mysqlService = Get-Service -Name 'NarakaMySQL57' -ErrorAction Stop
if ($mysqlService.Status -ne 'Running') {
    throw 'MySQL is not running.'
}

$memoryBefore = Get-CimInstance Win32_PerfFormattedData_PerfOS_Memory
if ($memoryBefore.AvailableMBytes -lt $MinimumAvailableMemoryMiB) {
    throw "Available memory is below $MinimumAvailableMemoryMiB MiB: $($memoryBefore.AvailableMBytes) MiB"
}

$releaseId = [string]$manifest.ReleaseId
$stagingRoot = [System.IO.Path]::GetFullPath("C:\NarakaDeploy\releases\$releaseId")
$backupRoot = [System.IO.Path]::GetFullPath('C:\NarakaDeploy\backups')
if (-not $stagingRoot.StartsWith($allowedDeploymentRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'Staging directory is outside the deployment root.'
}
if (-not $backupRoot.StartsWith($allowedDeploymentRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'Backup directory is outside the deployment root.'
}
if (Test-Path -LiteralPath $stagingRoot) {
    throw "Release staging directory already exists: $stagingRoot"
}

$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$backupHost = Join-Path $backupRoot "$timestamp-$releaseId-previous"
$failedHost = Join-Path $backupRoot "$timestamp-$releaseId-failed"
$stagedHost = Join-Path $stagingRoot 'host'
$stagedMigrator = Join-Path $stagingRoot 'migrator'

foreach ($deploymentPath in @($backupHost,$failedHost,$stagedHost,$stagedMigrator)) {
    $fullPath = [System.IO.Path]::GetFullPath($deploymentPath)
    if (-not $fullPath.StartsWith($allowedDeploymentRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Generated path is outside the deployment root: $fullPath"
    }
}

if (-not (Test-Path -LiteralPath $resolvedActiveHost -PathType Container)) {
    throw "Active Host directory is missing: $resolvedActiveHost"
}
if (Test-Path -LiteralPath $backupHost) {
    throw "Backup destination already exists: $backupHost"
}

$activeMoved = $false
$newMoved = $false

try {
    Stop-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2
    Get-Process -Name 'Naraka.Server.Host' -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Seconds 2

    $remainingHosts = @(Get-Process -Name 'Naraka.Server.Host' -ErrorAction SilentlyContinue)
    if ($remainingHosts.Count -ne 0) {
        throw 'The active Host process did not stop.'
    }

    New-Item -ItemType Directory -Path $stagedHost,$stagedMigrator,$backupRoot -Force | Out-Null
    Expand-Archive -LiteralPath $migratorArchive -DestinationPath $stagedMigrator
    [GC]::Collect()
    [GC]::WaitForPendingFinalizers()
    Expand-Archive -LiteralPath $hostArchive -DestinationPath $stagedHost
    [GC]::Collect()
    [GC]::WaitForPendingFinalizers()

    $hostExe = Join-Path $stagedHost 'Naraka.Server.Host.exe'
    $migratorExe = Join-Path $stagedMigrator 'Naraka.Server.DatabaseMigrator.exe'
    if (-not (Test-Path -LiteralPath $hostExe -PathType Leaf)) {
        throw 'The staged Host executable is missing.'
    }
    if (-not (Test-Path -LiteralPath $migratorExe -PathType Leaf)) {
        throw 'The staged migrator executable is missing.'
    }

    foreach ($migration in @($manifest.Migrations)) {
        $migrationPath = Join-Path (Join-Path $stagedMigrator 'Migrations') $migration.FileName
        if (-not (Test-Path -LiteralPath $migrationPath -PathType Leaf)) {
            throw "A manifest migration is missing: $($migration.FileName)"
        }
        $migrationHash = (Get-FileHash -LiteralPath $migrationPath -Algorithm SHA256).Hash
        if ($migrationHash -ne $migration.SHA256) {
            throw "Migration SHA-256 mismatch: $($migration.FileName)"
        }
    }

    $machineConnection = [Environment]::GetEnvironmentVariable(
        'NARAKA_MYSQL_CONNECTION_STRING',
        'Machine'
    )
    if ([string]::IsNullOrWhiteSpace($machineConnection)) {
        throw 'The machine database connection variable is missing.'
    }

    $env:NARAKA_MYSQL_CONNECTION_STRING = $machineConnection
    $machineConnection = $null
    Push-Location -LiteralPath $stagedMigrator
    try {
        & $migratorExe --dry-run
        if ($LASTEXITCODE -ne 0) {
            throw "Migration dry-run failed with exit code $LASTEXITCODE."
        }
        & $migratorExe
        if ($LASTEXITCODE -ne 0) {
            throw "Migration failed with exit code $LASTEXITCODE."
        }
        & $migratorExe
        if ($LASTEXITCODE -ne 0) {
            throw "Migration repeat verification failed with exit code $LASTEXITCODE."
        }
    }
    finally {
        Pop-Location
        $env:NARAKA_MYSQL_CONNECTION_STRING = $null
    }

    Move-Item -LiteralPath $resolvedActiveHost -Destination $backupHost
    $activeMoved = $true
    Move-Item -LiteralPath $stagedHost -Destination $resolvedActiveHost
    $newMoved = $true

    Start-ScheduledTask -TaskName $TaskName

    $live = $null
    $ready = $null
    for ($attempt = 1; $attempt -le 30; $attempt++) {
        Start-Sleep -Seconds 1
        try {
            $live = Invoke-RestMethod -Uri 'http://127.0.0.1:5222/health/live' -TimeoutSec 3 -ErrorAction Stop
            $ready = Invoke-RestMethod -Uri 'http://127.0.0.1:5222/health/ready' -TimeoutSec 3 -ErrorAction Stop
            if ($live.status -eq 'live' -and $ready.status -eq 'ready') {
                break
            }
        }
        catch {
            $live = $null
            $ready = $null
        }
    }

    if ($null -eq $live -or $null -eq $ready) {
        throw 'The new Host did not pass its health checks.'
    }

    $hostProcesses = @(Get-Process -Name 'Naraka.Server.Host' -ErrorAction SilentlyContinue)
    if ($hostProcesses.Count -ne 1) {
        throw "Unexpected Host process count: $($hostProcesses.Count)"
    }

    $listeners = @(
        Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue |
            Where-Object { $_.LocalPort -in 3306,5222,8011 }
    )
    $port3306 = @($listeners | Where-Object { $_.LocalPort -eq 3306 }).Count -eq 1
    $port5222 = @($listeners | Where-Object { $_.LocalPort -eq 5222 }).Count -eq 1
    $port8011 = @($listeners | Where-Object { $_.LocalPort -eq 8011 }).Count -eq 1
    if (-not $port3306 -or -not $port5222 -or -not $port8011) {
        throw 'One or more required loopback listeners are missing.'
    }

    $bootstrap = Invoke-RestMethod -Uri 'http://127.0.0.1:5222/bootstrap/config-version' -TimeoutSec 3
    if ($bootstrap.configVersion -ne $manifest.Compatibility.ConfigVersion) {
        throw 'The deployed ConfigVersion does not match the release manifest.'
    }
    if ($bootstrap.minimumClientVersion -ne $manifest.Compatibility.MinimumClientVersion) {
        throw 'The deployed MinimumClientVersion does not match the release manifest.'
    }
    if ($bootstrap.maximumClientVersion -ne $manifest.Compatibility.MaximumClientVersion) {
        throw 'The deployed MaximumClientVersion does not match the release manifest.'
    }
    if ($bootstrap.protocolVersion -ne $manifest.Compatibility.ProtocolVersion) {
        throw 'The deployed ProtocolVersion does not match the release manifest.'
    }
    $serverCapabilities = @($bootstrap.serverCapabilities)
    if ($serverCapabilities.Count -ne $manifest.ExpectedServerCapabilityCount) {
        throw "Unexpected server capability count: $($serverCapabilities.Count)"
    }

    $configVersion = Invoke-RestMethod -Uri 'http://127.0.0.1:5222/config/version' -TimeoutSec 3
    if ($configVersion.configVersion -ne $manifest.GameConfig.ConfigVersion) {
        throw 'The deployed generated ConfigVersion does not match the release manifest.'
    }
    if ($configVersion.schemaVersion -ne $manifest.GameConfig.SchemaVersion) {
        throw 'The deployed generated config schema does not match the release manifest.'
    }
    if ($configVersion.catalogSha256 -ne $manifest.GameConfig.CatalogSHA256) {
        throw 'The deployed generated config catalog hash does not match the release manifest.'
    }
}
catch {
    $deploymentFailure = $_
    Stop-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    Get-Process -Name 'Naraka.Server.Host' -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Seconds 2

    if ($newMoved -and (Test-Path -LiteralPath $resolvedActiveHost)) {
        Move-Item -LiteralPath $resolvedActiveHost -Destination $failedHost
    }
    if ($activeMoved -and (Test-Path -LiteralPath $backupHost)) {
        Move-Item -LiteralPath $backupHost -Destination $resolvedActiveHost
    }
    if (Test-Path -LiteralPath $resolvedActiveHost) {
        Start-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    }

    throw $deploymentFailure
}
finally {
    $env:NARAKA_MYSQL_CONNECTION_STRING = $null
}

$taskState = (Get-ScheduledTask -TaskName $TaskName).State
$memoryAfter = Get-CimInstance Win32_PerfFormattedData_PerfOS_Memory
$hostProcess = Get-Process -Name 'Naraka.Server.Host' -ErrorAction Stop

$result = [PSCustomObject]@{
    ReleaseId = $releaseId
    GitCommit = $manifest.GitCommit
    BackupDirectory = $backupHost
    TaskState = $taskState
    HostProcessCount = 1
    HostWorkingSetMiB = [math]::Round($hostProcess.WorkingSet64 / 1MB,1)
    LiveStatus = $live.status
    ReadyStatus = $ready.status
    Database = $ready.database
    ConfigVersion = $bootstrap.configVersion
    ServerCapabilityCount = @($bootstrap.serverCapabilities).Count
    GameConfigVersion = $configVersion.configVersion
    ProtocolVersion = $bootstrap.protocolVersion
    AvailableMemoryBeforeMiB = $memoryBefore.AvailableMBytes
    AvailableMemoryAfterMiB = $memoryAfter.AvailableMBytes
}

$result | Format-List
