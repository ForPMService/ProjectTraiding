<#
.SYNOPSIS
    Замер производительности History-DataMoex — все endpoints.
    При FAIL пишет диагностику в docs/performance/debug-log.txt
    Дополнительно проверяет, что во время вызовов endpoint-ов приложение пишет MOEX-логи.

.DESCRIPTION
    Изменения относительно Baseline1:
    - TTFB (time-to-first-byte) через HttpClient вместо Invoke-WebRequest
    - Размер ответа в байтах, а не символах
    - Подсчёт строк без ConvertFrom-Json (считаем элементы JSON-массива по паттерну)
    - GC.Collect между endpoint'ами для чистоты MemDelta
    - Медиана вместо среднего в сводке

.EXAMPLE
    # Окно 1: dotnet run --project ".\History DataMoex.csproj"
    # Окно 2: мониторинг по желанию
    # Окно 3: .\Measure-Baseline2.LogEvidence.ps1 -OutputName "baseline-002"

    # Если приложение пишет логи в файл, укажи файл явно:
    # .\Measure-Baseline2.LogEvidence.ps1 -AppLogPath ".\docs\performance\app-manual.log"

    # Если приложение пишет только в консоль, внешний скрипт не может прочитать уже открытое консольное окно.
    # Тогда запускай приложение через Tee-Object или подключай file logging в приложении.
#>

param(
    [string]$BaseUrl = "http://localhost:5025",
    [string]$OutputName = "baseline-002",
    [int]$Iterations = 3,
    [int]$WarmupIterations = 1,
    [int]$TimeoutSec = 120,

    # Файл логов уже запущенного приложения.
    # Скрипт НЕ запускает приложение. Он только читает этот файл, если он есть.
    [string]$AppLogPath = "",

    # Сколько ждать, пока лог-файл пополнится после вызова endpoint-а.
    [int]$LogEvidenceWaitMs = 3000
)

$endpoints = @(
    # === Reference (ISS, без ключа) ===
    @{ Name = "GetStockMarkets";       Url = "/GetStockMarkets";       Group = "ISS Reference" },
    @{ Name = "GetFuturesMarkets";     Url = "/GetFuturesMarkets";     Group = "ISS Reference" },

    # === ALGOPACK Candles (FixedPage500) ===
    @{ Name = "GetCandlesAsset";       Url = "/GetCandlesAsset";       Group = "ALGOPACK Candles" },
    @{ Name = "GetCandlesFutures";     Url = "/GetCandlesFutures";     Group = "ALGOPACK Candles" },

    # === ALGOPACK FUTOI (FixedPage1000) ===
    @{ Name = "GetFutoi";              Url = "/GetFutoi";              Group = "ALGOPACK FUTOI" },

    # === ALGOPACK SuperCandles EQ (Cursor) ===
    @{ Name = "GetSuperCandlesTradeStats";          Url = "/GetSuperCandlesTradeStats";          Group = "ALGOPACK SuperCandles EQ" },
    @{ Name = "GetSuperCandlesOrderStats";          Url = "/GetSuperCandlesOrderStats";          Group = "ALGOPACK SuperCandles EQ" },
    @{ Name = "GetSuperCandlesOrderBookStats";      Url = "/GetSuperCandlesOrderBookStats";      Group = "ALGOPACK SuperCandles EQ" },

    # === ALGOPACK SuperCandles FO (Cursor) ===
    @{ Name = "GetSuperCandlesFuturesTradeStats";   Url = "/GetSuperCandlesFuturesTradeStats";   Group = "ALGOPACK SuperCandles FO" },
    @{ Name = "GetSuperCandlesFuturesOrderBookStat";Url = "/GetSuperCandlesFuturesOrderBookStat";Group = "ALGOPACK SuperCandles FO" },

    # === ALGOPACK HI2 (Cursor) ===
    @{ Name = "GetHi2Asset";           Url = "/GetHi2Asset";           Group = "ALGOPACK HI2" },
    @{ Name = "GetHi2Furure";          Url = "/GetHi2Furure";          Group = "ALGOPACK HI2" },

    # === ALGOPACK MegaAlerts (Cursor) ===
    @{ Name = "GetMegaAlerts";         Url = "/GetMegaAlerts";         Group = "ALGOPACK MegaAlerts" },
    @{ Name = "GetMegaAlertsFutures";  Url = "/GetMegaAlertsFutures";  Group = "ALGOPACK MegaAlerts" },

    # === Calendar ===
    @{ Name = "calendar/offdays-all";       Url = "/calendar/offdays-all";       Group = "Calendar" },
    @{ Name = "calendar/stock-offdays";     Url = "/calendar/stock-offdays";     Group = "Calendar" },
    @{ Name = "calendar/futures-offdays";   Url = "/calendar/futures-offdays";   Group = "Calendar" },
    @{ Name = "calendar/stock-session";     Url = "/calendar/stock-session";     Group = "Calendar Sessions" },
    @{ Name = "calendar/stock-session-types";   Url = "/calendar/stock-session-types";   Group = "Calendar Sessions" },
    @{ Name = "calendar/futures-session";       Url = "/calendar/futures-session";       Group = "Calendar Sessions" },
    @{ Name = "calendar/futures-session-types"; Url = "/calendar/futures-session-types"; Group = "Calendar Sessions" },
    @{ Name = "calendar/forts-contracts";   Url = "/calendar/forts-contracts";   Group = "Calendar Futures" },
    @{ Name = "calendar/options-series";    Url = "/calendar/options-series";    Group = "Calendar Futures" },
    @{ Name = "calendar/suspended-reasons"; Url = "/calendar/suspended-reasons"; Group = "Calendar Suspended" },
    @{ Name = "calendar/suspended";         Url = "/calendar/suspended";         Group = "Calendar Suspended" },
    @{ Name = "calendar/security-attributes"; Url = "/calendar/security-attributes"; Group = "Calendar Securities" },
    @{ Name = "calendar/security-changes";    Url = "/calendar/security-changes";    Group = "Calendar Securities" }
)

# ── HttpClient (один на весь скрипт, как браузер) ───────────

$httpClient = [System.Net.Http.HttpClient]::new()
$httpClient.Timeout = [TimeSpan]::FromSeconds($TimeoutSec)

# ── Лог-файл для диагностики FAIL ──────────────────────────

$outputDir = "docs\performance"
if (-not (Test-Path $outputDir)) { New-Item -Path $outputDir -ItemType Directory -Force | Out-Null }
$debugLogPath = Join-Path $outputDir "debug-log-$OutputName.txt"

"=== Debug Log: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') ===" | Out-File -FilePath $debugLogPath -Encoding utf8
"Timeout: $TimeoutSec сек" | Out-File -FilePath $debugLogPath -Encoding utf8 -Append
"" | Out-File -FilePath $debugLogPath -Encoding utf8 -Append

function Write-DebugLog {
    param([string]$Message)
    $timestamp = Get-Date -Format "HH:mm:ss"
    "[$timestamp] $Message" | Out-File -FilePath $debugLogPath -Encoding utf8 -Append
}

function Resolve-AppLogPath {
    param(
        [string]$PathFromParam,
        [string]$OutputDir
    )

    if (-not [string]::IsNullOrWhiteSpace($PathFromParam)) {
        return $PathFromParam
    }

    $candidates = @()

    $manual = Join-Path $OutputDir "app-manual.log"
    if (Test-Path $manual) { $candidates += Get-Item $manual }

    $byOutput = Join-Path $OutputDir "app-$OutputName.log"
    if (Test-Path $byOutput) { $candidates += Get-Item $byOutput }

    $appLogs = Get-ChildItem -Path $OutputDir -Filter "app-*.log" -File -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending
    if ($appLogs) { $candidates += $appLogs }

    $selected = $candidates |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1

    if ($selected) { return $selected.FullName }

    return ""
}

function Get-LogOffset {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) { return $null }
    if (-not (Test-Path $Path)) { return $null }

    try {
        $file = Get-Item $Path -ErrorAction Stop
        return [int64]$file.Length
    } catch {
        return $null
    }
}

function Read-LogSegment {
    param(
        [string]$Path,
        [int64]$FromOffset,
        [int64]$Length
    )

    if ($Length -le 0) { return "" }

    $fs = $null
    try {
        $fs = [System.IO.FileStream]::new(
            $Path,
            [System.IO.FileMode]::Open,
            [System.IO.FileAccess]::Read,
            [System.IO.FileShare]::ReadWrite
        )

        $null = $fs.Seek($FromOffset, [System.IO.SeekOrigin]::Begin)
        $buffer = [byte[]]::new([int]$Length)
        $read = $fs.Read($buffer, 0, $buffer.Length)
        if ($read -le 0) { return "" }
        return [System.Text.Encoding]::UTF8.GetString($buffer, 0, $read)
    }
    catch {
        return ""
    }
    finally {
        if ($fs) { $fs.Dispose() }
    }
}

function Test-AppLogEvidence {
    param(
        [string]$Path,
        [object]$FromOffset,
        [string]$EndpointName,
        [int]$WaitMs
    )

    $result = [ordered]@{
        Enabled       = $false
        Path          = $Path
        Evidence      = "DISABLED"
        BytesAdded    = 0
        Pattern       = ""
        EndpointMatch = $false
        Snippet       = ""
    }

    if ([string]::IsNullOrWhiteSpace($Path)) {
        $result.Evidence = "NO_LOG_PATH"
        return $result
    }

    $result.Enabled = $true

    if (-not (Test-Path $Path)) {
        $result.Evidence = "NO_LOG_FILE"
        return $result
    }

    if ($null -eq $FromOffset) {
        $result.Evidence = "NO_START_OFFSET"
        return $result
    }

    $from = [int64]$FromOffset
    $deadline = (Get-Date).AddMilliseconds($WaitMs)
    $endOffset = $from

    do {
        try {
            $file = Get-Item $Path -ErrorAction Stop
            $endOffset = [int64]$file.Length
            if ($endOffset -gt $from) { break }
        } catch {
            $result.Evidence = "LOG_READ_ERROR"
            return $result
        }

        Start-Sleep -Milliseconds 100
    }
    while ((Get-Date) -lt $deadline)

    $bytesAdded = [int64]([Math]::Max(0, $endOffset - $from))
    $result.BytesAdded = $bytesAdded

    if ($bytesAdded -le 0) {
        $result.Evidence = "NO_LOG_BYTES"
        return $result
    }

    # Ограничиваем чтение хвоста, чтобы длинный endpoint не заставлял скрипт читать мегабайты логов.
    $maxRead = [int64](256KB)
    $readOffset = $from
    $readLength = $bytesAdded
    if ($readLength -gt $maxRead) {
        $readOffset = $endOffset - $maxRead
        $readLength = $maxRead
    }

    $segment = Read-LogSegment -Path $Path -FromOffset $readOffset -Length $readLength
    if ([string]::IsNullOrWhiteSpace($segment)) {
        $result.Evidence = "LOG_SEGMENT_EMPTY"
        return $result
    }

    $patterns = @(
        "MOEX load started",
        "MOEX HTTP response received",
        "MOEX single page processed",
        "MOEX data page received",
        "MOEX paging stopped",
        "MOEX request failed",
        "MOEX parse failed",
        "MOEX request cancelled",
        "MOEX retry attempt"
    )

    foreach ($pattern in $patterns) {
        if ($segment.Contains($pattern)) {
            $result.Evidence = "OK"
            $result.Pattern = $pattern
            break
        }
    }

    if ($segment.Contains($EndpointName)) {
        $result.EndpointMatch = $true
    }

    if ($result.Evidence -ne "OK") {
        $result.Evidence = "MOEX_PATTERN_MISSING"
    }

    $oneLine = ($segment -replace "`r", "" -replace "`n", " ").Trim()
    if ($oneLine.Length -gt 240) { $oneLine = $oneLine.Substring(0, 240) }
    $result.Snippet = $oneLine

    return $result
}

# ── Функции ─────────────────────────────────────────────────

function Get-ProcessSnapshot {
    param([System.Diagnostics.Process]$Process)
    if (-not $Process) { return $null }
    try {
        $Process.Refresh()
        return @{
            WorkingSetMB     = [math]::Round($Process.WorkingSet64 / 1MB, 1)
            PrivateMemoryMB  = [math]::Round($Process.PrivateMemorySize64 / 1MB, 1)
            PeakWorkingSetMB = [math]::Round($Process.PeakWorkingSet64 / 1MB, 1)
            HandleCount      = $Process.HandleCount
            ThreadCount      = $Process.Threads.Count
            TotalProcessorSec = [math]::Round($Process.TotalProcessorTime.TotalSeconds, 2)
        }
    } catch { return $null }
}

function Get-Median {
    param([double[]]$Values)
    if ($Values.Count -eq 0) { return 0 }
    $sorted = $Values | Sort-Object
    $mid = [math]::Floor($sorted.Count / 2)
    if ($sorted.Count % 2 -eq 0) {
        return [math]::Round(($sorted[$mid - 1] + $sorted[$mid]) / 2, 0)
    } else {
        return [math]::Round($sorted[$mid], 0)
    }
}

function Count-JsonArrayElements {
    <#
    .SYNOPSIS
        Считает элементы верхнеуровневого JSON-массива без десериализации.
        Работает на byte[], не аллоцирует DOM. Точность ±0 для плоских массивов объектов.
    #>
    param([byte[]]$Bytes)

    if ($Bytes.Length -lt 2) { return 0 }
    # Ожидаем [ ... ]
    if ($Bytes[0] -ne 0x5B) { return -1 }  # не массив

    $depth = 0
    $count = 0
    $inString = $false
    $escaped = $false

    for ($i = 1; $i -lt $Bytes.Length; $i++) {
        $b = $Bytes[$i]

        if ($escaped) { $escaped = $false; continue }

        if ($b -eq 0x5C -and $inString) { $escaped = $true; continue }  # backslash

        if ($b -eq 0x22) { $inString = -not $inString; continue }  # quote

        if ($inString) { continue }

        switch ($b) {
            0x7B { # {
                if ($depth -eq 0) { $count++ }
                $depth++
            }
            0x5B { $depth++ }  # [
            0x7D { $depth-- }  # }
            0x5D { $depth-- }  # ]
            0x2C { # comma
                # Элементы-примитивы на верхнем уровне (без {})
                # Не считаем — наши ответы всегда массивы объектов
            }
        }
    }
    return $count
}

function Measure-Endpoint {
    param(
        [string]$Url,
        [string]$Name,
        [System.Diagnostics.Process]$Process,
        [string]$AppLogPath,
        [int]$LogEvidenceWaitMs
    )

    $memBefore = Get-ProcessSnapshot -Process $Process
    $swTotal = [System.Diagnostics.Stopwatch]::StartNew()
    $ttfbMs = -1
    $logOffset = Get-LogOffset -Path $AppLogPath

    try {
        # ── Запрос с замером TTFB ──
        $request = [System.Net.Http.HttpRequestMessage]::new(
            [System.Net.Http.HttpMethod]::Get, $Url)

        $swTtfb = [System.Diagnostics.Stopwatch]::StartNew()
        $response = $httpClient.SendAsync(
            $request,
            [System.Net.Http.HttpCompletionOption]::ResponseHeadersRead
        ).GetAwaiter().GetResult()
        $swTtfb.Stop()
        $ttfbMs = $swTtfb.ElapsedMilliseconds

        $statusCode = [int]$response.StatusCode

        if ($statusCode -ne 200) {
            $bodyStr = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
            $snippet = if ($bodyStr.Length -gt 500) { $bodyStr.Substring(0, 500) } else { $bodyStr }
            $swTotal.Stop()
            $logEvidence = Test-AppLogEvidence -Path $AppLogPath -FromOffset $logOffset -EndpointName $Name -WaitMs $LogEvidenceWaitMs
            Write-DebugLog "FAIL $Name | HTTP $statusCode | Тело: $snippet"
            Write-DebugLog "  LogEvidence: $($logEvidence.Evidence), BytesAdded=$($logEvidence.BytesAdded), Pattern=$($logEvidence.Pattern)"
            $response.Dispose(); $request.Dispose()
            return @{
                Success       = $false
                ElapsedMs     = $swTotal.ElapsedMilliseconds
                TtfbMs        = $ttfbMs
                Error         = "HTTP $statusCode"
                StatusCode    = $statusCode
                BodySnippet   = $snippet
                LogEvidence   = $logEvidence.Evidence
                LogBytesAdded = $logEvidence.BytesAdded
                LogPattern    = $logEvidence.Pattern
                LogEndpointMatch = $logEvidence.EndpointMatch
            }
        }

        # ── Читаем тело как byte[] (точный размер, без UTF-16 раздувания) ──
        $bodyBytes = $response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult()
        $swTotal.Stop()

        $responseBytes = $bodyBytes.Length
        $rows = Count-JsonArrayElements -Bytes $bodyBytes

        $memAfter = Get-ProcessSnapshot -Process $Process
        $logEvidence = Test-AppLogEvidence -Path $AppLogPath -FromOffset $logOffset -EndpointName $Name -WaitMs $LogEvidenceWaitMs

        $response.Dispose(); $request.Dispose()

        return @{
            Success       = $true
            ElapsedMs     = $swTotal.ElapsedMilliseconds
            TtfbMs        = $ttfbMs
            Rows          = $rows
            ResponseBytes = $responseBytes
            StatusCode    = $statusCode
            MemBeforeMB   = if ($memBefore) { $memBefore.WorkingSetMB } else { "-" }
            MemAfterMB    = if ($memAfter)  { $memAfter.WorkingSetMB }  else { "-" }
            MemDeltaMB    = if ($memBefore -and $memAfter) { [math]::Round($memAfter.WorkingSetMB - $memBefore.WorkingSetMB, 1) } else { "-" }
            Error         = $null
            LogEvidence   = $logEvidence.Evidence
            LogBytesAdded = $logEvidence.BytesAdded
            LogPattern    = $logEvidence.Pattern
            LogEndpointMatch = $logEvidence.EndpointMatch
        }
    } catch {
        $swTotal.Stop()

        $errorMsg = $_.Exception.Message
        # Разворачиваем AggregateException от HttpClient
        if ($_.Exception.InnerException) {
            $errorMsg = $_.Exception.InnerException.Message
        }

        $errorType = "UNKNOWN"
        if ($errorMsg -like "*timeout*" -or $errorMsg -like "*timed out*" -or $errorMsg -like "*TaskCanceled*") {
            $errorType = "TIMEOUT"
        } elseif ($errorMsg -like "*connection*" -or $errorMsg -like "*connect*" -or $errorMsg -like "*refused*") {
            $errorType = "CONNECTION"
        }

        Write-DebugLog "─── FAIL: $Name ───"
        Write-DebugLog "  Тип: $errorType"
        Write-DebugLog "  Время: $($swTotal.ElapsedMilliseconds) мс"
        Write-DebugLog "  TTFB: $ttfbMs мс"
        $logEvidence = Test-AppLogEvidence -Path $AppLogPath -FromOffset $logOffset -EndpointName $Name -WaitMs $LogEvidenceWaitMs
        Write-DebugLog "  Ошибка: $errorMsg"
        Write-DebugLog "  LogEvidence: $($logEvidence.Evidence), BytesAdded=$($logEvidence.BytesAdded), Pattern=$($logEvidence.Pattern)"
        Write-DebugLog ""

        return @{
            Success     = $false
            ElapsedMs   = $swTotal.ElapsedMilliseconds
            TtfbMs      = $ttfbMs
            Error       = $errorMsg
            ErrorType   = $errorType
            StatusCode  = $null
            BodySnippet = ""
            LogEvidence = $logEvidence.Evidence
            LogBytesAdded = $logEvidence.BytesAdded
            LogPattern = $logEvidence.Pattern
            LogEndpointMatch = $logEvidence.EndpointMatch
        }
    }
}


# ── Источник логов приложения ───────────────────────────────

$resolvedAppLogPath = Resolve-AppLogPath -PathFromParam $AppLogPath -OutputDir $outputDir
$appLogEnabled = -not [string]::IsNullOrWhiteSpace($resolvedAppLogPath)

if ($appLogEnabled) {
    Write-DebugLog "AppLogPath: $resolvedAppLogPath"
    if (-not (Test-Path $resolvedAppLogPath)) {
        Write-DebugLog "AppLogPath указан, но файл пока не существует: $resolvedAppLogPath"
    }
} else {
    Write-DebugLog "AppLogPath не задан и app-*.log в docs/performance не найден. LogEvidence будет NO_LOG_PATH."
}

# ── Начало ──────────────────────────────────────────────────

Write-Host ""
Write-Host "=== History-DataMoex Performance v2 ===" -ForegroundColor Cyan
Write-Host "Endpoints: $($endpoints.Count), Iterations: $Iterations" -ForegroundColor Cyan
Write-Host "Timeout: $TimeoutSec сек" -ForegroundColor Cyan
Write-Host "Debug log: $debugLogPath" -ForegroundColor Cyan
if ($appLogEnabled) {
    Write-Host "App log evidence: $resolvedAppLogPath" -ForegroundColor Cyan
} else {
    Write-Host "App log evidence: NO_LOG_PATH — endpoint-ы будут замерены, но доказательство логов будет недоступно." -ForegroundColor Yellow
}

# Проверка сервера
try {
    $check = $httpClient.GetAsync("$BaseUrl/GetStockMarkets").GetAwaiter().GetResult()
    if ($check.IsSuccessStatusCode) {
        Write-Host "Сервер доступен." -ForegroundColor Green
    } else {
        Write-Host "Сервер ответил HTTP $([int]$check.StatusCode)" -ForegroundColor Red
    }
    $check.Dispose()
} catch {
    Write-Host "ОШИБКА: Сервер не отвечает на $BaseUrl" -ForegroundColor Red
    Write-Host "Запусти: dotnet run --project '.\History DataMoex.csproj'" -ForegroundColor Red
    $httpClient.Dispose()
    exit 1
}

# Процесс
$proc = Get-Process -Name "dotnet" -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $proc) { $proc = Get-Process -Name "History DataMoex" -ErrorAction SilentlyContinue | Select-Object -First 1 }
if ($proc) {
    Write-Host "PID=$($proc.Id), WorkingSet=$([math]::Round($proc.WorkingSet64/1MB,1)) MB" -ForegroundColor Green
    Write-DebugLog "Процесс PID=$($proc.Id), WorkingSet=$([math]::Round($proc.WorkingSet64/1MB,1)) MB"
}

# Машина
$os = Get-CimInstance Win32_OperatingSystem
$cpu = Get-CimInstance Win32_Processor | Select-Object -First 1
$ramGB = [math]::Round($os.TotalVisibleMemorySize / 1MB, 1)
$dotnetVer = (dotnet --version 2>$null) ?? "unknown"
$gitCommit = (git rev-parse --short HEAD 2>$null) ?? "unknown"
$gitBranch = (git branch --show-current 2>$null) ?? "unknown"

# ── Прогрев ─────────────────────────────────────────────────

if ($WarmupIterations -gt 0) {
    Write-Host "`nПрогрев ($WarmupIterations)..." -ForegroundColor Yellow

    for ($w = 1; $w -le $WarmupIterations; $w++) {
        foreach ($ep in $endpoints) {
            try {
                $warmResp = $httpClient.GetAsync("$BaseUrl$($ep.Url)").GetAwaiter().GetResult()
                $null = $warmResp.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult()
                Write-Host "  [w] $($ep.Name) OK ($([int]$warmResp.StatusCode))" -ForegroundColor DarkGray
                $warmResp.Dispose()
            }
            catch {
                Write-Host "  [w] $($ep.Name) FAIL" -ForegroundColor Red
                $warmErr = $_.Exception.InnerException?.Message ?? $_.Exception.Message
                Write-DebugLog "─── WARMUP FAIL: $($ep.Name) ───"
                Write-DebugLog "  Ошибка: $warmErr"
                Write-DebugLog ""
            }
        }
    }
}

# ── Замеры ──────────────────────────────────────────────────

Write-Host "`nЗамеры ($Iterations x $($endpoints.Count) endpoints)..." -ForegroundColor Yellow
Write-Host ""

$results = @{}
foreach ($ep in $endpoints) {
    $results[$ep.Name] = @{ Group = $ep.Group; Runs = @() }

    # GC перед каждым endpoint'ом — чтобы MemDelta был чистым
    [System.GC]::Collect()
    [System.GC]::WaitForPendingFinalizers()
    [System.GC]::Collect()
    Start-Sleep -Milliseconds 200

    for ($i = 1; $i -le $Iterations; $i++) {
        if ($i -gt 1) { Start-Sleep -Milliseconds 300 }
        $m = Measure-Endpoint -Url "$BaseUrl$($ep.Url)" -Name $ep.Name -Process $proc -AppLogPath $resolvedAppLogPath -LogEvidenceWaitMs $LogEvidenceWaitMs
        $results[$ep.Name].Runs += $m

        if ($m.Success) {
            $logMark = if ($m.LogEvidence -eq "OK") { "LOG OK" } else { "LOG $($m.LogEvidence)" }
            Write-Host "  [$i] $($ep.Name): $($m.ElapsedMs) ms (TTFB $($m.TtfbMs) ms), $($m.Rows) rows, $logMark" -ForegroundColor White
        } else {
            $shortErr = if ($m.ErrorType) { $m.ErrorType } else { "ERROR" }
            Write-Host "  [$i] $($ep.Name): FAIL ($shortErr, $($m.ElapsedMs) ms), LOG $($m.LogEvidence) — см. $debugLogPath" -ForegroundColor Red
        }
    }
}

# Финальный снимок
$finalSnap = Get-ProcessSnapshot -Process $proc

# ── Отчёт ───────────────────────────────────────────────────

$date = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
$report = "# Performance: $OutputName`n`n"
$report += "| Параметр | Значение |`n|---|---|`n"
$report += "| Дата | $date |`n"
$report += "| OS | $($os.Caption) |`n"
$report += "| CPU | $($cpu.Name.Trim()) |`n"
$report += "| RAM | $ramGB GB |`n"
$report += "| .NET | $dotnetVer |`n"
$report += "| Git | ``$gitBranch`` @ ``$gitCommit`` |`n"
$report += "| Endpoints | $($endpoints.Count) |`n"
$report += "| Итерации | $Iterations |`n"
$report += "| Timeout | $TimeoutSec сек |`n"
$report += "| AppLogPath | $(if ($appLogEnabled) { $resolvedAppLogPath } else { 'NO_LOG_PATH' }) |`n"
$report += "| LogEvidenceWait | $LogEvidenceWaitMs ms |`n`n"

# Сводка по группам
$groups = $endpoints | Select-Object -ExpandProperty Group -Unique
foreach ($group in $groups) {
    $groupEps = $endpoints | Where-Object { $_.Group -eq $group }

    $report += "## $group`n`n"
    $report += "| Endpoint | Медиана мс | TTFB мс | Мин | Макс | Строк | Байт | Дельта MB | Логи OK/Run | Последнее доказательство |`n"
    $report += "|---|---|---|---|---|---|---|---|---|---|`n"

    foreach ($ep in $groupEps) {
        $runs = $results[$ep.Name].Runs | Where-Object { $_.Success }
        if ($runs.Count -gt 0) {
            $times = @($runs | ForEach-Object { [double]$_.ElapsedMs })
            $ttfbs = @($runs | ForEach-Object { [double]$_.TtfbMs })
            $median = Get-Median -Values $times
            $ttfbMedian = Get-Median -Values $ttfbs
            $min = ($runs | Measure-Object -Property ElapsedMs -Minimum).Minimum
            $max = ($runs | Measure-Object -Property ElapsedMs -Maximum).Maximum
            $rows = $runs[0].Rows
            $bytes = $runs[0].ResponseBytes
            $deltas = ($runs | Where-Object { $_.MemDeltaMB -ne "-" } | ForEach-Object { $_.MemDeltaMB })
            $avgDelta = if ($deltas.Count -gt 0) { [math]::Round(($deltas | Measure-Object -Average).Average, 1) } else { "-" }

            # Форматируем байты читаемо
            $bytesStr = if ($bytes -ge 1MB) { "$([math]::Round($bytes / 1MB, 1)) MB" }
                        elseif ($bytes -ge 1KB) { "$([math]::Round($bytes / 1KB, 0)) KB" }
                        else { "$bytes B" }

            $allRunsForLog = $results[$ep.Name].Runs
            $logOk = ($allRunsForLog | Where-Object { $_.LogEvidence -eq "OK" }).Count
            $logLast = if ($allRunsForLog.Count -gt 0) { $allRunsForLog[-1].LogEvidence } else { "-" }
            $report += "| $($ep.Name) | $median | $ttfbMedian | $min | $max | $rows | $bytesStr | $avgDelta | $logOk/$($allRunsForLog.Count) | $logLast |`n"
        } else {
            $failType = if ($results[$ep.Name].Runs[0].ErrorType) { $results[$ep.Name].Runs[0].ErrorType } else { "ERROR" }
            $allRunsForLog = $results[$ep.Name].Runs
            $logOk = ($allRunsForLog | Where-Object { $_.LogEvidence -eq "OK" }).Count
            $logLast = if ($allRunsForLog.Count -gt 0) { $allRunsForLog[-1].LogEvidence } else { "-" }
            $report += "| $($ep.Name) | FAIL ($failType) | - | - | - | - | - | - | $logOk/$($allRunsForLog.Count) | $logLast |`n"
        }
    }
    $report += "`n"
}

# Детальные прогоны
$report += "## Детали по прогонам`n`n"
$report += "| Endpoint | # | мс | TTFB мс | Строк | Байт | Mem до MB | Mem после MB | Дельта MB | Логи | Log bytes | Pattern | Ошибка |`n"
$report += "|---|---|---|---|---|---|---|---|---|---|---|---|---|`n"

foreach ($ep in $endpoints) {
    $runNum = 0
    foreach ($run in $results[$ep.Name].Runs) {
        $runNum++
        if ($run.Success) {
            $report += "| $($ep.Name) | $runNum | $($run.ElapsedMs) | $($run.TtfbMs) | $($run.Rows) | $($run.ResponseBytes) | $($run.MemBeforeMB) | $($run.MemAfterMB) | $($run.MemDeltaMB) | $($run.LogEvidence) | $($run.LogBytesAdded) | $($run.LogPattern) | - |`n"
        } else {
            $errShort = if ($run.ErrorType) { $run.ErrorType } else { "ERROR" }
            $report += "| $($ep.Name) | $runNum | $($run.ElapsedMs) | $($run.TtfbMs) | - | - | - | - | - | $($run.LogEvidence) | $($run.LogBytesAdded) | $($run.LogPattern) | $errShort |`n"
        }
    }
}


# Доказательство логов приложения
$allMeasuredRuns = @()
foreach ($ep in $endpoints) {
    $allMeasuredRuns += $results[$ep.Name].Runs
}
$logTotal = $allMeasuredRuns.Count
$logOkTotal = ($allMeasuredRuns | Where-Object { $_.LogEvidence -eq "OK" }).Count
$logBytesTotal = 0
foreach ($run in $allMeasuredRuns) {
    if ($run.LogBytesAdded -is [int] -or $run.LogBytesAdded -is [long]) {
        $logBytesTotal += [int64]$run.LogBytesAdded
    }
}

$report += "`n## Доказательство записи логов приложения`n`n"
$report += "| Метрика | Значение |`n|---|---|`n"
$report += "| Проверяемый файл логов | $(if ($appLogEnabled) { $resolvedAppLogPath } else { 'NO_LOG_PATH' }) |`n"
$report += "| Проверок логов | $logTotal |`n"
$report += "| LOG OK | $logOkTotal/$logTotal |`n"
$report += "| Новых байт в логах во время замеров | $logBytesTotal |`n"
$report += "| Правило доказательства | После HTTP-вызова endpoint-а файл логов должен увеличиться и новый фрагмент должен содержать один из MOEX-паттернов. |`n"

$evidenceGroups = $allMeasuredRuns | Group-Object -Property LogEvidence | Sort-Object Name
$report += "`n| Evidence | Count |`n|---|---|`n"
foreach ($g in $evidenceGroups) {
    $report += "| $($g.Name) | $($g.Count) |`n"
}
$report += "`n"

if ($finalSnap) {
    $report += "`n## Процесс после замеров`n`n"
    $report += "| Метрика | Значение |`n|---|---|`n"
    $report += "| Working Set | $($finalSnap.WorkingSetMB) MB |`n"
    $report += "| Private | $($finalSnap.PrivateMemoryMB) MB |`n"
    $report += "| Peak Working Set | $($finalSnap.PeakWorkingSetMB) MB |`n"
    $report += "| Handles | $($finalSnap.HandleCount) |`n"
    $report += "| Threads | $($finalSnap.ThreadCount) |`n"
    $report += "| Total CPU | $($finalSnap.TotalProcessorSec) сек |`n"
}

# Запись
$outputPath = Join-Path $outputDir "$OutputName.md"
$report | Out-File -FilePath $outputPath -Encoding utf8

# Cleanup
$httpClient.Dispose()

# ── Итого ───────────────────────────────────────────────────

$totalFails = 0
foreach ($ep in $endpoints) {
    $fails = ($results[$ep.Name].Runs | Where-Object { -not $_.Success }).Count
    $totalFails += $fails
}

Write-Host ""
Write-Host "=== Отчёт: $outputPath ===" -ForegroundColor Green
Write-Host "Endpoints: $($endpoints.Count), итераций: $Iterations" -ForegroundColor Cyan
Write-Host "Log evidence: $logOkTotal/$logTotal OK" -ForegroundColor Cyan
if ($totalFails -gt 0) {
    Write-Host "FAIL-ов: $totalFails — подробности в $debugLogPath" -ForegroundColor Red
}
Write-Host ""

# Краткая сводка
foreach ($group in $groups) {
    Write-Host "  $group" -ForegroundColor Yellow
    $groupEps = $endpoints | Where-Object { $_.Group -eq $group }
    foreach ($ep in $groupEps) {
        $runs = $results[$ep.Name].Runs | Where-Object { $_.Success }
        if ($runs.Count -gt 0) {
            $times = @($runs | ForEach-Object { [double]$_.ElapsedMs })
            $ttfbs = @($runs | ForEach-Object { [double]$_.TtfbMs })
            $median = Get-Median -Values $times
            $ttfbMedian = Get-Median -Values $ttfbs
            $rows = $runs[0].Rows
            $bytes = $runs[0].ResponseBytes
            $bytesStr = if ($bytes -ge 1MB) { "$([math]::Round($bytes / 1MB, 1)) MB" }
                        elseif ($bytes -ge 1KB) { "$([math]::Round($bytes / 1KB, 0)) KB" }
                        else { "$bytes B" }
            $logOk = ($results[$ep.Name].Runs | Where-Object { $_.LogEvidence -eq "OK" }).Count
            Write-Host "    $($ep.Name): ~$median ms (TTFB $ttfbMedian ms), $rows rows, $bytesStr, LOG $logOk/$($results[$ep.Name].Runs.Count)"
        } else {
            $failType = if ($results[$ep.Name].Runs[0].ErrorType) { $results[$ep.Name].Runs[0].ErrorType } else { "ERROR" }
            $logOk = ($results[$ep.Name].Runs | Where-Object { $_.LogEvidence -eq "OK" }).Count
            Write-Host "    $($ep.Name): FAIL ($failType), LOG $logOk/$($results[$ep.Name].Runs.Count)" -ForegroundColor Red
        }
    }
}
Write-Host ""
