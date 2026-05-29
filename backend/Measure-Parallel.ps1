#requires -Version 7.0
#requires -Modules ThreadJob

<#
.SYNOPSIS
    Measure-Parallel.ps1 — параллельный нагрузочный замер endpoint-ов источника.
    Не является промышленным загрузчиком данных.
    Запускает все endpoint-ы одновременно — нагружает rate limiter.
    Детальный анализ — в Grafana, скрипт даёт только время и ошибки.

.EXAMPLE
    # Окно 1: dotnet run (с OTLP)
    # Окно 2: .\Measure-Parallel.ps1
    # Окно 3: Grafana http://localhost:3000

    # По умолчанию: стресс-замер, 3 итерации, все endpoint-ы параллельно:
    # .\Measure-Parallel.ps1

    # Разовый более мягкий прогон:
    # .\Measure-Parallel.ps1 -Iterations 1 -TimeoutSec 600

    # С прогревом:
    # .\Measure-Parallel.ps1 -Warmup
#>

param(
    [string]$BaseUrl = "http://localhost:5025",
    [int]$Iterations = 3,
    [int]$TimeoutSec = 300,
    [switch]$Warmup,
    [string]$OutputName = "parallel-001"
)

# ── Endpoint-ы ─────────────────────────────────────────────

$endpoints = @(
    @{ Name = "GetStockMarkets";                    Url = "/GetStockMarkets";                    Group = "ISS" },
    @{ Name = "GetFuturesMarkets";                  Url = "/GetFuturesMarkets";                  Group = "ISS" },
    @{ Name = "GetCandlesAsset";                    Url = "/GetCandlesAsset";                    Group = "ALGOPACK" },
    @{ Name = "GetCandlesFutures";                  Url = "/GetCandlesFutures";                  Group = "ALGOPACK" },
    @{ Name = "GetFutoi";                           Url = "/GetFutoi";                           Group = "ALGOPACK" },
    @{ Name = "GetSuperCandlesTradeStats";          Url = "/GetSuperCandlesTradeStats";          Group = "ALGOPACK" },
    @{ Name = "GetSuperCandlesOrderStats";          Url = "/GetSuperCandlesOrderStats";          Group = "ALGOPACK" },
    @{ Name = "GetSuperCandlesOrderBookStats";      Url = "/GetSuperCandlesOrderBookStats";      Group = "ALGOPACK" },
    @{ Name = "GetSuperCandlesFuturesTradeStats";   Url = "/GetSuperCandlesFuturesTradeStats";   Group = "ALGOPACK" },
    @{ Name = "GetSuperCandlesFuturesOrderBookStat";Url = "/GetSuperCandlesFuturesOrderBookStat";Group = "ALGOPACK" },
    @{ Name = "GetHi2Asset";                        Url = "/GetHi2Asset";                        Group = "ALGOPACK" },
    @{ Name = "GetHi2Furure";                       Url = "/GetHi2Furure";                       Group = "ALGOPACK" },
    @{ Name = "GetMegaAlerts";                      Url = "/GetMegaAlerts";                      Group = "ALGOPACK" },
    @{ Name = "GetMegaAlertsFutures";               Url = "/GetMegaAlertsFutures";               Group = "ALGOPACK" },
    @{ Name = "calendar/offdays-all";               Url = "/calendar/offdays-all";               Group = "Calendar" },
    @{ Name = "calendar/stock-offdays";             Url = "/calendar/stock-offdays";             Group = "Calendar" },
    @{ Name = "calendar/futures-offdays";           Url = "/calendar/futures-offdays";           Group = "Calendar" },
    @{ Name = "calendar/stock-session";             Url = "/calendar/stock-session";             Group = "Calendar" },
    @{ Name = "calendar/stock-session-types";       Url = "/calendar/stock-session-types";       Group = "Calendar" },
    @{ Name = "calendar/futures-session";           Url = "/calendar/futures-session";           Group = "Calendar" },
    @{ Name = "calendar/futures-session-types";     Url = "/calendar/futures-session-types";     Group = "Calendar" },
    @{ Name = "calendar/forts-contracts";           Url = "/calendar/forts-contracts";           Group = "Calendar" },
    @{ Name = "calendar/options-series";            Url = "/calendar/options-series";            Group = "Calendar" },
    @{ Name = "calendar/suspended-reasons";         Url = "/calendar/suspended-reasons";         Group = "Calendar" },
    @{ Name = "calendar/suspended";                 Url = "/calendar/suspended";                 Group = "Calendar" },
    @{ Name = "calendar/security-attributes";       Url = "/calendar/security-attributes";       Group = "Calendar" },
    @{ Name = "calendar/security-changes";          Url = "/calendar/security-changes";          Group = "Calendar" }
)

# ── Проверка сервера ───────────────────────────────────────

Write-Host ""
Write-Host "=== Parallel Benchmark ===" -ForegroundColor Cyan
Write-Host "Endpoints: $($endpoints.Count), Iterations: $Iterations, Timeout: ${TimeoutSec}s" -ForegroundColor Cyan
Write-Host "Режим: все $($endpoints.Count) endpoint-ов одновременно" -ForegroundColor Yellow
Write-Host ""

$BaseUrl = $BaseUrl.TrimEnd('/')

$checkClient = [System.Net.Http.HttpClient]::new()
$checkClient.Timeout = [TimeSpan]::FromSeconds(10)
try {
    $resp = $checkClient.GetAsync("$BaseUrl/healthz").GetAwaiter().GetResult()
    if (-not $resp.IsSuccessStatusCode) {
        Write-Host "Сервер ответил HTTP $([int]$resp.StatusCode)" -ForegroundColor Red; exit 1
    }
    $resp.Dispose()
    Write-Host "Сервер доступен." -ForegroundColor Green
} catch {
    Write-Host "Сервер не отвечает: $BaseUrl" -ForegroundColor Red; exit 1
} finally {
    $checkClient.Dispose()
}

# ── Прогрев (последовательный, один раз) ───────────────────

if ($Warmup) {
    Write-Host "`nПрогрев..." -ForegroundColor Yellow
    $warmClient = [System.Net.Http.HttpClient]::new()
    $warmClient.Timeout = [TimeSpan]::FromSeconds($TimeoutSec)
    $warmupOk = 0; $warmupFail = 0
    foreach ($ep in $endpoints) {
        try {
            $wr = $warmClient.GetAsync("$BaseUrl$($ep.Url)").GetAwaiter().GetResult()
            $null = $wr.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult()
            Write-Host "  [w] $($ep.Name) OK" -ForegroundColor DarkGray
            $wr.Dispose()
            $warmupOk++
        } catch {
            Write-Host "  [w] $($ep.Name) FAIL: $($_.Exception.InnerException?.Message ?? $_.Exception.Message)" -ForegroundColor Red
            $warmupFail++
        }
    }
    $warmClient.Dispose()
    Write-Host "Прогрев: OK $warmupOk, FAIL $warmupFail" -ForegroundColor $(if ($warmupFail -gt 0) { "Yellow" } else { "Green" })
    Write-Host ""
} else {
    $warmupOk = 0; $warmupFail = 0
}

# ── Вспомогательные функции ─────────────────────────────────

function Escape-MarkdownCell {
    param([string]$Value)
    if ($null -eq $Value) { return "" }
    return ($Value -replace "\|", "\|" -replace "`r", " " -replace "`n", " ")
}

# ── Замеры — все endpoint-ы параллельно ────────────────────

$allResults = @()
$iterationSummaries = @()

for ($iter = 1; $iter -le $Iterations; $iter++) {

    Write-Host "Итерация $iter/$Iterations — запуск $($endpoints.Count) endpoint-ов параллельно..." -ForegroundColor Yellow
    $iterSw = [System.Diagnostics.Stopwatch]::StartNew()

    # Запуск всех endpoint-ов через ThreadJob (PowerShell 7)
    $jobs = @()
    foreach ($ep in $endpoints) {
        $jobs += Start-ThreadJob -ScriptBlock {
            param($BaseUrl, $Url, $Name, $TimeoutSec)

            $client = [System.Net.Http.HttpClient]::new()
            $client.Timeout = [TimeSpan]::FromSeconds($TimeoutSec)

            try {
                $request = [System.Net.Http.HttpRequestMessage]::new(
                    [System.Net.Http.HttpMethod]::Get, "$BaseUrl$Url")

                $sw = [System.Diagnostics.Stopwatch]::StartNew()
                $response = $client.SendAsync(
                    $request,
                    [System.Net.Http.HttpCompletionOption]::ResponseHeadersRead
                ).GetAwaiter().GetResult()
                $ttfbMs = $sw.ElapsedMilliseconds

                $statusCode = [int]$response.StatusCode
                $bodyBytes = $response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult()
                $sw.Stop()
                $response.Dispose()
                $request.Dispose()

                @{
                    Name       = $Name
                    Success    = ($statusCode -eq 200)
                    StatusCode = $statusCode
                    ElapsedMs  = $sw.ElapsedMilliseconds
                    TtfbMs     = $ttfbMs
                    Bytes      = $bodyBytes.Length
                    Error      = if ($statusCode -ne 200) { "HTTP $statusCode" } else { $null }
                }
            } catch {
                if ($sw) { $sw.Stop() }
                $errMsg = $_.Exception.InnerException?.Message ?? $_.Exception.Message
                @{
                    Name       = $Name
                    Success    = $false
                    StatusCode = $null
                    ElapsedMs  = if ($sw) { $sw.ElapsedMilliseconds } else { -1 }
                    TtfbMs     = -1
                    Bytes      = 0
                    Error      = $errMsg
                }
            } finally {
                $client.Dispose()
            }
        } -ArgumentList $BaseUrl, $ep.Url, $ep.Name, $TimeoutSec
    }

    # Ждём все
    $iterResults = @($jobs | Wait-Job | Receive-Job)
    $jobs | Remove-Job -Force

    $iterSw.Stop()
    $iterTimeSec = [math]::Round($iterSw.Elapsed.TotalSeconds, 1)

    # Подсчёт
    $okCount = ($iterResults | Where-Object { $_.Success }).Count
    $failCount = ($iterResults | Where-Object { -not $_.Success }).Count
    $totalRequests = $iterResults.Count

    Write-Host "  Готово за ${iterTimeSec}s — OK: $okCount, FAIL: $failCount" -ForegroundColor $(if ($failCount -gt 0) { "Red" } else { "Green" })

    # Ошибки
    $errors = $iterResults | Where-Object { -not $_.Success }
    foreach ($err in $errors) {
        Write-Host "    FAIL: $($err.Name) — $($err.Error)" -ForegroundColor Red
    }

    # Топ-5 медленных
    $slowest = $iterResults | Where-Object { $_.Success } | Sort-Object ElapsedMs -Descending | Select-Object -First 5
    Write-Host "  Топ-5 медленных:" -ForegroundColor DarkGray
    foreach ($s in $slowest) {
        $bytesStr = if ($s.Bytes -ge 1MB) { "$([math]::Round($s.Bytes / 1MB, 1))MB" }
                    elseif ($s.Bytes -ge 1KB) { "$([math]::Round($s.Bytes / 1KB, 0))KB" }
                    else { "$($s.Bytes)B" }
        Write-Host "    $($s.Name): $($s.ElapsedMs)ms (TTFB $($s.TtfbMs)ms), $bytesStr" -ForegroundColor DarkGray
    }
    Write-Host ""

    foreach ($r in $iterResults) {
        $allResults += @{ Iteration = $iter; Data = $r }
    }
    $iterationSummaries += @{
        Iteration     = $iter
        WallSec       = $iterTimeSec
        Ok            = $okCount
        Fail          = $failCount
        EndpointCount = $totalRequests
    }
}

# ── Отчёт ──────────────────────────────────────────────────

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$outputDir = Join-Path $scriptRoot "docs\performance"
if (-not (Test-Path $outputDir)) { New-Item -Path $outputDir -ItemType Directory -Force | Out-Null }

$date = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
$report = "# Parallel Benchmark: $OutputName`n`n"
$report += "| Параметр | Значение |`n|---|---|`n"
$report += "| Дата | $date |`n"
$report += "| Режим | Параллельный (все $($endpoints.Count) endpoint-ов одновременно) |`n"
$report += "| Итерации | $Iterations |`n"
$report += "| Timeout | $TimeoutSec сек |`n`n"

# Прогрев
if ($Warmup) {
    $report += "## Прогрев`n`n| OK | FAIL |`n|---|---|`n| $warmupOk | $warmupFail |`n`n"
}

# Сводка по итерациям
$report += "## Итерации`n`n"
$report += "| # | Время сек | OK | FAIL | API endpoint/s |`n|---|---|---|---|---|`n"
# Время итерации — реальное wall-clock из Stopwatch, не пересчёт из max endpoint.
# Реальное количество HTTP-запросов к MOEX смотреть в Grafana/Prometheus:
# sum(rate(moex_http_requests_total[5m]))
foreach ($s in $iterationSummaries) {
    $reqPerSec = if ($s.WallSec -gt 0) { [math]::Round($s.EndpointCount / $s.WallSec, 1) } else { 0 }
    $report += "| $($s.Iteration) | $($s.WallSec) | $($s.Ok) | $($s.Fail) | ~$reqPerSec |`n"
}
$report += "`n"

# Сводка по endpoint-ам
$report += "## Endpoint-ы (медиана по итерациям)`n`n"
$report += "| Endpoint | Медиана мс | TTFB мс | Мин | Макс | Байт | Ошибки |`n|---|---|---|---|---|---|---|`n"

foreach ($ep in $endpoints) {
    $epRuns = @($allResults | Where-Object { $_.Data.Name -eq $ep.Name })
    $okRuns = @($epRuns | Where-Object { $_.Data.Success })
    $failRuns = @($epRuns | Where-Object { -not $_.Data.Success })

    if ($okRuns.Count -gt 0) {
        $times = @($okRuns | ForEach-Object { [double]$_.Data.ElapsedMs }) | Sort-Object
        $ttfbs = @($okRuns | ForEach-Object { [double]$_.Data.TtfbMs }) | Sort-Object
        $mid = [math]::Floor($times.Count / 2)
        $median = if ($times.Count % 2 -eq 0) { [math]::Round(($times[$mid-1] + $times[$mid])/2) } else { [math]::Round($times[$mid]) }
        $ttfbMid = if ($ttfbs.Count % 2 -eq 0) { [math]::Round(($ttfbs[$mid-1] + $ttfbs[$mid])/2) } else { [math]::Round($ttfbs[$mid]) }
        $min = ($times | Measure-Object -Minimum).Minimum
        $max = ($times | Measure-Object -Maximum).Maximum
        $bytes = $okRuns[0].Data.Bytes
        $bytesStr = if ($bytes -ge 1MB) { "$([math]::Round($bytes/1MB,1)) MB" } elseif ($bytes -ge 1KB) { "$([math]::Round($bytes/1KB,0)) KB" } else { "$bytes B" }
        $failStr = if ($failRuns.Count -gt 0) { "$($failRuns.Count) FAIL" } else { "-" }
        $report += "| $($ep.Name) | $median | $ttfbMid | $min | $max | $bytesStr | $failStr |`n"
    } else {
        $errSample = Escape-MarkdownCell $failRuns[0].Data.Error
        if ($errSample.Length -gt 60) { $errSample = $errSample.Substring(0, 60) + "..." }
        $report += "| $($ep.Name) | FAIL | - | - | - | - | $errSample |`n"
    }
}
$report += "`n"

# Ошибки
$allErrors = @($allResults | Where-Object { -not $_.Data.Success })
if ($allErrors.Count -gt 0) {
    $report += "## Ошибки`n`n"
    $report += "| Итерация | Endpoint | Время мс | Ошибка |`n|---|---|---|---|`n"
    foreach ($e in $allErrors) {
        $errShort = Escape-MarkdownCell $e.Data.Error
        if ($errShort.Length -gt 100) { $errShort = $errShort.Substring(0, 100) + "..." }
        $report += "| $($e.Iteration) | $(Escape-MarkdownCell $e.Data.Name) | $($e.Data.ElapsedMs) | $errShort |`n"
    }
}

$outputPath = Join-Path $outputDir "$OutputName.md"
$report | Out-File -FilePath $outputPath -Encoding utf8

# ── Финал ──────────────────────────────────────────────────

$totalOk = ($allResults | Where-Object { $_.Data.Success }).Count
$totalFail = ($allResults | Where-Object { -not $_.Data.Success }).Count

Write-Host "=== Готово ===" -ForegroundColor Green
Write-Host "Отчёт: $outputPath" -ForegroundColor Green
Write-Host "OK: $totalOk, FAIL: $totalFail из $($allResults.Count)" -ForegroundColor $(if ($totalFail -gt 0) { "Red" } else { "Cyan" })
Write-Host "Детальный анализ — в Grafana: http://localhost:3000" -ForegroundColor Cyan
Write-Host ""
