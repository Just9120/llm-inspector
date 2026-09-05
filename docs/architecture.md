# Architecture baseline

> Status: `GOAL-006 IN_PROGRESS — GO MIGRATION; C# REFERENCE RETAINED UNTIL CUTOVER`
> Decision scope: `GOAL-002`, user-approved `GOAL-006`; implementation scopes: `GOAL-003`–`GOAL-006`
> Evidence reviewed: `2026-09-05`

## 0. Go migration boundary

Новый production target: Go core + stable Wails v2 desktop shell + Svelte/TypeScript, русский UI с progressive disclosure. Это approved GOAL-006, а не альтернативный продукт. Существующие contracts поведения, privacy, SQLite/settings, loopback/remote security и process ownership сохраняются.

Первый increment добавляет изолированный Go core без подключения к пользовательской БД или реальному LLM runtime:

| Package | Ownership / boundary |
|---|---|
| `internal/domain` | Content-free records, metric quality/units/provenance invariants |
| `internal/telemetry` | Bounded incremental JSON/SSE projection: 256-byte token window, depth 64, максимум 64 keys/object; oversized/ambiguous metadata даёт unavailable, relay не изменяется. Values private categories не декодируются; output presence — только boolean |
| `internal/gateway` | Loopback `127.0.0.1`, allowlisted routes, transparent byte relay, cancellation; без environment proxy, decompression, redirects, automatic retry или raw logging. Nonblocking completion channel отделяет forwarding от consumers |
| `internal/state` | Bounded live state/ETA (1024 retained active requests), exact adjacent context delta (1024 sessions, isolated by client/backend), operation/tool graph (1024 operations / 8192 total records). Total active counter независим от display cap; capacity не разрешает остановить busy backend |
| `internal/diagnostics` | Versioned rules/thresholds с русскими explanations, typed Evidence, fact/hypothesis/insufficient_data. CPU offload всегда hypothesis; stall требует explicit request-scoped backend signal |

LM Studio native `/api/v1/chat` имеет отдельный parser mode: только terminal stats подтверждают cold/warm; OpenAI-compatible flow не получает guessed native metrics. Correlation headers валидируются и удаляются до backend. Request projection считает tools/trailing tool roles без content decode; response projection собирает не более 256 tool names до 128 bytes, хранит finish state и отвергает multi-choice/index ambiguity. Operation grouping требует exact adjacent turns и count-compatible pending tool results; measured duration — calculated call-to-next-turn wall time, не guessed tool execution time. Persisted history переносится отдельно.

Toolchain pinned в `.go-version`/`go.mod`, проверка — `scripts/validate-go.ps1`, CI — `windows-go` плюс прежний `windows-dotnet`. Ниже до cutover сохранена C# architecture/reference, а не утверждение о готовом Go runtime. Исторические C# test/release percentages не переносятся в Go readiness. Owner отдельно разрешил синхронизировать только стек/команды в `AGENTS.md` и CI/CD profile при cutover; safety contract, permissions, release gates и disabled CD не меняются.

## 1. Evidence boundary

Этот документ задаёт implementation baseline для ратифицированного [`project-spec.md`](project-spec.md), но сам по себе не является product runtime Evidence. Verified `main` `74f710ab1f7a9457377045191b0f62a472e8f40c` включает core epics, EPIC-12 profiles/harness, B01 lifecycle, B02 secure remote boundary, tray P/Invoke hotfix и deterministic resource-monitor fixture fix; exact-main CI `33850992022` успешен с `260/260` tests. Historical [`v1.0.0-rc.3`](https://github.com/Just9120/llm-inspector/releases/tag/v1.0.0-rc.3) опубликован из прежней isolated line exact SHA `821b17abf68bb63dd09f83a834d2d3bdec2e899c`: release run `33842346524`, payload checksums, SPDX/provenance, public smoke и доступные Windows Pro Ollama/OpenCode/Hermes flows успешны. Он не является финальным `v1.0.0`. Windows Home/full release matrix, controlled E12 measurements и B02 two-host LIVE Evidence ещё отсутствуют и относятся к отдельной будущей manual-validation Goal.

Server/runtime deployment target отсутствует. LLM Inspector устанавливается на Windows PC, поэтому CD отключён. Windows build, signing и distribution остаются release concerns, но не являются deployment на управляемый runtime host.

## 2. Принятые решения

| ID | Решение | Основание и trade-off |
|---|---|---|
| `ADR-001` | C# и `.NET 10 LTS` | Единый managed stack покрывает desktop, embedded HTTP proxy, async streaming и Windows interop. `.NET 10` находится в active LTS support до `2028-11-14`; self-contained release должен регулярно включать актуальный patch runtime. |
| `ADR-002` | `Avalonia UI` для desktop shell | Avalonia использует Win32 напрямую, поддерживает tray/native menus и не закрывает согласованный post-demand Linux/macOS backlog. WinUI 3 лучше интегрирован только с Windows, но создаёт будущий UI rewrite; Electron и Tauri добавляют второй web/Rust runtime без необходимости для текущего продукта. |
| `ADR-003` | Embedded `ASP.NET Core Kestrel` reverse proxy | Client использует штатную OpenAI-compatible endpoint configuration. Listener программно bind-ится только к `127.0.0.1` и `::1`; wildcard/`ListenAnyIP` и generic environment override запрещены. |
| `ADR-004` | Один tray-resident process, modular monolith | UI, proxy и collectors имеют общий lifecycle и не требуют IPC/второй installer. Async boundaries изолируют telemetry failures от forwarding; полный process crash остаётся известным риском и покрывается restart/crash tests. |
| `ADR-005` | SQLite в WAL mode, один application writer | Локальная транзакционная history без отдельного service. WAL допускает параллельное чтение, но только одного writer, поэтому writes сериализуются и checkpoints выполняются явно. DB не поддерживается на network filesystem. |
| `ADR-006` | NuGet `PackageReference` + Central Package Management + committed lock files | Версии централизуются в `Directory.Packages.props`; floating versions запрещены; CI использует locked restore. SDK фиксируется exact `global.json` с `rollForward: disable`, а upgrade выполняется отдельным reviewed change. |
| `ADR-007` | Portable unsigned self-contained single-file `win-x64` executable как первый release unit | Не требует установленного .NET, installer или admin rights. GitHub Release публикует exact executable, SHA-256, SBOM/provenance и SmartScreen disclosure. Store/MSIX/trusted signing/automatic Store updates отложены в release backlog. |
| `ADR-008` | CI и Windows release разделены; server/runtime CD остаётся disabled | Untrusted PR проверяет code без release credentials. Trusted tag job в будущем публикует exact locally/CI-validated artifact; это artifact distribution, не deployment на runtime host. |
| `ADR-009` | Single-main final-only versioning | `main` — единственная development/release line. До первой stable публикации продукт остаётся `1.0`; следующий public tag — final `v1.0.0` после required validation, затем development version становится `1.1`. Новые prerelease tags и version branches не используются. Historical `v1.0.0-rc.*` остаются immutable Evidence и не переиспользуются. |
| `ADR-010` | Three built-in performance profiles plus bounded custom profile | `Бережный`, `Сбалансированный` и `Детальный` имеют отдельные sampling intervals/budgets и обязаны проходить independently; custom profile не является release Evidence. |
| `ADR-011` | Lifecycle только для Inspector-owned backend processes через typed capability adapters | Exact process identity, official interface и parameter allowlist ограничивают destructive surface; externally owned process остаётся observation-only, crash recovery — manual. |
| `ADR-012` | Remote через loopback Inspector + private Tailscale Serve | Inspector не становится LAN/public server. Tailscale обеспечивает encrypted tailnet transport, а отдельный application bearer token защищает Inspector endpoint; Funnel и direct backend exposure запрещены. |

Ни `NativeAOT`, ни trimming не входят в baseline: они допустимы только после compatibility и privacy tests всех UI/serialization/native dependencies. Single-file publish является утверждённым release target, но допустимый self-extract behavior native dependencies должен быть зафиксирован в artifact manifest и проверен до `E01-AC01`.

## 3. Supported platform matrix

Initial release имеет узкий support contract:

| Platform | Architecture | State | Required release Evidence |
|---|---|---|---|
| Windows 11 `25H2`, Home/Pro, актуальный cumulative update | `x64` | **SUPPORTED / minimum** | Clean install, upgrade, launch, tray/background, proxy, SQLite recovery и critical end-to-end suite на реальной или nested-virtualized Windows installation |
| Windows 11 `26H1` | `ARM64`-oriented selective hardware release | **NOT SUPPORTED in initial release** | Отдельный `win-arm64` design/build/test Goal до добавления в matrix |
| Windows 11 `24H2` Home/Pro | `x64` | **NOT SUPPORTED in initial release** | Исключена: servicing заканчивается `2026-10-13`, до ожидаемого первого product release |
| Windows 10 и более ранние Windows | any | **NOT SUPPORTED** | Out-of-support platform не используется как release gate |
| Windows 11 Enterprise/Education/LTSC | `x64` | **UNVERIFIED / best effort** | Требует owner demand и отдельного edition-specific matrix addition |

Compatibility minimum: x64 device должен соответствовать системным требованиям Windows 11 и иметь ресурсы для выбранного пользователем local LLM backend. Dedicated GPU не требуется для запуска Inspector; отсутствие supported GPU/driver metric source даёт `unavailable`, а не application failure. Backend/model hardware sizing находится вне ownership Inspector.

Controlled performance reference, не minimum requirement: Windows 11 Pro `25H2` x64 build `26200.9168`; AMD Ryzen 7 9800X3D (`8C/16T`, max `4700 MHz`); `64 GB` nominal RAM (`61.7 GiB` available); NVIDIA RTX 5060 Ti `16311 MiB`, driver `610.74`, плюс integrated AMD Radeon; Samsung SSD 970 EVO Plus 1TB NVMe (`931.5 GiB`) для app/DB/fixtures; WDC WD30EZRZ 3TB HDD фиксируется как inventory, но не benchmark storage; active Balanced power plan `381b4222-f694-41f0-9685-ff5bb260df2e`. Для NVIDIA VRAM authoritative fixture использует `nvidia-smi`; противоречивое WMI `4 GB` значение не принимается.

Matrix пересматривается перед каждой release Goal. Новая Windows release не становится supported автоматически: сначала нужны build/runtime tests. Удаление ещё поддерживаемой версии или добавление architecture/edition изменяет durable compatibility contract и требует explicit owner decision.

## 4. Logical components и repository layout

Фактическая solution boundary, созданная как compile-only foundation:

```text
src/
  LlmInspector.Domain/             pure contracts, value objects, quality/provenance
  LlmInspector.Application/        use cases, ports, lifecycle/correlation orchestration
  LlmInspector.Gateway/            Kestrel listener, HTTP/SSE relay, cancellation
  LlmInspector.Adapters/           Ollama, llama.cpp, LM Studio capability adapters
  LlmInspector.Telemetry/          allowlist projection, stages, timings, bounded queues
  LlmInspector.Storage.Sqlite/     schema, migrations, retention, query/read models
  LlmInspector.Resources.Windows/  system/process/GPU collectors behind capability ports
  LlmInspector.Diagnostics/        versioned rules, error taxonomy, safe snapshot projection
  LlmInspector.App/                Avalonia UI, tray, composition root, settings UX
tests/
  LlmInspector.UnitTests/
  LlmInspector.ContractTests/
  LlmInspector.IntegrationTests/
  LlmInspector.PrivacyTests/
  LlmInspector.WindowsTests/
  LlmInspector.PerformanceTests/
benchmarks/
  fixtures/                        synthetic, content-labelled benchmark corpus only
docs/
  runbooks/                        created only when an approved operation needs one
```

Dependency direction:

```text
App (composition/UI)
 ├─> Application ─> Domain
 ├─> Gateway ─────> Application ports + Domain
 ├─> Adapters ────> Application lifecycle ports + Domain
 ├─> Telemetry ───> Application ports + Domain
 ├─> Storage ─────> Application ports + Domain
 ├─> Resources ───> Application ports + Domain
 └─> Diagnostics ─> Application read ports + Domain
```

`Domain` не зависит от UI, HTTP, SQLite или Windows APIs. `Gateway` не пишет в database и не вызывает UI. `Storage` принимает только уже allowlisted domain records. Backend-specific fields остаются namespaced и не проникают в common model без declared semantics/unit.

Все девять production boundaries содержат product code. `Diagnostics` владеет versioned explainable rules и typed conclusion/evidence contracts, но получает только allowlisted Domain/Application projections. Dependency graph проверяется автоматически в `LlmInspector.UnitTests`. Наличие project boundary само по себе не является implementation Evidence соответствующей product feature.

EPIC-06 добавил per-request Windows resource sessions через Application ports, не вводя зависимость Gateway от Windows APIs. EPIC-07 добавил versioned diagnostic rules и typed error taxonomy: resource Evidence учитывается только при exact request correlation и само по себе не доказывает root cause. EPIC-10 оставляет gateway/history composition-root owned при скрытом UI; bounded observation channel передаёт только allowlisted terminal records в typed notification rules, а native Win32 tray не получает arbitrary title/body input из proxy data. EPIC-11 читает bounded `TechnicalHistorySlice` через Application port; Diagnostics владеет fixed allowlist DTO и serializer, App — только selection/preview/save UX. EPIC-12 расширил Domain только typed runtime facts, Application — origin/correlation policy, а SQLite v5 остаётся единственным durable owner. BACKLOG-06 повторно использует тот же bounded history projection и atomic local writer; export aggregates строятся только из exact projected records, раздельно для request/resource metric categories. BACKLOG-05 сохраняет один correlated resource record на GPU device; только primary record несёт host/process/traffic metrics, поэтому multi-device rows не искажают их aggregates.

## 5. Runtime/process model

Один per-user process владеет:

- Avalonia dispatcher, main window и system tray;
- Kestrel loopback listener;
- outbound connection pools к явно настроенным local backends;
- supervised telemetry/resource workers;
- SQLite writer и read connections;
- notification scheduler.

Один instance guard предотвращает конкурирующих writers/listeners. Закрытие main window скрывает UI, но не завершает process; explicit tray `Exit` останавливает listener, перестаёт принимать новые requests, даёт текущим requests bounded graceful drain, flush-ит accepted metadata и закрывает SQLite. Значение drain timeout будет boundary-tested и versioned в configuration; оно не является product performance budget.

EPIC-10 реализует per-user Win32 tray как отдельный STA message loop, HKCU Run autostart с exact quoted executable и `--background`, а также atomic `%LOCALAPPDATA%\LLM Inspector\settings.json` schema v1. Четыре notification categories включаются независимо; presentation строится только из typed technical fields, silent mode передаётся как `NIIF_NOSOUND`. `notification-policy-v1` подавляет одинаковый event key на 15 минут и ограничивает delivery тремя уведомлениями за rolling 10 minutes; buffer capacity `256` не блокирует request forwarding и считает drops.

Collectors, retention и diagnostics работают как independently supervised background services. Их exception переводит конкретный source в degraded/unavailable, создаёт safe diagnostic event и применяет bounded restart backoff. Ни request relay, ни client response не await-ит database, analytics, resource sampling или UI.

## 6. Network boundary и request flow

Inspector — explicit reverse proxy, не system-wide MITM. B01 development line добавляет отдельную capability boundary только для Inspector-owned backend processes; gateway launch configuration по-прежнему не принимает lifecycle CLI mutation commands.

Текущий runtime использует default listener `127.0.0.1:5117`. Versioned launch configuration v1 выбирает Ollama, llama.cpp или LM Studio с default ports `11434`, `8080` и `1234`; explicit backend URL/port остаётся literal-loopback-only. Generic и четыре per-client base paths поддерживают transparent `GET /v1/models` и `POST /v1/chat/completions`, а на backend всегда направляются стандартные `/v1/*` paths. При выбранном LM Studio отдельный generic route `POST /api/v1/chat` прозрачно сохраняет native path и получает отдельный telemetry adapter; для других backend route отсутствует. Dynamic listener port `0` разрешён отдельной factory только для test fixtures. Generic hosting URL configuration очищается и не может добавить wildcard endpoint; `localhost` backend нормализуется в literal `127.0.0.1` без DNS resolution.

```text
OpenAI-compatible client
  │  generic /v1, explicit /clients/<known-client>/v1 or LM Studio /api/v1/chat
  ▼
Kestrel loopback gateway
  ├─ validate route + configured backend identity
  ├─ stream request to explicit loopback backend target
  ├─ relay model discovery or chat status/headers/body/SSE in original semantic order
  └─ for chat only, inspect one bounded token window while relaying each chunk
            │
            ├─> adapter + allowlist normalizer
            └─> bounded volatile live-request tracker ─> Avalonia snapshot projection
            │ non-blocking metadata events only
            ▼
  latest allowlisted observation (bounded process memory)
            │ bounded non-blocking persistence boundary
            ▼
  lifecycle queue ─> SQLite writer ─> read models ─> UI/diagnostics
  sample queue ─────> SQLite writer
```

### Secure remote target (реализован локально, LIVE pending)

Inspector остаётся literal-loopback listener на `127.0.0.1`; private remote ingress предоставляет Tailscale Serve по HTTPS только внутри tailnet. Funnel, wildcard bind, public Internet и direct backend port exposure запрещены. Remote mode выключен по умолчанию и требует explicit UI confirmation плюс отдельный random `256-bit` application bearer token. Token создаётся/ротируется локально, показывается только в этот момент и хранится как DPAPI CurrentUser ciphertext в `%LOCALAPPDATA%\LLM Inspector\remote-access.json`; disable удаляет persisted credential. Inspector показывает setup/status, но не устанавливает Tailscale, не выполняет login и не изменяет ACL/Serve state.

Ingress guard доверяет remote request только при сочетании `*.ts.net` Host, единственного non-empty `Tailscale-User-Login` и валидного application bearer token. Funnel и tagged-device traffic без user identity fail closed; remote Authorization, Tailscale identity/capability и forwarding headers не передаются backend. Local literal-loopback requests без proxy headers сохраняют прежний local flow.

Explicit remote backend option `--remote-backend-url=https://<node>.<tailnet>.ts.net[:port]/` допускает только first-profile Tailscale HTTPS target с normal certificate validation и выключенными redirects. Отдельный bounded DNS+TCP connect probe даёт availability и calculated connect latency; эта метрика не включает TLS/inference и не вычитается из request duration. Windows local host/process/GPU probe для remote target не запускается, gateway byte counters остаются допустимыми, остальные resource fields получают typed `unavailable` с remote source version. Другие WireGuard/private overlays могут называться `Compatible`, но не `Проверено` и пока не проходят validator без отдельного profile/reproduction. `BACKLOG-02` требует actual encrypted Windows↔VPS/second-PC LIVE Evidence; server deployment по-прежнему отсутствует.

### Forwarding invariants

1. Local backend target должен быть `localhost`, `127.0.0.1` или `::1`; normalized destination и redirects не могут выйти из loopback. Remote target принимается только отдельной explicit launch option и только как root `https://*.ts.net[:port]/`; credentials/path/query/fragment запрещены.
2. Generic `ASPNETCORE_URLS`, wildcard hostname, `0.0.0.0`, `[::]` и `ListenAnyIP` не могут расширить listener. Port conflict останавливает listener с явной UI error, а не выбирает скрытый alternate endpoint.
3. Hop-by-hop HTTP headers обрабатываются по proxy rules; остальные method/path/query/headers/body и response status/headers/body сохраняются семантически. Локально потребляемые security/metadata exceptions: четыре Inspector correlation headers всегда удаляются; Tailscale/proxy identity headers всегда удаляются; remote application `Authorization` удаляется только после успешной ingress authentication. Local backend `Authorization` сохраняет прежний configured pass-through. Inspector не добавляет generation parameters и не заменяет model/tool payload.
4. Request и response bodies relay-ятся streaming; full-body buffering запрещено. Parser хранит не более `256` bytes текущего lexical token и не влияет на flow control клиента; container depth больше `64`, malformed JSON или parser exception переводят telemetry в `unavailable`, не прерывая relay.
5. SSE event order и bytes внутри relayed data не переупорядочиваются. Fragmented tool-call name может быть assembled только в bounded volatile state; arguments/results проходят к client/backend, но отбрасываются telemetry projection.
6. Client cancellation немедленно propagates к backend. Inspector не replay-ит и не retry-ит generation request после начала forwarding: duplicate inference опаснее явного failure.
7. Backend TLS certificate validation не отключается. Local Authorization/cookie/proxy-auth headers могут проходить к backend по configured policy, но исключены из logging, metrics, snapshots и exception text; remote Inspector bearer token является ingress credential и никогда не forwarding credential.

## 7. Privacy enforcement и threat boundary

Inspector неизбежно видит proxied content в process memory, чтобы переслать его. Privacy promise означает zero persistence/index/log/analytics/snapshot occurrence, а не end-to-end encryption от самого process.

Enforcement выполняется в трёх независимых слоях; первые два частично реализованы в EPIC-09 increment, третий проверяется в EPIC-08:

1. `Gateway`: raw body/headers никогда не передаются structured logger; HTTP body logging и framework request logging disabled.
2. `Telemetry`: schema-first allowlist projection создаёт новый metadata record. Запрещённые значения не маскируются post hoc — они отсутствуют в output type.
3. `Storage/Diagnostics`: database schema и snapshot DTO содержат только allowlisted fields; serialization неизвестного поля fail-closed.

Persistent allowlist: timestamps/durations, token counts, normalized model/backend/client identities, generated or pseudonymized correlation IDs, tool names/count/status/duration, HTTP/error categories без raw body, quality/provenance, resource samples, OS/backend/client/driver version identifiers и versioned configuration fingerprints. Backend URL не сохраняется raw в history; runtime configuration identity использует SHA-256 fingerprint, а UI может показать текущий validated target только in-memory. Dedicated Inspector correlation GUIDs считаются уже псевдонимными и могут сохраняться; backend/account/Tailscale identities, tokens и paths не сохраняются raw, а будущая correlation по внешнему stable ID потребует per-install keyed pseudonymization.

Всегда запрещены: prompt/response/reasoning text, images/audio, embeddings, tool arguments/results, user code, authorization/cookie values, full request/response/error bodies, raw query strings, arbitrary headers, stack traces с local paths и unsanitized exception messages.

Release configuration не отправляет telemetry, history или settings external services. Crash reporting внешнему service отсутствует. User-created diagnostic snapshot и analytics export сначала создаются локально, проходят общий allowlist/negative corpus и доступны для exact preview; их дальнейшая передача — отдельное explicit user action вне приложения.

`diagnostic-snapshot-v1` имеет executable schema allowlist. Root содержит только `schema_version`, `generated_at_utc`, `selection`, `environment`, `requests`, `resource_samples`, `truncation`. Selection содержит scope и только UTC bounds либо operation ID. Environment содержит availability/value/source-version facts для OS, GPU driver, backend, client, application и framework versions. Request entry содержит pseudonymous request/operation IDs, UTC start, HTTP status, typed outcome/error, client/backend, normalized model fact, model-load state и allowlisted qualified runtime metrics. Resource entry содержит pseudonymous sample/request/operation IDs, UTC capture, stage/evidence, normalized GPU ID, dropped count и qualified system metrics. Каждый metric содержит только key, numeric value, unit, quality, source/source version и optional derivation version. Output bounded: до `1000` requests и `5000` resource samples с explicit truncation flags. Свободных content/error/path полей в schema нет; DTO reflection test и end-to-end negative corpus блокируют silent allowlist growth.

## 8. State ownership, SQLite и retention

| State | Owner | Location / durability |
|---|---|---|
| Versioned non-secret settings | `LlmInspector.App` через Application settings port | `%LOCALAPPDATA%\LLM Inspector\settings.json`, atomic replace, current-user ACL |
| Backend credential, pseudonymization key | Windows credential protector abstraction | Ciphertext/credential reference only; plaintext не попадает в settings/logs/DB |
| Technical history | `LlmInspector.Storage.Sqlite` | `%LOCALAPPDATA%\LLM Inspector\data\inspector.db` + WAL/SHM |
| Volatile active request/session state | `LlmInspector.Application` | Memory only; reconstructed as incomplete/aborted after restart |
| Diagnostic logs | Structured safe logger | Size-bounded rolling files under `%LOCALAPPDATA%\LLM Inspector\logs`, default retention 7 days |
| User-created snapshot | `LlmInspector.Diagnostics` | User-selected local path; not auto-uploaded and not silently indexed |
| User-created analytics export | `LlmInspector.Diagnostics` | User-selected local path; exact preview required, not auto-uploaded and not silently indexed |

Текущая schema v5 реализует `history_settings`, `requests`, `request_metrics`, `sessions`, `operations`, `turns`, `tool_events`, `resource_samples`, normalized `resource_sample_metrics` и `schema_migrations`. Migration v2 добавляет request correlation/model-load fields; migration v3 — turn/tool quality/provenance; migration v4 — request/stage/process/GPU correlation и allowlisted resource metrics; forward-only transaction migration v5 добавляет typed error origin, runtime configuration fingerprint и optional Inspector/framework/OS/adapter/backend/client/model/GPU-driver version facts. Legacy error origin backfill использует только typed error category, а неоднозначный relay failure остаётся `unknown`. Raw content/blob columns запрещены; derived aggregates являются recomputable read models и следуют той же privacy/retention boundary.

SQLite rules:

- WAL mode, `foreign_keys=ON`, busy timeout и one-writer queue;
- short transactions; UI analytics uses read-only connections/snapshots;
- schema migrations versioned, forward-only and transactional; listener may run in explicit `history unavailable` degraded mode if migration/storage fails;
- destructive migration требует отдельной Goal, verified backup/recovery path и stop criteria;
- SQLite восстанавливает committed WAL transactions после process termination; explicit idle/threshold checkpoint остаётся release-hardening debt и не выполняется на request critical path;
- startup выполняет `quick_check(1)` до migration/write transaction и при failure переводит composition root в `history unavailable`; automatic overwrite отсутствует, отдельный read-only quarantine UI ещё не реализован;
- DB/WAL/SHM backup рассматривается как единый state set; live file copy без SQLite backup mechanism запрещён.

History default retention — `30 days`; user options точно соответствуют `7 days`, `30 days`, `90 days`, `indefinite`. Один cutoff применяется к request/session/operation/tool/resource/derived history, чтобы не оставлять orphan records. Cleanup выполняется bounded batches oldest-first и не блокирует forwarding. Manual clear требует preview scope, confirmation и transaction. Settings, release metadata и user-exported snapshots не удаляются history cleanup.

Текущий writer использует bounded channel capacity `256`, single reader и non-blocking `TryWrite`; full queue увеличивает drop counter, а storage failure — failure counter с типом ошибки, не задерживая proxy response. Read models открывают read-only connections. Finite retention удаляет oldest records батчами по `500`, каждая batch имеет отдельную transaction; default cleanup выполняется при startup и сразу после сохранения нового retention setting.

Analytics группирует trends по UTC day. Arithmetic mean и median считаются по точным sample values. P95 использует nearest-rank: для отсортированного массива из `n` samples выбирается индекс `ceil(0.95 × n) - 1`. Aggregate статистически достаточен только при `n >= 3`; меньшая выборка отображается с values, но явно маркируется `insufficient` и не подтверждает degradation. Comparison считает `candidate mean - baseline mean`; рост latency/load/error и падение throughput считаются degradation только при достаточной выборке обеих сторон. Runtime correlation группирует полные typed configuration/version facts, сравнивает earliest и latest distinct cohorts и отдельно показывает недостаточность no/single/undersampled configuration data. Token-count delta сам по себе не классифицируется как performance degradation.

EPIC-12 process-kill fault injection подтверждает recovery committed WAL history и приём новой telemetry после normal/crash restart. Не реализованы explicit idle/threshold WAL checkpoint, read-only quarantine UX, SQLite backup mechanism, disk-full fixture и destructive corruption fixture; они остаются release-hardening debt и не используются как Evidence `E12-AC01..06`.

## 9. Telemetry semantics и quality model

Каждое value хранит `source`, `source_version`, `captured_at`, `unit` и один quality state:

- `exact` — backend/OS API явно сообщила value с совместимой semantics;
- `calculated` — детерминированная формула из exact inputs; formula/version сохраняются;
- `estimated` — estimator с model/version/sample sufficiency; UI явно маркирует estimate;
- `unavailable` — source/capability/association отсутствует или недостоверна; numeric placeholder запрещён.

`calculated` и `estimated` остаются различимыми в storage, даже если UI объединяет их визуально. Common units: duration `ns` internally with documented display conversion, rates `tokens/s`, bytes as integer bytes, utilization `0..100 percent`, temperature `°C`, power `W`. Backend-native field сохраняется в namespaced extension только вместе с provenance и не переименовывается в common metric без semantic mapping.

Текущий live-state contract хранит для каждого active request ровно одну stage: `model loading`, `queue/waiting`, `prompt processing`, `reasoning/generation` или `tool wait`; terminal outcome отображается как `completed`, `cancelled` или `error`. Gateway публикует только protocol-observed lifecycle stages и не объявляет их backend-exact. `model loading`, `tool wait` и другие richer stages становятся current только через typed backend-reported signal; supported OpenAI-compatible flow их не угадывает.

Elapsed time вычисляется monotonic clock как `calculated`. Progress percentage принимает только typed exact backend signal `0..100`; при отсутствии такого signal UI показывает `unavailable` без percentage. Bounded linear ETA estimator использует до четырёх samples и выдаёт `estimated` только после минимум трёх strictly increasing samples одного source со span не меньше `5` percentage points; regression или source-version change сбрасывает estimator evidence. Terminal request не показывает ETA. Tracker хранит active set и только последний terminal snapshot в volatile memory; UI получает immutable snapshot каждые `250 ms`, а его failure не участвует в request relay.

Latest-request projection использует versioned OpenAI Chat Completions fixture v2 и LM Studio native fixture v1: input/output/cached/reasoning token counts принимаются только как non-negative whole exact values; llama.cpp `cache_n`, `prompt_per_second` и `predicted_per_second` одновременно сохраняют native metric и получают common mapping с backend provenance. Current context usage равен exact input/prompt token count; limit/history/tools и queue остаются typed `unavailable`, пока adapter не получает совместимый exact source. LM Studio native complete terminal `stats` без load signal подтверждает warm request с exact zero load, а optional `model_load_time_seconds` или completed `model_load.end.load_time_seconds` подтверждает cold load; started, но незавершённый lifecycle остаётся `unavailable`. Streaming TTFT — `calculated` monotonic interval до первого непустого OpenAI `choices[].delta.content` или LM Studio `message.delta.content`; role/reasoning/tool-only и non-streaming response не выдают TTFB за TTFT. Total duration — monotonic calculated metric для каждого terminal observation. Non-allowlisted string values, включая response и reasoning content, parser не декодирует в managed telemetry strings.

Cross-turn correlation является explicit opt-in: client передаёт полный triplet Inspector-reserved `session ID`, `turn ID`, `turn sequence`; IDs должны быть non-empty GUID в canonical 32-hex format, sequence — положительным. Gateway удаляет headers до backend, хранит не более `1024` session states и считает signed context delta только между соседними sequence одной session при exact token count. First turn, duplicate, gap, out-of-order, incomplete или malformed metadata дают `unavailable`. Time proximity и connection reuse не используются как доказательство.

Agent-operation correlation добавляет optional `operation ID` того же GUID format. Operation начинается только с sequence `1` и принимает следующий turn лишь при exact adjacent sequence, новой turn ID, той же session/client/backend и совпадении числа trailing tool-result messages с pending tool calls. Иначе request сохраняет самостоятельную telemetry, но не присоединяется к operation. Bounded capture до `1 MiB` наблюдает request/response bytes во время transparent relay и извлекает только array counts, role=`tool`, normalized function names и finish disposition; overflow/malformed/unsupported metadata дают `unavailable`. Tool wall duration маркируется `calculated` от завершения tool-call response до начала exact next result turn.

## 10. Backend capability matrix

Матрица — design input, а не runtime promise. Конкретная backend version и fixture suite определяют capability при implementation; brand-name detection не даёт fabricated support.

| Capability | Ollama | llama.cpp `llama-server` | LM Studio | Normalized behavior |
|---|---|---|---|---|
| OpenAI Chat Completions | `/v1/chat/completions` documented | `/v1/chat/completions` documented; project предупреждает, что full OpenAI compatibility не гарантируется | `/v1/chat/completions` documented | Unknown field проходит transparently; adapter parses only tested subset |
| Non-streaming / streaming | Оба documented | Оба documented | Оба documented | Total duration `calculated`; TTFT `calculated` только по first non-empty streaming content delta, иначе `unavailable`; event order contract-tested |
| Tool calls | Documented input support; model dependent | Требует `--jinja`/compatible chat template; parallel calls model/template dependent | Documented; streamed name/arguments fragmented across chunks | Persist tool name/count/status/timing only; arguments/results never persist |
| Token usage | `stream_options.include_usage` accepted; native `/api/chat` exposes token counters | Standard `usage` plus backend `timings` documented | OpenAI flow плюс explicit native `/api/v1/chat`; native terminal stats expose input/output/reasoning counters | Exact only when present and semantics mapped; otherwise unavailable, not retokenized silently |
| Prompt/generation timing | Native `/api/chat` has prompt/eval durations, but initial OpenAI path cannot assume them | Response `timings` exposes prompt/prediction counts/rates | Native REST v1 events/stats expose rate, model-load time and message deltas | llama.cpp fixture maps exact prompt/generation rates; LM Studio native maps exact generation rate/load and calculated streaming TTFT; queue stays unavailable without exact source |
| Stage/progress | OpenAI flow has no guaranteed load/queue percentages | Optional `/metrics` is aggregate and enabled only by `--metrics`; not per-request proof | Native v1 has load/prompt/tool events; OpenAI flow has less detail | Stage from exact event where available; otherwise protocol-observed stage without percentage |
| Optional probes | Read-only capability/version probes only; no duplicate generation | `/metrics` only when explicitly enabled; aggregate provenance | Native read-only capability/version endpoints only | Probe failure never blocks request and never upgrades per-request attribution |

Adapters не переключают client с OpenAI-compatible protocol на native generation API и не посылают duplicate prompt ради metrics. LM Studio `/api/v1/chat` является отдельным explicit supported flow: его выбирает сам client, а gateway только relays один исходный request. Другие native generation endpoints остаются вне текущего scope.

### Реализованная lifecycle/compatibility boundary

Managed built-ins — Ollama, llama.cpp и LM Studio. Generic literal-loopback OpenAI-compatible runtime получает observation, но lifecycle доступен только через capability adapter и только для Inspector-owned process. Discovery проверяет official standard paths/PATH, показывает exact version/path/endpoint для user confirmation и предоставляет manual executable picker; models перечисляются official API/CLI, а llama.cpp model выбирается explicit `.gguf` path. Download/install/update backend или model не выполняется.

Lifecycle command выполняется без shell и сериализуется per backend. Start idempotent; port conflict только диагностируется. Stop/restart/model switch блокируются при active Inspector requests и показывают count. Graceful official stop предшествует bounded force exact PID, причём force допускается лишь при совпадении PID, process start time и executable identity. Restart использует тот же verified executable и last valid typed configuration. Readiness probe направлен на exact endpoint; failed start очищает только частично созданный owned process. Model load считается успешным после official model-identity confirmation. Crash не запускает automatic recovery: UI показывает typed `Crashed` и one-click manual restart.

Parameter UI строится из adapter allowlist и native defaults: Ollama — local port, context, keep-alive, parallel requests, max loaded models, max queue; llama.cpp — local port, context, GPU layers `auto/off/all/N`, CPU threads, parallel slots; LM Studio — local port, context, GPU offload `auto/off/max/0..1`, model TTL, model ID. Unsupported control unavailable; reset возвращает backend default; arbitrary args/env, CORS/public bind и privileged service commands отсутствуют.

Canonical version data хранится в embedded `config/runtime-compatibility.json`: exact runtime version match, operation capabilities, Windows matrix, verification date, Inspector evidence revision, sanitized Evidence, limitations и status. UI переводит status как `Проверено`, `Совместимо`, `Только наблюдение`, `Не поддерживается`. Unknown/newer versions проходят safe executable/version/readiness probes и не считаются verified. Первые baselines: локально проверенный Ollama `0.33.2`; llama.cpp `b10516` и LM Studio `lms 0.0.47+` остаются target/PENDING_EXTERNAL_GATE, причём exact LM Studio app/runtime фиксируется при первом actual test. Unsigned remote matrix и automatic runtime update запрещены.

`Application` владеет serialized manager/state/active-request gate и typed plans; `Adapters` — official flags, environment allowlist, readiness/model confirmation и embedded compatibility matrix; `Resources.Windows` — no-shell process execution, TCP listener ownership, exact executable identity и bounded stop; `App` — confirmation-first Russian UX. Detached LM Studio ownership принимается только если endpoint был свободен до official `lms server start`, а после него появился единственный allowlisted listener owner.

## 11. Resource collectors

`Resources.Windows` implements a per-request monitor behind Application capability ports. Host CPU/RAM and exact process CPU/RAM/read-write counters come from Windows APIs; process association is accepted only when the configured literal-loopback backend listener has one exact TCP owner PID plus process start time/image identity. Gateway-relayed request/response byte counters provide request-scoped network traffic. A fixed-path, bounded-time `nvidia-smi` provider reports utilization, VRAM, temperature and power for up to 16 ordered distinct NVIDIA devices; absent executable/device/field becomes `unavailable`.

- System-wide sample может быть exact для host, но не автоматически attributed конкретному request.
- Process association требует exact PID/start-time/backend identity; name/time heuristics недостаточны.
- GPU metric содержит device/adapter identity и source. Каждый supported device получает отдельную timeline row; host/process/traffic fields присутствуют только в primary row, чтобы не дублировать totals. Unsupported counter/driver/device yields `unavailable`.
- Sampling starts with each request, follows the versioned request stage and stops at its terminal outcome; samples carry exact request/operation IDs and timestamps.
- Each request is bounded to `2048` samples. Overflow increments an explicit persisted gap counter; collector, sink and UI failures remain best-effort and never backpressure model streaming.

Default sampling interval — versioned implementation constant `1 s`; tests inject a shorter interval and deterministic sources. NVIDIA — текущий supported GPU source; другие vendors остаются `unavailable`, не inferred. `nvidia-smi` даёт device-wide readings, поэтому UI не приписывает request/workload конкретному GPU и явно показывает attribution как `unavailable`.

### Explainable diagnostics и error analytics

`LlmInspector.Diagnostics` применяет immutable defaults `diagnostic-rules-v1` к последнему completed request, exact-correlated resource sample и active-request snapshot. Threshold rules охватывают large prompt, slow generation, VRAM pressure, model-load/queue latency и high context usage. Exact/calculated Evidence может дать `FACT`; estimated Evidence по threshold даёт только `HYPOTHESIS`; CPU offload остаётся hypothesis даже при high process CPU/low GPU, потому что эти counters не доказывают placement layers.

Для каждого conclusion сохраняются rule/version, human-readable explanation и typed supporting evidence. Missing, mismatched или inconsistent telemetry даёт `INSUFFICIENT_DATA`, а не guessed cause. Active lifecycle/stage подтверждает только продолжающийся request; `ConfirmedStall` возможен лишь при exact request-matched typed backend activity signal. Большая elapsed duration без такого signal явно недостаточна.

Gateway классифицирует bounded technical outcomes без сохранения exception/error body: connection refused, model loading/HTTP 503, HTTP/API error, timeout, allowlisted context-overflow code/HTTP 413, client cancellation, backend disconnect/crash category и generic unavailable/relay failure. Backend-crash category описывает transport termination, но не выдаёт process-crash causality за доказанный факт.

History считает recurring group от двух occurrences внутри selected query/period. Frequency comparison использует `occurrences / all requests in that period` отдельно для baseline/candidate и показывает percentage-point delta. Error correlation требует общего explicit operation/session identifier и показывает first/last timestamps; time proximity без metadata остаётся uncorrelated. Эта correlation не является causal attribution.

## 12. Failure isolation и error ownership

| Failure | Forwarding behavior | Recorded state |
|---|---|---|
| Collector/diagnostic/UI failure | Request continues | Affected metric `unavailable`; safe component/error category |
| Telemetry/sample queue full | Request continues; no blocking retry | Explicit gap/drop counter; affected history incomplete |
| SQLite locked/corrupt/migration failure | Request continues in `history unavailable` mode | In-memory health state; safe local log; no fabricated persistence success |
| Backend connection refused/timeout/HTTP error | Preserve backend/network result to client where protocol permits | Origin `backend` or `network`, exact status/category, no raw body |
| Client cancellation/disconnect | Cancel outbound request; no replay | Origin `client`, state `cancelled` |
| Gateway parsing failure after relay starts | Continue byte relay; disable telemetry projection for that request | Origin `inspector`, quality unavailable |
| Gateway bind/process crash | Client cannot use Inspector endpoint | Origin `inspector`; restart recovery marks open operations incomplete |
| Unknown origin | No guessed attribution | Origin `unknown`, supporting facts only |

Lifecycle events use a higher-priority bounded queue than resource samples. If even lifecycle metadata cannot be accepted, request still wins; UI must expose degraded completeness.

## 13. Test strategy and traceability

| Layer | Critical seams | Canonical coverage |
|---|---|---|
| Pure unit/property tests | units, quality/provenance, stage machine, correlation, diagnostics thresholds, retention cutoff | `E02-AC04..06`, `E03-*`, `E04-*`, `E05-AC06..08`, `E07-*`, `E08-AC07..10` |
| Backend contract fixtures | sync/SSE, fragmented JSON, tools, errors, missing fields, backend-version fixtures for all three adapters | `E02-*`, `E04-*`, `E05-*`, `E12-AC09` |
| Proxy integration with stub backend | headers/body/status, live stages/outcomes, cancellation, disconnect, concurrency, slow consumer, malformed SSE, parser/UI-sink failure | `E02-AC12..15`, `E03-*`, `E09-AC09..12`, `E12-AC07..09` |
| Automated privacy negative corpus | canary prompt/response/reasoning/tool args/results/code across DB/WAL/logs/snapshot/crash artifacts | `E09-AC01..07`, `E11-AC06`, `E11-AC10` |
| SQLite integration/fault injection | migration, WAL checkpoint, concurrent readers, cutoff boundaries, manual clear, disk-full/locked/corrupt/restart | `E08-AC16..18`, `E12-AC10..12` |
| Windows integration | actual OS collectors, unavailable semantics, install/upgrade/uninstall, tray/background/autostart/notifications | `E01-AC01`, `E06-*`, `E10-*` |
| Lifecycle unit/contract/Windows tests | serialization, active-request gates, exact process identity, official CLI/API plans, compatibility matrix, crash/manual recovery | `B01-AC01..05` |
| Secure remote unit/integration/Windows tests | default-off/rotation/revocation, Serve identity+bearer gate, header stripping, `*.ts.net` validation, DNS+TCP probe, DPAPI CurrentUser round-trip, remote resource `unavailable` | `B02-AC01..09` code/test Evidence |
| Paired performance benchmark | baseline vs Inspector, idle and active workloads, throughput/latency/resource deltas | `E12-AC01..06` |
| End-to-end client/backend matrix | at least one supported client fixture against each pinned backend version | `E01-AC02..04`, `E02-*`, `E03-*`, `E04-*`, `E05-*` |

Privacy tests сканируют structured stores и raw files byte-for-byte after DB close/checkpoint, а не только query projections. Pass-through tests сравнивают parsed semantics и event order; raw hop-by-hop transport equality не является корректным reverse-proxy invariant.

## 14. Configuration и secret handling

- Settings имеют schema version, typed validation и atomic write. Unknown security-sensitive keys fail closed; compatible UI-only unknown keys сохраняются только после explicit migration rule.
- Release build не принимает generic ASP.NET hosting configuration, способную изменить bind address. Listener и backend destination проходят отдельные validators.
- Release environment variables не являются primary settings/secret store. Test/dev overrides получают префикс `LLMINSPECTOR_`, allowlist и documentation в Goal, где они появятся.
- Backend credentials и per-install keys защищаются Windows current-user mechanism через abstraction; plaintext не хранится в repository, JSON, SQLite, logs или artifacts.
- Remote access default — disabled. `remote-access.json` schema v1 сохраняется атомарно и содержит только enabled state, timestamp и DPAPI CurrentUser ciphertext; schema/unknown-field/DPAPI mismatch fail closed. Token не принимается через CLI/environment variable и не выводится после creation/rotation.
- Local log level default не включает payloads. Trace mode не может отключить content exclusion.
- No external telemetry/crash endpoint, update channel или remote listener включается неявно.

## 15. Performance and idle benchmark contract

Numeric budgets и reference corpus утверждены в contract `1.2`. Runtime profiles, schema-v1→v2 settings migration, atomic sampling-interval switch, fail-closed gate evaluator и frozen fixture `benchmarks/fixtures/epic12/v1/reference-workloads.json` реализованы в EPIC-12 increment. Fixture digest закреплён automated test; unavailable mandatory metric, protocol короче `5` alternating pairs или idle run короче `10 min + 1 h` не могут дать pass. Это CODE/TEST evidence для механизма, но `E12-AC01..06` не получают completion до controlled measurements каждого built-in profile.

User-facing profiles:

| Profile | Sampling | Purpose |
|---|---:|---|
| `Бережный` | `2 s` | minimum observer impact |
| `Сбалансированный` | `1 s` | default/recommended |
| `Детальный` | `500 ms` | denser diagnostics within higher explicit budget |
| `Свой профиль` | `250 ms`–`10 s` | validated user choice with warning/reset; never release Evidence |

| Gate | Бережный | Сбалансированный | Детальный |
|---|---:|---:|---:|
| Active CPU mean / P95, pp total logical capacity | `1.5 / 4` | `3 / 8` | `5 / 12` |
| Private bytes P95 / growth after warm-up per 30 min | `192 / 16 MiB` | `256 / 32 MiB` | `384 / 64 MiB` |
| GPU delta mean / P95, pp; dedicated VRAM P95 | `1 / 3; 128 MiB` | `2 / 5; 192 MiB` | `3 / 8; 256 MiB` |
| Disk writes | `1 MiB/min` | `2 MiB/min` | `5 MiB/min` |
| Throughput regression median / P95 | `3% / 5%` | `5% / 10%` | `8% / 15%` |
| Idle CPU mean / P95 | `0.25% / 1%` | `0.5% / 2%` | `1% / 4%` |
| Idle RAM growth / disk writes per hour | `8 MiB / 0.25 MiB` | `16 MiB / 1 MiB` | `32 MiB / 5 MiB` |
| Idle wakeups mean / P95 per second | `2 / 8` | `5 / 15` | `15 / 30` |

Воспроизводимый protocol:

1. Зафиксировать Inspector commit/package hash, OS edition/build/update, CPU/RAM, storage, GPU/driver, power plan, backend/version/config, model identity/hash/quantization/context, client/version и Inspector profile.
2. Reference runtime — Ollama `0.33.2`, executable SHA-256 `c79df1e0c1bfa10ed813c7030ac4c3ba38bb0e350bd7322d9bb58320343235c6`; installed community model `orcarouter/Qwen3.8-27B-Uncensored:q4_K_M`, digest `6fac2f98fdf716f292de04c8554681b1e1f3a0d71445e374afebb3433911f705`, GGUF/Q4_K_M, `27.3B`, `17741860746` bytes, fixed context `8192`. Модель не распространяется; size больше `16311 MiB` VRAM, поэтому ожидается hybrid offload.
3. Использовать immutable synthetic corpus без real user content; deterministic seed/output limit, если backend это допускает. Workloads: idle, cold load, hybrid GPU/CPU, CPU-only, streaming/non-streaming, concurrency `1/4`, tools/fragmented stream, collector unavailable/failure. CPU-only может иметь меньший fixed output.
4. Снять `Inspector off` baseline и `Inspector on` после одинакового warm-up; чередовать `AB/BA`, минимум `5` paired repetitions для каждого built-in profile. Idle: `10 min` warm-up + `1 h` measurement.
5. Измерять process-tree CPU/private bytes, RAM growth, disk writes/wakeups, reliable GPU utilization/VRAM, throughput, TTFT и total latency. Throughput regression: `(baseline_rate - inspector_rate) / baseline_rate`; overhead: `(inspector - baseline)` в указанной absolute unit.
6. Report median и P95, применяя оба соответствующих gates. Unavailable mandatory metric не pass. GPU gate обязателен на supported discrete GPU/reliable source. User customization не меняет pass/fail built-in profile.
7. Contaminated run исключается только по predeclared OS update, antivirus, thermal/power throttling или foreign-load signal. Hosted CI ловит gross regressions; canonical release gate выполняется на controlled Windows reference hardware.

## 16. Build, CI и Windows release design

Реализованный toolchain foundation:

- `global.json`: exact `.NET SDK 10.0.400`, `rollForward: disable`, prerelease disabled;
- `Directory.Build.props`: `net10.0`, explicit development `VersionPrefix` `1.0.0`, C# 14, nullable, SDK analyzers, warnings-as-errors, deterministic/CI builds, NuGet audit и lock-file generation;
- `Directory.Packages.props`: Central Package Management с Avalonia `12.1.2`, MSTest `4.3.3` и Microsoft.NET.Test.Sdk `18.9.0`;
- 15 per-project `packages.lock.json` для normal solution graph и 9 `packages.win-x64.lock.json` для RID-specific application/project-reference graph; оба режима подтверждаются отдельными locked restore;
- `LlmInspector.slnx`: 9 production и 6 test projects;
- `NuGet.Config`: единственный configured source `nuget.org` с explicit source mapping.

Executable command contract из repository root:

```powershell
dotnet restore LlmInspector.slnx --locked-mode
dotnet format LlmInspector.slnx --verify-no-changes --no-restore
dotnet build LlmInspector.slnx -c Release --no-restore
dotnet test LlmInspector.slnx -c Release --no-build --logger "console;verbosity=minimal"
dotnet restore src/LlmInspector.App/LlmInspector.App.csproj --locked-mode -r win-x64
dotnet publish src/LlmInspector.App/LlmInspector.App.csproj -c Release -r win-x64 --self-contained true --no-restore -o artifacts/win-x64
.\artifacts\win-x64\LlmInspector.App.exe --smoke-test
```

Последняя terminal merged-main validation подтвердила exact SDK `10.0.400`, locked normal/RID restores, `dotnet format`, Release build без warnings/errors, `260/260` tests без skips, single-file self-contained `win-x64` publish и smoke. PR #27 CI `33840633983` и exact-main CI `33840821568` завершились успешно на merge `d2c3df58fb111ce62968b6144cf58720ab036053`.

Configured CI foundation:

- events: `pull_request` и `push` только в `main`;
- ephemeral standard GitHub-hosted `windows-2025` x64 runner; explicit `contents: read`, no secrets for PR code;
- restore locked → format → build → tests → self-contained publish smoke;
- workflow/job names: `CI` / `windows-dotnet`; фактический run ID/SHA фиксируется только после PR execution;
- repository branch protection/rulesets отсутствовали на `2026-09-02`, поэтому workflow check пока не заявляется как enforced required check;
- external actions pinned by full commit SHA per [`ci-cd-rules.md`](ci-cd-rules.md); policy tests проверяют pins, read-only permissions и отсутствие privileged triggers/secrets/environments;
- artifacts/caches не публикуются; self-contained output существует только в ephemeral job workspace;
- standard hosted runner usage для public repository бесплатен; speculative reruns запрещены без подтверждённой transient причины.

EPIC-01 release automation использует отдельный tag-only workflow с immutable action pins и split permissions. Unprivileged build job принимает только final SemVer tag, проверяет ancestry из единственной trusted line `main`, выполняет полный CI-equivalent, один single-file publish и создаёт checksum/SPDX/manifest/release notes. Privileged publish job не checkout-ит repository, перепроверяет exact downloaded payload, создаёт GitHub/Sigstore build provenance и SBOM attestation и публикует GitHub Release. Historical runs сохраняют Evidence: `v1.0.0-rc.2` run `33815294790` технически завершил pipeline, но artifact был rejected после manual Pro failure; прежняя isolated line получила fix через PR #28 и trusted `v1.0.0-rc.3` run `33842346524`, exact executable SHA-256 `8816be54377101d73030e5a876a61b971f21ec9783c9c36905e1df9054ec2c48`. Checksums, SPDX 2.3, build provenance и SBOM attestation проверены для exact source `821b17abf68bb63dd09f83a834d2d3bdec2e899c`. Subsequent explicit owner decision от `2026-09-04` отменила version branches и prerelease publication; server/runtime CD остаётся disabled.

PR #26 реализовал release-line mapping и прошёл exact-head CI `33839994417`. Его exact-main run `33840160541` обнаружил timing race в synthetic resource-monitor fixture, а не observed runtime regression. PR #27 детерминировал fixture; repeated focused validation, полный local CI-equivalent, exact-head CI `33840633983` и exact-main CI `33840821568` успешны.

Release design:

1. Trusted tag/release flow performs locked restore, build, tests и один self-contained single-file `win-x64` publish; downstream manifest/checksum/SBOM/provenance consume that exact hashed output without rebuild.
2. Development и validation продолжаются на `main` под product version `1.0` без новых public prerelease. Первый следующий release tag — final `v1.0.0` после required validation; все tags immutable.
3. Publish unsigned portable executable, SHA-256, SBOM, provenance and user-facing SmartScreen warning to GitHub Releases. No installer/admin requirement and no automatic update behavior.
4. Verify artifact identity, launch, tray/background, proxy, SQLite recovery and critical end-to-end behavior on Windows 11 `25H2` Home and Pro. Manual results always reference exact artifact hash.
5. После публикации final `v1.0.0` development version переходит к `1.1` в том же `main`; отдельные version branches не создаются по умолчанию.

Microsoft Store/MSIX/trusted signing/automatic Store updates form a separate release backlog and do not block the approved portable channel. No release artifact is a server deployment; applicable Evidence is build/artifact/Windows runtime validation, while `DEPLOY`/`LIVE` remain `N/A` for E01.

## 17. Known risks and deferred decisions

| Risk / decision | State | Gate |
|---|---|---|
| Full process crash interrupts proxy | Accepted for initial modular-monolith baseline | Reconsider sidecar only if crash/fault tests or uptime requirements justify IPC complexity |
| Non-NVIDIA GPU/provider coverage | `BACKLOG` | Current fixed-path NVIDIA source fails closed; multi-device/vendor expansion requires scoped provider and tests |
| Controlled E12 measurements | `PENDING_EXTERNAL_GATE` after harness implementation | Run every built-in profile on exact reference hardware/runtime/model; unavailable mandatory metric is not pass |
| Store signing/MSIX/update | `BACKLOG`, not E01 blocker | Separate owner-approved release Goal after portable channel |
| Portable distribution | `FINAL V1.0.0 PENDING VALIDATION` | Historical exact `v1.0.0-rc.3` pipeline, payload identity, attestations, public smoke and available Pro flows pass; it is not final. Windows Home and remaining required validation gate final `v1.0.0` publication from `main` |
| ARM64 / Windows 11 26H1 | `BACKLOG` | Dedicated build/native dependency/test matrix and owner scope decision |
| Secure remote | `CODE+CI COMPLETE / LIVE PENDING` | PR #24 / merge `538d1f0` / exact-main CI `33822719346` pass; actual Windows↔VPS/second-PC encrypted LIVE Evidence required |
| Default listener port | `IMPLEMENTED` | `5117`; exact loopback bind проверяется integration/runtime Evidence |
| Lifecycle implementation | `READY` | PR #23, merge `7c71fbc`, exact-main CI `33819193701`; `5/5`, SPEC/CODE/TEST/CI `✅` |
| Lifecycle compatibility versions | `PENDING_EXTERNAL_GATE` for two adapters | Ollama `0.33.2` verified locally; llama.cpp `b10516` and LM Studio/lms target versions require actual-runtime Evidence |
| Release attestation action maintenance | `DEFERRED` | `actions/attest-sbom` emitted a deprecation annotation in run `33842346524`; migrate to supported `actions/attest` in a separate authorized CI/CD Goal without weakening the trust boundary |

## 18. Primary evidence sources

- [.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core) and [.NET on supported Windows versions](https://learn.microsoft.com/en-us/dotnet/core/install/windows#supported-versions) — `.NET 10` lifecycle, patching responsibility and Windows 11 `25H2`/`x64` support.
- [Avalonia on Windows](https://docs.avaloniaui.net/docs/platform-specific-guides/windows/) and [TrayIcon](https://docs.avaloniaui.net/docs/reference/controls/tray-icon/) — Win32 runtime model and tray capability.
- [Kestrel endpoint configuration](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel/endpoints?view=aspnetcore-10.0) — loopback bindings and endpoint configuration.
- [NuGet Central Package Management](https://learn.microsoft.com/en-us/nuget/consume-packages/central-package-management) and [dependency lock files](https://learn.microsoft.com/en-us/nuget/consume-packages/package-references-in-project-files#locking-dependencies) — centralized versions and locked restore.
- [SQLite WAL](https://www.sqlite.org/wal.html) — reader/writer/checkpoint/filesystem constraints.
- [Windows client support matrix](https://learn.microsoft.com/en-us/windows/release-health/supported-versions-windows-client) and [Windows 11 26H1 scope](https://learn.microsoft.com/en-us/windows/whats-new/windows-11-version-26h1) — current servicing dates and 26H1 hardware scope.
- [.NET publishing](https://learn.microsoft.com/en-us/dotnet/core/deploying/) — self-contained and RID-specific output.
- [MSIX signing](https://learn.microsoft.com/en-us/windows/msix/package/signing-package-overview) and [Windows distribution paths](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/choose-distribution-path) — signature/trust/timestamp and distribution trade-offs.
- [Ollama OpenAI compatibility](https://docs.ollama.com/api/openai-compatibility) and [native chat fields](https://docs.ollama.com/api/chat) — streaming/tools/usage request support and native timing counters.
- [OpenAI Chat Completions API reference](https://developers.openai.com/api/reference/cli/resources/chat/subresources/completions) — canonical `usage`, streaming `choices[].delta.content` и technical token-detail semantics для compatible wire contract.
- [llama.cpp server](https://github.com/ggml-org/llama.cpp/blob/master/tools/server/README.md) — OpenAI-compatible flow, tools, timings and optional metrics.
- [Ollama FAQ](https://docs.ollama.com/faq) and [generate API](https://docs.ollama.com/api/generate) — documented lifecycle environment allowlist and native model preload/keep-alive operation.
- [LM Studio CLI](https://lmstudio.ai/docs/cli), [server start](https://lmstudio.ai/docs/cli/serve/server-start) and [model load](https://lmstudio.ai/docs/cli/load) — official local server/model lifecycle commands and typed parameters.
- [LM Studio tool streaming](https://lmstudio.ai/docs/developer/openai-compat/tools), [native chat](https://lmstudio.ai/docs/developer/rest/chat) and [native streaming events](https://lmstudio.ai/docs/developer/rest/streaming-events) — fragmented tool calls, terminal stats and exact model-load lifecycle signals.
- [Tailscale Serve](https://tailscale.com/docs/features/tailscale-serve) and [Funnel](https://tailscale.com/docs/features/tailscale-funnel) — private tailnet HTTPS exposure versus explicitly forbidden public exposure.
- [LibreChat custom endpoints](https://www.librechat.ai/docs/quick_start/custom_endpoints) and [`baseURL` contract](https://www.librechat.ai/docs/configuration/librechat_yaml/object_structure/custom_endpoint) — OpenAI-compatible VPS client configuration with environment-owned API key.
- [OpenCode custom provider](https://opencode.ai/docs/providers/#custom-provider), [Hermes providers](https://github.com/hermes-agent-org/hermes/blob/main/website/docs/integrations/providers.md) and [Open WebUI OpenAI-compatible connections](https://docs.openwebui.com/getting-started/quick-start/connect-a-provider/) — configuration surfaces for the existing `/v1/models` and `/v1/chat/completions` contract; actual client compatibility remains a manual gate.
