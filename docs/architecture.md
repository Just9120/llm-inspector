# Architecture baseline

> Status: `DECIDED — NOT IMPLEMENTED`
> Decision scope: `GOAL-002`
> Evidence reviewed: `2026-09-02`

## 1. Evidence boundary

Этот документ задаёт implementation baseline для ратифицированного [`project-spec.md`](project-spec.md), но не является runtime Evidence. На revision, с которой начата `GOAL-002`, source code, dependency manifests, executable commands, tests, workflows, packages и runtime observations отсутствуют. Поэтому принятые ниже решения имеют `SPEC`-силу; `CODE`, `TEST`, `CI` и release Evidence появятся только в отдельно согласованных Goals.

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
| `ADR-007` | `win-x64` self-contained publish как build unit; signed/timestamped MSIX как installable release unit | Self-contained package не требует установленного .NET, но владелец приложения обязан выпускать runtime security updates. MSIX даёт чистые install/uninstall и package integrity; production package нельзя считать release-ready без trusted signature и timestamp. |
| `ADR-008` | CI и Windows release разделены; CD остаётся disabled | Untrusted PR проверяет code без signing secrets. Trusted release job в будущем packages/signs exact validated artifact. Никакого server deploy или LIVE endpoint нет. |

Ни `NativeAOT`, ни trimming не входят в baseline: они допустимы только после compatibility и privacy tests всех UI/serialization/native dependencies. Single-file publish также не является обязательным — native dependencies могут извлекаться на диск и усложнять diagnostics.

## 3. Supported platform matrix

Initial release имеет узкий support contract:

| Platform | Architecture | State | Required release Evidence |
|---|---|---|---|
| Windows 11 `25H2`, Home/Pro, актуальный cumulative update | `x64` | **SUPPORTED / minimum** | Clean install, upgrade, launch, tray/background, proxy, SQLite recovery и critical end-to-end suite на реальной или nested-virtualized Windows installation |
| Windows 11 `26H1` | `ARM64`-oriented selective hardware release | **NOT SUPPORTED in initial release** | Отдельный `win-arm64` design/build/test Goal до добавления в matrix |
| Windows 11 `24H2` Home/Pro | `x64` | **NOT SUPPORTED in initial release** | Исключена: servicing заканчивается `2026-10-13`, до ожидаемого первого product release |
| Windows 10 и более ранние Windows | any | **NOT SUPPORTED** | Out-of-support platform не используется как release gate |
| Windows 11 Enterprise/Education/LTSC | `x64` | **UNVERIFIED / best effort** | Требует owner demand и отдельного edition-specific matrix addition |

Hardware baseline: x64 device должен соответствовать системным требованиям Windows 11 и иметь ресурсы для выбранного пользователем local LLM backend. Dedicated GPU не требуется для запуска Inspector; отсутствие supported GPU/driver metric source даёт `unavailable`, а не application failure. Backend/model hardware sizing находится вне ownership Inspector.

Matrix пересматривается перед каждой release Goal. Новая Windows release не становится supported автоматически: сначала нужны build/runtime tests. Удаление ещё поддерживаемой версии или добавление architecture/edition изменяет durable compatibility contract и требует explicit owner decision.

## 4. Logical components и repository layout

Планируемая solution boundary:

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
 ├─> Adapters ────> Domain
 ├─> Telemetry ───> Application ports + Domain
 ├─> Storage ─────> Application ports + Domain
 ├─> Resources ───> Application ports + Domain
 └─> Diagnostics ─> Application read ports + Domain
```

`Domain` не зависит от UI, HTTP, SQLite или Windows APIs. `Gateway` не пишет в database и не вызывает UI. `Storage` принимает только уже allowlisted domain records. Backend-specific fields остаются namespaced и не проникают в common model без declared semantics/unit.

## 5. Runtime/process model

Один per-user process владеет:

- Avalonia dispatcher, main window и system tray;
- Kestrel loopback listener;
- outbound connection pools к явно настроенным local backends;
- supervised telemetry/resource workers;
- SQLite writer и read connections;
- notification scheduler.

Один instance guard предотвращает конкурирующих writers/listeners. Закрытие main window скрывает UI, но не завершает process; explicit tray `Exit` останавливает listener, перестаёт принимать новые requests, даёт текущим requests bounded graceful drain, flush-ит accepted metadata и закрывает SQLite. Значение drain timeout будет boundary-tested и versioned в configuration; оно не является product performance budget.

Collectors, retention и diagnostics работают как independently supervised background services. Их exception переводит конкретный source в degraded/unavailable, создаёт safe diagnostic event и применяет bounded restart backoff. Ни request relay, ни client response не await-ит database, analytics, resource sampling или UI.

## 6. Network boundary и request flow

Inspector — explicit reverse proxy, не system-wide MITM и не backend lifecycle manager.

```text
OpenAI-compatible client
  │  base URL = http://127.0.0.1:<configured-port>/v1
  ▼
Kestrel loopback gateway
  ├─ validate route + configured backend identity
  ├─ stream request to explicit loopback backend target
  ├─ relay status/headers/body/SSE to client in original semantic order
  └─ tee bounded in-memory bytes to privacy projection
            │
            ▼
  adapter + allowlist normalizer
            │ non-blocking metadata events only
            ▼
  lifecycle queue ─> SQLite writer ─> read models ─> UI/diagnostics
  sample queue ─────> SQLite writer
```

### Forwarding invariants

1. Backend target принимается только из versioned settings и в initial release должен быть `localhost`, `127.0.0.1` или `::1`; normalized destination и redirects не могут выйти из loopback. Remote/LAN target требует backlog authorization.
2. Generic `ASPNETCORE_URLS`, wildcard hostname, `0.0.0.0`, `[::]` и `ListenAnyIP` не могут расширить listener. Port conflict останавливает listener с явной UI error, а не выбирает скрытый alternate endpoint.
3. Hop-by-hop HTTP headers обрабатываются по proxy rules; остальные method/path/query/headers/body и response status/headers/body сохраняются семантически. Inspector не добавляет generation parameters и не заменяет model/tool payload.
4. Request и response bodies relay-ятся streaming; full-body buffering запрещено. Parser получает bounded transient view и не влияет на flow control клиента.
5. SSE event order и bytes внутри relayed data не переупорядочиваются. Fragmented tool-call name может быть assembled только в bounded volatile state; arguments/results проходят к client/backend, но отбрасываются telemetry projection.
6. Client cancellation немедленно propagates к backend. Inspector не replay-ит и не retry-ит generation request после начала forwarding: duplicate inference опаснее явного failure.
7. Backend TLS certificate validation не отключается. Authorization/cookie/proxy-auth headers могут проходить к backend по configured policy, но исключены из logging, metrics, snapshots и exception text.

## 7. Privacy enforcement и threat boundary

Inspector неизбежно видит proxied content в process memory, чтобы переслать его. Privacy promise означает zero persistence/index/log/analytics/snapshot occurrence, а не end-to-end encryption от самого process.

Enforcement выполняется в трёх независимых слоях:

1. `Gateway`: raw body/headers никогда не передаются structured logger; HTTP body logging и framework request logging disabled.
2. `Telemetry`: schema-first allowlist projection создаёт новый metadata record. Запрещённые значения не маскируются post hoc — они отсутствуют в output type.
3. `Storage/Diagnostics`: database schema и snapshot DTO содержат только allowlisted fields; serialization неизвестного поля fail-closed.

Persistent allowlist: timestamps/durations, token counts, normalized model/backend/client identities, generated or pseudonymized correlation IDs, tool names/count/status/duration, HTTP/error categories без raw body, quality/provenance, resource samples, OS/backend/client/driver version identifiers и versioned configuration fingerprints. Backend URL хранится как user label и normalized loopback identity без credentials/query. Client-provided IDs и paths не сохраняются raw; при необходимости стабильной correlation используется per-install keyed pseudonymization.

Всегда запрещены: prompt/response/reasoning text, images/audio, embeddings, tool arguments/results, user code, authorization/cookie values, full request/response/error bodies, raw query strings, arbitrary headers, stack traces с local paths и unsanitized exception messages.

Release configuration не отправляет telemetry, history или settings external services. Crash reporting внешнему service отсутствует. User-created diagnostic snapshot сначала создаётся локально, проходит тот же allowlist/negative corpus и доступен для preview; его дальнейшая передача — отдельное explicit user action вне приложения.

## 8. State ownership, SQLite и retention

| State | Owner | Location / durability |
|---|---|---|
| Versioned non-secret settings | `LlmInspector.App` через Application settings port | `%LOCALAPPDATA%\LLM Inspector\settings.json`, atomic replace, current-user ACL |
| Backend credential, pseudonymization key | Windows credential protector abstraction | Ciphertext/credential reference only; plaintext не попадает в settings/logs/DB |
| Technical history | `LlmInspector.Storage.Sqlite` | `%LOCALAPPDATA%\LLM Inspector\data\inspector.db` + WAL/SHM |
| Volatile active request/session state | `LlmInspector.Application` | Memory only; reconstructed as incomplete/aborted after restart |
| Diagnostic logs | Structured safe logger | Size-bounded rolling files under `%LOCALAPPDATA%\LLM Inspector\logs`, default retention 7 days |
| User-created snapshot | `LlmInspector.Diagnostics` | User-selected local path; not auto-uploaded and not silently indexed |

History schema families: `requests`, `sessions`, `operations`, `turns`, `tool_events`, `resource_samples`, `diagnostic_events`, `version_facts`, `quality_facts`, `schema_migrations`. Raw content/blob columns are forbidden. Derived aggregates are recomputable read models and follow the same privacy/retention boundary.

SQLite rules:

- WAL mode, `foreign_keys=ON`, busy timeout и one-writer queue;
- short transactions; UI analytics uses read-only connections/snapshots;
- schema migrations versioned, forward-only and transactional; listener may run in explicit `history unavailable` degraded mode if migration/storage fails;
- destructive migration требует отдельной Goal, verified backup/recovery path и stop criteria;
- WAL checkpoint выполняется после idle/threshold signal, не на request critical path;
- startup runs integrity diagnostics; повреждённая DB quarantined read-only, не перезаписывается автоматически;
- DB/WAL/SHM backup рассматривается как единый state set; live file copy без SQLite backup mechanism запрещён.

History default retention — `30 days`; user options точно соответствуют `7 days`, `30 days`, `90 days`, `indefinite`. Один cutoff применяется к request/session/operation/tool/resource/derived history, чтобы не оставлять orphan records. Cleanup выполняется bounded batches oldest-first и не блокирует forwarding. Manual clear требует preview scope, confirmation и transaction. Settings, release metadata и user-exported snapshots не удаляются history cleanup.

## 9. Telemetry semantics и quality model

Каждое value хранит `source`, `source_version`, `captured_at`, `unit` и один quality state:

- `exact` — backend/OS API явно сообщила value с совместимой semantics;
- `calculated` — детерминированная формула из exact inputs; formula/version сохраняются;
- `estimated` — estimator с model/version/sample sufficiency; UI явно маркирует estimate;
- `unavailable` — source/capability/association отсутствует или недостоверна; numeric placeholder запрещён.

`calculated` и `estimated` остаются различимыми в storage, даже если UI объединяет их визуально. Common units: duration `ns` internally with documented display conversion, rates `tokens/s`, bytes as integer bytes, utilization `0..100 percent`, temperature `°C`, power `W`. Backend-native field сохраняется в namespaced extension только вместе с provenance и не переименовывается в common metric без semantic mapping.

Correlation использует generated request ID, connection metadata и exact protocol IDs. Time proximity само по себе не доказывает session/tool/process association; ambiguous membership получает `unavailable`.

## 10. Backend capability matrix

Матрица — design input, а не runtime promise. Конкретная backend version и fixture suite определяют capability при implementation; brand-name detection не даёт fabricated support.

| Capability | Ollama | llama.cpp `llama-server` | LM Studio | Normalized behavior |
|---|---|---|---|---|
| OpenAI Chat Completions | `/v1/chat/completions` documented | `/v1/chat/completions` documented; project предупреждает, что full OpenAI compatibility не гарантируется | `/v1/chat/completions` documented | Unknown field проходит transparently; adapter parses only tested subset |
| Non-streaming / streaming | Оба documented | Оба documented | Оба documented | Local total duration/TTFT may be `calculated`; event order contract-tested |
| Tool calls | Documented input support; model dependent | Требует `--jinja`/compatible chat template; parallel calls model/template dependent | Documented; streamed name/arguments fragmented across chunks | Persist tool name/count/status/timing only; arguments/results never persist |
| Token usage | `stream_options.include_usage` accepted; native `/api/chat` exposes token counters | Standard `usage` plus backend `timings` documented | OpenAI flow must be established by versioned fixtures; richer native APIs expose stats | Exact only when present and semantics mapped; otherwise unavailable, not retokenized silently |
| Prompt/generation timing | Native `/api/chat` has prompt/eval durations, but initial OpenAI path cannot assume them | Response `timings` exposes prompt/prediction counts/rates | Native REST v1 events/stats expose TTFT/rate, but OpenAI path cannot assume them | Use backend exact fields only if present in observed flow; local elapsed/TTFT marked calculated |
| Stage/progress | OpenAI flow has no guaranteed load/queue percentages | Optional `/metrics` is aggregate and enabled only by `--metrics`; not per-request proof | Native v1 has load/prompt/tool events; OpenAI flow has less detail | Stage from exact event where available; otherwise protocol-observed stage without percentage |
| Optional probes | Read-only capability/version probes only; no duplicate generation | `/metrics` only when explicitly enabled; aggregate provenance | Native read-only capability/version endpoints only | Probe failure never blocks request and never upgrades per-request attribution |

Adapters не переключают client с OpenAI-compatible protocol на native generation API и не посылают duplicate prompt ради metrics. Native endpoints используются только для read-only capabilities/health или когда будущая explicit protocol Goal добавит их как separate supported flow.

## 11. Resource collectors

`Resources.Windows` exposes independent capability ports for system CPU/RAM, process CPU/RAM/I/O, disk/network and GPU/VRAM/temperature/power. Provider implementations may use supported Windows performance APIs or vendor APIs only after a focused compatibility/security review.

- System-wide sample может быть exact для host, но не автоматически attributed конкретному request.
- Process association требует exact PID/start-time/backend identity; name/time heuristics недостаточны.
- GPU metric содержит device/adapter identity и source. Unsupported counter/driver/device yields `unavailable`.
- Sampling starts/stops from active-request reference count with low-frequency background baseline only when product behavior needs it.
- Collector queues lossy under pressure: sample drop increments a safe gap counter and quality marker; it never backpressures model streaming.

Конкретный GPU provider и sampling interval остаются implementation spike decisions. Они не блокируют repository bootstrap, но обязательны до `EPIC-06 READY` и performance validation.

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
| Proxy integration with stub backend | headers/body/status, cancellation, disconnect, concurrency, slow consumer, malformed SSE, parser failure | `E02-AC12..15`, `E09-AC09..12`, `E12-AC07..09` |
| Automated privacy negative corpus | canary prompt/response/reasoning/tool args/results/code across DB/WAL/logs/snapshot/crash artifacts | `E09-AC01..07`, `E11-AC06`, `E11-AC10` |
| SQLite integration/fault injection | migration, WAL checkpoint, concurrent readers, cutoff boundaries, manual clear, disk-full/locked/corrupt/restart | `E08-AC16..18`, `E12-AC10..12` |
| Windows integration | actual OS collectors, unavailable semantics, install/upgrade/uninstall, tray/background/autostart/notifications | `E01-AC01`, `E06-*`, `E10-*` |
| Paired performance benchmark | baseline vs Inspector, idle and active workloads, throughput/latency/resource deltas | `E12-AC01..06` |
| End-to-end client/backend matrix | at least one supported client fixture against each pinned backend version | `E01-AC02..04`, `E02-*`, `E03-*`, `E04-*`, `E05-*` |

Privacy tests сканируют structured stores и raw files byte-for-byte after DB close/checkpoint, а не только query projections. Pass-through tests сравнивают parsed semantics и event order; raw hop-by-hop transport equality не является корректным reverse-proxy invariant.

## 14. Configuration и secret handling

- Settings имеют schema version, typed validation и atomic write. Unknown security-sensitive keys fail closed; compatible UI-only unknown keys сохраняются только после explicit migration rule.
- Release build не принимает generic ASP.NET hosting configuration, способную изменить bind address. Listener и backend destination проходят отдельные validators.
- Release environment variables не являются primary settings/secret store. Test/dev overrides получают префикс `LLMINSPECTOR_`, allowlist и documentation в Goal, где они появятся.
- Backend credentials и per-install keys защищаются Windows current-user mechanism через abstraction; plaintext не хранится в repository, JSON, SQLite, logs или artifacts.
- Local log level default не включает payloads. Trace mode не может отключить content exclusion.
- No external telemetry/crash endpoint, update channel или remote listener включается неявно.

## 15. Performance and idle benchmark contract

Numeric budgets отсутствуют в ratified source и **не устанавливаются этой architecture Goal**. До их explicit owner approval `EPIC-12 SPEC` остаётся `◐`, а `E12-AC01..06` не могут считаться выполненными.

Воспроизводимый protocol:

1. Зафиксировать Inspector commit/package hash, OS edition/build/update, CPU/RAM, storage, GPU/driver, power plan, backend/version/config, model identity/hash/quantization/context, client/version и Inspector settings.
2. Использовать один immutable synthetic corpus без real user content; поддерживать deterministic seed/output limit, если backend это допускает.
3. Снять `Inspector off` baseline и `Inspector on` run на одной машине после одинакового warm-up; чередовать порядок `AB/BA`, минимум три paired repetitions.
4. Workloads: idle after startup; warm non-streaming; warm streaming; cold model load; one client; concurrent clients; tools with fragmented streaming; collector unavailable/failure.
5. Измерять process CPU time, working set/private bytes, disk read/write bytes and wakeups, available GPU metrics, request total/TTFT, prompt and generation rate, error/drop counts.
6. Report median и P95 per run plus paired delta. Throughput regression: `(baseline_rate - inspector_rate) / baseline_rate`; latency/resource overhead: `(inspector - baseline)` в absolute и relative units.
7. Background activity, antivirus interference, thermal/power throttling и backend cache state фиксировать; contaminated run исключается только по predeclared rule.
8. Performance suite на GitHub-hosted runner может ловить gross regressions, но numeric release gate требует controlled Windows hardware.

Owner должен отдельно утвердить budgets для CPU, RAM, GPU, disk, wakeups, throughput regression и reference hardware/workloads. До этого результаты только measurements, не pass/fail.

## 16. Build, CI и Windows release design

Planned toolchain files для следующей authorized Goal:

- `global.json`: exact current `.NET 10` SDK, `rollForward: disable`, no prerelease;
- `Directory.Build.props`: nullable, analyzers, deterministic build, warnings-as-errors policy;
- `Directory.Packages.props`: Central Package Management, exact package versions;
- `packages.lock.json`: committed executable-app dependency closure;
- `LlmInspector.slnx`: explicit production/test projects.

Planned command contract (сейчас команды **не executable**, потому что files отсутствуют):

```powershell
dotnet restore LlmInspector.slnx --locked-mode
dotnet format LlmInspector.slnx --verify-no-changes --no-restore
dotnet build LlmInspector.slnx -c Release --no-restore
dotnet test LlmInspector.slnx -c Release --no-build
dotnet restore src/LlmInspector.App/LlmInspector.App.csproj --locked-mode -r win-x64
dotnet publish src/LlmInspector.App/LlmInspector.App.csproj -c Release -r win-x64 --self-contained true --no-restore
```

GOAL-003 должна создать минимальный skeleton и доказать/скорректировать команды; до этого repository `AGENTS.md` и CI/CD profile обязаны показывать `UNSET`, а не выдавать plan за working command.

CI design:

- events: `pull_request` и `push` в `main`;
- ephemeral GitHub-hosted Windows runner; explicit `contents: read`, no secrets for PR code;
- restore locked → format → build → tests → self-contained publish smoke;
- required check именуется стабильно только после фактического workflow/ruleset configuration;
- test results и unsigned self-contained publish ZIP могут быть short-retention CI artifacts, identified by repository + exact commit + RID + SDK/dependency lock hash;
- external actions pinned by full commit SHA per [`ci-cd-rules.md`](ci-cd-rules.md).

Release design:

1. Trusted tag/release flow performs locked restore, build, tests и один self-contained `win-x64` publish; subsequent packaging consumes that exact hashed publish output without rebuilding it.
2. Package into MSIX; package identity/version are derived from release metadata, not branch name.
3. Sign with a trusted code-signing identity and trusted timestamp. Signing secret is available only to the trusted release job/environment, never PR jobs.
4. Verify signature, package manifest, clean install, upgrade, launch, proxy smoke and uninstall on the supported Windows matrix.
5. Publish checksum, SBOM/provenance and MSIX to the explicitly approved channel.

Production signing identity and distribution channel remain external gates: Microsoft Store can sign/host a submission; direct MSIX distribution needs an appropriately trusted certificate and hosting, while `.appinstaller` can add update behavior. Automatic updates are not a ratified product feature and will not be implemented implicitly. No release artifact is `LIVE`; applicable Evidence is build/package/install validation, while `DEPLOY`/`LIVE` remain `N/A`.

## 17. Known risks and deferred decisions

| Risk / decision | State | Gate |
|---|---|---|
| Full process crash interrupts proxy | Accepted for initial modular-monolith baseline | Reconsider sidecar only if crash/fault tests or uptime requirements justify IPC complexity |
| GPU/provider coverage and trustworthy process attribution | `DEFER` | Focused Windows collector spike before `EPIC-06` implementation promise |
| Numeric overhead/idle/throughput budgets | `BLOCKER` for `EPIC-12 READY`, not for repository bootstrap | Explicit owner approval after baseline measurements |
| Production signing identity/certificate | `PENDING_EXTERNAL_GATE` for release | Owner selects Store vs trusted direct-signing route |
| Distribution/update channel | `PENDING_EXTERNAL_GATE` for release | Explicit release Goal; no hidden external network behavior |
| ARM64 / Windows 11 26H1 | `BACKLOG` | Dedicated build/native dependency/test matrix and owner scope decision |
| Remote/LAN listener/backend | `BACKLOG` | Threat model, authentication, encryption and DEPLOY/LIVE applicability decision |
| Default port and concrete settings schema | `DEFER` | GOAL-003 implementation tests; loopback-only invariant is already fixed |

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
- [llama.cpp server](https://github.com/ggml-org/llama.cpp/blob/master/tools/server/README.md) — OpenAI-compatible flow, tools, timings and optional metrics.
- [LM Studio tool streaming](https://lmstudio.ai/docs/developer/openai-compat/tools) and [native streaming events](https://lmstudio.ai/docs/developer/rest/streaming-events) — fragmented tool calls and richer native stage/stat signals.
