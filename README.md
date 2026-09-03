# LLM Inspector

LLM Inspector — Windows-first desktop-приложение для локального мониторинга, аналитики и диагностики работы LLM. Согласованный продуктовый scope и acceptance criteria находятся в `docs/project-spec.md`.

## Текущее состояние

Проект имеет repository/CI foundation, merged privacy/proxy foundation, готовые `EPIC-02`/`EPIC-03`, partial `EPIC-04` и локально завершённый кандидат `EPIC-08`:

- solution содержит девять production boundaries и шесть test projects из `docs/architecture.md`;
- Avalonia application запускает embedded Kestrel proxy на `http://127.0.0.1:5117`; доступны Ollama (`:11434`), llama.cpp (`:8080`) и LM Studio (`:1234`) adapters с безопасным override literal-loopback URL;
- proxy поддерживает transparent `GET /v1/models` и non-streaming/streaming/tool-calling `POST /v1/chat/completions`, не следует redirects и propagates cancellation;
- bounded streaming parser извлекает только allowlisted `model`, OpenAI token usage/details и документированные llama.cpp `timings`; response/reasoning strings не декодируются в telemetry, отсутствующие или недостоверные metrics имеют `unavailable`;
- UI показывает gateway/backend state, generic и per-client base URLs, все active requests с одной текущей stage и qualified elapsed/progress/ETA, а также latest request tokens/context/timings с quality state; streaming TTFT считается только по первому непустому content delta, non-streaming/tool-only TTFT остаётся `unavailable`;
- SQLite WAL в `%LOCALAPPDATA%\LLM Inspector\data\inspector.db` сохраняет только allowlisted technical metadata через bounded non-blocking writer; UI предоставляет history filters, operation detail, daily aggregates, comparisons, retention и explicit clear preview/confirmation;
- выбран design stack: C# / `.NET 10 LTS`, Avalonia UI, embedded loopback-only Kestrel proxy и SQLite WAL;
- initial support matrix: Windows 11 `25H2` Home/Pro, `x64`, с актуальным cumulative update;
- SDK зафиксирован exact version `10.0.400`, NuGet packages — через Central Package Management, 15 normal и 9 `win-x64` committed lock files;
- для EPIC-09 core, EPIC-02, EPIC-03 и EPIC-04 подтверждены PR/main CI; EPIC-04 выполняет `10/12`, а `E04-AC03`/`E04-AC12` ждут trustworthy session/model-load evidence; EPIC-08 локально выполняет `18/18`, но не получает `READY` до PR/main CI;
- product contract ратифицирован: initial release содержит 139 atomic AC, полный согласованный roadmap — 164 AC;
- PR/`main` CI определён на ephemeral GitHub-hosted `windows-2025` runner с read-only token и SHA-pinned actions; фактический run Evidence см. в `docs/delivery-plan.md`;
- server/runtime CD явно не используется: приложение устанавливается на Windows PC, а не deploy-ится на runtime host.

Текущая локальная readiness по независимо проверенным atomic AC: `70/139 = 50.4%` initial release и `70/164 = 42.7%` full roadmap. `EPIC-02` имеет `READY 15/15`, `EPIC-03` — `READY 13/13`; EPIC-04 остаётся `10/12`; EPIC-08 выполняет `18/18` локально, а его negative SQLite schema/canary tests закрывают последний product AC EPIC-09. CI Evidence нового инкремента появится только после PR.

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

Техническое наблюдение сохраняется локально с default retention `30 days`; доступны точные варианты `7 days`, `30 days`, `90 days`, `indefinite`. При старте и после изменения setting применяется bounded oldest-first cleanup. Raw request/response/reasoning/tool content не сохраняется, не индексируется и не логируется; negative runtime canary test проверяет основной DB/WAL surface.

History filters принимают period, client, backend, model, session GUID, status и error type. Comparison dimension выбирается из period/model/backend/client; для period используется формат `<ISO-8601 start>..<ISO-8601 end>`. Aggregates показывают arithmetic mean, median и nearest-rank P95; минимум статистической достаточности — `3` samples. Manual clear выполняется только после preview exact UTC scope и отдельного confirmation.

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
- Repository/CI foundation merged через [PR #2](https://github.com/Just9120/llm-inspector/pull/2); EPIC-09 core — через [PR #3](https://github.com/Just9120/llm-inspector/pull/3); EPIC-02 — через [PR #4](https://github.com/Just9120/llm-inspector/pull/4), а его CI stabilization — через [PR #5](https://github.com/Just9120/llm-inspector/pull/5) в verified commit `e1e1b735116b94d73fa87559da8759c5f58d243c`.
