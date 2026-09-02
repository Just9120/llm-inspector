# LLM Inspector — canonical product contract

> Contract status: `RATIFIED`  
> Contract version: `1.1`
> Ratified by: explicit user instruction от `2026-09-02`  
> Compatibility amendment: approved `GOAL-002` от `2026-09-02`
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
- Initial release не требует server deployment. `DEPLOY` и `LIVE` имеют `N/A` в feature epic DoD; Windows package/release validation относится к отдельному будущему release Goal.
- CD на runtime host отключён. CI и будущий build/release pipeline остаются отдельными concerns.

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
- Для всех feature epics required Evidence: `SPEC`, `CODE`, `TEST`, `CI`. `DEPLOY` и `LIVE`: `N/A` по текущему DoD.
- `SPEC: ◐` допускается при ратифицированном intent, но открытом измеримом threshold/compatibility decision; такой epic не может стать READY до закрытия gap.

## 5. Initial release epics

### EPIC-01 — Windows application и границы первой версии

**Status:** ⬜ BACKLOG  
**Goal:** предоставить Windows desktop application для observation/analytics/diagnostics локальных LLM без lifecycle management.

Acceptance criteria:

- `E01-AC01`: Application распространяется и запускается как desktop application на каждой Windows version из утверждённой support matrix. [source: `UP-C01-01`]
- `E01-AC02`: Desktop UI содержит доступные пользователю monitoring, analytics и diagnostics surfaces. [source: `UP-C01-01`, `UP-C01-02`]
- `E01-AC03`: Initial release подключается к LLM backends на том же Windows PC. [source: `UP-C01-04`]
- `E01-AC04`: Initial release не содержит команд запуска, остановки, restart, загрузки моделей или изменения backend runtime parameters. [source: `UP-C01-03`]

Definition of Done: `4/4` AC, SPEC/CODE/TEST/CI `✅`, DEPLOY/LIVE `N/A`.

### EPIC-02 — Backend/client adapters и OpenAI-compatible API

**Status:** 🟦 IN PROGRESS

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

**Status:** ⬜ BACKLOG  
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

**Status:** ⬜ BACKLOG  
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

**Status:** ⬜ BACKLOG  
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

**Status:** ⬜ BACKLOG  
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

**Status:** ⬜ BACKLOG  
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

**Status:** ⬜ BACKLOG  
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

**Status:** 🟦 IN PROGRESS
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

**Status:** ⬜ BACKLOG  
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

**Status:** ⬜ BACKLOG  
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

**Status:** ⬜ BACKLOG  
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

Backlog requirements согласованы, но их implementation не начата и не авторизована. Они не входят в initial release readiness.

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

Definition of Done: `9/9` AC, security/threat-model review и SPEC/CODE/TEST/CI `✅`; DEPLOY/LIVE applicability должна быть пересмотрена при authorization этого epic.

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

**Status:** ⬜ BACKLOG

- `B05-AC01`: Inspector обнаруживает несколько supported GPU devices. [source: `UP-C04-03`]
- `B05-AC02`: Resource UI показывает metrics раздельно для каждого device. [source: `UP-C04-03`]
- `B05-AC03`: Workload-to-device attribution показывается только при достоверном source, иначе unavailable. [source: `UP-C04-03`]

Definition of Done: `3/3` AC, SPEC/CODE/TEST/CI `✅`, DEPLOY/LIVE `N/A`.

### BACKLOG-06 — Export analytics

**Status:** ⬜ BACKLOG

- `B06-AC01`: Пользователь экспортирует выбранный range anonymized technical history. [source: `UP-C09-04`]
- `B06-AC02`: Пользователь экспортирует aggregate metrics за выбранный range. [source: `UP-C09-04`]
- `B06-AC03`: Export проходит тот же negative content corpus, что diagnostic snapshot, и не содержит request/response content. [source: `UP-C09-04`]

Definition of Done: `3/3` AC, SPEC/CODE/TEST/CI `✅`, DEPLOY/LIVE `N/A`.

## 7. Current readiness и Evidence

Фактическое состояние пересчитано с нуля по 164 atomic product AC. EPIC-09 core выполняет `E09-AC01..05` и `E09-AC07..14`; `E09-AC06` остаётся невыполненным до появления реальной history/analytics schema в EPIC-08. Другие product AC не кредитуются.

| Epic | Status | Completed / total | Readiness | SPEC | CODE | TEST | CI | DEPLOY | LIVE |
|---|---|---:|---:|---|---|---|---|---|---|
| EPIC-01 | ⬜ BACKLOG | 0/4 | 0% | ✅ | — | — | — | N/A | N/A |
| EPIC-02 | 🟦 IN PROGRESS | 0/15 | 0% | ✅ | — | — | — | N/A | N/A |
| EPIC-03 | ⬜ BACKLOG | 0/13 | 0% | ✅ | — | — | — | N/A | N/A |
| EPIC-04 | ⬜ BACKLOG | 0/12 | 0% | ✅ | — | — | — | N/A | N/A |
| EPIC-05 | ⬜ BACKLOG | 0/8 | 0% | ✅ | — | — | — | N/A | N/A |
| EPIC-06 | ⬜ BACKLOG | 0/8 | 0% | ✅ | — | — | — | N/A | N/A |
| EPIC-07 | ⬜ BACKLOG | 0/16 | 0% | ✅ | — | — | — | N/A | N/A |
| EPIC-08 | ⬜ BACKLOG | 0/18 | 0% | ✅ | — | — | — | N/A | N/A |
| EPIC-09 | 🟦 IN PROGRESS | 13/14 | 93% | ✅ | ◐ | ◐ | ◐ | N/A | N/A |
| EPIC-10 | ⬜ BACKLOG | 0/8 | 0% | ✅ | — | — | — | N/A | N/A |
| EPIC-11 | ⬜ BACKLOG | 0/10 | 0% | ✅ | — | — | — | N/A | N/A |
| EPIC-12 | ⬜ BACKLOG | 0/13 | 0% | ◐ | — | — | — | N/A | N/A |
| **Initial release total** | **🟦 IN PROGRESS** | **13/139** | **9.4%** | **◐** | **◐** | **◐** | **◐** | **N/A** | **N/A** |
| BACKLOG-01 | ⬜ BACKLOG | 0/5 | 0% | ✅ | — | — | — | N/A | N/A |
| BACKLOG-02 | ⬜ BACKLOG | 0/9 | 0% | ◐ | — | — | — | —* | —* |
| BACKLOG-03 | ⬜ BACKLOG | 0/3 | 0% | ◐ | — | — | — | N/A | N/A |
| BACKLOG-04 | ⬜ BACKLOG | 0/2 | 0% | ✅ | — | — | — | N/A | N/A |
| BACKLOG-05 | ⬜ BACKLOG | 0/3 | 0% | ✅ | — | — | — | N/A | N/A |
| BACKLOG-06 | ⬜ BACKLOG | 0/3 | 0% | ✅ | — | — | — | N/A | N/A |
| **Full agreed roadmap total** | **🟦 IN PROGRESS** | **13/164** | **7.9%** | **◐** | **◐** | **◐** | **◐** | **—*** | **—*** |

`*` Для remote backlog applicability DEPLOY/LIVE ещё не определена: защищённый remote component может потребовать operational Evidence, хотя initial Windows desktop release не имеет CD target.

## 8. Remaining SPEC gaps

Scope согласован; gaps ниже не меняют перечень features, но блокируют READY соответствующих epics:

1. `EPIC-12`: утвердить numeric CPU/RAM/GPU/disk/idle/throughput performance budgets и frozen reference hardware/workload fixtures; measurement protocol уже определён в `docs/architecture.md`.
2. `BACKLOG-02`: определить remote topology, identity/authentication model и required DEPLOY/LIVE Evidence до начала реализации.
3. `BACKLOG-03`: определить supported Linux distributions/macOS versions только после demand gate.

Versioned diagnostic thresholds, minimum sample size и notification anti-spam policy являются implementation/configuration decisions, но должны быть explicit и boundary-tested до READY.

---

## 9. Operational status block

Этот блок можно обновлять по фактам без изменения durable product scope.

- Last recalculation: `2026-09-03`.
- Repository: `https://github.com/Just9120/llm-inspector`.
- Initial documentation base commit: `e0860e4972e486e59fcf3a8499b5da0f2863b96c`.
- Architecture baseline: PR [#1](https://github.com/Just9120/llm-inspector/pull/1), merge commit `00ca8c3ef727d784ca2e0c9d837231be7f68c5e4`.
- Verified `GOAL-003` base SHA: `00ca8c3ef727d784ca2e0c9d837231be7f68c5e4`.
- Foundation code/toolchain commit: `1d74b4a5b053b0c2e908ca7e5fa18aa89d9bc83c`; CI workflow/policy-test commit: `5fd0b67213044b7b7318553d32195621fa488d3f`; separate normal/RID lock-graph commit: `dc1a9b6f1938307160872f8fe99044c5f56f0e3c`.
- GitHub Actions: PR/`main` workflow proven green by GOAL-003 PR/main runs; EPIC-09 exact PR CI pending. Repository rulesets/branch protection отсутствуют на last verified settings check.
- Code/tests/runtime: loopback streaming gateway, privacy-safe observation contract, disclosure UI и 26 tests; SQLite/adapters/rich telemetry не реализованы.
- EPIC-09 local completion candidate: `13/14`; `E09-AC06` deferred to selected EPIC-08 PR because a real history/analytics schema does not yet exist.
- Initial release readiness: `13/139 = 9.4%`.
- Full agreed roadmap readiness: `13/164 = 7.9%`.
- GOAL-003 delivery: PR [#2](https://github.com/Just9120/llm-inspector/pull/2), merge commit `384556f693df9b3dbbc9d06dc2ddbd67328fa5d7`; PR/main CI terminal success.
- Active approved Goal: `GOAL-004 IN_PROGRESS`; `13/72` selected AC complete locally, EPIC-09 PR/CI Evidence pending; branch `codex/epic-09-privacy-proxy` from verified base `384556f693df9b3dbbc9d06dc2ddbd67328fa5d7`.
