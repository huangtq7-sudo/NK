[CmdletBinding()]
param(
    [string]$InstallRoot = 'E:\NarakaLocal\mysql57'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$resolvedRoot = [System.IO.Path]::GetFullPath($InstallRoot)
$configPath = Join-Path $resolvedRoot 'my.ini'
$mysqldExe = Join-Path $resolvedRoot 'mysql-5.7.26-winx64\bin\mysqld.exe'

$listener = Get-NetTCPConnection -State Listen -LocalPort 3306 -ErrorAction SilentlyContinue
if ($null -ne $listener) {
    $ownerIds = @($listener | Select-Object -ExpandProperty OwningProcess -Unique)
    $owners = @(
        Get-CimInstance Win32_Process |
            Where-Object { $_.ProcessId -in $ownerIds }
    )
    $isExpectedInstance =
        @($listener | Where-Object LocalAddress -ne '127.0.0.1').Count -eq 0 -and
        $owners.Count -gt 0 -and
        @(
            $owners |
                Where-Object {
                    [string]::IsNullOrWhiteSpace($_.ExecutablePath) -or
                    [System.IO.Path]::GetFullPath($_.ExecutablePath) -ne $mysqldExe
                }
        ).Count -eq 0

    if (-not $isExpectedInstance) {
        throw 'TCP port 3306 is already used by another listener.'
    }

    Write-Output 'The local MySQL port is already listening on 127.0.0.1:3306.'
    exit 0
}

if (-not (Test-Path -LiteralPath $mysqldExe -PathType Leaf)) {
    throw 'The local MySQL runtime is not installed at the expected path.'
}

if (-not (Test-Path -LiteralPath $configPath -PathType Leaf)) {
    throw 'The local MySQL configuration is missing.'
}

$process = Start-Process `
    -FilePath $mysqldExe `
    -ArgumentList "--defaults-file=$configPath" `
    -WindowStyle Hidden `
    -PassThru

$deadline = (Get-Date).AddSeconds(30)
do {
    Start-Sleep -Milliseconds 500
    if ($process.HasExited) {
        throw "MySQL exited during startup with exit code $($process.ExitCode)."
    }

    $listener = Get-NetTCPConnection -State Listen -LocalPort 3306 -ErrorAction SilentlyContinue
} while ($null -eq $listener -and (Get-Date) -lt $deadline)

if ($null -eq $listener -or $listener.LocalAddress -ne '127.0.0.1') {
    throw 'MySQL did not start on the required loopback endpoint.'
}

[PSCustomObject]@{
    ProcessId = $process.Id
    LocalAddress = $listener.LocalAddress
    LocalPort = $listener.LocalPort
    State = 'Running'
} | Format-List
