# Matriz de capacidad: resume cada ejecución de net-test en una línea para TEST_REPORTS.
# Uso: tools\net-matrix.ps1 [-Seconds 40] [-Only b12,b12-100ms]
param([int]$Seconds = 40, [string]$Only = '')
$Tests = @(
    @{ Tag = 'b4'; N = 4; Extra = @() },
    @{ Tag = 'b8'; N = 8; Extra = @() },
    @{ Tag = 'b12'; N = 12; Extra = @('-Extra', '1', '-Reconnect', '12') },
    @{ Tag = 'b12-50ms'; N = 12; Extra = @('-Latency', '50', '-Jitter', '10', '-Loss', '1', '-Travelers', '4') },
    @{ Tag = 'b12-100ms'; N = 12; Extra = @('-Latency', '100', '-Jitter', '15', '-Loss', '2', '-Travelers', '4') },
    @{ Tag = 'b12-150ms'; N = 12; Extra = @('-Latency', '150', '-Jitter', '20', '-Loss', '3', '-Travelers', '4') },
    @{ Tag = 'small-12'; N = 12; Extra = @('-Scenario', 'small', '-Travelers', '2') },
    @{ Tag = 'medium-12'; N = 12; Extra = @('-Scenario', 'medium', '-Travelers', '2') },
    @{ Tag = 'stress-12'; N = 12; Extra = @('-Scenario', 'stress', '-Travelers', '2') }
)
$port = 27820
foreach ($t in $Tests) {
    if ($Only -ne '' -and ($Only -split ',') -notcontains $t.Tag) { continue }
    $port++
    $a = @('-ExecutionPolicy', 'Bypass', '-File', "$PSScriptRoot\net-test.ps1", '-Clients', $t.N, '-Seconds', $Seconds, '-Tag', $t.Tag, '-Port', $port) + $t.Extra
    $o = & powershell @a 2>&1 | ForEach-Object { $_.ToString() }
    $bots = @($o | Where-Object { $_ -match '^bot\d' })
    $ok = 0; $fail = 0; $mis = 0; $chk = 0; $rtt = 0; $rej = ''; $drop = 0; $fwd = 0; $travels = 0
    foreach ($b in $bots) {
        if ($b -match ' ok=(\d+) failed=(\d+)') { $ok += [int]$Matches[1]; $fail += [int]$Matches[2] }
        if ($b -match 'hashChecks=(\d+) hashMismatch=(\d+) maxRttMs=(\d+)') { $chk += [int]$Matches[1]; $mis += [int]$Matches[2]; $rtt = [Math]::Max($rtt, [int]$Matches[3]) }
        if ($b -match 'relayDropped=(\d+) relayForwarded=(\d+) travels=(\d+)') { $drop += [int]$Matches[1]; $fwd += [int]$Matches[2]; $travels += [int]$Matches[3] }
        if ($b -match '^bot1\d\d .*closeReason=(\S*)') { $rej = $Matches[1] }
    }
    $line = $o | Where-Object { $_ -match '^Salida: ' } | Select-Object -Last 1
    if (-not $line) { "$($t.Tag): SIN RESULTADO"; $o | Select-Object -Last 15; continue }
    $dir = $line -replace '^Salida: ', ''
    $csv = @(Import-Csv "$dir\metrics.csv")
    $full = @($csv | Where-Object { [int]$_.players -ge ($t.N - 1) })
    $avg = ($full | ForEach-Object { [double]$_.tick_avg_ms } | Measure-Object -Average).Average
    $p99 = ($full | ForEach-Object { [double]$_.tick_p99_ms } | Measure-Object -Maximum).Maximum
    $sent = ($full | ForEach-Object { [double]$_.sent_kbps } | Measure-Object -Average).Average
    $recv = ($full | ForEach-Object { [double]$_.recv_kbps } | Measure-Object -Average).Average
    $mem = ($csv | ForEach-Object { [double]$_.working_set_mb } | Measure-Object -Maximum).Maximum
    $o | Where-Object { $_ -match 'Escenario' } | Select-Object -First 1
    Get-Content "$dir\server.log" -TotalCount 3 | Where-Object { $_ -match 'Escenario' }
    "{0}: bots={1} ok={2} rechazadas={3} firmas={4} divergencias={5} rttMax={6} perdidos={14}/{15} viajes={16} 13.º={7} | tick {8:F2} ms, p99 máx {9:F1} ms, tardíos {10} | envío {11:F0} kbit/s, recepción {12:F0} kbit/s, memoria {13:F0} MB" -f $t.Tag, $bots.Count, $ok, $fail, $chk, $mis, $rtt, $rej, $avg, $p99, $csv[-1].late, $sent, $recv, $mem, $drop, ($drop + $fwd), $travels
    $o | Where-Object { $_ -match '^STDERR|NO SE CERR' } | Select-Object -First 5
}
