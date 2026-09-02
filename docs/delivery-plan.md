# Delivery plan

> Dashboard status: `GOAL-002 IN_PROGRESS`
> Updated: `2026-09-02T18:45:10Z`

## Current Goal

### `GOAL-002 — Утвердить architecture baseline и executable delivery foundation design`

- **State:** `IN_PROGRESS`.
- **Authorization source:** explicit user approval от `2026-09-02`: «Да, вот теперь идем через ПРы по инструкции» в ответ на proposal `GOAL-002`.

**Scope**

- Выбрать и обосновать Windows desktop/runtime stack, package manager и repository layout.
- Определить transparent OpenAI-compatible observation boundary и client/backend data flow.
- Определить process/module boundaries, failure isolation и state ownership.
- Определить local storage/retention/migration model и privacy enforcement points.
- Сформировать backend capability matrix для Ollama, llama.cpp и LM Studio.
- Определить supported Windows matrix и measurable performance/idle benchmark protocol; numeric budgets вынести на explicit user approval, если source их не задаёт.
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

**Required Evidence:** `SPEC: ✅`; `CODE: N/A`; `TEST: ✅` (structural/link/count validation); `CI: N/A`; `DEPLOY: N/A`; `LIVE: N/A`.

**Known blockers/dependencies**

- Numeric performance/idle/throughput budgets отсутствуют в ратифицированном source; в этой Goal определяется protocol, а значения остаются owner blocker.
- Signing identity/certificate и release channel не создаются в этой Goal; architecture должна отделить выбранную package model от external release prerequisites.

**Stop condition:** после завершения architecture baseline и applicable PR flow остановиться; repository/code bootstrap не начинать без нового approval.

## Active execution checkpoint

| Field | Verified state |
|---|---|
| Updated UTC | `2026-09-02T18:45:10Z` |
| Expected base branch | `main` |
| Base SHA | `581e18097a6e9e13098f510fc1f82d3e45f849f7` — verified `origin/main` at Goal start |
| Working branch | `codex/architecture-baseline` |
| Last verified revision | `d589fb5af762661b2964a5141c97f5d4efd03c81` — architecture decision commit; current documentation sync is uncommitted |
| Initial worktree state | Clean; local `main`, `origin/main` и GitHub `main` synchronized at base SHA |
| Current worktree state | Architecture commit complete; README, product operational fields и CI/CD project profile edited for consistency |
| Completed work | Stack/boundaries/data flow/privacy/storage/capability matrix/test strategy/support matrix/performance protocol/release design fixed in `docs/architecture.md` |
| Current step | Canonical/operational documentation synchronization and structural validation |
| Next exact action | Обновить этот checkpoint, выполнить full local documentation validation и создать final local documentation commit |
| PR / CI | PR не создан; workflows отсутствуют и находятся вне scope этой Goal |
| Deployment | Server/runtime CD explicitly disabled; self-contained `win-x64` + signed MSIX release design выбран, но pipeline/signing/channel не настроены |
| Blockers | Numeric performance budgets и external signing identity/channel отсутствуют; они не блокируют DoD `GOAL-002` |
| Unverified assumptions | Design не подтверждён source code/runtime; concrete package versions, commands и backend fixture versions должны быть проверены в следующих Goals |
| Preserved pre-existing changes | Goal начата на clean worktree; unrelated changes не обнаружены |

## Project readiness snapshots

| Snapshot | Timestamp | Initial release | Full agreed roadmap | Denominator и основание |
|---|---|---:|---:|---|
| Current | `2026-09-02T18:45:10Z` | `0/139 = 0%` | `0/164 = 0%` | Все 164 atomic AC пересчитаны; architecture закрыла EPIC-01 SPEC gap, но ни один product AC ещё не имеет CODE/TEST/CI Evidence |
| Previous | `2026-09-02T18:01:24Z` | `0/139 = 0%` | `0/164 = 0%` | 139 atomic AC в 12 initial-release epics; ещё 25 AC в 6 canonical backlog epics; code/tests отсутствовали |

Delta: `0 п.п.` для initial release и full roadmap. `EPIC-01 SPEC` изменён с `◐` на `✅` после утверждения support matrix, но completion остаётся `0/4`: architecture сама по себе не выполняет runtime acceptance criteria.

## Epic readiness и Evidence

| Epic | Status | Completed / total | Readiness | SPEC | CODE | TEST | CI | DEPLOY | LIVE |
|---|---|---:|---:|---|---|---|---|---|---|
| EPIC-01 Windows application/boundary | ⬜ BACKLOG | 0/4 | 0% | ✅ | — | — | — | N/A | N/A |
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

1. `SPEC`: numeric monitoring/idle/throughput budgets и frozen reference hardware/workload fixtures не утверждены (`EPIC-12`); measurement protocol определён.
2. `TECH`: trustworthy GPU/provider coverage, sampling interval и process attribution требуют focused Windows spike до `EPIC-06 READY`.
3. `RELEASE`: production signing identity и distribution channel требуют owner/external decision до Windows release Goal; CD остаётся отключён.
4. `EVIDENCE`: architecture decisions пока не подтверждены code/tests/runtime; это ожидаемое состояние до code Goals.

## Roadmap

Roadmap — sequencing proposal, не implementation authorization.

1. **R0 — Product contract:** завершён локально; 139 initial-release и 25 backlog AC.
2. **R1 — Architecture baseline:** decisions и support matrix завершены в working branch; merge Evidence pending.
3. **R2 — Repository/CI foundation:** следующая proposal — reproducible `.NET 10` toolchain, compile-only module skeleton, tests и PR CI без CD.
4. **R3 — Privacy-preserving OpenAI-compatible vertical slice:** один client/backend path, streaming, telemetry quality и negative privacy tests.
5. **R4 — Required adapters и live telemetry:** Ollama, llama.cpp, LM Studio, known clients, timings/context.
6. **R5 — Resources, diagnostics и analytics:** collectors, explainable rules, history/retention.
7. **R6 — Windows UX/reliability:** tray, notifications, snapshot, crash recovery и performance budgets.
8. **R7 — Windows release:** package/signing/distribution/update validation; это build/release flow, не server CD.

## Candidate next Goal

### `GOAL-003 — Создать reproducible .NET repository skeleton и PR CI foundation`

- **State:** `PROPOSED`.
- **Authorization needed:** отдельное explicit user approval после закрытия `GOAL-002`.

**Scope**

- Создать `.NET 10` solution/project skeleton по dependency direction из `docs/architecture.md` без product behavior.
- Зафиксировать SDK, Central Package Management, executable-app lock files, deterministic build/analyzer policy и repository ignore/editor settings.
- Создать минимальный Avalonia application shell/composition root, который компилируется и запускается как empty development shell без proxy listener, persistence или telemetry.
- Создать test projects и architecture/dependency smoke tests, доказывающие intended module direction.
- Сделать install/format/build/test и self-contained `win-x64` publish commands фактически executable; синхронизировать `AGENTS.md`, README и CI/CD profile.
- Добавить least-privilege PR/`main` CI на ephemeral GitHub-hosted Windows runner с SHA-pinned actions и без secrets/deploy.
- Проверить CI на Pull Request; server/runtime CD не добавлять.

**Non-goals**

- Functional reverse proxy, backend adapters или traffic parsing.
- SQLite schema/history, resource collectors, analytics, diagnostics, tray/autostart/notifications.
- MSIX packaging, code signing, release publication, automatic update или any CD.
- Выполнение product feature AC только фактом scaffold/placeholder UI.

**Acceptance criteria**

1. Exact supported `.NET 10` SDK и dependency graph reproducibly restore-ятся в locked mode.
2. Все planned production/test projects существуют, compile и соблюдают automatic dependency-boundary tests.
3. Minimal Avalonia shell запускается локально; placeholder не заявляется как выполненный product UI.
4. Documented format/build/test/`win-x64` publish commands выполняются с clean checkout-equivalent state.
5. PR CI имеет explicit read-only permissions, pinned external actions, no secrets and no deployment steps.
6. Required repository check фактически проходит на exact PR revision либо absence of enforceable ruleset фиксируется отдельно без fabricated enforcement claim.
7. README, `AGENTS.md`, CI/CD profile и delivery checkpoint соответствуют реально проверенным commands/files/settings.
8. Следующая vertical-slice Goal только proposed; proxy/storage/product implementation автоматически не начинается.

**Required Evidence:** `SPEC: ✅`; `CODE: ✅`; `TEST: ✅`; `CI: ✅`; `DEPLOY: N/A`; `LIVE: N/A`.

**Known blockers/dependencies:** package versions выбираются и проверяются только при authorization; GitHub Actions usage/settings надо проверить до workflow runs. Signing identity, MSIX и performance budgets не блокируют эту Goal.

**Stop condition:** после green PR/merge и metadata synchronization остановиться; OpenAI-compatible vertical slice не начинать без следующего approval.
