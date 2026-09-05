# LLM Inspector

LLM Inspector — Windows-first desktop-приложение для локального мониторинга, аналитики и диагностики работы LLM. Согласованный продуктовый scope и acceptance criteria находятся в `docs/project-spec.md`.

## Текущее состояние

Активна `GOAL-006`: перенос на Go + Wails v2 + Svelte/TypeScript и русский современный UI. Текущий pipeline и readiness — в [delivery-plan.md](docs/delivery-plan.md). Ниже описана C# reference implementation до cutover; её Evidence не подтверждает Go runtime. Manual/runtime validation остаётся отдельной будущей Goal:

- solution содержит девять production boundaries и шесть test projects из `docs/architecture.md`;
- Avalonia application запускает embedded Kestrel proxy на `http://127.0.0.1:5117`; доступны Ollama (`:11434`), llama.cpp (`:8080`) и LM Studio (`:1234`) adapters с безопасным override literal-loopback URL;
- proxy поддерживает transparent `GET /v1/models`, non-streaming/streaming/tool-calling `POST /v1/chat/completions` и, только при выбранном LM Studio, native `POST /api/v1/chat`; он не следует redirects и propagates cancellation;
- bounded streaming parser извлекает только allowlisted `model`, OpenAI token usage/details, документированные llama.cpp `timings` и LM Studio native `stats`/`model_load.*`; response/reasoning strings не декодируются в telemetry, отсутствующие или недостоверные metrics имеют `unavailable`;
- UI показывает gateway/backend state, generic и per-client base URLs, все active requests с одной текущей stage и qualified elapsed/progress/ETA, latest request tokens/context/timings с quality state и content-free diagnostics; versioned rules отделяют `FACT`, `HYPOTHESIS` и `INSUFFICIENT_DATA`, а stall не объявляется без typed backend signal; streaming TTFT считается только по первому непустому content delta, non-streaming/tool-only TTFT остаётся `unavailable`;
- SQLite WAL schema v5 в `%LOCALAPPDATA%\LLM Inspector\data\inspector.db` сохраняет только allowlisted technical metadata через bounded non-blocking writer; request-correlated resource timeline включает host CPU/RAM, exact process CPU/RAM/disk counters при доказанной listener ownership, gateway traffic и NVIDIA GPU/driver/VRAM/temperature/power либо явные `unavailable`; history хранит typed error origin и available version/runtime configuration facts, а analytics сопоставляет достаточно представительные configuration cohorts; startup выполняет integrity check, normal restart и process-kill recovery покрыты integration test;
- Windows background runtime продолжает proxy/history monitoring после скрытия main window, предоставляет native tray, per-user autostart и четыре independently configurable content-free notification events с silent mode и versioned anti-spam policy;
- локальный `diagnostic-snapshot-v1` создаётся по выбранному UTC range или operation: user сначала просматривает exact allowlist JSON и SHA-256, затем сохраняет тот же preview; upload path отсутствует;
- локальный `analytics-export-v1` экспортирует выбранный UTC range anonymized technical history и раздельные request/resource aggregates (`n`, mean, median, P95); oversized range отклоняется без неполного export, exact JSON preview обязателен до сохранения и проходит тот же negative content corpus;
- supported NVIDIA GPUs обнаруживаются списком до 16 distinct devices; live/history показывают device-wide metrics раздельно, а workload-to-device attribution честно остаётся `unavailable` без достоверного source;
- monitoring sampling настраивается через user-friendly профили `Бережный` (`2 s`), `Сбалансированный` (`1 s`, default/recommended), `Детальный` (`500 ms`) и validated `Свой профиль` (`250 ms`–`10 s`); custom profile явно не является release-performance Evidence;
- B01 предоставляет user-confirmed lifecycle для Inspector-owned Ollama, llama.cpp и LM Studio processes: start/stop/restart, model load и только typed allowlisted parameters; чужой listener остаётся observation-only, а crash требует ручного restart;
- B02 implementation сохраняет loopback-only listener, добавляет default-off Tailscale Serve ingress с DPAPI CurrentUser-protected 256-bit bearer token, explicit `HTTPS *.ts.net` remote backend, отдельные availability/DNS+TCP connect metrics и typed `unavailable` вместо local resource attribution; actual encrypted two-host LIVE Evidence ещё отсутствует;
- выбран design stack: C# / `.NET 10 LTS`, Avalonia UI, embedded loopback-only Kestrel proxy и SQLite WAL;
- initial support matrix: Windows 11 `25H2` Home/Pro, `x64`, с актуальным cumulative update;
- SDK зафиксирован exact version `10.0.400`, development `VersionPrefix` — `1.0.0`, NuGet packages — через Central Package Management, 15 normal и 9 `win-x64` committed lock files;
- EPIC-02/03/04/05/06/07/08/09/10/11, BACKLOG-01/05/06 имеют terminal PR/main CI; EPIC-12 profiles/harness доставлены, но эпик остаётся partial `7/13` до controlled measurements; EPIC-01 — partial `3/4`: public `v1.0.0-rc.3` проходит release pipeline и доступные Pro exact-artifact flows, но Windows Home/full matrix ещё pending; B02 code/CI завершены, но two-host LIVE gate отсутствует;
- product contract ратифицирован: initial release содержит 139 atomic AC, полный согласованный roadmap — 164 AC;
- PR/`main` CI определён на ephemeral GitHub-hosted `windows-2025` runner с read-only token и SHA-pinned actions; фактический run Evidence см. в `docs/delivery-plan.md`;
- server/runtime CD явно не используется: приложение устанавливается на Windows PC, а не deploy-ится на runtime host.

Verified product base `main` — `74f710ab1f7a9457377045191b0f62a472e8f40c`: initial release `132/139 = 95.0%`, full roadmap `152/164 = 92.7%`. BACKLOG-02 доставлен через [PR #24](https://github.com/Just9120/llm-inspector/pull/24), tray forward fix — через [PR #25](https://github.com/Just9120/llm-inspector/pull/25), deterministic CI fixture fix — через [PR #27](https://github.com/Just9120/llm-inspector/pull/27), terminal reconciliation — через [PR #29](https://github.com/Just9120/llm-inspector/pull/29), single-main/final-only policy — через [PR #30](https://github.com/Just9120/llm-inspector/pull/30). Exact-main CI `33850992022` успешен (`260/260`). B02 не READY без обязательного encrypted two-host LIVE test.

Важно: exact public [`v1.0.0-rc.3`](https://github.com/Just9120/llm-inspector/releases/tag/v1.0.0-rc.3) — immutable historical prerelease, а не финальная версия `1.0`. Он выпущен из прежней isolated line через [PR #28](https://github.com/Just9120/llm-inspector/pull/28); executable SHA-256 `8816be54377101d73030e5a876a61b971f21ec9783c9c36905e1df9054ec2c48`, exact asset set/checksums/SPDX/provenance, public smoke и Windows Pro Ollama `0.33.2`, OpenCode `1.18.25`, Hermes `0.20.0` flows прошли. `E01-AC01` остаётся невыполненным до полной Windows Home/Pro exact-artifact matrix.

Утверждённый release path: единственная development/release line `main`; до полной validation продукт остаётся версией `1.0`, новые prerelease и version branches не создаются. Первый следующий public stable tag — `v1.0.0`; после публикации development version переходит к `1.1`. Distribution unit — unsigned portable self-contained single-file `win-x64` executable в GitHub Releases с SHA-256, SBOM/provenance и документированным SmartScreen warning. Store/MSIX/signing/automatic updates отложены. Secure remote target использует loopback-only Inspector, private Tailscale Serve и отдельный application token; public exposure запрещён. Linux/macOS и новые API protocols сейчас не реализуются.

## Быстрый старт

### Go migration — GOAL-006

Go foundation находится в `internal/domain`, `internal/telemetry` и `internal/gateway`. Exact toolchain — Go `1.27.1` из `.go-version`; проверка из repository root:

```powershell
./scripts/validate-go.ps1
```

Скрипт проверяет version pin, formatting, module integrity, vet, unit/integration tests и build. Дополнительный CI check — `windows-go`; прежний `windows-dotnet` остаётся обязательным reference gate до cutover. Foundation пока не является готовым Go desktop executable: UI, persistent storage, Windows integration и managed connectivity переносятся следующими increments той же Goal. Проверки parser используют existing synthetic fixtures без реальных prompt/response data.

### C# reference application — до Go cutover

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

Versioned launch configuration v2 принимает `--backend=ollama|llama-cpp|lm-studio`, local `--backend-url=http[s]://<literal-loopback>:<port>/`, remote `--remote-backend-url=https://<node>.<tailnet>.ts.net[:port]/` и `--listener-port=1..65535`. Local/remote options взаимоисключающие; non-Tailscale remote host, plain HTTP remote, credentials/path/query/fragment, duplicate и неизвестные options fail closed без вывода исходного значения.

Secure remote ingress включается только в UI после подтверждения private HTTPS Serve/user identity/ACL/no-Funnel boundary. Inspector создаёт one-time bearer token и хранит только его DPAPI CurrentUser ciphertext; Tailscale install/login/ACL/Serve остаются внешними явными действиями. Setup для Inspector ingress, remote backend, LibreChat на VPS, rotation/disable, threat model и обязательный LIVE checklist описаны в [`docs/runbooks/secure-remote-access.md`](docs/runbooks/secure-remote-access.md).

Lifecycle на development line настраивается в разделе «Управление локальным backend»: выберите runtime, найдите standard executable или укажите полный `.exe` path, проверьте показанные path/version/endpoint и подтвердите target. Только после этого доступны start/stop/restart, загрузка модели и понятные allowlisted parameters. Для llama.cpp model выбирается полным `.gguf` path. Inspector не устанавливает и не обновляет runtimes/models и никогда не останавливает процесс, который не запускал сам. Пошаговая процедура и compatibility states описаны в [`docs/runbooks/backend-lifecycle.md`](docs/runbooks/backend-lifecycle.md).

Профиль частоты сбора метрик выбирается в разделе «Производительность мониторинга». Настройка сохраняется атомарно в `%LOCALAPPDATA%\LLM Inspector\settings.json`; прежняя schema v1 автоматически читается как рекомендованный `Сбалансированный` профиль. Кнопка «Вернуть рекомендуемый» сбрасывает profile и interval, а некорректное custom value не применяется.

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

Release `win-x64` publish создаёт ровно один self-contained executable; установленный .NET runtime не требуется. Tag-only workflow формирует versioned executable, `SHA256SUMS.txt`, `portable-release-v1` manifest, SPDX 2.3 SBOM и Sigstore provenance/SBOM attestations, затем публикует их в GitHub Releases. Package unsigned, поэтому release notes содержат обязательный SmartScreen warning. Полная процедура и verification commands находятся в [`docs/runbooks/windows-release.md`](docs/runbooks/windows-release.md); до успешного exact release и Windows 11 25H2 Home/Pro exact-artifact checks это implementation capability, а не completed `E01-AC01`.

## Навигация

- [`AGENTS.md`](AGENTS.md) — главный repository router, authority model и execution boundaries.
- [`docs/project-spec.md`](docs/project-spec.md) — ратифицированный canonical product contract с initial release/backlog epics, atomic AC, readiness и Evidence.
- [`docs/delivery-plan.md`](docs/delivery-plan.md) — текущий delivery dashboard, readiness snapshots, blockers и candidate next Goal.
- [`docs/delivery-plan-archive.md`](docs/delivery-plan-archive.md) — исторический архив; не используется для текущей readiness.
- [`docs/ci-cd-rules.md`](docs/ci-cd-rules.md) — обязательный CI/CD и production safety contract.
- [`docs/architecture.md`](docs/architecture.md) — выбранный stack, runtime/data/privacy boundaries, backend capability matrix, test strategy и Windows release design.
- [`docs/runbooks/windows-release.md`](docs/runbooks/windows-release.md) — approved procedure создания и проверки portable GitHub Release.
- [`docs/runbooks/backend-lifecycle.md`](docs/runbooks/backend-lifecycle.md) — безопасный user flow для discovery, exact target confirmation, lifecycle, model load и parameters.
- [`docs/runbooks/secure-remote-access.md`](docs/runbooks/secure-remote-access.md) — private Tailscale Serve ingress, token handling, remote backend/LibreChat setup, threat model и two-host LIVE checklist.
- [Upstream requirements](https://docs.google.com/document/d/1r4o0UiJohJf34j3nL56LWnOxGRi7WDC3jqoDIIjDTnA/edit) — provenance source ратифицированных требований; текущим source of truth остаётся `docs/project-spec.md`.

Optional contracts для `Context Bundle Builder` и AI delivery infrastructure не созданы: соответствующие workstreams отсутствуют.

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
- Repository/CI foundation merged через [PR #2](https://github.com/Just9120/llm-inspector/pull/2); EPIC-09 core — через PR #3; EPIC-02 — через PR #4/#5; EPIC-03 — через PR #6; EPIC-04 — через PR #7/#9; EPIC-08 — через PR #8; EPIC-01 partial — через PR #10, release automation через [PR #21](https://github.com/Just9120/llm-inspector/pull/21) и release fix через [PR #22](https://github.com/Just9120/llm-inspector/pull/22); EPIC-05 — через PR #11; EPIC-06 — через PR #12; EPIC-07 — через PR #13; EPIC-10 — через PR #14; EPIC-11 — через PR #15; EPIC-12 partial — через PR #16 и profiles/harness через [PR #20](https://github.com/Just9120/llm-inspector/pull/20); BACKLOG-06 — через PR #17; BACKLOG-05 — через PR #18; decision ratification — через PR #19; BACKLOG-01 — через [PR #23](https://github.com/Just9120/llm-inspector/pull/23); BACKLOG-02 — через [PR #24](https://github.com/Just9120/llm-inspector/pull/24); tray forward fix — через [PR #25](https://github.com/Just9120/llm-inspector/pull/25); historical v1.0 release-line policy/fix — через PR [#26](https://github.com/Just9120/llm-inspector/pull/26)/[#27](https://github.com/Just9120/llm-inspector/pull/27); historical `rc.3` — через [PR #28](https://github.com/Just9120/llm-inspector/pull/28); GOAL-005 terminal reconciliation — через [PR #29](https://github.com/Just9120/llm-inspector/pull/29); single-main/final-only policy — через [PR #30](https://github.com/Just9120/llm-inspector/pull/30). Verified product `main`: `74f710ab1f7a9457377045191b0f62a472e8f40c`; current policy keeps no version branches.
