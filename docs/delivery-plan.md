# Delivery plan

> Dashboard status: `GOAL-005 IN_PROGRESS`
> Updated: `2026-09-03T06:46:30Z`

## Current Goal

### `GOAL-005 — Реализовать все оставшиеся canonical product AC`

- **State:** `IN_PROGRESS`.
- **Authorization source:** explicit user instruction от `2026-09-03`: «Теперь бери оставшиеся AC и реализуй в рамках Goal» после указания вести эпики разными PR, не останавливать безопасный partial PR из-за product blocker и закрывать gaps последующими fix PR.
- **Verified base:** `origin/main` / local `main` = `6aaae88e3b24306d1c4c2ec165945436eeee05f2`; exact-main CI [33722434890](https://github.com/Just9120/llm-inspector/actions/runs/33722434890) succeeded.
- **Exact product denominator:** `92` оставшихся atomic AC: initial release `67` (`EPIC-01 4 + EPIC-05 8 + EPIC-06 8 + EPIC-07 16 + EPIC-10 8 + EPIC-11 10 + EPIC-12 13`) и backlog `25` (`BACKLOG-01 5 + BACKLOG-02 9 + BACKLOG-03 3 + BACKLOG-04 2 + BACKLOG-05 3 + BACKLOG-06 3`).

**Scope**

- Реализовать все ещё не выполненные canonical AC из `docs/project-spec.md` reviewable epic/fix PR.
- Для каждого increment выполнять focused checks, полный local CI-equivalent перед единственным initial push, required PR CI, merge, exact-main CI, sync/cleanup и независимый readiness recalculation.
- При product/SPEC/external blocker доставлять только безопасную подтверждаемую часть; невыполненные AC не кредитовать и продолжать fix increment внутри этой Goal, когда gate доступен.
- Синхронизировать operational fields `project-spec`, architecture facts и delivery metadata без изменения durable requirements.

**Non-goals**

- Изменение canonical scope, business rules, atomic AC, support matrix или CI/CD safety contract.
- Выдумывание remote topology/authentication, target OS matrix, performance budgets, signing identity, distribution channel, credentials или runtime deployment target.
- Обход privacy, CI, release, security или repository protection gates.
- Product behavior вне 92 перечисленных AC и unrelated refactoring.

**Goal acceptance criteria**

1. Каждый из 92 AC либо выполнен с traceable Evidence, либо явно оставлен невыполненным с точным blocker/gate и без readiness credit.
2. Каждый bounded epic/fix increment проходит required local validation и exact-revision GitHub CI до merge.
3. После каждого merge exact-main CI terminal success подтверждён до начала следующего branch; local `main` синхронизирован, merged branch безопасно удалён.
4. Initial/full readiness каждый раз считается только как `completed atomic AC / 139` и `completed atomic AC / 164`; предыдущие оценки не используются как доказательство.
5. Goal достигает `DONE` только при `164/164` и required Evidence либо останавливается в `BLOCKED` / `PENDING_EXTERNAL_GATE`, если после всех безопасных increments остаётся внешний gate.

**Required Evidence:** для feature epics `SPEC`, `CODE`, `TEST`, `CI` согласно их DoD; `DEPLOY`/`LIVE` — `N/A`, кроме ещё не определённой applicability `BACKLOG-02`. Exact status не повышается без фактического Evidence.

**Known blockers/dependencies**

- `EPIC-01/E01-AC01`: release Evidence на Windows 11 25H2 Home/Pro требует будущего release Goal, signing/distribution decision и clean install/upgrade/runtime matrix.
- `EPIC-12`: numeric performance budgets и frozen hardware/workload fixtures отсутствуют; `SPEC: ◐` блокирует READY, но не measurement infrastructure и reliability hardening.
- `BACKLOG-02`: remote topology, identity/authentication и DEPLOY/LIVE applicability не определены; `SPEC: ◐` блокирует implementation, затрагивающую security boundary.
- `BACKLOG-03`: supported Linux distributions/macOS versions не определены после demand gate; `SPEC: ◐` блокирует platform-support claim.

**Stop condition:** после `164/164` с required Evidence либо после исчерпания безопасных increments при подтверждённом `BLOCKED` / `PENDING_EXTERNAL_GATE`; к новой Goal без explicit authorization не переходить.

## PR execution sequence

1. `EPIC-01` — Windows application boundary и доступные product surfaces; release-matrix gate остаётся честно отделён.
2. `EPIC-05` — agent operations, tools и concurrency.
3. `EPIC-06` — Windows resource telemetry.
4. `EPIC-07` — explainable diagnostics и errors.
5. `EPIC-10` — background/tray/settings/notifications.
6. `EPIC-11` — anonymized diagnostic snapshot.
7. `EPIC-12` — reliability/measurement hardening до границы approved numeric budgets.
8. `BACKLOG-01`, `BACKLOG-04`, `BACKLOG-05`, `BACKLOG-06` — lifecycle, protocols, multi-GPU и export отдельными PR.
9. `BACKLOG-02`, `BACKLOG-03` — только после закрытия canonical SPEC/external gates; до этого допускаются лишь нерасширяющие boundary seams/tests/docs.

Sequence — рабочий pipeline внутри одной authorized Goal; точный порядок fix PR может меняться по фактическим dependencies, denominator не меняется.

## Active execution checkpoint

| Field | Verified state |
|---|---|
| Updated UTC | `2026-09-03T06:46:30Z` |
| Expected base branch | `main` |
| Base SHA | `6aaae88e3b24306d1c4c2ec165945436eeee05f2` — verified local/`origin/main`; exact-main CI `33722434890` success |
| Working branch | `codex/goal-005-epic-05` |
| Last verified revision | `ba6c81a39054280389bb588ae34776fe292b790e` — complete candidate tree covered by full local CI-equivalent; this checkpoint is docs-only |
| Initial worktree state | Clean branch created from verified `origin/main`; no open PR and no unrelated changes |
| Current worktree state | Clean at full-validation boundary except this expected docs-only checkpoint update; no unrelated changes |
| Completed work | `eaff62c` adds fail-closed operation graph/domain tracker; `e3de08a` adds bounded JSON/SSE tool projection, ordered SQLite schema v3 persistence/UI and concurrency/privacy coverage; `ba6c81a` maps AC/readiness. Full exact-SDK pipeline: locked normal/RID restores; format unchanged; Release build `0` warnings/errors; `142/142` tests, zero skips; clean self-contained `win-x64` publish; smoke `exit 0` |
| Current step | Record the final local checkpoint, then perform the one initial push and create the EPIC-05 PR |
| Next exact action | Commit this docs-only checkpoint, push `codex/goal-005-epic-05` once, create PR against `main`, then wait for exact-head required CI |
| PR / CI | No EPIC-05 PR yet; branch has not been pushed; exact-revision CI pending |
| Deployment | N/A for current Windows desktop feature DoD; server/runtime CD remains disabled |
| Blockers | None for EPIC-05; `E01-AC01` remains an independent release-matrix blocker and is not credited |
| Unverified assumptions | Full-body OpenAI protocol variants beyond the versioned tested JSON/SSE fixtures are not used as readiness Evidence; unsupported/ambiguous metadata fails closed |
| Preserved pre-existing changes | Goal started from clean synchronized worktree; unrelated changes absent |

## Project readiness snapshots

| Snapshot | Timestamp | Initial release | Full agreed roadmap | Denominator и основание |
|---|---|---:|---:|---|
| Current | `2026-09-03T06:40:21Z` | `83/139 = 59.7%` | `83/164 = 50.6%` | Independent AC-by-AC calculation credits E05-AC01..08 after focused CODE/TEST Evidence; exact-revision CI remains open |
| Previous | `2026-09-03T06:17:58Z` | `75/139 = 54.0%` | `75/164 = 45.7%` | Terminal EPIC-01 calculation confirmed by PR #10 and exact-main CI `33722434890` |

Delta: `+5.7 п.п.` initial release и `+4.9 п.п.` full roadmap; меньше 10 п.п. Причина — локально подтверждены все восемь ранее невыполненных EPIC-05 criteria при неизменных denominators `139`/`164`.

## Epic readiness и Evidence

| Epic | Status | Completed / total | Readiness | SPEC | CODE | TEST | CI | DEPLOY | LIVE |
|---|---|---:|---:|---|---|---|---|---|---|
| EPIC-01 | 🟦 IN PROGRESS ⛔ | 3/4 | 75% | ✅ | ◐ | ◐ | ◐ | N/A | N/A |
| EPIC-02 | 🟩 READY | 15/15 | 100% | ✅ | ✅ | ✅ | ✅ | N/A | N/A |
| EPIC-03 | 🟩 READY | 13/13 | 100% | ✅ | ✅ | ✅ | ✅ | N/A | N/A |
| EPIC-04 | 🟩 READY | 12/12 | 100% | ✅ | ✅ | ✅ | ✅ | N/A | N/A |
| EPIC-05 | 🟦 IN PROGRESS | 8/8 | 100% | ✅ | ✅ | ✅ | ◐ | N/A | N/A |
| EPIC-06 | ⬜ BACKLOG | 0/8 | 0% | ✅ | — | — | — | N/A | N/A |
| EPIC-07 | ⬜ BACKLOG | 0/16 | 0% | ✅ | — | — | — | N/A | N/A |
| EPIC-08 | 🟩 READY | 18/18 | 100% | ✅ | ✅ | ✅ | ✅ | N/A | N/A |
| EPIC-09 | 🟩 READY | 14/14 | 100% | ✅ | ✅ | ✅ | ✅ | N/A | N/A |
| EPIC-10 | ⬜ BACKLOG | 0/8 | 0% | ✅ | — | — | — | N/A | N/A |
| EPIC-11 | ⬜ BACKLOG | 0/10 | 0% | ✅ | — | — | — | N/A | N/A |
| EPIC-12 | ⬜ BACKLOG ⛔ | 0/13 | 0% | ◐ | — | — | — | N/A | N/A |
| **Initial release** | **🟦 IN PROGRESS** | **83/139** | **59.7%** | **◐** | **◐** | **◐** | **◐** | **N/A** | **N/A** |
| BACKLOG-01 | ⬜ BACKLOG | 0/5 | 0% | ✅ | — | — | — | N/A | N/A |
| BACKLOG-02 | ⬜ BACKLOG ⛔ | 0/9 | 0% | ◐ | — | — | — | —* | —* |
| BACKLOG-03 | ⬜ BACKLOG ⛔ | 0/3 | 0% | ◐ | — | — | — | N/A | N/A |
| BACKLOG-04 | ⬜ BACKLOG | 0/2 | 0% | ✅ | — | — | — | N/A | N/A |
| BACKLOG-05 | ⬜ BACKLOG | 0/3 | 0% | ✅ | — | — | — | N/A | N/A |
| BACKLOG-06 | ⬜ BACKLOG | 0/3 | 0% | ✅ | — | — | — | N/A | N/A |
| **Full roadmap** | **🟦 IN PROGRESS** | **83/164** | **50.6%** | **◐** | **◐** | **◐** | **◐** | **—*** | **—*** |

`*` Applicability `BACKLOG-02` DEPLOY/LIVE остаётся canonical SPEC gap.

### EPIC-01 AC evidence map

| Atomic AC | Current evidence |
|---|---|
| `E01-AC01` | Не выполнен: self-contained `win-x64` validation publish не заменяет clean install/upgrade/launch/tray/proxy/SQLite recovery suite на Windows 11 25H2 Home/Pro; signing/distribution path не утверждён |
| `E01-AC02` | MainWindow имеет доступные `Live requests`, `History and analytics` и `Diagnostics` surfaces; `DesktopProductBoundaryTests` проверяет headings и named output controls |
| `E01-AC03` | `AppLaunchConfiguration` принимает только literal IPv4/IPv6 loopback backend URLs; defaults Ollama/llama.cpp/LM Studio и derived gateway options покрыты focused Windows tests |
| `E01-AC04` | Versioned launch parser отвергает start/stop/restart/model-load/runtime-mutation commands как unknown; UI не содержит lifecycle controls |

### EPIC-05 AC evidence map

| Atomic AC | Current evidence |
|---|---|
| `E05-AC01` | `AgentOperationTracker` объединяет explicit request/turn/tool/final lifecycle в один ordered `TechnicalOperationGraph`; SQLite v3 сохраняет request membership, turns и tool events |
| `E05-AC02` | Operation detail упорядочен по explicit turn sequence и tool index; lifecycle хранит только technical state, без user/assistant/tool/final content |
| `E05-AC03` | Bounded request projection считает top-level `tools`; turn metric хранит value, quality, source, version и derivation |
| `E05-AC04` | JSON/SSE response projection считает `tool_calls` и объединяет fragmented function name только по tool index |
| `E05-AC05` | Tool event хранит normalized name, status/error category и calculated wall duration до exact next tool-result turn; arguments/results не входят в schema |
| `E05-AC06` | Operation ID и request ID разделяют одновременные requests; parallel integration fixture подтверждает восемь независимых operations |
| `E05-AC07` | Tracker требует совпадение operation/session/client/backend и rejects cross-session/cross-client continuation; parallel tests подтверждают отсутствие смешения |
| `E05-AC08` | Missing/malformed/duplicate/gap/out-of-order/inconsistent correlation остаётся ungrouped или `unavailable`; time proximity не используется |

## Current blockers и decisions

1. Partial safe PR разрешён Goal policy, но incomplete AC остаётся открытым и не получает credit.
2. Required CI failure/skip, privacy/security regression или release safety failure блокирует merge до исправления.
3. External gates не подменяются code/config assumptions: они остаются explicit blockers до owner Evidence/decision.
4. Репозиторий не имеет approved post-merge metadata writer; terminal Evidence фиксируется в merged PR comment и восстанавливается в следующий substantive PR без metadata-only loop.

## Candidate next Goals

Новые Goals не предлагаются, пока `GOAL-005` активна. Implementation authorization относится только к этой Goal; после её terminal state агент останавливается.
