# Архитектура LLM Inspector

> Актуальный Go runtime, GOAL-006. Дата сверки: 2026-09-05; code `49d1e9aaf48c8b6780803dcc115100e3a2a5b5f7`, [main CI SUCCESS](https://github.com/Just9120/llm-inspector/actions/runs/33975817279).
> Фактическая карта, не замена canonical product contract или TEST/CI/LIVE Evidence.

## Стек и границы

Один Windows x64 process: Go core, Wails v2 host и встроенный русский Svelte/TypeScript frontend под установленным WebView2. `main_windows.go` / `host_windows.go` владеют application lifetime и native dialogs; `internal/desktop` связывает независимые services. C#/.NET/Avalonia исходники удалены при cutover; reference revision `ee32a97fd63110abe7688b48994579d97f8fdb05` доступна в Git.

Exact Go/Node/npm pins — `.go-version`, `.node-version`, `.npm-version`; Go dependencies — `go.mod/go.sum`, npm — `frontend/package-lock.json`. SQLite реализован pure-Go driver. Пользователю не нужны Go/Node/.NET; установленный WebView2 обязателен, auto-download отключён.

Single-instance mutex захватывается до открытия SQLite. Закрытие окна скрывает приложение только при доступном tray; иначе выполняется выход. Explicit exit останавливает producers, drains bounded queues и закрывает storage. Закрытие Inspector не останавливает managed backend скрыто.

## Components и data flow

<a id="3-supported-platform-matrix"></a>

Утверждённая platform matrix — Windows 11 25H2 Home/Pro x64, как в `project-spec.md` §2. Windows 10, 24H2, 26H1/ARM64 и другие editions не получают support автоматически; требуется отдельное согласование. Clean-machine запуск, tray, recovery и backend/client flows на обеих editions остаются manual gate текущего Go executable. Историческая compatibility rationale сохранена в reference revision; она не является Go runtime Evidence.

Client → literal-loopback HTTP gateway → выбранный backend. Telemetry parser наблюдает bytes, но не меняет relay. Request completion → bounded Hub → UI projection / notification worker / SQLite writer; resource timeline публикуется после request observation. UI queries и local exports не находятся в critical forwarding path.

| Package | Ownership / boundary |
|---|---|
| `internal/domain` | Content-free records, metric quality/units/provenance invariants |
| `internal/telemetry` | Bounded incremental JSON/SSE projection: 256-byte token window, depth 64, максимум 64 keys/object; oversized/ambiguous metadata даёт unavailable, relay не изменяется. Values private categories не декодируются; output presence — только boolean |
| `internal/gateway` | Loopback `127.0.0.1`, allowlisted routes, transparent byte relay, cancellation; без environment proxy, decompression, redirects, automatic retry или raw logging. Nonblocking completion channel отделяет forwarding от consumers |
| `internal/state` | Bounded live state/ETA (1024 retained active requests), exact adjacent context delta (1024 sessions, isolated by client/backend), operation/tool graph (1024 operations / 8192 total records). Total active counter независим от display cap; capacity не разрешает остановить busy backend |
| `internal/diagnostics` | Versioned rules/thresholds с русскими explanations, typed Evidence, fact/hypothesis/insufficient_data. CPU offload всегда hypothesis; stall требует explicit request-scoped backend signal |
| `internal/history` | Pure-Go SQLite driver `modernc.org/sqlite v1.58.0`, прежняя schema v5 и UTC timestamp representation; single writer, WAL, read-only snapshots, bounded queues/queries/retention, analytics и runtime/error correlation |
| `internal/artifact` | Explicit `diagnostic-snapshot-v1` / `analytics-export-v1` DTO allowlists; stable legacy field/enum identities, local preview/hash/exact-byte save, без raw domain serialization или upload |
| `internal/resources`, `internal/winhost` | Windows CPU/RAM/process APIs, exact loopback listener PID/start/exe association, bounded NVIDIA CSV; максимум 128 asynchronous request collectors / 2048 samples per request. Remote resources unavailable, host metrics не дублируются по GPU |
| `internal/performance` | Canonical profiles/budgets и fail-closed evaluator; custom не даёт release Evidence, mandatory missing не даёт pass |
| `internal/background` | Совместимые settings v1/v2, atomic save без перезаписи newer/corrupt document; current-user autostart, точный rollback, bounded notification worker, русский native tray на dedicated OS thread и lifetime controls |
| `internal/remote` | Opt-in 256-bit bearer manager, one-time creation/rotation, constant-time verification/revocation; bounded legacy-compatible DPAPI CurrentUser file, atomic persistence и fail-closed error state. Не управляет Tailscale/ACL/Serve |
| `internal/lifecycle` | Сериализованные typed backend operations, exact target confirmation, allowlisted parameters/defaults, embedded compatibility matrix, strict readiness/model identity; Windows handles + unnamed Job Object доказывают ownership до первого выполнения process |
| `internal/desktop` | Testable composition: gateway, settings, optional history, resource monitor, notifications и remote/lifecycle services. Bounded fanout сохраняет observation-before-resource ordering; UI получает detached technical DTO, failures optional services не останавливают proxy |

Go lifecycle создаёт backend suspended, присоединяет его к собственному unnamed Job Object и только затем возобновляет execution. Detached listener принимается лишь при kernel-confirmed membership и exact image/PID/start identity; ранее запущенный LM Studio GUI/daemon не присваивается Inspector. Перед model mutation и после readiness повторно проверяется владелец endpoint. Force — только индивидуальный retained handle с повторной identity/job проверкой, без tree kill/TerminateJobObject. Failed-start cleanup ограничен собственными job members; Close освобождает handles без скрытой остановки backend. CLI stdout/stderr ограничиваются при capture (64 KiB каждый), HTTP — allowlisted literal-loopback paths, без redirects/environment proxy, с ограничением 64 KiB. Unknown version не получает capabilities только за успешный `--version`; read-only help probes дают лишь доказанные CLI operations, не LIVE. Историческая matrix сохранена с явной reference-revision оговоркой.

Ollama model load предварительно проверяет installed ID и подтверждает фактически loaded ID через `/api/ps`, не `/api/tags`. LM Studio использует exact fields из `ls --json` / `ps --json`; GPU `auto` означает отсутствие `--gpu`, а не недокументированное literal value. llama.cpp подтверждает selected GGUF через exact model ID после readiness. Полные пути/CLI text — transient control data, не telemetry/history/export.

Go ingress требует loopback peer. Обычный literal-loopback/localhost Host без identity headers остаётся local flow с неизменным backend Authorization. Remote flow требует private `*.ts.net` Host, ровно одну непустую Serve user identity, включённый authorizer и отдельный bearer token; remote Authorization и proxy identity headers удаляются перед forwarding. Authorizer failure закрывает remote ingress. Explicit remote DNS+TCP probe ограничен 3 секундами, не отправляет HTTP payload и не измеряет TLS/inference/RTT; при cancellation latency unavailable и состояние не застревает в Probing.

LM Studio native `/api/v1/chat` имеет отдельный parser mode: только terminal stats подтверждают cold/warm; OpenAI-compatible flow не получает guessed native metrics. Correlation headers валидируются и удаляются до backend. Request projection считает tools/trailing tool roles без content decode; response projection собирает не более 256 tool names до 128 bytes, хранит finish state и отвергает multi-choice/index ambiguity. Operation grouping требует exact adjacent turns и count-compatible pending tool results; measured duration — calculated call-to-next-turn wall time, не guessed tool execution time.

Go storage повторяет SQL migrations v1–v5, numeric enum codes и `.NET O` UTC representation с 7 fraction digits / `+00:00`, чтобы старые lexical range indexes оставались корректными. Startup quick-check/newer-schema failure не удаляет и не пересоздаёт DB. Проверены legacy migrations, чтение реального WAL от C# test worker и normal/process-kill Go recovery на изолированных fixtures; пользовательская DB не открывалась. Runtime versions остаются nullable technical identifiers. Queue capacity — 256 observations + 16 resource batches по максимум 256 samples; terminal resources подаются после observation, shutdown сначала останавливает producers, затем drains writer, затем закрывает store. Drop/failure counters явны; forwarding не ждёт SQLite.

Snapshot/history bounds — 1000 requests / 5000 resources. Snapshot показывает truncation; analytics/export отказываются строить неполную выборку и предлагают сузить период. Mean/median/nearest-rank P95 имеют minimum sample `3`; recurring error minimum `2`. Finite retention выполняется batches по `500`, timestamp equal cutoff сохраняется. Parent с более свежими CASCADE children остаётся structural anchor до их истечения; cleanup не удаляет newer samples косвенно. Manual clear проверяет digest конкретной выборки в writer transaction, а не только прежние counts. Settings и exported artifacts не затрагиваются.


## Desktop и доверенные действия

Wails bind-ит только narrow `desktop.Facade` и два host endpoints: состояние shell и handshake frontend smoke. Engine, stores, raw process execution и произвольные filesystem APIs не экспортируются в JavaScript. Facade проверяет typed input, guarded confirmation, exact ranges/hash и возвращает content-free DTO.

Frontend разделён на обзор, историю, аналитику, backend и настройки. Русские подписи, metric quality/provenance, раскрываемые детали и независимые busy/error states. State projections — detached plain data; Wails classes используются только для arguments. Polling UI прекращается при скрытии страницы, background monitoring продолжает жить в Go.

CSP и embedded assets не разрешают external fonts/scripts/telemetry; bridge/logger не пишет private payload. Native save/model/executable dialogs ограничивают пользовательский выбор. Export сохраняет exact preview bytes после проверки hash, без network/UNC upload.

Диагностика завершённого запроса использует сохранённый actual nonterminal resource sample именно этого request с явным timestamp. Terminal marker без CPU/GPU не заменяется вымышленными нулями или историческим значением под видом current load. Список current resource samples и diagnostic evidence разделены.

## Хранение и privacy

Versioned policies: `diagnostic-rules-v1` — large prompt ≥8192 tokens, slow generation ≤10 tokens/s, offload hypothesis при process CPU ≥60% и GPU ≤20%, VRAM/context pressure ≥90%, model-load/queue ≥1000 ms. Stall assessment 30000 ms не доказывает stall без explicit request-scoped backend signal. `notification-policy-v1`: одинаковый event key подавляется 15 min, global максимум 3 публикации за 10 min; thresholds и точные границы тестируются в `internal/diagnostics/rules_test.go` и `internal/background/notifications_test.go`. Performance budgets/protocol остаются canonical в `project-spec.md` §3.1.

State directory: `%LOCALAPPDATA%\\LLM Inspector\\`; DB `data/inspector.db`, settings `settings.json`, DPAPI token `remote-access.json`. Ни synthetic tests, ни native smoke не открывают пользовательские settings/history или настоящий backend. Native smoke создаёт собственный временный root и loopback stub.

Migrations v1–v5 закреплены независимыми SHA-256 normalized SQL из C# reference revision; protocol fixtures перенесены без изменения в `internal/telemetry/testdata`. Все exports используют закрытые DTO, не автоматическую сериализацию full domain state. Version facts request-scoped; mixed/unknown values остаются unavailable. Configuration correlation содержит SHA-256 technical identifier, не raw launch options.

## Сборка, проверки и delivery

`scripts/build-windows.ps1` выполняет exact pins, readonly Go dependencies, явный locked npm install, pinned Wails generation/compilation, Svelte/TypeScript/format/Node tests, Go vet/tests и actual Windows executable smoke. Smoke проходит WebView2 bridge, все пять screens, synthetic request/history, custom-profile reactivity и отсутствие private canary в storage.

`eng/release/Test-ReleaseTools.ps1` проверяет local payload и tamper-negative cases без публикации. SPDX содержит linked Go dependencies, Go toolchain/runtime и locked npm build dependencies с разными relationship types. Manifest связывает source SHA, executable SHA/size, frontend lock и prerequisite WebView2.

CI `windows-go` работает на ephemeral `windows-2025` с read-only token. Trusted final-tag release строит executable один раз и передаёт immutable payload отдельному минимально привилегированному publish job. Версия остаётся 1.0; server CD disabled, DEPLOY N/A. Safety/permissions/release gates при stack cutover не изменены. Процедура — [Windows release runbook](runbooks/windows-release.md).

## Ограничения и технический долг

- Native smoke — автоматический isolated runtime test, не Windows Home/Pro manual matrix, не LIVE compatibility реальных backend/client versions.
- E12 budget evaluator и profiles не доказывают фактический CPU/RAM/GPU/disk/throughput/idle overhead: нужны controlled measurements.
- B02 остаётся без encrypted two-host LIVE. Local auth/TLS fixtures не подтверждают внешние ACL/Serve/VPS configuration.
- Один process — простые lifetime/data boundaries, но общий process crash остаётся shared failure domain; persisted history recovery проверен.
- Bounded queues могут терять telemetry при перегрузке: counters видны, proxy не блокируется. History/export limits требуют более узкой выборки.
- GPU probe использует NVIDIA interface; другие devices/sources дают unavailable. Device-wide metrics не доказывают workload attribution.
- Native listener/process ownership, permissions и external runtime CLI зависят от Windows/runtime version; unsupported capability не включается автоматически.
- Локальный race detector недоступен без C compiler; deterministic concurrency/repeat tests не являются его заменой.
- Release workflow ещё не выполнялся для Go public artifact; signing/installer/WinGet/manual tests — отдельные gates. Исторический C# release не подтверждает Go.
- Локальные root/clean checkout дали одинаковый executable hash, но hosted CI hash отличается. Cross-host byte identity не доказана; manual/release Evidence нельзя переносить между hashes. Exact candidate gate остаётся открытым до публикации.
- Автоматический post-deploy metadata writer отсутствует, CD выключен. Для GOAL-006 owner отдельно разрешил этот документационный closeout PR и terminal-комментарий в PR #39 с actual final SHA/CI/cleanup. Это разовое разрешение, не общий metadata write mechanism и не обход protections.

Canonical scope и readiness — [project-spec](project-spec.md); current execution — [delivery-plan](delivery-plan.md); закрытая история — [archive](delivery-plan-archive.md).
