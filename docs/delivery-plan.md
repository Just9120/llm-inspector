# Delivery plan

> Dashboard status: `IN_PROGRESS`
> Updated UTC: `2026-09-05T20:32:30Z`

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
- **Delivery:** узкие локальные commits; initial push/PR после полной локальной validation законченного bounded implementation increment. Failed CI/review исправляется grouped batch в той же Goal; gates не обходятся. GOAL-006 terminal comment path был разовым разрешением для предыдущей Goal, не blanket direct-write permission. Post-merge metadata этой Goal требует предусмотренного contract пути либо отдельного решения owner.

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
| Updated UTC | 2026-09-05T20:32:30Z — repository/environment preflight |
| Base branch / SHA | main / `88cbe291309105c495da5f488d23d48f8c74b007`; local main = origin/main = GitHub main, fetch/readback verified |
| Working branch | `codex/goal-007-validation` |
| Last verified revision | `88cbe291309105c495da5f488d23d48f8c74b007`; prior main CI `33978125113` SUCCESS |
| Worktree | Clean at branch creation, no pre-existing user changes; only own main checkout |
| Recovery | GOAL-006 DONE 9/9 по [terminal Evidence](https://github.com/Just9120/llm-inspector/pull/39#issuecomment-5553245062); stale pre-merge dashboard не является новой authorization. Closed Goal перенесена в archive |
| Completed work | Goal scope/reference questions поставлены; Windows-only и Ollama 0.33.3 owner decisions получены. Safe read-only hardware/runtime preflight |
| Current step | Scope/reference update и frozen corpus v2 (только Ollama 0.33.3) выполнены; идёт независимая validation tooling work. Computer Use остановлен Escape, UI Evidence не заявлено |
| Next exact action | Реализовать и проверить bounded read-only process-tree measurements для exact running executable; не возобновлять Computer Use без сигнала пользователя |
| Environment | Windows Pro 25H2 x64 26200.9168, Ryzen 7 9800X3D 8C/16T, RTX 5060 Ti 16311 MiB / driver 610.74. Ollama API 0.33.3, reference model digest installed, 0 loaded models на preflight |
| Existing state preserved | Ollama user process уже запущен; не owned Inspector, не останавливать. Existing Inspector DB присутствует; settings/token files отсутствовали. Не очищать историю и не изменять remote/privacy settings скрыто |
| Artifact | Existing local `build/bin/LlmInspector.exe` SHA-256 `9c327b2e3385b6ca3820e41cb7591301680dec303265954911b0ae2c256234ec`; supporting candidate, не public release. Prior hosted hash отличается; cross-host identity не доказана |
| PR / CI | GOAL-007 PR ещё нет; новый CI ещё не запускался |
| Deployment / LIVE | CD disabled / DEPLOY N/A; B02 LIVE отсутствует, не считать local fixture подтверждением |
| Blockers / unverified | Windows Home, second host/Tailscale, exact other backend/client versions, B04 decision, controlled resource/wakeup source и stable candidate identity |

## Project readiness snapshots

| Snapshot | Initial release | Full roadmap | Basis |
|---|---:|---:|---|
| Current — baseline при старте GOAL-007 | 136/143 = 95.1% | 152/168 = 90.5% | Последний independently verified ledger GOAL-006; новые runtime checks ещё не добавили credit |
| Previous — GOAL-006 final review | 136/143 = 95.1% | 152/168 = 90.5% | Exact main 88cbe29 / CI 33978125113; 16 missing AC перечислены в project-spec §7 |

Это baseline, не новый полный audit. Изменение 0 п.п.; отказ от Linux/macOS в текущей Goal не уменьшает full denominator. После фактических failures пересчитывать затронутые AC вниз так же, как после PASS — вверх.

## Pipeline / next items

1. **Active:** Windows-only scope/reference/candidate reconciliation → real Pro UI + isolated regression/E2E → measurement harness и controlled E12 runs → available Home/remote gates → bounded fixes/PR/CI/merge → final evidence/cleanup.
2. **Waiting owner:** B04 exact protocol/client; Windows Home и private second-host resources; вмешательство в existing runtime/foreign load только по конкретному разрешению.
3. **Not authorized:** final publication/WinGet до validation и отдельного согласования; Linux/macOS; новая Goal.
