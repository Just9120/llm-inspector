# Windows validation: подготовка Evidence

Это рабочая процедура GOAL-007, не новый product contract. Обязательные AC, reference, бюджеты и длительности находятся в [project-spec §3.1](../project-spec.md#31-утверждённые-performance-profiles); текущие результаты и blockers — в [delivery-plan](../delivery-plan.md). Final tag/release/WinGet не создаются этой процедурой.

## Границы проверки

- До запуска записать exact source SHA, SHA-256 candidate, edition/build/architecture Windows, runtime/model identity и actual profile. Один source SHA не доказывает одинаковые binaries на разных машинах.
- Не закрывать чужие apps/backend, не менять privacy/remote settings, не очищать user history и не устанавливать runtime/model скрыто. Изменяющие state ручные сценарии выполняются отдельно, с известными target и подтверждением.
- Для проверки GUI нужно разрешённое управление интерфейсом; Escape/остановка пользователем прекращает UI automation. Read-only счётчики не возобновляют UI walkthrough.
- Заранее определить foreign-load/OS-update/antivirus/thermal contamination signals. Нельзя исключать неудачный run после просмотра результатов без такого основания.
- Отчёты сохраняются в новом локальном файле под ignored `artifacts/`. Не публиковать raw environment, command lines, user content, secrets или полный пользовательский путь. Sanitized checkpoint содержит hash, время, охват и ограничения.

## Read-only process-tree capture

`internal/cmd/validation` — отдельная Windows CLI, **не входит в desktop executable**. Она присоединяется к уже работающему exact PID, проверяет executable file identity и SHA-256, держит query/synchronize handles и снимает счётчики root/обнаруженных descendants. Приложение не запускает и не останавливает, settings/SQLite не читает и не меняет.

Из root с pinned Go и readonly modules (см. README), под тем же обычным Windows user:

```powershell
$env:GOTOOLCHAIN = 'local'
$env:GOFLAGS = '-mod=readonly'
$candidate = (Resolve-Path -LiteralPath '.\build\bin\LlmInspector.exe').Path
$candidateHash = (Get-FileHash -LiteralPath $candidate -Algorithm SHA256).Hash.ToLowerInvariant()
# Взять из записи сборки этого candidate, не подставлять текущий HEAD автоматически.
$candidateSource = '<EXACT_40_HEX_CANDIDATE_SOURCE_SHA>'
$matches = @(Get-Process -Name LlmInspector -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -ieq $candidate })
if ($matches.Count -ne 1) { throw 'Select one already running exact candidate.' }
$captureDirectory = Join-Path (Get-Location).Path 'artifacts\windows-validation'
$null = New-Item -ItemType Directory -Path $captureDirectory -Force
$output = Join-Path $captureDirectory ('pilot-' + [guid]::NewGuid().ToString('N') + '.json')
go run ./internal/cmd/validation "--pid=$($matches[0].Id)" "--exe=$candidate" `
    "--sha256=$candidateHash" "--source-sha=$candidateSource" `
    --profile=balanced --phase=pilot --duration=30s --interval=1s "--output=$output"
$captureExit = $LASTEXITCODE
if (Test-Path -LiteralPath $output) {
    $report = Get-Content -LiteralPath $output -Raw | ConvertFrom-Json
    $report | Select-Object terminal_status, release_evidence, summary
    Get-FileHash -LiteralPath $output -Algorithm SHA256
}
if ($captureExit -ne 0) { throw "Capture did not complete successfully (exit $captureExit)." }
```

Проверить, что drive и parent directories действительно local: lexical path guard отклоняет explicit UNC/device/ADS paths, но не доказывает локальность mapped drive/junction. Не использовать network directory. Existing output никогда не перезаписывается. После interruption сначала проверить existing process/file; incomplete JSON — не Evidence и не основание повторять запись в тот же файл.

Параметры: `profile=saver|balanced|detailed`, `phase=pilot|idle|active`; interval `200ms..10s`, duration от interval до `2h`. Это параметры **сборщика**, а не изменение profile/sampling приложения. `source-sha`, profile/phase — declarations; соответствие сборке и реальному UI проверяется отдельно. Exit `0` — capture завершён и наблюдённые samples валидны; `1` — input/identity/output error; `2` — cancellation/counter failure/invalid summary. Во всех случаях `release_evidence=false`.

### Семантика и ограничения

| Поле | Что действительно измеряется |
|---|---|
| CPU | Delta kernel+user CPU time / фактический interval / logical CPU count; mean взвешен временем, P95 nearest-rank по observed intervals |
| Private bytes | Private committed memory живых обнаруженных процессов; P95, end-minus-start growth |
| I/O writes | Generic process `WriteTransferCount`, **не disk-only bytes**, не wakeups |
| Live / retained | Число живых / всех обнаруженных процессов; handles завершившихся children удерживаются, чтобы сохранить terminal CPU/I/O |
| UTC / elapsed / probe | Время наблюдения, monotonic elapsed и длительность probe; нужны для выявления gaps/observer overhead |

Семантика Windows: [GetProcessTimes](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-getprocesstimes), [PROCESS_MEMORY_COUNTERS_EX](https://learn.microsoft.com/en-us/windows/win32/api/psapi/ns-psapi-process_memory_counters_ex), [IO_COUNTERS](https://learn.microsoft.com/en-us/windows/win32/api/winnt/ns-winnt-io_counters). Working set не заменяет private commit, generic I/O не заменяет физический диск.

Snapshot discovery может пропустить child, который появился и завершился между probes. Полный process-tree охват требует отдельного event-level Evidence. Slow probe/scheduling может пропустить tick: `observed_samples_valid` не доказывает отсутствие gaps или выполнение canonical duration. Raw timestamps сохраняются; unavailable/reset/error не превращаются в нулевой успешный показатель. Collector не измеряет GPU/VRAM, disk-only attribution, wakeups, paired throughput или contamination.

**Даже час такого capture не закрывает E12.** Для каждого built-in profile нужны все canonical gates: idle после 10 min warm-up измеряется 1 h; active RAM growth — 30 min; минимум 5 paired AB/BA repetitions на immutable corpus, отдельные reliable GPU/VRAM/disk/wakeup sources. Mandatory unavailable означает незавершённый gate. Не ослаблять budgets для нового UI и не заменять measured Evidence synthetic evaluator tests.

## Остальные обязательные gates

- **E01-AC01:** тот же exact artifact на Windows 11 25H2 Home и Pro x64, весь [manual Windows release gate](windows-release.md#manual-windows-gate). Local native smoke не является проверкой Home или SmartScreen.
- **B02:** реальные независимые host/client/backend и private authenticated encrypted transport; выполнить [LIVE checklist](secure-remote-access.md). Localhost fixtures или один ПК не подтверждают two-host flow. Tailscale install/login/ACL/Serve выполняет owner; token не включать в отчёт.
- **Client/backend compatibility:** записать exact versions, endpoint, source/artifact и сценарий. Новый Ollama reference `0.33.3` не переносит автоматически historical lifecycle `VERIFIED` с `0.33.2`.
- **B03/B04:** Windows-only Goal не закрывает Linux/macOS AC; новый protocol не выбирается без конкретного owner demand. Эти решения не подменяются тестированием уже существующих OpenAI-compatible client routes.

После каждой проверки обновлять только operational Evidence/checkpoint и соответствующий AC credit, если весь критерий подтверждён. Не переносить результат на другой hash, недоступную ОС, protocol или runtime version.
