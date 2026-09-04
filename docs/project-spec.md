# LLM Inspector — canonical product contract

> Contract status: `RATIFIED`  
> Contract version: `1.2`
> Ratified by: explicit user instruction от `2026-09-02`  
> Compatibility amendment: approved `GOAL-002` от `2026-09-02`
> Delivery amendments: explicit user decisions от `2026-09-03`–`2026-09-04`
> Source revision verified: `ANLCKQnzyDom5DeN3wi-2dxPnK0emPJPFJh5F8R-W-gdIOdgbB97DBs9sGlbKtyj4pIU-4jyzwSBurSlDjbO0NS94cy-90xjHspDIs7Vmtc`

## 1. Назначение и authority

Этот документ — единственный canonical source of truth для согласованных product scope, features, business rules, acceptance criteria и durable constraints LLM Inspector.

Пользователь явно согласовал все требования из [upstream Google Doc](https://docs.google.com/document/d/1r4o0UiJohJf34j3nL56LWnOxGRi7WDC3jqoDIIjDTnA/edit). Пункты без `[Backlog]` входят в initial release scope. Пункты с `[Backlog]` являются согласованным product backlog и не входят в readiness denominator initial release до отдельной authorization реализации.

Upstream документ остаётся provenance source, но после ратификации не заменяет этот contract. Изменения upstream сами по себе не меняют scope. Изменять требования, business rules, AC или durable constraints можно только по новой explicit user instruction.

## 2. Product boundary и release scope

- LLM Inspector — Windows-first desktop-приложение для мониторинга, аналитики и диагностики локальных LLM.
- Initial release наблюдает локальные backends на том же Windows PC и не управляет lifecycle моделей/backends.
- Приложение помогает понять текущую стадию работы модели, причины медленного ответа или apparent stall и ограничивающий ресурс.
- Пользовательский content не является telemetry: prompt, response, reasoning, tool arguments, tool results и user code не должны попадать в persistence, indexes, logs, analytics или diagnostic snapshot.
- Initial release не требует server deployment. `DEPLOY` и `LIVE` имеют `N/A` в feature epic DoD; Windows package/release validation относится к `E01-AC01`, а не к runtime deployment.
- CD на runtime host отключён. CI и будущий build/release pipeline остаются отдельными concerns.
- Product release `v1.0` остаётся observation-only и не содержит lifecycle commands. Backend/model lifecycle management начинается с `v1.1`; release candidates `v1.0` и `v1.1` валидируются раздельно.
- Текущий distribution target — GitHub Releases: unsigned portable self-contained single-file `win-x64` executable без installer/admin requirement, с SHA-256, SBOM и provenance. Update/download выполняется вручную; SmartScreen warning документируется. Microsoft Store/MSIX/trusted signing/automatic Store updates отложены в отдельный release backlog и не блокируют `E01-AC01`.

### Утверждённая support matrix initial release

- Supported platform: Windows 11 `25H2` Home/Pro с актуальным cumulative update, architecture `x64`.
- Windows 11 `24H2` Home/Pro не входит в matrix: servicing заканчивается `2026-10-13`, до ожидаемого первого product release.
- Windows 11 `26H1`/ARM64, Windows 10, Enterprise/Education/LTSC editions и другие OS/architectures не входят в initial release support matrix; их добавление требует отдельного scope decision и Evidence.
- Inspector не требует dedicated GPU для запуска. Недоступный GPU/driver metric source даёт `unavailable`; hardware sizing самого LLM backend не принадлежит Inspector.
- Release Evidence для supported matrix требует clean install, upgrade, launch, tray/background, proxy, SQLite recovery и critical end-to-end suite на соответствующей Windows installation.

Основание compatibility decision: explicit approval `GOAL-002` от `2026-09-02`; подробная lifecycle rationale и evidence links находятся в [`architecture.md`](architecture.md#3-supported-platform-matrix).

## 3. Cross-cutting business rules

1. **No fabricated telemetry.** Если source не предоставляет metric или attribution недостаточно надёжен, значение показывается как `unavailable`/`Generic/Unknown`.
2. **Metric provenance.** Каждая metric различает `exact`, `calculated/estimated` и `unavailable`.
3. **Transparent pass-through.** Inspector не меняет semantics, structure или order LLM API requests/responses, streaming events и tool calls.
4. **Content privacy.** Content categories, перечисленные в product boundary, запрещены во всех persistent и diagnostic surfaces.
5. **Local by default.** Telemetry, history и settings остаются на текущем PC; local API/telemetry interfaces по умолчанию не доступны из LAN/Internet.
6. **Fault isolation.** Ошибка collector/Inspector не должна обрывать или маскировать client-to-backend request.
7. **Evidence before READY.** Epic получает `🟩 READY` только при выполнении всех своих AC и всех required Evidence.

### 3.1. Утверждённые performance profiles

Пользователь выбирает один из трёх built-in profiles либо создаёт user-friendly custom profile. Sampling interval: `Бережный` — `2 s`, `Сбалансированный` — `1 s` и является default/recommended, `Детальный` — `500 ms`; `Свой профиль` допускает `250 ms`–`10 s`, валидируется, предупреждает о риске overhead и имеет reset. Custom profile не является release Evidence и не может превратить unavailable/failed mandatory metric в pass.

| Metric / gate | Бережный | Сбалансированный | Детальный |
|---|---:|---:|---:|
| Active CPU mean / P95, percentage points total logical capacity | `1.5 / 4` | `3 / 8` | `5 / 12` |
| Process-tree private bytes P95 | `192 MiB` | `256 MiB` | `384 MiB` |
| Active RAM growth after warm-up / 30 min | `16 MiB` | `32 MiB` | `64 MiB` |
| GPU utilization delta mean / P95, percentage points | `1 / 3` | `2 / 5` | `3 / 8` |
| Dedicated VRAM P95 | `128 MiB` | `192 MiB` | `256 MiB` |
| Disk writes | `1 MiB/min` | `2 MiB/min` | `5 MiB/min` |
| Throughput regression median / P95 | `3% / 5%` | `5% / 10%` | `8% / 15%` |
| Idle CPU mean / P95 | `0.25% / 1%` | `0.5% / 2%` | `1% / 4%` |
| Idle RAM growth / hour | `8 MiB` | `16 MiB` | `32 MiB` |
| Idle disk writes / hour | `0.25 MiB` | `1 MiB` | `5 MiB` |
| Idle wakeups mean / P95 per second | `2 / 8` | `5 / 15` | `15 / 30` |

Каждый built-in profile обязан отдельно пройти собственные gates. Controlled Windows protocol: idle после `10 min` warm-up измеряется `1 h`; active measurements используют минимум `5` paired `AB/BA` repetitions; gates применяются к median и указанному P95. Contaminated run исключается только по заранее определённым OS update/antivirus/thermal/foreign-load signals. Mandatory unavailable metric не считается pass. GPU gate обязателен на supported discrete GPU с reliable source. Hosted CI ловит только gross regressions; release Evidence формируется на controlled Windows hardware. TTFT и total latency измеряются, но canonical product gate — throughput regression.

Reference hardware/configuration: Windows 11 Pro `25H2` x64 build `26200.9168`; Ryzen 7 9800X3D (`8C/16T`, max `4700 MHz`); `64 GB` nominal RAM (`61.7 GiB` available); NVIDIA RTX 5060 Ti `16311 MiB`, driver `610.74`, плюс integrated AMD Radeon; system Samsung SSD 970 EVO Plus 1TB NVMe (`931.5 GiB`) для app/DB/fixtures; WDC WD30EZRZ 3TB HDD документируется, но не используется как benchmark storage; Windows Balanced power plan `381b4222-f694-41f0-9685-ff5bb260df2e`. WMI `4 GB` NVIDIA value не используется вместо verified `nvidia-smi` value. Это controlled reference, не minimum hardware requirement.

Reference runtime/model: Ollama `0.33.2`, executable SHA-256 `c79df1e0c1bfa10ed813c7030ac4c3ba38bb0e350bd7322d9bb58320343235c6`; installed community model `orcarouter/Qwen3.8-27B-Uncensored:q4_K_M`, digest `6fac2f98fdf716f292de04c8554681b1e1f3a0d71445e374afebb3433911f705`, GGUF/Q4_K_M, `27.3B`, size `17741860746` bytes, model context capability `262144`, fixed benchmark context `8192`. Model не распространяется с Inspector; из-за размера больше reference VRAM ожидается hybrid offload. Workloads: idle, cold load, hybrid GPU/CPU inference, CPU-only, streaming/non-streaming, concurrency `1/4`, tools/fragmented stream и collector failure на immutable synthetic corpus с deterministic seed/output, когда supported.

### 3.2. Утверждённый lifecycle и compatibility contract

- Managed built-ins: Ollama, llama.cpp и LM Studio; generic observation доступно любому literal-loopback OpenAI-compatible runtime. Extension — capability-based community adapters.
- Start/stop/restart разрешены только для Inspector-owned processes. Externally started backend остаётся observation-only на process level. Model operations используют только official version-pinned interfaces и explicit target; arbitrary commands, arbitrary args/env, privileged service management, wildcard/public bind и automatic backend/model download/install/update запрещены.
- Discovery использует official standard paths/PATH и показывает version/path/endpoint перед подтверждением; fallback — manual file picker. Model enumeration использует official API/CLI, для llama.cpp — explicit `.gguf` picker.
- Runtime parameters ограничены allowlist: Ollama — local port, context, keep-alive, parallel requests, max loaded models, max queue; llama.cpp — local port, context, GPU layers `auto/off/all/N`, CPU threads, parallel slots; LM Studio — local port, context, GPU offload `auto/off/max/0..1`, model TTL, model ID. Unsupported controls unavailable; default — native backend value; reset возвращает backend default.
- Operation сериализуются per backend. Start idempotent; port conflict не убивает occupant. Stop/restart/model switch блокируются при active Inspector requests и показывают их count. Graceful official stop предшествует force; force разрешён только exact PID при совпадении start time и executable identity. Restart использует exact verified executable и last valid config. Readiness проверяется exact endpoint; failed start очищает только partial owned process. Model load успешен только после official identity confirmation. Crash даёт typed `Crashed` и manual one-click restart; auto-restart не входит в scope.
- Canonical machine-readable matrix — `config/runtime-compatibility.json`, встраиваемая в Inspector без unsigned remote updates. Она фиксирует exact runtime version, capabilities, Windows matrix, date, Inspector revision, sanitized Evidence, limitations и status. UI statuses: `Проверено`, `Совместимо`, `Только наблюдение`, `Не поддерживается`. Unknown/newer version допускается operation-by-operation только после safe capability probes и не называется verified; community report имеет `community-reported` до local reproduction.
- First version baselines: Ollama `0.33.2` — `VERIFIED`; llama.cpp `b10516` — target `PENDING_EXTERNAL_GATE`; LM Studio `lms` CLI `0.0.47+` — target, exact app/runtime pin определяется первым test и до него остаётся `PENDING_EXTERNAL_GATE`. Inspector не обновляет runtime автоматически.

### 3.3. Утверждённый remote и conditional-platform contract

- Verified remote transport — Tailscale. Inspector продолжает слушать только `127.0.0.1`; private HTTPS exposure внутри tailnet выполняет Tailscale Serve. Funnel, public Internet, wildcard bind и direct backend port exposure запрещены.
- Remote access выключен по умолчанию. Дополнительно требуется random `256-bit` application bearer token из Windows current-user protected storage; token показывается только при creation/rotation. Tailscale install/login/ACL остаются explicit user operations; Inspector предоставляет wizard/status и не меняет tailnet.
- Backend на другом PC задаётся explicit remote configuration через private encrypted overlay. Network/transport latency отделяется от inference; недоступная remote telemetry не заменяется local attribution. LibreChat использует custom OpenAI endpoint/token. Другие WireGuard/private overlays имеют status `Compatible`, но не first verified profile.
- Для `BACKLOG-02` server CD отсутствует, поэтому `DEPLOY: N/A`; `LIVE: ✅` обязательно через фактический encrypted Windows↔VPS/second-PC test. Недоступность такой среды после merge означает `PENDING_EXTERNAL_GATE`, а не pass.
- Linux/macOS не требуются без подтверждённого demand: `BACKLOG-03` остаётся `BACKLOG`, `0/3` и не блокирует Windows work.
- OpenCode, Hermes и Open WebUI используют уже существующие `/v1/models` и `/v1/chat/completions`; новый protocol сейчас не нужен, поэтому `BACKLOG-04` остаётся `BACKLOG`, `0/2`. Для OpenCode используется `@ai-sdk/openai-compatible`. Configuration examples и automated contract tests входят в релевантные implementation PR; каждый manual client E2E подтверждается отдельно в финальной manual-validation phase, иначе остаётся `PENDING_EXTERNAL_GATE`. `/v1/responses`, Anthropic Messages и Ollama native generation вне текущего scope.

## 4. Status, readiness и Evidence

```text
Status: ⬜ BACKLOG | 🟦 IN PROGRESS | 🟩 READY | ⛔ BLOCKED (modifier)
Evidence: SPEC | CODE | TEST | CI | DEPLOY | LIVE
Evidence status: ✅ | ◐ | ❌ | — | N/A
```

- Completion = число полностью выполненных AC / общее число AC эпика.
- Частичное выполнение AC не даёт дробного кредита: AC либо выполнен, либо нет.
- Initial release readiness использует только `EPIC-01`–`EPIC-12`.
- Full roadmap readiness использует initial release и `BACKLOG-01`–`BACKLOG-06`.
- Для всех feature epics required Evidence: `SPEC`, `CODE`, `TEST`, `CI`. `DEPLOY` и `LIVE`: `N/A`, кроме обязательного `LIVE` для `BACKLOG-02` согласно его DoD.
- `SPEC: ◐` допускается при ратифицированном intent, но открытом измеримом threshold/compatibility decision; такой epic не может стать READY до закрытия gap.

## 5. Initial release epics

### EPIC-01 — Windows application и границы первой версии

**Status:** 🟦 IN PROGRESS
**Goal:** предоставить Windows desktop application для observation/analytics/diagnostics локальных LLM без lifecycle management.

Acceptance criteria:

- `E01-AC01`: Application распространяется и запускается как desktop application на каждой Windows version из утверждённой support matrix. [source: `UP-C01-01`]
- `E01-AC02`: Desktop UI содержит доступные пользователю monitoring, analytics и diagnostics surfaces. [source: `UP-C01-01`, `UP-C01-02`]
- `E01-AC03`: Initial release подключается к LLM backends на том же Windows PC. [source: `UP-C01-04`]
- `E01-AC04`: Initial release не содержит команд запуска, остановки, restart, загрузки моделей или изменения backend runtime parameters. [source: `UP-C01-03`]

Definition of Done: `4/4` AC, SPEC/CODE/TEST/CI `✅`, DEPLOY/LIVE `N/A`.

### EPIC-02 — Backend/client adapters и OpenAI-compatible API

**Status:** 🟩 READY

**Goal:** наблюдать обязательные local backends и compatible clients через штатную endpoint configuration.

Acceptance criteria:

- `E02-AC01`: Ollama adapter сохраняет основную request telemetry для supported OpenAI-compatible flow. [source: `UP-C02-01`]
- `E02-AC02`: llama.cpp adapter сохраняет основную request telemetry для supported OpenAI-compatible flow. [source: `UP-C02-02`]
- `E02-AC03`: LM Studio adapter сохраняет основную request telemetry для supported OpenAI-compatible flow. [source: `UP-C02-03`]
- `E02-AC04`: Одинаково названные common metrics имеют одинаковые units и semantics во всех трёх adapters. [source: `UP-C02-04`]
- `E02-AC05`: Backend-specific telemetry остаётся доступной и не маскируется искусственной normalization. [source: `UP-C02-04`]
- `E02-AC06`: Metric, не предоставленная backend и не вычислимая доказуемо, отображается `unavailable`, без fabricated numeric value. [source: `UP-C02-05`]
- `E02-AC07`: OpenCode Desktop распознаётся как known client при наличии достоверного attribution evidence. [source: `UP-C02-06`]
- `E02-AC08`: Hermes Desktop распознаётся как known client при наличии достоверного attribution evidence. [source: `UP-C02-06`]
- `E02-AC09`: Cline распознаётся как known client при наличии достоверного attribution evidence. [source: `UP-C02-06`]
- `E02-AC10`: Open WebUI распознаётся как known client при наличии достоверного attribution evidence. [source: `UP-C02-06`]
- `E02-AC11`: Compatible client без достоверной known-client attribution отображается как `Generic/Unknown`, сохраняя основную request telemetry. [source: `UP-C02-07`]
- `E02-AC12`: Подключение supported client выполняется штатной настройкой provider/API endpoint и не требует fork или source modification client. [source: `UP-C02-08`]
- `E02-AC13`: Поддержан non-streaming OpenAI-compatible Chat Completions request/response flow. [source: `UP-C02-11`]
- `E02-AC14`: Поддержан streaming OpenAI-compatible Chat Completions flow без нарушения event order. [source: `UP-C02-11`]
- `E02-AC15`: Поддержан OpenAI-compatible tool-calling flow без нарушения call/result order. [source: `UP-C02-11`]

Definition of Done: `15/15` AC, SPEC/CODE/TEST/CI `✅`, DEPLOY/LIVE `N/A`.

### EPIC-03 — Live state и качество telemetry

**Status:** 🟩 READY
**Goal:** показывать реальное состояние active request без fabricated progress.

Acceptance criteria:

- `E03-AC01`: Для каждого active request UI показывает одну текущую stage и elapsed time. [source: `UP-C01-02`, `UP-C03-01`, `UP-C03-02`]
- `E03-AC02`: State model различает `model loading`. [source: `UP-C03-01`]
- `E03-AC03`: State model различает `queue/waiting`. [source: `UP-C03-01`]
- `E03-AC04`: State model различает `prompt processing`. [source: `UP-C03-01`]
- `E03-AC05`: State model различает `reasoning/generation`. [source: `UP-C03-01`]
- `E03-AC06`: State model различает `tool wait`. [source: `UP-C03-01`]
- `E03-AC07`: State model различает успешное completion. [source: `UP-C03-01`]
- `E03-AC08`: State model различает cancellation. [source: `UP-C03-01`]
- `E03-AC09`: State model различает error. [source: `UP-C03-01`]
- `E03-AC10`: ETA показывается только когда estimator сообщает достаточно данных; UI явно маркирует ETA как estimate. [source: `UP-C03-02`]
- `E03-AC11`: Progress percentage показывается только при достоверном backend progress signal. [source: `UP-C03-03`]
- `E03-AC12`: Без достоверного progress signal UI показывает stage без percentage. [source: `UP-C03-03`]
- `E03-AC13`: Каждая displayed metric содержит quality state `exact`, `calculated/estimated` или `unavailable`. [source: `UP-C03-04`]

Definition of Done: `13/13` AC, SPEC/CODE/TEST/CI `✅`, DEPLOY/LIVE `N/A`.

### EPIC-04 — Tokens, context и timings

**Status:** 🟩 READY
**Goal:** объяснять объём prompt/context и latency decomposition по доступным backend data.

Acceptance criteria:

- `E04-AC01`: При наличии source data UI показывает input, output и cached token counts раздельно. [source: `UP-C03-05`]
- `E04-AC02`: При наличии source data UI показывает current context usage и context limit. [source: `UP-C03-05`]
- `E04-AC03`: Для последовательных agent turns отображается изменение context size. [source: `UP-C03-06`]
- `E04-AC04`: При наличии доказуемых данных context breakdown показывает вклад history, tools и cache в следующий prompt; недоступные части маркируются unavailable. [source: `UP-C03-06`]
- `E04-AC05`: Reasoning token count может отображаться только как technical metric; reasoning content не извлекается и не сохраняется. [source: `UP-C03-07`]
- `E04-AC06`: При наличии данных отображается prompt/prefill speed. [source: `UP-C03-08`]
- `E04-AC07`: При наличии данных отображается generation speed. [source: `UP-C03-08`]
- `E04-AC08`: При наличии данных отображается TTFT. [source: `UP-C03-08`]
- `E04-AC09`: При наличии данных отображается model load time. [source: `UP-C03-08`]
- `E04-AC10`: При наличии данных отображается queue time. [source: `UP-C03-08`]
- `E04-AC11`: Для каждого завершённого request отображается total duration. [source: `UP-C03-08`]
- `E04-AC12`: Analytics и request detail различают cold start/model load и warm request. [source: `UP-C03-09`]

Definition of Done: `12/12` AC, SPEC/CODE/TEST/CI `✅`, DEPLOY/LIVE `N/A`.

### EPIC-05 — Agent operations, tools и concurrency

**Status:** 🟩 READY
**Goal:** представлять multi-turn agent workflow как изолированную ordered operation.

Acceptance criteria:

- `E05-AC01`: Связанные user request, LLM turns, tool lifecycle и final response группируются в одну agent/session operation. [source: `UP-C03-10`]
- `E05-AC02`: Внутри operation сохраняется технический порядок turns, tool calls/results и final completion без сохранения content. [source: `UP-C03-10`]
- `E05-AC03`: Operation показывает количество tools, доступных client/model на соответствующем turn. [source: `UP-C03-11`]
- `E05-AC04`: Operation показывает количество фактически вызванных tools. [source: `UP-C03-11`]
- `E05-AC05`: Для tool call сохраняются duration, status и error category без arguments/result content. [source: `UP-C03-11`]
- `E05-AC06`: Параллельные requests имеют отдельные telemetry records и correlation IDs. [source: `UP-C03-12`]
- `E05-AC07`: Параллельные clients/sessions не смешивают operation membership или history. [source: `UP-C03-12`]
- `E05-AC08`: Неоднозначная correlation маркируется unknown/unavailable и не объединяет operations предположительно. [source: `UP-C03-12`]

Definition of Done: `8/8` AC, SPEC/CODE/TEST/CI `✅`, DEPLOY/LIVE `N/A`.

### EPIC-06 — System resource telemetry

**Status:** 🟩 READY
**Goal:** коррелировать system resource load с request timeline без недостоверной attribution.

Acceptance criteria:

- `E06-AC01`: Во время inference собираются time-series GPU utilization, VRAM, temperature и power для доступного primary GPU source. [source: `UP-C04-01`]
- `E06-AC02`: Во время inference собираются time-series CPU utilization и RAM usage. [source: `UP-C04-01`]
- `E06-AC03`: Resource samples имеют timestamps, позволяющие совместить их с request timeline. [source: `UP-C04-01`, `UP-C04-05`]
- `E06-AC04`: Related processes показываются только при доказуемой process association; иначе association unavailable. [source: `UP-C04-02`]
- `E06-AC05`: Per-process resource metrics показываются только при достоверном Windows/driver source. [source: `UP-C04-02`]
- `E06-AC06`: Disk I/O доступен на request timeline, когда source data помогает объяснить model loading/bottleneck. [source: `UP-C04-04`]
- `E06-AC07`: Network I/O доступен на request timeline, когда source data помогает объяснить client/backend traffic. [source: `UP-C04-04`]
- `E06-AC08`: UI позволяет сопоставить изменение resource load с конкретным request и stage либо явно показывает отсутствие достоверной correlation. [source: `UP-C04-05`]

Definition of Done: `8/8` AC, SPEC/CODE/TEST/CI `✅`, DEPLOY/LIVE `N/A`.

### EPIC-07 — Explainable diagnostics и errors

**Status:** 🟩 READY
**Goal:** объяснять bottlenecks и errors на основе наблюдаемых metrics, отделяя facts от hypotheses.

Acceptance criteria:

- `E07-AC01`: Diagnostic ruleset умеет выявлять large prompt по явному versioned rule/threshold. [source: `UP-C05-01`]
- `E07-AC02`: Diagnostic ruleset умеет выявлять slow generation по явному versioned rule/threshold. [source: `UP-C05-01`]
- `E07-AC03`: Diagnostic ruleset умеет выявлять CPU offload при наличии supporting telemetry. [source: `UP-C05-01`]
- `E07-AC04`: Diagnostic ruleset умеет выявлять VRAM pressure при наличии supporting telemetry. [source: `UP-C05-01`]
- `E07-AC05`: Diagnostic ruleset умеет выявлять model loading как источник latency. [source: `UP-C05-01`]
- `E07-AC06`: Diagnostic ruleset умеет выявлять queue/waiting как источник latency. [source: `UP-C05-01`]
- `E07-AC07`: Diagnostic ruleset умеет выявлять high context usage по явному versioned rule/threshold. [source: `UP-C05-01`]
- `E07-AC08`: Diagnostic ruleset умеет выявлять unavailable backend. [source: `UP-C05-01`]
- `E07-AC09`: Диагностика различает confirmed stall и продолжающиеся prompt processing/generation; без достаточных signals stall не объявляется fact. [source: `UP-C01-02`, `UP-C05-02`]
- `E07-AC10`: Каждый diagnostic conclusion содержит human-readable explanation. [source: `UP-C05-03`]
- `E07-AC11`: Каждый diagnostic conclusion ссылается на supporting technical metrics/rules; отсутствие evidence показано явно. [source: `UP-C05-03`]
- `E07-AC12`: Неподтверждённая причина маркируется hypothesis или insufficient data, а не fact. [source: `UP-C05-04`]
- `E07-AC13`: Error model различает connection refused, model loading/503, HTTP/API error, timeout, context overflow, cancellation и backend crash. [source: `UP-C05-05`]
- `E07-AC14`: Client/backend errors коррелируются по time/correlation metadata без чтения content; недоказанная связь не утверждается. [source: `UP-C05-06`]
- `E07-AC15`: UI отличает recurring error group от единичного failure. [source: `UP-C05-07`]
- `E07-AC16`: Analytics показывает изменение частоты recurring error по выбранному периоду. [source: `UP-C05-07`]

Definition of Done: `16/16` AC, SPEC/CODE/TEST/CI `✅`, DEPLOY/LIVE `N/A`.

### EPIC-08 — Local history, analytics и retention

**Status:** 🟩 READY
**Goal:** хранить и анализировать только technical metadata с управляемым retention.

Acceptance criteria:

- `E08-AC01`: Local history хранит technical metadata request records. [source: `UP-C06-01`]
- `E08-AC02`: Local history хранит technical metadata session records. [source: `UP-C06-01`]
- `E08-AC03`: Local history хранит technical metadata agent operation records без content categories. [source: `UP-C06-01`]
- `E08-AC04`: History filters поддерживают period, client, backend, model, session, status и error type. [source: `UP-C06-02`]
- `E08-AC05`: Operation detail показывает ordered turns/tools, timings, errors и resource load metadata. [source: `UP-C06-03`]
- `E08-AC06`: Period analytics показывает trends input/output tokens, TTFT, prompt/generation speed, context usage, resource load и errors. [source: `UP-C06-04`]
- `E08-AC07`: Для key latency/performance metrics вычисляется arithmetic mean. [source: `UP-C06-05`]
- `E08-AC08`: Для key latency/performance metrics вычисляется median. [source: `UP-C06-05`]
- `E08-AC09`: Для key latency/performance metrics вычисляется P95 по документированному percentile method. [source: `UP-C06-05`]
- `E08-AC10`: Minimum sample policy документирована и boundary-tested; при недостаточной sample aggregate не выдаётся как статистически достаточный. [source: `UP-C06-05`]
- `E08-AC11`: Analytics сравнивает два выбранных periods. [source: `UP-C06-06`]
- `E08-AC12`: Analytics сравнивает выбранные models. [source: `UP-C06-06`]
- `E08-AC13`: Analytics сравнивает выбранные backends. [source: `UP-C06-06`]
- `E08-AC14`: Analytics сравнивает выбранные clients. [source: `UP-C06-06`]
- `E08-AC15`: Comparison view выделяет performance degradation, подтверждённую выбранной metric и baseline. [source: `UP-C06-06`]
- `E08-AC16`: Retention setting предлагает 7 days, 30 days, 90 days и indefinite. [source: `UP-C06-07`]
- `E08-AC17`: Records старше выбранного finite retention автоматически удаляются без удаления более новых records. [source: `UP-C06-08`]
- `E08-AC18`: Пользователь может вручную очистить local history с явным подтверждением scope операции. [source: `UP-C06-08`]

Definition of Done: `18/18` AC, SPEC/CODE/TEST/CI `✅`, DEPLOY/LIVE `N/A`.

### EPIC-09 — Privacy, locality и transparent proxying

**Status:** 🟩 READY
**Goal:** доказуемо исключить user content из data surfaces и сохранить API semantics.

Acceptance criteria:

- `E09-AC01`: Prompt content не сохраняется, не индексируется и не пишется в application logs. [source: `UP-C07-01`]
- `E09-AC02`: Response content не сохраняется, не индексируется и не пишется в application logs. [source: `UP-C07-01`]
- `E09-AC03`: Reasoning content не сохраняется, не индексируется и не пишется в application logs. [source: `UP-C07-01`]
- `E09-AC04`: Tool arguments не сохраняются, не индексируются и не пишутся в application logs. [source: `UP-C07-01`]
- `E09-AC05`: Tool results не сохраняются, не индексируются и не пишутся в application logs. [source: `UP-C07-01`]
- `E09-AC06`: History/analytics schema разрешает только timings, tokens, model/backend/client, tool names, statuses, errors и resource metrics либо явно ратифицированное расширение allowlist. [source: `UP-C07-02`]
- `E09-AC07`: По default configuration telemetry, history и settings не передаются external services. [source: `UP-C07-04`]
- `E09-AC08`: Local API/telemetry listeners по default bind policy недоступны из LAN и Internet. [source: `UP-C07-05`]
- `E09-AC09`: Inspector сохраняет request semantics, structure и order при pass-through. [source: `UP-C07-07`]
- `E09-AC10`: Inspector сохраняет response semantics, structure и order при pass-through. [source: `UP-C07-07`]
- `E09-AC11`: Inspector сохраняет meaning и order streaming events. [source: `UP-C07-07`]
- `E09-AC12`: Inspector сохраняет meaning, structure и order tool calls/results. [source: `UP-C07-07`]
- `E09-AC13`: UI перечисляет категории technical data, сохраняемые приложением. [source: `UP-C07-08`]
- `E09-AC14`: UI показывает retention, применяемый к каждой persistent category или dataset. [source: `UP-C07-08`]

Definition of Done: `14/14` AC, включая automated negative privacy/pass-through tests; SPEC/CODE/TEST/CI `✅`, DEPLOY/LIVE `N/A`.

### EPIC-10 — Background operation и Windows notifications

**Status:** 🟩 READY
**Goal:** продолжать monitoring в фоне и уведомлять без спама.

Acceptance criteria:

- `E10-AC01`: Закрытие main window не прекращает active monitoring process. [source: `UP-C08-01`]
- `E10-AC02`: После закрытия main window новые technical history records продолжают сохраняться. [source: `UP-C08-01`]
- `E10-AC03`: Пользователь может открыть приложение и основные background controls из Windows system tray. [source: `UP-C08-01`]
- `E10-AC04`: Пользователь может включить Windows autostart в settings. [source: `UP-C08-02`]
- `E10-AC05`: Пользователь может выключить ранее включённый Windows autostart в settings. [source: `UP-C08-02`]
- `E10-AC06`: Notification settings отдельно управляют событиями backend unavailable, long operation completion, recurring error и high context usage. [source: `UP-C08-03`]
- `E10-AC07`: Notifications имеют silent mode без звука. [source: `UP-C08-04`]
- `E10-AC08`: Повтор одного события проходит через documented and boundary-tested deduplication/rate-limit policy. [source: `UP-C08-04`]

Definition of Done: `8/8` AC, SPEC/CODE/TEST/CI `✅`, DEPLOY/LIVE `N/A`.

### EPIC-11 — Anonymized diagnostic snapshot

**Status:** 🟩 READY
**Goal:** создавать локально проверяемый diagnostic artifact, безопасный для передачи человеку или coding agent.

Acceptance criteria:

- `E11-AC01`: Пользователь может создать diagnostic snapshot без подключения к external service. [source: `UP-C09-01`]
- `E11-AC02`: Snapshot содержит доступные OS, driver, backend и client versions с unavailable markers для отсутствующих values. [source: `UP-C09-01`]
- `E11-AC03`: Snapshot содержит model identifier и allowlisted runtime metadata. [source: `UP-C09-01`]
- `E11-AC04`: Snapshot содержит relevant error metadata. [source: `UP-C09-01`]
- `E11-AC05`: Snapshot содержит системные metrics только за выбранный relevant interval. [source: `UP-C09-01`]
- `E11-AC06`: Snapshot содержит zero occurrences prompt, response, reasoning, tool arguments, tool results и user code по negative-test corpus. [source: `UP-C07-03`, `UP-C09-02`]
- `E11-AC07`: До передачи snapshot пользователь может локально просмотреть его состав. [source: `UP-C09-02`]
- `E11-AC08`: Snapshot можно сформировать по выбранному time range. [source: `UP-C09-03`]
- `E11-AC09`: Snapshot можно сформировать по выбранной operation. [source: `UP-C09-03`]
- `E11-AC10`: Snapshot schema versioned и ограничена documented allowlist полей. [source: `UP-C09-02`]

Definition of Done: `10/10` AC, включая automated negative privacy tests; SPEC/CODE/TEST/CI `✅`, DEPLOY/LIVE `N/A`.

### EPIC-12 — Reliability, overhead и regression correlation

**Status:** 🟦 IN PROGRESS
**Goal:** не ухудшать inference заметно, изолировать collector failures и сохранять историю корректной.

Acceptance criteria:

- `E12-AC01`: Sustained monitoring CPU overhead не превышает утверждённый performance budget в reference workload. [source: `UP-C10-01`]
- `E12-AC02`: Sustained monitoring RAM overhead не превышает утверждённый performance budget в reference workload. [source: `UP-C10-01`]
- `E12-AC03`: Sustained monitoring GPU overhead не превышает утверждённый performance budget в reference workload. [source: `UP-C10-01`]
- `E12-AC04`: Sustained monitoring disk overhead не превышает утверждённый performance budget в reference workload. [source: `UP-C10-01`]
- `E12-AC05`: Inspector не снижает model throughput больше утверждённого performance budget в paired benchmark. [source: `UP-C10-01`]
- `E12-AC06`: При отсутствии active requests CPU/RAM/disk wakeups остаются в утверждённом idle budget. [source: `UP-C10-02`]
- `E12-AC07`: Failure одного collector не отменяет и не разрывает proxied LLM request. [source: `UP-C10-03`]
- `E12-AC08`: Unavailability одного metric source не отменяет и не разрывает proxied LLM request. [source: `UP-C10-03`]
- `E12-AC09`: Error origin различает Inspector, client, backend и model; unknown origin не маскируется под конкретный component. [source: `UP-C10-04`]
- `E12-AC10`: Restart после crash/normal exit не повреждает ранее committed history. [source: `UP-C10-05`]
- `E12-AC11`: После restart приложение принимает и сохраняет новые telemetry records. [source: `UP-C10-05`]
- `E12-AC12`: При наличии data history сохраняет backend, client, model, GPU driver versions и key runtime configuration identifiers без user content. [source: `UP-C10-06`]
- `E12-AC13`: Analytics позволяет связать performance degradation или error-frequency growth с version/runtime-config change либо явно показывает недостаточность correlation data. [source: `UP-C10-07`]

Definition of Done: `13/13` AC, утверждённые numeric performance/idle budgets, SPEC/CODE/TEST/CI `✅`, DEPLOY/LIVE `N/A`.

## 6. Canonical product backlog epics

Backlog requirements согласованы и не входят в initial release readiness. `BACKLOG-01` и `BACKLOG-02` авторизованы в активной `GOAL-005`; `BACKLOG-03`/`BACKLOG-04` остаются conditional backlog, `BACKLOG-05`/`BACKLOG-06` уже завершены.

### BACKLOG-01 — Backend/model lifecycle management

**Status:** ⬜ BACKLOG

- `B01-AC01`: Пользователь может запустить supported backend. [source: `UP-C01-05`]
- `B01-AC02`: Пользователь может остановить supported backend. [source: `UP-C01-05`]
- `B01-AC03`: Пользователь может restart supported backend. [source: `UP-C01-05`]
- `B01-AC04`: Пользователь может инициировать загрузку supported model. [source: `UP-C01-05`]
- `B01-AC05`: Пользователь может изменять allowlisted runtime parameters с validation и explicit target identity. [source: `UP-C01-05`]

Definition of Done: `5/5` AC, SPEC/CODE/TEST/CI `✅`, DEPLOY/LIVE `N/A`.

### BACKLOG-02 — Secure remote/LAN connectivity

**Status:** ⬜ BACKLOG

- `B02-AC01`: Inspector наблюдает backend на другом PC при explicit remote configuration. [source: `UP-C01-06`]
- `B02-AC02`: Inspector наблюдает backend в LAN без assumption, что transport latency равна inference latency. [source: `UP-C01-06`]
- `B02-AC03`: Remote backend availability имеет отдельное observable state. [source: `UP-C01-06`]
- `B02-AC04`: LibreChat/другой supported web client на VPS может подключиться к local Inspector. [source: `UP-C02-09`]
- `B02-AC05`: Remote client connection использует authenticated and encrypted channel. [source: `UP-C02-09`, `UP-C07-06`]
- `B02-AC06`: Remote flow показывает network latency отдельно от inference latency. [source: `UP-C02-10`]
- `B02-AC07`: Remote access выключен по умолчанию и включается только explicit user action. [source: `UP-C07-06`]
- `B02-AC08`: Enabling remote access не публикует LLM backend endpoint напрямую в Internet. [source: `UP-C07-06`]
- `B02-AC09`: При недоступности remote telemetry component request data не получает fabricated local attribution. [source: `UP-C02-10`]

Definition of Done: `9/9` AC, security/threat-model review и SPEC/CODE/TEST/CI `✅`, `DEPLOY: N/A`, `LIVE: ✅` по фактическому encrypted Windows↔VPS/second-PC test.

### BACKLOG-03 — Linux и macOS

**Status:** ⬜ BACKLOG

- `B03-AC01`: После подтверждённого product demand приложение имеет поддерживаемый Linux distribution target. [source: `UP-C01-07`]
- `B03-AC02`: После подтверждённого product demand приложение имеет поддерживаемый macOS distribution target. [source: `UP-C01-07`]
- `B03-AC03`: Добавление platforms не отменяет Windows-first support и regression gates. [source: `UP-C01-07`]

Definition of Done: `3/3` AC, platform matrices и SPEC/CODE/TEST/CI `✅`; DEPLOY/LIVE `N/A` unless redefined by release DoD.

### BACKLOG-04 — Дополнительные API protocols

**Status:** ⬜ BACKLOG

- `B04-AC01`: Новый protocol добавляется только при подтверждённом использовании supported client. [source: `UP-C02-12`]
- `B04-AC02`: Добавленный protocol сохраняет cross-cutting telemetry quality, privacy и transparent pass-through rules. [source: `UP-C02-12`]

Definition of Done: `2/2` AC, SPEC/CODE/TEST/CI `✅`, DEPLOY/LIVE `N/A`.

### BACKLOG-05 — Multiple GPUs

**Status:** 🟩 READY

- `B05-AC01`: Inspector обнаруживает несколько supported GPU devices. [source: `UP-C04-03`]
- `B05-AC02`: Resource UI показывает metrics раздельно для каждого device. [source: `UP-C04-03`]
- `B05-AC03`: Workload-to-device attribution показывается только при достоверном source, иначе unavailable. [source: `UP-C04-03`]

Definition of Done: `3/3` AC, SPEC/CODE/TEST/CI `✅`, DEPLOY/LIVE `N/A`.

### BACKLOG-06 — Export analytics

**Status:** 🟩 READY

- `B06-AC01`: Пользователь экспортирует выбранный range anonymized technical history. [source: `UP-C09-04`]
- `B06-AC02`: Пользователь экспортирует aggregate metrics за выбранный range. [source: `UP-C09-04`]
- `B06-AC03`: Export проходит тот же negative content corpus, что diagnostic snapshot, и не содержит request/response content. [source: `UP-C09-04`]

Definition of Done: `3/3` AC, SPEC/CODE/TEST/CI `✅`, DEPLOY/LIVE `N/A`.

## 7. Current readiness и Evidence

Фактическое branch-local состояние пересчитано с нуля по 164 atomic product AC. Exact maintenance base `release/v1.0` / `v1.0.0-rc.2` source `8aae2fbdd69ae8d8d2c1dd0b4796d1bea6883479` подтверждает EPIC-01 `3/4`, EPIC-02–EPIC-11 полностью, EPIC-12 `E12-AC07..13`, BACKLOG-05 `3/3` и BACKLOG-06 `3/3`. Public `rc.2` publication/SBOM/provenance успешны, но exact artifact на Windows 11 Pro `25H2` build `26200.9168` падает в critical proxy flow из-за неверного tray P/Invoke entry point; `E01-AC01` не кредитуется. Current branch переносит isolated exact-entry-point fix и release-line infrastructure, но не получает credit до reviewed merge, нового exact candidate и Home/Pro matrix. `E12-AC01..06`, `B01-*` и `B02-*` также не кредитуются в этой observation-only lineage без required Evidence; поздние B01/B02 commits из `main` намеренно не backport-ятся.

| Epic | Status | Completed / total | Readiness | SPEC | CODE | TEST | CI | DEPLOY | LIVE |
|---|---|---:|---:|---|---|---|---|---|---|
| EPIC-01 | 🟦 IN PROGRESS | 3/4 | 75% | ✅ | ◐ | ❌ | ✅ | N/A | N/A |
| EPIC-02 | 🟩 READY | 15/15 | 100% | ✅ | ✅ | ✅ | ✅ | N/A | N/A |
| EPIC-03 | 🟩 READY | 13/13 | 100% | ✅ | ✅ | ✅ | ✅ | N/A | N/A |
| EPIC-04 | 🟩 READY | 12/12 | 100% | ✅ | ✅ | ✅ | ✅ | N/A | N/A |
| EPIC-05 | 🟩 READY | 8/8 | 100% | ✅ | ✅ | ✅ | ✅ | N/A | N/A |
| EPIC-06 | 🟩 READY | 8/8 | 100% | ✅ | ✅ | ✅ | ✅ | N/A | N/A |
| EPIC-07 | 🟩 READY | 16/16 | 100% | ✅ | ✅ | ✅ | ✅ | N/A | N/A |
| EPIC-08 | 🟩 READY | 18/18 | 100% | ✅ | ✅ | ✅ | ✅ | N/A | N/A |
| EPIC-09 | 🟩 READY | 14/14 | 100% | ✅ | ✅ | ✅ | ✅ | N/A | N/A |
| EPIC-10 | 🟩 READY | 8/8 | 100% | ✅ | ✅ | ✅ | ✅ | N/A | N/A |
| EPIC-11 | 🟩 READY | 10/10 | 100% | ✅ | ✅ | ✅ | ✅ | N/A | N/A |
| EPIC-12 | 🟦 IN PROGRESS | 7/13 | 53.8% | ✅ | ◐ | ◐ | ◐ | N/A | N/A |
| **Initial release total** | **🟦 IN PROGRESS** | **132/139** | **95.0%** | **✅** | **◐** | **❌** | **◐** | **N/A** | **N/A** |
| BACKLOG-01 | ⬜ BACKLOG | 0/5 | 0% | ✅ | — | — | — | N/A | N/A |
| BACKLOG-02 | ⬜ BACKLOG | 0/9 | 0% | ✅ | — | — | — | N/A | — |
| BACKLOG-03 | ⬜ BACKLOG | 0/3 | 0% | ✅ | — | — | — | N/A | N/A |
| BACKLOG-04 | ⬜ BACKLOG | 0/2 | 0% | ✅ | — | — | — | N/A | N/A |
| BACKLOG-05 | 🟩 READY | 3/3 | 100% | ✅ | ✅ | ✅ | ✅ | N/A | N/A |
| BACKLOG-06 | 🟩 READY | 3/3 | 100% | ✅ | ✅ | ✅ | ✅ | N/A | N/A |
| **Full agreed roadmap total** | **🟦 IN PROGRESS** | **138/164** | **84.1%** | **✅** | **◐** | **❌** | **◐** | **N/A** | **—** |

Для remote backlog runtime deployment отсутствует (`DEPLOY: N/A`), но фактический encrypted two-host test обязателен (`LIVE: ✅`) до READY.

## 8. Resolved decisions и external gates

Все известные SPEC decisions для активного implementation scope согласованы. Оставшиеся gaps относятся к реализации или внешнему Evidence, а не к denominator:

1. `EPIC-12`: profiles/evaluator/frozen corpus merged; выполнить controlled reference runs для `E12-AC01..06`.
2. `EPIC-01`: immutable `v1.0.0-rc.2` опубликован run `33815294790`; executable/payload/SBOM/provenance проверены. Exact artifact провалил Pro `25H2` critical proxy flow из-за tray P/Invoke crash. Пользователь утвердил observation-only maintenance line `release/v1.0`; current branch содержит exact `Shell_NotifyIconW` forward fix. Требуются reviewed merge, новый `v1.0.0-rc.3` candidate и отдельные Home/Pro checks; `rc.2` не переиспользуется.
3. `BACKLOG-01`: llama.cpp и LM Studio exact versions остаются `PENDING_EXTERNAL_GATE` до установки и manual compatibility tests; это не блокирует safe code PR.
4. `BACKLOG-02`: encrypted Windows↔VPS/second-PC `LIVE` test остаётся `PENDING_EXTERNAL_GATE`; это не блокирует safe code PR.
5. `BACKLOG-03`: Linux/macOS остаются conditional backlog до нового explicit demand.
6. `BACKLOG-04`: новый protocol не нужен для OpenCode/Hermes/Open WebUI; manual client E2E выполняются в финальной validation phase без readiness credit по `B04-*`.

Versioned diagnostic thresholds, minimum sample size и notification anti-spam policy являются implementation/configuration decisions, но должны быть explicit и boundary-tested до READY.

---

## 9. Operational status block

Этот блок можно обновлять по фактам без изменения durable product scope.

- Last recalculation: `2026-09-04T05:42:42Z`.
- Repository: `https://github.com/Just9120/llm-inspector`.
- Initial documentation base commit: `e0860e4972e486e59fcf3a8499b5da0f2863b96c`.
- Architecture baseline: PR [#1](https://github.com/Just9120/llm-inspector/pull/1), merge commit `00ca8c3ef727d784ca2e0c9d837231be7f68c5e4`.
- Verified `GOAL-003` base SHA: `00ca8c3ef727d784ca2e0c9d837231be7f68c5e4`.
- Foundation code/toolchain commit: `1d74b4a5b053b0c2e908ca7e5fa18aa89d9bc83c`; CI workflow/policy-test commit: `5fd0b67213044b7b7318553d32195621fa488d3f`; separate normal/RID lock-graph commit: `dc1a9b6f1938307160872f8fe99044c5f56f0e3c`.
- GitHub Actions: prior terminal core runs remain recorded in delivery history. EPIC-01 release-fix PR/main runs `33814760385`/`33814980537` and trusted-tag release run `33815294790` completed successfully at exact source `8aae2fbdd69ae8d8d2c1dd0b4796d1bea6883479`. Main-only release-line policy/fix PRs #26/#27 and exact-main run `33840821568` are supporting Evidence; this branch still requires its own PR/exact-branch CI.
- Code/tests/runtime: public `v1.0.0-rc.2` contains 6 assets; executable SHA-256 `4e78ee7cdcde7eb6188d8299f9576447b65faad7439f839e739e32048bd7e683`, manifest exact source SHA `8aae2fbdd69ae8d8d2c1dd0b4796d1bea6883479`, SPDX 2.3 (`32` packages/`1` file), both Sigstore bundles and GitHub attestation verification pass. Exact Pro artifact smoke and `/models` pass, but proxied POST terminates the app with `EntryPointNotFoundException` for tray `ShellNotifyIconW`. Current local commits `3269292`/`47d37ec`/`e7a685d` add trusted release-line mapping, bind exported `Shell_NotifyIconW`, verify the native symbol and remove a synthetic resource-test race. Focused policy tests `5/5`, tray test `1/1` and formerly flaky test `20/20` pass; full final CI-equivalent is pending after documentation commit.
- EPIC-09 completion: `14/14`; real SQLite schema/disclosure/privacy corpus confirmed by PR #9 and exact-main CI.
- Initial release readiness: `132/139 = 95.0%` (`EPIC-12 7/13`; `E12-AC01..06` remain pending controlled measurements and uncredited).
- Full agreed roadmap readiness: `138/164 = 84.1%`.
- GOAL-003 delivery: PR [#2](https://github.com/Just9120/llm-inspector/pull/2), merge commit `384556f693df9b3dbbc9d06dc2ddbd67328fa5d7`; PR/main CI terminal success.
- Active approved Goal: `GOAL-005 IN_PROGRESS`; public `rc.2` exists but failed the Pro manual gate. Branch `codex/goal-005-v1.0.0-rc.3` is the bounded observation-only forward fix based on `release/v1.0`; it excludes lifecycle/remote code and awaits PR/CI/release Evidence.
