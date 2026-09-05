# Delivery plan

> Dashboard status: `IN_PROGRESS`
> Updated UTC: `2026-09-05T21:12:00Z`

## Current Goal

**GOAL-007 — закрыть оставшийся Windows scope и обязательное Evidence**

- **State:** `IN_PROGRESS`.
- **Authorization:** explicit user instruction `2026-09-05`: «Значит нужно добить весь скоуп. Ставь goal и приступай». Последующее уточнение: «Пока только windows» — B03 остаётся conditional backlog вне Goal. Выбор нового protocol/client для B04 ещё запрошен; не выбирать его произвольно.
- **Scope:** E01-AC01, E12-AC01..06, B02-AC01/02/04/05; доступная actual Windows/backend/client/manual validation и исправление связанных defects с сохранением остальных AC. B04 включается только после конкретного owner protocol/client decision; до него независимая Windows work продолжается. Новые platform/protocol AC не выдумываются.
- **Reference amendment:** owner явно разрешил использовать установленную Ollama `0.33.3` вместо `0.33.2` для reference measurements; model/digest, hardware, workload, budgets, durations неизменны. Historical lifecycle verification не переносится на новую версию автоматически.
- **Non-goals:** Linux/macOS, installer/signing/automatic updates, final tag/release/WinGet до отдельного согласования и validation; public exposure/Funnel, automatic runtime/model download/update, изменения tailnet/ACL/credentials, остановка чужих процессов и CI/CD safety changes.
- **Required Evidence:** SPEC/CODE/TEST/CI для изменений; actual exact-artifact Windows matrix для E01, controlled hardware measurements для E12, actual encrypted two-host LIVE для B02. DEPLOY N/A; LIVE обязательно для B02 и этой части Goal.
- **Known blockers/dependencies:** Windows Home / second PC/VPS access ещё не предоставлены; Tailscale отсутствует в PATH и standard install location. B04 protocol/client не выбран. Runtime reference amendment согласован. Другие запущенные apps создают потенциальную foreign load; не останавливать их и не исключать contaminated runs постфактум без predefined signals.
- **Stop condition:** все применимые Goal AC и product gates реально подтверждены, PR/CI/merge/metadata/cleanup завершены → DONE; либо исчерпана независимая работа и требуется конкретное owner decision/resource → BLOCKED / PENDING_EXTERNAL_GATE. Не переходить к следующей Goal.
- **Delivery:** узкие локальные commits; initial push/PR после полной локальной validation законченного bounded implementation increment. Failed CI/review исправляется grouped batch в той же Goal; gates не обходятся. Owner explicit approval `2026-09-05`: для GOAL-007 в main остаётся датированный pre-merge checkpoint, итоговые CI/merge SHA и cleanup Evidence фиксируются в комментарии соответствующего PR без metadata-only follow-up PR. Это Goal-scoped exception, не direct-push permission и не изменение CI/CD safety rules.

### Goal acceptance criteria

| ID | Criterion | State |
|---|---|---|
| G07-01 | Оставшийся scope и exact candidate/reference/env известны; безопасная исполняемая validation procedure сохраняет evidence и не изменяет user data скрыто | IN_PROGRESS |
| G07-02 | E01-AC01: exact candidate проходит всю утверждённую Windows 25H2 Home/Pro x64 matrix; distribution identity и required release Evidence подтверждены | PENDING |
| G07-03 | E12-AC01..06: каждый built-in profile проходит все обязательные actual budgets и durations, без unavailable-as-pass или fabricated measurements | PENDING |
| G07-04 | B02-AC01/02/04/05: actual LAN/other-PC/VPS encrypted authenticated flow и весь runbook LIVE checklist подтверждены | PENDING_EXTERNAL_GATE — среда не предоставлена |
| G07-05 | Applicable Windows/backend/client UI и E2E scenarios проверены; обнаруженные defects исправлены с regression tests, прежние AC перепроверены по затронутому scope | IN_PROGRESS |
| G07-06 | B04: owner указал конкретный protocol/client и реализация прошла его AC, либо явно подтвердил сохранение B04 в conditional backlog вне Goal | PENDING_DECISION |
| G07-07 | Изменения локально проверены, delivered через PR/required CI/merge; readiness/Evidence актуальны, local main синхронен и только собственные merged branches/worktrees safely удалены | PENDING |

Goal AC не добавляются в product denominator. Полный roadmap по-прежнему 168 AC; initial — 143. B03 вне этой Goal не означает выполнения его 3 AC.

## Active execution checkpoint

| Field | Verified state |
|---|---|
| Updated UTC | 2026-09-05T21:12:00Z — pre-push checkpoint, completed local build-identity validation; later delivery facts — terminal PR comment |
| Base branch / SHA | main / `b9d49eea60962b9301f9329a3c4b913d6719c7de`; local main = origin/main = merge #42 SHA, fetch/readback verified |
| Working branch | `codex/goal-007-build-identity` |
| Last verified revision | `6f1027f07e336ce93fae6521b63813ec20e02126` — full local canonical build/tests/native smoke/release tools PASS при incoming CGO_ENABLED=1; executable mode 0/hash verified, caller env restored to 1. Base main CI `33992039896` SUCCESS |
| Worktree | Clean at branch creation, no pre-existing user changes. Own detached validation checkout `artifacts/goal007-build`; root running candidate/user state preserved |
| Recovery | PR #42 merge/head/main CI и branch cleanup verified по [terminal Evidence](https://github.com/Just9120/llm-inspector/pull/42#issuecomment-5554787705). GOAL-007 продолжается; pre-merge checkpoint не является claim о незавершённом прошлой веткой delivery |
| Completed work | Reference/capture increment delivered PR #42; [delivery Evidence](https://github.com/Just9120/llm-inspector/pull/42#issuecomment-5554787705). Own merged branch удалена local/remote. Причина hash mismatch доказана controlled CGO 0/1 local builds; новое исправление фиксирует shared build mode 0 и проверяет embedded metadata, caller environment восстанавливается |
| Current step | Build identity fix локально подтверждён; initial PR ready. Workflows, AGENTS, CI/CD safety, dependency pins не меняются. Product AC не изменены; новых credits нет. Computer Use остановлен Escape, UI Evidence не заявлено; существующий GUI/backend не закрывать |
| Next exact action | Initial push/PR → exact-head CI и hosted hash comparison → merge/main CI/terminal Evidence/own cleanup; затем продолжение только доступных Goal gates |
| Environment | Windows Pro 25H2 x64 26200.9168, Ryzen 7 9800X3D 8C/16T, RTX 5060 Ti 16311 MiB / driver 610.74. Ollama API 0.33.3, reference model digest installed, 0 loaded models на preflight |
| Existing state preserved | Ollama user process уже запущен; не owned Inspector, не останавливать. Existing Inspector DB присутствует; settings/token files отсутствовали. Не очищать историю и не изменять remote/privacy settings скрыто |
| Artifact | Local fixed-script mode 0 при incoming 1: `9c327b2e3385b6ca3820e41cb7591301680dec303265954911b0ae2c256234ec`, same prior candidate. Controlled unpinned local mode 1: `87af704dfb9600a99af501c9a78beb30f9c0129c5862b11ac16c17fec37ba151` — exact match prior hosted CI `33978125113`. Cause and local fix confirmed; new hosted comparison ещё требуется. Это supporting candidate, не public release |
| Pilot | Actual read-only capture `2026-09-05T20:47:45Z`–`20:48:15Z`, 31 samples / 30.006883 measured seconds, 7 observed processes. Private bytes P95 249102336 (~237.56 MiB), growth 16384 bytes, observed CPU delta and generic I/O-write delta 0. Profile balanced только declared; не verified UI state. Report SHA-256 `cda5f3cc5c1899464185a6c1b2fc79493e9a9c0461af276d9bcc73cf5f27d4b4`, local ignored `artifacts/goal007-validation/process-pilot-88cbe29.json`; pre-commit tool schema. Supporting pilot, **не E12 PASS** |
| PR / CI | PR #42 MERGED / exact-head и main CI SUCCESS. Build-identity PR ещё нет; [поиск новой ветки](https://github.com/Just9120/llm-inspector/pulls?q=head%3Acodex%2Fgoal-007-build-identity). Project check windows-go обязателен независимо от enforcement. Exact terminal IDs/results — comment соответствующего PR |
| Deployment / LIVE | CD disabled / DEPLOY N/A; B02 LIVE отсутствует, не считать local fixture подтверждением |
| Blockers / unverified | Windows Home, second host/Tailscale, exact other backend/client versions, B04 decision, controlled resource/wakeup source и stable candidate identity |

## Project readiness snapshots

| Snapshot | Initial release | Full roadmap | Basis |
|---|---:|---:|---|
| Current — GOAL-007 pre-PR scope/credit review | 136/143 = 95.1% | 152/168 = 90.5% | Независимый пересчёт 143/168 unique canonical AC; все AC byte-equivalent baseline. 7 initial / 16 full missing остаются без credit; process pilot не подтверждает их gates |
| Previous — GOAL-006 final review | 136/143 = 95.1% | 152/168 = 90.5% | Exact main 88cbe29 / CI 33978125113; 16 missing AC перечислены в project-spec §7 |

Это scope/credit review изменённого workstream, не новый полный manual audit. Изменение 0 п.п.; отказ от Linux/macOS в текущей Goal не уменьшает full denominator. Новые code/tests не меняют production binary или existing credited behavior. После фактических failures пересчитывать затронутые AC вниз так же, как после PASS — вверх.

## Pipeline / next items

1. **Active:** Windows-only scope/reference/candidate reconciliation → real Pro UI + isolated regression/E2E → measurement harness и controlled E12 runs → available Home/remote gates → bounded fixes/PR/CI/merge → final evidence/cleanup.
2. **Waiting owner:** B04 exact protocol/client; Windows Home и private second-host resources; вмешательство в existing runtime/foreign load только по конкретному разрешению.
3. **Not authorized:** final publication/WinGet до validation и отдельного согласования; Linux/macOS; новая Goal.
