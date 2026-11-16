# Samples ONEVO.Api RAM / CPU / SQL connections every N seconds into a CSV.
# Usage: pwsh -File sample-process.ps1 -ProcessId 42788 -Seconds 1320 -Out process-samples.csv
param([int]$ProcessId, [int]$Seconds = 1320, [int]$Interval = 5, [string]$Out = 'process-samples.csv')

"time,working_set_mb,private_mb,cpu_s,threads,sql_conns,tcp_established" | Set-Content $Out
$end = (Get-Date).AddSeconds($Seconds)
while ((Get-Date) -lt $end) {
    $p = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
    if (-not $p) { break }
    $tcp = @(Get-NetTCPConnection -OwningProcess $ProcessId -State Established -ErrorAction SilentlyContinue)
    $sql = @($tcp | Where-Object RemotePort -eq 1433).Count
    "{0},{1},{2},{3},{4},{5},{6}" -f (Get-Date -Format 'HH:mm:ss'),
        [math]::Round($p.WorkingSet64 / 1MB), [math]::Round($p.PrivateMemorySize64 / 1MB),
        [math]::Round($p.CPU, 1), $p.Threads.Count, $sql, $tcp.Count | Add-Content $Out
    Start-Sleep -Seconds $Interval
}
