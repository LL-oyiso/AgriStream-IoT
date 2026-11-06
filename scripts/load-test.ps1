<#
.SYNOPSIS
  Simulates many concurrent users and device traffic against the deployed Agristream web app,
  then watches the irrigation queue drain. Works in Windows PowerShell 5.1 and PowerShell 7.

.DESCRIPTION
  Each virtual user loops for -DurationSeconds and rotates through four request types:
    1. GET  /api/ping                  cheap health check (shows which instance answered)
    2. GET  /api/load/work?ms=N        burns CPU so the App Service plan's CPU metric rises
    3. GET  /api/telemetry/{field}     calls the HTTP GET function, like the field dashboard does
    4. POST /api/simulate/irrigation   calls the HTTP POST function, which fills the queue
  Optional bursts first write sensor telemetry (-SensorBurst) and upload drone images (-ImageBurst).
  Afterwards it polls /api/evidence until the queue backlog is empty and reports the drain time.
  The simulator key is read from -SimulatorKey or the AGRISTREAM_SIM_KEY environment variable
  (the SimulatorKey app setting of the web app). Never paste real keys into files you commit.

.EXAMPLE
  $env:AGRISTREAM_SIM_KEY = "<your SimulatorKey value>"
  .\load-test.ps1 -StudentNumber st10504517 -Users 50 -DurationSeconds 120 -Label baseline-1-instance
#>
param(
    [string]$StudentNumber,
    [string]$BaseUrl,
    [string]$SimulatorKey = $env:AGRISTREAM_SIM_KEY,
    [int]$Users = 50,
    [int]$DurationSeconds = 120,
    [int]$WorkMs = 150,
    [string]$FieldId = "field-01",
    [int]$SensorBurst = 0,
    [int]$ImageBurst = 0,
    [int]$DrainTimeoutSeconds = 600,
    [string]$Label = "run"
)

if (-not $BaseUrl) {
    if (-not $StudentNumber) { throw "Provide -BaseUrl or -StudentNumber." }
    $BaseUrl = "https://$($StudentNumber.ToLowerInvariant()).azurewebsites.net"
}
$BaseUrl = $BaseUrl.TrimEnd('/')
if (-not $SimulatorKey) { throw "Set -SimulatorKey or the AGRISTREAM_SIM_KEY environment variable." }

Add-Type -AssemblyName System.Net.Http
[System.Net.ServicePointManager]::DefaultConnectionLimit = [Math]::Max($Users * 2, 100)
[System.Net.ServicePointManager]::SecurityProtocol = [System.Net.SecurityProtocolType]::Tls12

function Invoke-Json($method, $path, $body) {
    $params = @{ Method = $method; Uri = "$BaseUrl$path"; Headers = @{ "X-Simulator-Key" = $SimulatorKey }; TimeoutSec = 300 }
    if ($body) { $params.Body = ($body | ConvertTo-Json); $params.ContentType = "application/json" }
    Invoke-RestMethod @params
}

function Get-Evidence { try { Invoke-Json "Get" "/api/evidence" $null } catch { $null } }

Write-Host "=== Agristream load test [$Label] ===" -ForegroundColor Green
Write-Host "Target: $BaseUrl   Users: $Users   Duration: ${DurationSeconds}s   CPU work per request: ${WorkMs} ms"

$before = Get-Evidence
if ($before) {
    Write-Host ("Before: commands processed={0}  queue backlog={1}  blobs={2}" -f $before.commands.total, $before.queueBacklog, $before.blobCount)
}

# ---- Optional bursts (data streams) ----
if ($SensorBurst -gt 0) {
    $readingsEach = 20
    $sensors = [Math]::Max(1, [int]($SensorBurst / $readingsEach))
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $r = Invoke-Json "Post" "/api/simulate/telemetry" @{ fieldId = $FieldId; sensors = $sensors; readingsPerSensor = $readingsEach }
    Write-Host ("Sensor burst: wrote {0} readings in {1} table transactions ({2:N1}s)" -f $r.readings, $r.batches, $sw.Elapsed.TotalSeconds)
}
if ($ImageBurst -gt 0) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $r = Invoke-Json "Post" "/api/simulate/images" @{ fieldId = $FieldId; count = $ImageBurst }
    Write-Host ("Drone burst: uploaded {0} images ({1:N1}s)" -f $r.uploaded, $sw.Elapsed.TotalSeconds)
}

# ---- Concurrent users ----
$latencies = New-Object System.Collections.Concurrent.ConcurrentBag[double]
$errors = New-Object System.Collections.Concurrent.ConcurrentBag[string]
$instances = New-Object System.Collections.Concurrent.ConcurrentDictionary[string, int]
$irrigationQueued = [ref]0

$worker = {
    param($baseUrl, $key, $seconds, $workMs, $fieldId, $latencies, $errors, $instances, $seed)

    Add-Type -AssemblyName System.Net.Http
    $client = New-Object System.Net.Http.HttpClient
    $client.Timeout = [TimeSpan]::FromSeconds(60)
    $client.DefaultRequestHeaders.Add("X-Simulator-Key", $key)
    $valves = @("V-F01-01", "V-F01-02", "V-F02-01", "V-F02-02", "V-F03-01", "V-F03-02")
    $rand = New-Object System.Random($seed)
    $end = [DateTime]::UtcNow.AddSeconds($seconds)
    $i = $seed

    while ([DateTime]::UtcNow -lt $end) {
        $i++
        $kind = $i % 4
        $sw = [Diagnostics.Stopwatch]::StartNew()
        try {
            if ($kind -eq 0) { $resp = $client.GetAsync("$baseUrl/api/ping").Result }
            elseif ($kind -eq 1) { $resp = $client.GetAsync("$baseUrl/api/load/work?ms=$workMs").Result }
            elseif ($kind -eq 2) { $resp = $client.GetAsync("$baseUrl/api/telemetry/$fieldId").Result }
            else {
                $valve = $valves[$rand.Next($valves.Count)]
                $json = '{"deviceId":"' + $valve + '","fieldId":"' + $valve.Substring(2, 3).Replace('F', 'field-') + '","action":"start","durationMinutes":15}'
                $content = New-Object System.Net.Http.StringContent($json, [Text.Encoding]::UTF8, "application/json")
                $resp = $client.PostAsync("$baseUrl/api/simulate/irrigation", $content).Result
            }
            $sw.Stop()
            if ($resp.IsSuccessStatusCode) {
                $latencies.Add($sw.Elapsed.TotalMilliseconds)
                $values = $null
                if ($resp.Headers.TryGetValues("X-Instance-Id", [ref]$values)) {
                    $inst = ($values | Select-Object -First 1)
                    [void]$instances.AddOrUpdate($inst, 1, [Func[string, int, int]] { param($k, $v) $v + 1 })
                }
                if ($kind -eq 3) { $latencies.Add(-1) }  # marker: one irrigation command queued
            }
            else {
                $errors.Add("HTTP $([int]$resp.StatusCode) on request type $kind")
            }
            $resp.Dispose()
        }
        catch {
            $errors.Add($_.Exception.GetBaseException().Message)
        }
    }
    $client.Dispose()
}

$pool = [RunspaceFactory]::CreateRunspacePool(1, $Users)
$pool.Open()
$jobs = @()
$started = Get-Date
for ($u = 0; $u -lt $Users; $u++) {
    $ps = [PowerShell]::Create()
    $ps.RunspacePool = $pool
    [void]$ps.AddScript($worker).AddArgument($BaseUrl).AddArgument($SimulatorKey).AddArgument($DurationSeconds).AddArgument($WorkMs).AddArgument($FieldId).AddArgument($latencies).AddArgument($errors).AddArgument($instances).AddArgument($u * 1000)
    $jobs += [pscustomobject]@{ Shell = $ps; Handle = $ps.BeginInvoke() }
}

Write-Host "Load running ($Users concurrent users)... record the portal Metrics now (CPU, requests, response time, instance count)."
$peakBacklog = 0
while ($jobs.Handle.IsCompleted -contains $false) {
    Start-Sleep -Seconds 10
    $e = Get-Evidence
    if ($e -and $e.queueBacklog -gt $peakBacklog) { $peakBacklog = [long]$e.queueBacklog }
    Write-Host ("{0:HH:mm:ss}  ok={1}  errors={2}  queue backlog={3}" -f (Get-Date), $latencies.Count, $errors.Count, $(if ($e) { $e.queueBacklog } else { "n/a" }))
}
foreach ($j in $jobs) { $j.Shell.EndInvoke($j.Handle); $j.Shell.Dispose() }
$pool.Close()
$loadSeconds = ((Get-Date) - $started).TotalSeconds

# ---- Drain: how long does the queue take to empty? ----
$drainStart = Get-Date
$drainSeconds = $null
while (((Get-Date) - $drainStart).TotalSeconds -lt $DrainTimeoutSeconds) {
    $e = Get-Evidence
    if ($e) {
        if ($e.queueBacklog -gt $peakBacklog) { $peakBacklog = [long]$e.queueBacklog }
        Write-Host ("{0:HH:mm:ss}  draining: backlog={1}  processed={2}  poison={3}" -f (Get-Date), $e.queueBacklog, $e.commands.total, $e.poisonMessages)
        if ($e.queueBacklog -eq 0) { $drainSeconds = ((Get-Date) - $drainStart).TotalSeconds; break }
    }
    Start-Sleep -Seconds 5
}

# ---- Summary ----
$ok = @($latencies.ToArray() | Where-Object { $_ -ge 0 })
$queuedCount = @($latencies.ToArray() | Where-Object { $_ -lt 0 }).Count
$sorted = $ok | Sort-Object
function Pct($p) { if ($sorted.Count -eq 0) { return 0 }; $sorted[[Math]::Min($sorted.Count - 1, [int][Math]::Floor($p * $sorted.Count))] }

$after = Get-Evidence
Write-Host ""
Write-Host "=== RESULTS [$Label] ===" -ForegroundColor Green
Write-Host ("Concurrent users:        {0}" -f $Users)
Write-Host ("Load duration:           {0:N0} s" -f $loadSeconds)
Write-Host ("Successful requests:     {0}  ({1:N1} requests/s)" -f $sorted.Count, ($sorted.Count / $loadSeconds))
Write-Host ("Failed requests:         {0}" -f $errors.Count)
Write-Host ("Latency p50 / p95 / p99: {0:N0} / {1:N0} / {2:N0} ms" -f (Pct 0.50), (Pct 0.95), (Pct 0.99))
Write-Host ("Irrigation commands queued by the test: {0}" -f $queuedCount)
Write-Host ("Peak queue backlog:      {0}" -f $peakBacklog)
if ($null -ne $drainSeconds) { Write-Host ("Queue drain time:        {0:N0} s after the load stopped" -f $drainSeconds) }
else { Write-Host ("Queue drain time:        not drained within {0} s" -f $DrainTimeoutSeconds) }
if ($after) {
    Write-Host ("Commands processed (SQL): {0}   avg queue latency {1:N0} ms   max {2} ms   poison {3}" -f $after.commands.total, $after.commands.avgLatencyMs, $after.commands.maxLatencyMs, $after.poisonMessages)
}
Write-Host "Requests per instance (X-Instance-Id):"
foreach ($k in $instances.Keys) { Write-Host ("  {0}: {1}" -f $k, $instances[$k]) }
if ($errors.Count -gt 0) {
    Write-Host "Top errors:"
    $errors.ToArray() | Group-Object | Sort-Object Count -Descending | Select-Object -First 5 | ForEach-Object { Write-Host ("  {0} x {1}" -f $_.Count, $_.Name) }
}
Write-Host "Now capture: portal Metrics (CPU Percentage, Memory working set, Requests, Response time, instance count), SQL DTU chart and the Evidence page."
