[CmdletBinding()]
param(
    [string]$InstallRoot = 'E:\NarakaLocal\mysql57'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$expectedExecutable = [System.IO.Path]::GetFullPath(
    (Join-Path $InstallRoot 'mysql-5.7.26-winx64\bin\mysqld.exe')
)

$matchingProcesses = @(
    Get-CimInstance Win32_Process -Filter "Name = 'mysqld.exe'" |
        Where-Object {
            -not [string]::IsNullOrWhiteSpace($_.ExecutablePath) -and
            [System.IO.Path]::GetFullPath($_.ExecutablePath) -eq $expectedExecutable
        }
)

foreach ($matchingProcess in $matchingProcesses) {
    Stop-Process -Id $matchingProcess.ProcessId -ErrorAction Stop
}

$deadline = (Get-Date).AddSeconds(15)
do {
    Start-Sleep -Milliseconds 250
    $listener = Get-NetTCPConnection -State Listen -LocalPort 3306 -ErrorAction SilentlyContinue
} while ($null -ne $listener -and (Get-Date) -lt $deadline)

if ($null -ne $listener) {
    throw 'TCP port 3306 is still in use; no unrelated process was stopped.'
}

[PSCustomObject]@{
    StoppedProcessCount = $matchingProcesses.Count
    Port3306Listening = $false
} | Format-List
