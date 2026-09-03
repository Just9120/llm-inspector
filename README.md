# LLM Inspector

LLM Inspector — Windows-first desktop-приложение для локального мониторинга, аналитики и диагностики работы LLM. Согласованный продуктовый scope и acceptance criteria находятся в `docs/project-spec.md`.

## Текущее состояние

Проект имеет repository/CI foundation, десять завершённых core epics и активную `GOAL-005` для всех оставшихся canonical AC:

- solution содержит девять production boundaries и шесть test projects из `docs/architecture.md`;
- Avalonia application запускает embedded Kestrel proxy на `http://127.0.0.1:5117`; доступны Ollama (`:11434`), llama.cpp (`:8080`) и LM Studio (`:1234`) adapters с безопасным override literal-loopback URL;
- proxy поддерживает transparent `GET /v1/models`, non-streaming/streaming/tool-calling `POST /v1/chat/completions` и, только при выбранном LM Studio, native `POST /api/v1/chat`; он не следует redirects и propagates cancellation;
- bounded streaming parser извлекает только allowlisted `model`, OpenAI token usage/details, документированные llama.cpp `timings` и LM Studio native `stats`/`model_load.*`; response/reasoning strings не декодируются в telemetry, отсутствующие или недостоверные metrics имеют `unavailable`;
- UI показывает gateway/backend state, generic и per-client base URLs, все active requests с одной текущей stage и qualified elapsed/progress/ETA, latest request tokens/context/timings с quality state и content-free diagnostics; versioned rules отделяют `FACT`, `HYPOTHESIS` и `INSUFFICIENT_DATA`, а stall не объявляется без typed backend signal; streaming TTFT считается только по первому непустому content delta, non-streaming/tool-only TTFT остаётся `unavailable`;
- SQLite WAL schema v5 в `%LOCALAPPDATA%\LLM Inspector\data\inspector.db` сохраняет только allowlisted technical metadata через bounded non-blocking writer; request-correlated resource timeline включает host CPU/RAM, exact process CPU/RAM/disk counters при доказанной listener ownership, gateway traffic и NVIDIA GPU/driver/VRAM/temperature/power либо явные `unavailable`; history хранит typed error origin и available version/runtime configuration facts, а analytics сопоставляет достаточно представительные configuration cohorts; startup выполняет integrity check, normal restart и process-kill recovery покрыты integration test;
- Windows background runtime продолжает proxy/history monitoring после скрытия main window, предоставляет native tray, per-user autostart и четыре independently configurable content-free notification events с silent mode и versioned anti-spam policy;
- локальный `diagnostic-snapshot-v1` создаётся по выбранному UTC range или operation: user сначала просматривает exact allowlist JSON и SHA-256, затем сохраняет тот же preview; upload path отсутствует;
- локальный `analytics-export-v1` экспортирует выбранный UTC range anonymized technical history и раздельные request/resource aggregates (`n`, mean, median, P95); oversized range отклоняется без неполного export, exact JSON preview обязателен до сохранения и проходит тот же negative content corpus;
- выбран design stack: C# / `.NET 10 LTS`, Avalonia UI, embedded loopback-only Kestrel proxy и SQLite WAL;
- initial support matrix: Windows 11 `25H2` Home/Pro, `x64`, с актуальным cumulative update;
- SDK зафиксирован exact version `10.0.400`, NuGet packages — через Central Package Management, 15 normal и 9 `win-x64` committed lock files;
- EPIC-02/03/04/05/06/07/08/09/10/11 имеют terminal PR/main CI; EPIC-12 доставлен partial `7/13`, EPIC-01 — partial `3/4`, а их release/performance blockers остаются без кредита;
- product contract ратифицирован: initial release содержит 139 atomic AC, полный согласованный roadmap — 164 AC;
- PR/`main` CI определён на ephemeral GitHub-hosted `windows-2025` runner с read-only token и SHA-pinned actions; фактический run Evidence см. в `docs/delivery-plan.md`;
- server/runtime CD явно не используется: приложение устанавливается на Windows PC, а не deploy-ится на runtime host.

Текущая readiness baseline на merged `main` `7c5528ec3c33396ce1068162fc0b6961a0dfe553`: `132/139 = 95.0%` initial release и `132/164 = 80.5%` full roadmap. Active BACKLOG-06 candidate выполняет `B06-AC01..03`, поэтому локальный независимый расчёт составляет `132/139 = 95.0%` и `135/164 = 82.3%`; полный local CI-equivalent прошёл `210/210` tests, exact-revision CI остаётся следующим gate. `E12-AC01..06` не кредитуются до утверждения numeric budgets и frozen benchmark fixtures.

## Быстрый старт

Prerequisite: Windows x64 и exact [.NET SDK `10.0.400`](https://dotnet.microsoft.com/en-us/download/dotnet/10.0). `global.json` запрещает автоматический roll-forward на другой SDK.

Проверить SDK и восстановить dependency graph:

```powershell
dotnet --version
dotnet restore LlmInspector.slnx --locked-mode
```

Первая команда должна вывести `10.0.400`. Запустить development build:

```powershell
dotnet run --project src/LlmInspector.App/LlmInspector.App.csproj --no-restore
```

Default backend — Ollama на `http://127.0.0.1:11434`. Выбор adapter и безопасного local endpoint выполняется launch options:

```powershell
dotnet run --project src/LlmInspector.App/LlmInspector.App.csproj --no-restore -- --backend=llama-cpp
dotnet run --project src/LlmInspector.App/LlmInspector.App.csproj --no-restore -- --backend=lm-studio --backend-url=http://127.0.0.1:4321/ --listener-port=5118
```

Versioned launch configuration v1 принимает `--backend=ollama|llama-cpp|lm-studio`, `--backend-url=http[s]://<literal-loopback>:<port>/` и `--listener-port=1..65535`. Remote host, credentials/path/query/fragment в backend URL, duplicate и неизвестные options fail closed без вывода исходного значения.

Для attribution настройте штатный OpenAI-compatible `baseURL` клиента:

| Client | Base URL по умолчанию |
|---|---|
| Generic/Unknown | `http://127.0.0.1:5117/v1` |
| OpenCode Desktop | `http://127.0.0.1:5117/clients/opencode/v1` |
| Hermes Desktop | `http://127.0.0.1:5117/clients/hermes/v1` |
| Cline | `http://127.0.0.1:5117/clients/cline/v1` |
| Open WebUI | `http://127.0.0.1:5117/clients/open-webui/v1` |

Это explicit endpoint attribution, а не process guessing: запросы через generic URL всегда остаются `Generic/Unknown`. Все base paths поддерживают `GET /models` и `POST /chat/completions`; исходный backend видит стандартные `/v1/models` и `/v1/chat/completions`. Возможность штатно менять base URL подтверждена документацией [OpenCode](https://dev.opencode.ai/docs/providers), [Hermes](https://github.com/hermes-agent-org/hermes/blob/main/website/docs/integrations/providers.md), [Cline](https://github.com/cline/cline/blob/main/apps/vscode/webview-ui/src/components/settings/providers/OpenAICompatible.tsx) и [Open WebUI](https://github.com/open-webui/open-webui/blob/main/backend/open_webui/routers/openai.py).

При `--backend=lm-studio` gateway дополнительно открывает generic `http://127.0.0.1:5117/api/v1/chat` и прозрачно передаёт одноимённый native LM Studio flow. Полные terminal `stats` и optional `model_load_time_seconds` либо streaming `model_load.start/end` дают exact cold/warm evidence; неполный или противоречивый lifecycle остаётся `unavailable`.

Опциональная cross-turn correlation включается только полным набором Inspector-reserved headers: `X-LLM-Inspector-Session-Id`, `X-LLM-Inspector-Turn-Id` (оба — non-empty GUID в 32-hex `N` format) и положительный `X-LLM-Inspector-Turn-Sequence`. Изменение context size рассчитывается только для соседних sequence одной session; первый, duplicate, gap, out-of-order или неполный набор отображается как `unavailable`, без time-based guessing.

Agent operation grouping дополнительно требует `X-LLM-Inspector-Operation-Id` с non-empty GUID в том же `N` format и начинается с turn sequence `1`. Только строго соседние turns одной session/client/backend входят в operation; malformed, duplicate, gap, out-of-order или несовпадающие metadata остаются ungrouped. Gateway boundedly извлекает из OpenAI-compatible JSON/SSE только available/invoked tool counts, normalized tool names и finish state; arguments/results/final content не сохраняются. Все четыре Inspector headers удаляются до forwarding к backend.

Техническое наблюдение сохраняется локально с default retention `30 days`; доступны точные варианты `7 days`, `30 days`, `90 days`, `indefinite`. При старте и после изменения setting применяется bounded oldest-first cleanup. Raw request/response/reasoning/tool content не сохраняется, не индексируется и не логируется; negative runtime canary test проверяет основной DB/WAL surface.

History filters принимают period, client, backend, model, session GUID, status и error type. Typed error model различает connection refused, model loading/503, HTTP/API error, timeout, context overflow, cancellation и backend crash/disconnect; отдельный origin принимает только `Inspector`, `Client`, `Backend`, `Model`, `Unknown` или `NotApplicable`, и arbitrary error body не сохраняется. UI помечает единичный failure и recurring group (`>=2` occurrences), а period comparison показывает per-type частоту с точным denominator и delta в percentage points. Correlation подтверждается только explicit operation/session либо version/runtime configuration facts; близость времени сама по себе не считается доказательством. Runtime comparison использует earliest/latest distinct configuration cohorts и объявляет regression только при `n >= 3` с обеих сторон, иначе явно показывает `insufficient correlation data`. Manual clear выполняется только после preview exact UTC scope и отдельного confirmation.

## Проверки и сборка

```powershell
dotnet format LlmInspector.slnx --verify-no-changes --no-restore
dotnet build LlmInspector.slnx -c Release --no-restore
dotnet test LlmInspector.slnx -c Release --no-build --logger "console;verbosity=minimal"
dotnet restore src/LlmInspector.App/LlmInspector.App.csproj --locked-mode -r win-x64
dotnet publish src/LlmInspector.App/LlmInspector.App.csproj -c Release -r win-x64 --self-contained true --no-restore -o artifacts/win-x64
.\artifacts\win-x64\LlmInspector.App.exe --smoke-test
```

`artifacts/` — локальный disposable output и не коммитится. CI выполняет ту же последовательность в [`.github/workflows/ci.yml`](.github/workflows/ci.yml), не публикует artifacts и не содержит deployment steps.

Self-contained `win-x64` publish из этой команды является validation build, а не поддерживаемым release package. Clean install, upgrade, launch и другие release-matrix checks на Windows 11 25H2 Home/Pro требуют отдельного release gate; signing identity и distribution channel пока не утверждены.

## Навигация

- [`AGENTS.md`](AGENTS.md) — главный repository router, authority model и execution boundaries.
- [`docs/project-spec.md`](docs/project-spec.md) — ратифицированный canonical product contract с initial release/backlog epics, atomic AC, readiness и Evidence.
- [`docs/delivery-plan.md`](docs/delivery-plan.md) — текущий delivery dashboard, readiness snapshots, blockers и candidate next Goal.
- [`docs/delivery-plan-archive.md`](docs/delivery-plan-archive.md) — исторический архив; не используется для текущей readiness.
- [`docs/ci-cd-rules.md`](docs/ci-cd-rules.md) — обязательный CI/CD и production safety contract.
- [`docs/architecture.md`](docs/architecture.md) — выбранный stack, runtime/data/privacy boundaries, backend capability matrix, test strategy и Windows release design.
- [Upstream requirements](https://docs.google.com/document/d/1r4o0UiJohJf34j3nL56LWnOxGRi7WDC3jqoDIIjDTnA/edit) — provenance source ратифицированных требований; текущим source of truth остаётся `docs/project-spec.md`.

Optional contracts для `Context Bundle Builder`, AI delivery infrastructure и runbooks не созданы: соответствующие workstreams и operations отсутствуют.

## Рабочий процесс

1. Перед работой прочитать root и применимые nested `AGENTS.md` / `AGENTS.override.md`.
2. Для product scope использовать только ратифицированную нормативную часть `docs/project-spec.md`.
3. Реализацию начинать только по explicit user instruction либо approved Current Goal в `docs/delivery-plan.md`.
4. Для CI/CD, secrets, deployment, production или migrations обязательно применять `docs/ci-cd-rules.md`.
5. Проценты готовности выводить только из выполненных atomic acceptance criteria с явным denominator и подтверждённым Evidence.

## Repository

- GitHub: <https://github.com/Just9120/llm-inspector>
- Ожидаемая production/default branch: `main`.
- На baseline-аудите `2026-09-02` remote repository был пуст; initial documentation bootstrap создал `main`.
- Repository/CI foundation merged через [PR #2](https://github.com/Just9120/llm-inspector/pull/2); EPIC-09 core — через PR #3; EPIC-02 — через PR #4/#5; EPIC-03 — через PR #6; EPIC-04 — через PR #7/#9; EPIC-08 — через PR #8; EPIC-01 partial — через PR #10; EPIC-05 — через PR #11; EPIC-06 — через PR #12; EPIC-07 — через PR #13; EPIC-10 — через PR #14; EPIC-11 — через PR #15; EPIC-12 partial — через PR #16 в verified `main` commit `7c5528ec3c33396ce1068162fc0b6961a0dfe553`. BACKLOG-06 candidate находится в отдельной локальной ветке.
