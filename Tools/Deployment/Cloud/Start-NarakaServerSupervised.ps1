[CmdletBinding()]
param(
    [string]$HostDirectory = 'C:\NarakaDeploy\cloud-82c02c7\host',
    [ValidateRange(15,300)]
    [int]$RestartDelaySeconds = 30,
    [ValidateRange(128,1024)]
    [int]$MinimumAvailableMemoryMiB = 256,
    [ValidateRange(1,100)]
    [int]$MaximumLogMiB = 10
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$allowedRoot = [System.IO.Path]::GetFullPath('C:\NarakaDeploy\')
$resolvedHostDirectory = [System.IO.Path]::GetFullPath($HostDirectory)
if (-not $resolvedHostDirectory.StartsWith($allowedRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Host directory is outside C:\NarakaDeploy: $resolvedHostDirectory"
}

$hostExe = Join-Path $resolvedHostDirectory 'Naraka.Server.Host.exe'
$stdoutLog = Join-Path $resolvedHostDirectory 'host.background.stdout.log'
$stderrLog = Join-Path $resolvedHostDirectory 'host.background.stderr.log'
$supervisorLog = Join-Path $resolvedHostDirectory 'host.supervisor.log'
$maximumLogBytes = $MaximumLogMiB * 1MB

function Write-SupervisorLog {
    param([Parameter(Mandatory = $true)][string]$Message)

    $line = '{0} {1}' -f [DateTime]::Now.ToString('o'),$Message
    Add-Content -LiteralPath $supervisorLog -Value $line -Encoding UTF8
}

function Rotate-LogIfNeeded {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return
    }

    $item = Get-Item -LiteralPath $Path
    if ($item.Length -lt $maximumLogBytes) {
        return
    }

    $archivePath = '{0}.{1}.log' -f $Path,(Get-Date -Format 'yyyyMMdd-HHmmss')
    Move-Item -LiteralPath $Path -Destination $archivePath

    $archivePattern = ([System.IO.Path]::GetFileName($Path)) + '.*.log'
    $oldArchives = @(
        Get-ChildItem -LiteralPath $resolvedHostDirectory -Filter $archivePattern -File |
            Sort-Object LastWriteTimeUtc -Descending |
            Select-Object -Skip 3
    )

    foreach ($oldArchive in $oldArchives) {
        Remove-Item -LiteralPath $oldArchive.FullName -Force
    }
}

$existingHosts = @(Get-Process -Name 'Naraka.Server.Host' -ErrorAction SilentlyContinue)
if ($existingHosts.Count -gt 0) {
    Write-SupervisorLog 'Host is already running; supervisor exited without starting another process.'
    exit 0
}

while ($true) {
    try {
        if (-not (Test-Path -LiteralPath $hostExe -PathType Leaf)) {
            Write-SupervisorLog 'Host executable is missing; retrying later.'
            Start-Sleep -Seconds 60
            continue
        }

        $availableMemory = (Get-CimInstance Win32_PerfFormattedData_PerfOS_Memory).AvailableMBytes
        if ($availableMemory -lt $MinimumAvailableMemoryMiB) {
            Write-SupervisorLog "Host start deferred because available memory is $availableMemory MiB."
            Start-Sleep -Seconds 60
            continue
        }

        $machineConnection = [Environment]::GetEnvironmentVariable(
            'NARAKA_MYSQL_CONNECTION_STRING',
            'Machine'
        )
        if ([string]::IsNullOrWhiteSpace($machineConnection)) {
            Write-SupervisorLog 'Host start deferred because the machine database variable is missing.'
            Start-Sleep -Seconds 60
            continue
        }

        Rotate-LogIfNeeded -Path $stdoutLog
        Rotate-LogIfNeeded -Path $stderrLog

        $env:NARAKA_MYSQL_CONNECTION_STRING = $machineConnection
        $machineConnection = $null
        $env:ASPNETCORE_URLS = 'http://127.0.0.1:5222'
        $env:ASPNETCORE_ENVIRONMENT = 'Production'

        Set-Location -LiteralPath $resolvedHostDirectory
        Write-SupervisorLog 'Starting Host.'
        & $hostExe 1>> $stdoutLog 2>> $stderrLog
        $hostExitCode = $LASTEXITCODE
        Write-SupervisorLog "Host exited with code $hostExitCode; restart is delayed."
    }
    catch {
        $exceptionType = $_.Exception.GetType().FullName
        Write-SupervisorLog "Supervisor caught $exceptionType; restart is delayed."
    }
    finally {
        $env:NARAKA_MYSQL_CONNECTION_STRING = $null
    }

    Start-Sleep -Seconds $RestartDelaySeconds
}
