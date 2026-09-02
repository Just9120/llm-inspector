# Delivery plan

> Dashboard status: `GOAL-003 IN_PROGRESS`
> Updated: `2026-09-02T20:11:52Z`

## Current Goal

### `GOAL-003 — Создать reproducible .NET repository skeleton и PR CI foundation`

- **State:** `IN_PROGRESS`.
- **Authorization source:** explicit user instruction от `2026-09-02`: «ставь цель и начинай» после proposal `GOAL-003`.

**Scope**

- Создать `.NET 10` solution/project skeleton по dependency direction из `docs/architecture.md` без product behavior.
- Зафиксировать SDK, Central Package Management, lock files, deterministic build/analyzer policy и repository ignore/editor settings.
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
- Изменение ратифицированного product scope, business rules или product AC.

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

**Current Goal verification:** `0/8` Goal AC complete at start. Evidence: `SPEC: ✅`; `CODE: —`; `TEST: —`; `CI: —`; `DEPLOY: N/A`; `LIVE: N/A`.

**Known blockers/dependencies**

- Package/action versions должны быть выбраны по current authoritative sources и подтверждены locked restore/CI.
- GitHub Actions usage/settings и repository rulesets должны быть проверены до claims о CI enforcement.
- Signing identity, MSIX и numeric performance budgets не входят в Goal и не блокируют её.

**Stop condition:** после green PR/merge, recovery-safe metadata synchronization и cleanup остановиться; OpenAI-compatible vertical slice не начинать без следующего approval.

## Active execution checkpoint

| Field | Verified state |
|---|---|
| Updated UTC | `2026-09-02T20:11:52Z` |
| Expected base branch | `main` |
| Base SHA | `00ca8c3ef727d784ca2e0c9d837231be7f68c5e4` — verified local/`origin/main`/GitHub `main` at Goal start |
| Working branch | `codex/repository-ci-foundation` |
| Last verified revision | `00ca8c3ef727d784ca2e0c9d837231be7f68c5e4` — merged architecture baseline and Goal base |
| Initial worktree state | Clean; local `main`, `origin/main` и GitHub `main` synchronized at base SHA |
| Current worktree state | Feature branch created; GOAL-002 recovery and GOAL-003 durable start metadata are being recorded |
| Completed work | PR #1 merge recovered; absence of branch protection confirmed; GOAL-003 scope authorized and branch isolated |
| Current step | Select and verify exact SDK/package/action versions, then create reproducible skeleton |
| Next exact action | Проверить installed/current supported `.NET 10` SDK and package versions against authoritative sources |
| PR / CI | PR не создан; Actions enabled, default workflow token read-only; `main` branch protection absent; workflow not yet present |
| Deployment | N/A — server/runtime CD explicitly disabled; this Goal only validates unsigned self-contained `win-x64` output locally/CI |
| Blockers | None at start; unavailable supported SDK/package restore or Actions capacity may become evidence blockers |
| Unverified assumptions | GitHub-hosted runner label/action SHAs, exact package versions and all planned commands still require verification |
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

### `GOAL-004 — Реализовать privacy-preserving OpenAI-compatible proxy vertical slice`

- **State:** `PROPOSED`.
- **Authorization needed:** отдельное explicit user approval после закрытия `GOAL-003`.

**Scope**

- Добавить loopback-only Kestrel gateway с отдельной validated backend destination и без generic hosting override для bind address.
- Прозрачно relay-ить один минимальный OpenAI-compatible chat-completions path в non-streaming и SSE streaming режимах.
- Выделять только allowlisted structural telemetry в memory; не сохранять prompt/response/reasoning/tool payloads.
- Добавить stub-backend integration tests для headers/status/body, fragmented SSE, cancellation, disconnect и parser-failure pass-through.
- Добавить automated negative privacy corpus, проверяющий отсутствие canary content в logs, in-memory projections и создаваемых файлах.
- Синхронизировать architecture/commands/checkpoint и провести PR/CI flow без CD.

**Non-goals**

- Полная capability matrix Ollama/llama.cpp/LM Studio и известные client adapters.
- SQLite schema/history, retention, resource collectors, analytics и diagnostics rules.
- Production UI, tray/autostart/notifications и Windows packaging.
- MSIX packaging, code signing, release publication, automatic update или any CD.
- Credential persistence и remote/LAN listening.

**Acceptance criteria**

1. Listener фактически bind-ится только к validated loopback endpoint; wildcard/LAN values fail closed.
2. Stub client получает semantically equivalent non-streaming и SSE responses/status/required headers от stub backend.
3. Structural projection не меняет relayed bytes/event order и переходит в unavailable/degraded state при parse failure без обрыва relay.
4. Cancellation/disconnect корректно отменяют outbound request; concurrent request isolation доказана tests.
5. Canary prompt/response/reasoning/tool content отсутствует во всех allowlisted inspection surfaces и filesystem scan после tests.
6. Scope-mapped product AC получают только фактически доказанный partial/full Evidence; остальные AC не кредитуются.
7. Local CI-equivalent validation и exact PR revision проходят green; DEPLOY/LIVE остаются `N/A`.
8. Следующая Goal только proposed; persistence/UI/backend-matrix scope автоматически не начинается.

**Required Evidence:** `SPEC: ✅`; `CODE: ✅`; `TEST: ✅`; `CI: ✅`; `DEPLOY: N/A`; `LIVE: N/A`.

**Known blockers/dependencies:** concrete endpoint/default port и exact OpenAI-compatible subset должны быть зафиксированы внутри Goal без расширения canonical protocol scope; real backend credentials не нужны благодаря stub fixtures.

**Stop condition:** после green PR/merge и metadata synchronization остановиться; persistence, UI telemetry и full backend matrix не начинать без следующего approval.
