# Delivery plan

> Dashboard status: `NO APPROVED GOAL`  
> Updated: `2026-09-02T18:16:23Z`

## Current Goal

Активная approved Goal отсутствует. `GOAL-001` завершена и перенесена в [`delivery-plan-archive.md`](delivery-plan-archive.md). `GOAL-002` ниже — только proposal и не авторизует implementation.

## Active execution checkpoint

| Field | Verified state |
|---|---|
| Updated UTC | `2026-09-02T18:16:23Z` |
| Expected base branch | `main` |
| Base SHA | `e0860e4972e486e59fcf3a8499b5da0f2863b96c` — initial documentation snapshot |
| Working branch | `main`; one-time empty-repository bootstrap explicitly authorized пользователем |
| Last verified revision | `e0860e4972e486e59fcf3a8499b5da0f2863b96c`; содержащий этот checkpoint metadata commit намеренно не self-referenced |
| Initial worktree state | Только `.git`; project files отсутствовали |
| Current worktree state | Documentation content committed; checkpoint metadata подготовлена отдельным commit перед единым initial push |
| Completed work | Empty-repository audit; canonical product ratification; 164 atomic AC; CD decision; initial `main` bootstrap |
| Current step | Ожидание approval следующей bounded Goal |
| Next exact action | Пользователь принимает, изменяет или отклоняет `GOAL-002`; до этого architecture/code implementation не начинать |
| PR / CI | `N/A`: initial empty-repository bootstrap cannot use PR without an existing base; workflows отсутствуют |
| Deployment | Server/runtime CD explicitly disabled; Windows build/release pipeline не определён |
| Blockers | Windows support matrix и numeric performance budgets не утверждены |
| Unverified assumptions | Desktop stack, process/data architecture, packaging/signing/update model |
| Preserved pre-existing changes | До audit/bootstrap project files отсутствовали; unrelated changes не обнаружены |

## Project readiness snapshots

| Snapshot | Timestamp | Initial release | Full agreed roadmap | Denominator и основание |
|---|---|---:|---:|---|
| Current | `2026-09-02T18:01:24Z` | `0/139 = 0%` | `0/164 = 0%` | 139 atomic AC в 12 initial-release epics; ещё 25 AC в 6 canonical backlog epics; code/tests отсутствуют |
| Previous | `2026-09-02T17:39:26Z` | `SPEC GAP` | `SPEC GAP` | Denominator был undefined до explicit ratification пользователя |

Numeric delta с previous snapshot не вычисляется: предыдущего процента не существовало. Появление `0%` — результат нового canonical denominator, а не регресс проекта.

## Epic readiness и Evidence

| Epic | Status | Completed / total | Readiness | SPEC | CODE | TEST | CI | DEPLOY | LIVE |
|---|---|---:|---:|---|---|---|---|---|---|
| EPIC-01 Windows application/boundary | ⬜ BACKLOG | 0/4 | 0% | ◐ | — | — | — | N/A | N/A |
| EPIC-02 Backends/clients/API | ⬜ BACKLOG | 0/15 | 0% | ✅ | — | — | — | N/A | N/A |
| EPIC-03 Live state/quality | ⬜ BACKLOG | 0/13 | 0% | ✅ | — | — | — | N/A | N/A |
| EPIC-04 Tokens/context/timings | ⬜ BACKLOG | 0/12 | 0% | ✅ | — | — | — | N/A | N/A |
| EPIC-05 Agent/tools/concurrency | ⬜ BACKLOG | 0/8 | 0% | ✅ | — | — | — | N/A | N/A |
| EPIC-06 System resources | ⬜ BACKLOG | 0/8 | 0% | ✅ | — | — | — | N/A | N/A |
| EPIC-07 Diagnostics/errors | ⬜ BACKLOG | 0/16 | 0% | ✅ | — | — | — | N/A | N/A |
| EPIC-08 History/analytics/retention | ⬜ BACKLOG | 0/18 | 0% | ✅ | — | — | — | N/A | N/A |
| EPIC-09 Privacy/locality/pass-through | ⬜ BACKLOG | 0/14 | 0% | ✅ | — | — | — | N/A | N/A |
| EPIC-10 Background/notifications | ⬜ BACKLOG | 0/8 | 0% | ✅ | — | — | — | N/A | N/A |
| EPIC-11 Diagnostic snapshot | ⬜ BACKLOG | 0/10 | 0% | ✅ | — | — | — | N/A | N/A |
| EPIC-12 Reliability/overhead | ⬜ BACKLOG | 0/13 | 0% | ◐ | — | — | — | N/A | N/A |
| **Initial release** | **⬜ BACKLOG** | **0/139** | **0%** | **◐** | **—** | **—** | **—** | **N/A** | **N/A** |
| BACKLOG-01 Lifecycle | ⬜ BACKLOG | 0/5 | 0% | ✅ | — | — | — | N/A | N/A |
| BACKLOG-02 Remote/LAN | ⬜ BACKLOG | 0/9 | 0% | ◐ | — | — | — | —* | —* |
| BACKLOG-03 Linux/macOS | ⬜ BACKLOG | 0/3 | 0% | ◐ | — | — | — | N/A | N/A |
| BACKLOG-04 Other protocols | ⬜ BACKLOG | 0/2 | 0% | ✅ | — | — | — | N/A | N/A |
| BACKLOG-05 Multiple GPUs | ⬜ BACKLOG | 0/3 | 0% | ✅ | — | — | — | N/A | N/A |
| BACKLOG-06 Analytics export | ⬜ BACKLOG | 0/3 | 0% | ✅ | — | — | — | N/A | N/A |
| **Full roadmap** | **⬜ BACKLOG** | **0/164** | **0%** | **◐** | **—** | **—** | **—** | **—*** | **—*** |

`*` Applicability DEPLOY/LIVE для Remote/LAN epic ещё не определена и должна быть решена до authorization, если появится operational remote component.

## Current blockers и decisions

1. `SPEC`: supported Windows versions и minimum hardware/driver baseline не определены (`EPIC-01`).
2. `SPEC`: numeric monitoring/idle/throughput budgets и reference workloads не определены (`EPIC-12`).
3. `ARCH`: desktop stack, proxy boundary, storage, process model и packaging/update topology не выбраны.
4. `ENV`: Git mutations в managed sandbox требуют отдельного approved elevated command; это operational constraint, не product blocker.

## Roadmap

Roadmap — sequencing proposal, не implementation authorization.

1. **R0 — Product contract:** завершён локально; 139 initial-release и 25 backlog AC.
2. **R1 — Architecture baseline:** выбрать Windows stack, boundaries, data/privacy model, test seams, support matrix и performance measurement contract.
3. **R2 — Repository/CI foundation:** initial base уже создан; далее feature branch, reproducible toolchain и lint/typecheck/test/build CI без CD.
4. **R3 — Privacy-preserving OpenAI-compatible vertical slice:** один client/backend path, streaming, telemetry quality и negative privacy tests.
5. **R4 — Required adapters и live telemetry:** Ollama, llama.cpp, LM Studio, known clients, timings/context.
6. **R5 — Resources, diagnostics и analytics:** collectors, explainable rules, history/retention.
7. **R6 — Windows UX/reliability:** tray, notifications, snapshot, crash recovery и performance budgets.
8. **R7 — Windows release:** package/signing/distribution/update validation; это build/release flow, не server CD.

## Candidate next Goal

### `GOAL-002 — Утвердить architecture baseline и executable delivery foundation design`

- **State:** `PROPOSED`.
- **Authorization needed:** explicit user approval.

**Scope**

- Выбрать и обосновать Windows desktop/runtime stack, package manager и repository layout.
- Определить transparent OpenAI-compatible observation boundary и client/backend data flow.
- Определить process/module boundaries, failure isolation и state ownership.
- Определить local storage/retention/migration model и privacy enforcement points.
- Сформировать backend capability matrix для Ollama, llama.cpp и LM Studio.
- Определить supported Windows matrix и measurable performance/idle benchmark protocol; numeric budgets вынести на explicit user approval, если источник их не задаёт.
- Определить test pyramid/seams для streaming, tools, concurrency, privacy и crash recovery.
- Определить Windows build/package/signing/distribution model и CI commands без CD.
- Обновить architecture, CI/CD project profile, README и delivery plan; подготовить bounded `GOAL-003` для repository/code bootstrap.

**Non-goals**

- Product source code, dependency installation или executable prototype.
- GitHub Actions/workflows, repository settings, secrets, signing certificates или release publishing.
- Server/runtime CD и production deployment.
- Изменение ратифицированного product scope/AC.

**Acceptance criteria**

1. `docs/architecture.md` содержит выбранный stack и logical/runtime component map с owners/boundaries.
2. Request/response/streaming/tool-call data flow показывает privacy filtering и failure behavior.
3. Storage/retention/migration ownership и content-exclusion invariant определены.
4. Capability matrix покрывает три required backends и unavailable semantics.
5. Supported Windows matrix зафиксирована либо явно вынесена как owner blocker.
6. Performance measurement protocol определён; numeric budgets утверждены либо остаются explicit blocker без invented values.
7. Test strategy связывает critical seams с relevant canonical AC.
8. CI/build/release profile отделяет Windows artifacts от отключённого CD.
9. Documentation links, AC counts и profile consistency проходят local structural validation.
10. Следующая code Goal bounded и не авторизована автоматически.

**Required Evidence**

- `SPEC: ✅` — architecture decisions traceable к canonical AC и explicit owner decisions.
- `CODE: N/A`.
- `TEST: ✅` — structural/link/count validation документации.
- `CI: N/A` — workflow creation вне scope.
- `DEPLOY: N/A`.
- `LIVE: N/A`.

**Known blockers**

- Numeric performance budgets отсутствуют в ратифицированном source.
- Windows support baseline и signing/distribution preferences не заданы.
- Initial Git base существует; будущие Git mutations должны выполняться в feature branch и могут требовать managed approval.

**Stop condition**

После architecture baseline остановиться; repository/code bootstrap не начинать без нового approval.
