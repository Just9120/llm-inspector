# Delivery plan archive

Этот файл не является current source of truth, implementation authorization или входом для текущего расчёта readiness. Текущее состояние находится в [`delivery-plan.md`](delivery-plan.md).

## Replaced readiness snapshots

- `2026-09-02T21:05:52Z`: initial release `0/139 = 0%`, full roadmap `0/164 = 0%`; start-of-GOAL-004 foundation had no completed product AC. Snapshot archived when it ceased to be one of the current/previous independently calculated states.
- `2026-09-02T21:43:13Z`: initial release `13/139 = 9.4%`, full roadmap `13/164 = 7.9%`; only EPIC-09 core AC were complete after PR #3. Archived when EPIC-02 terminal CI produced a newer snapshot without changing its already credited AC.

## GOAL-004 / EPIC-02 — Backend/client adapters и CI stabilization

- **Product outcome:** `READY 15/15`; SPEC/CODE/TEST/CI `✅`, DEPLOY/LIVE `N/A`.
- **Product PR:** [#4](https://github.com/Just9120/llm-inspector/pull/4), head `f10db376aeb5cf7a039719ae20ea22ce237e283b`, PR CI `33690474686` success, merge `275b7407a015f6a98361f87b74b5dc444f0a8355`.
- **Detected gate failure:** exact-merge `main` CI `33690756965` failed one transport-dependent abort assertion; downstream publish/smoke correctly skipped, so CI Evidence remained failed.
- **Fix PR:** [#5](https://github.com/Just9120/llm-inspector/pull/5), head `521b91c5960196f3effa048f4071bf3b2858f58a`, PR CI `33691566607` success, merge `e1e1b735116b94d73fa87559da8759c5f58d243c`.
- **Terminal CI:** exact-merge `main` run `33691761413` success; all required steps completed without required skips.
- **Cleanup:** both merged feature/fix branches had zero unique commits and were removed locally/remotely; local `main` was fast-forwarded before EPIC-03 branch creation.

## GOAL-003 — Создать reproducible .NET repository skeleton и PR CI foundation

- **State:** `DONE`.
- **Authorization:** explicit user instruction от `2026-09-02`: «ставь цель и начинай» после согласования PR-based flow.
- **Scope:** exact `.NET 10.0.400` toolchain, 9 production/6 test projects, empty Avalonia shell, normal/`win-x64` lock graphs, executable local commands и least-privilege PR/`main` CI без product behavior/CD.
- **Outcome:** foundation реализована; product readiness намеренно осталась `0/139` initial и `0/164` full roadmap.
- **Local Evidence:** locked restores, format, Release build `0 warnings / 0 errors`, `10/10` tests, self-contained `win-x64` publish, smoke и bounded UI launch.
- **Pull Request:** [#2](https://github.com/Just9120/llm-inspector/pull/2), merged `2026-09-02T20:48:29Z`.
- **Merge commit:** `384556f693df9b3dbbc9d06dc2ddbd67328fa5d7`.
- **CI Evidence:** PR run [33681233301](https://github.com/Just9120/llm-inspector/actions/runs/33681233301) success на `acc0a39cc15e228268973883f916ecf7f0e1a2d8`; `main` run [33681476344](https://github.com/Just9120/llm-inspector/actions/runs/33681476344) success на merge commit; all steps terminal success.
- **DEPLOY/LIVE:** N/A; runtime CD target отсутствует.
- **Cleanup:** local/remote feature branch удалена после ancestor/unique-commit checks; local `main == origin/main`, worktree clean.
- **Limitation:** approved post-merge metadata mechanism отсутствовал; terminal Evidence было также записано в merged PR comment, metadata-only follow-up PR/direct push не создавались.

## GOAL-002 — Утвердить architecture baseline и executable delivery foundation design

- **State:** `DONE`.
- **Authorization:** explicit user approval от `2026-09-02`: «Да, вот теперь идем через ПРы по инструкции» в ответ на proposal `GOAL-002`.
- **Scope:** Windows desktop/runtime stack, module/data/privacy boundaries, backend capability matrix, supported Windows matrix, test strategy, performance protocol и Windows build/release design без product implementation.
- **Outcome:** утверждены C# / `.NET 10 LTS`, Avalonia UI, embedded loopback-only Kestrel proxy, SQLite WAL, modular-monolith boundaries, NuGet CPM/lock files, self-contained `win-x64` validation build и signed MSIX release design; server/runtime CD оставлен отключённым.
- **Evidence:** SPEC ✅; CODE N/A; TEST ✅ (structural/link/count/profile validation); CI N/A; DEPLOY N/A; LIVE N/A.
- **Pull Request:** [#1](https://github.com/Just9120/llm-inspector/pull/1), merged `2026-09-02T18:52:30Z`.
- **Merge commit:** `00ca8c3ef727d784ca2e0c9d837231be7f68c5e4`.
- **Closed after recovery verification:** `2026-09-02T20:11:52Z`; local `main`, `origin/main` и GitHub default branch совпали на merge commit.
- **Limitations:** numeric performance budgets, signing identity и distribution channel остаются внешними решениями для будущих Goals.

## GOAL-001 — Ратифицировать product contract и CD applicability

- **State:** `DONE`.
- **Authorization:** explicit user instruction от `2026-09-02`: все requirements upstream документа согласованы; перенести их в `project-spec.md` и декомпозировать; Windows desktop product не использует runtime CD.
- **Scope:** canonicalize 72 upstream requirement bullets, сохранить 9 `[Backlog]` items как canonical backlog, сформировать atomic AC/Evidence/readiness, синхронизировать documentation и CI/CD project profile.
- **Non-goals:** architecture selection, product code, dependencies, CI workflows, remote Git writes, Windows package publication.
- **Outcome:** 12 initial-release epics / 139 AC; 6 backlog epics / 25 AC; full roadmap 164 AC; server/runtime CD disabled.
- **Evidence:** SPEC ✅; CODE N/A; TEST ✅ (local structural/count/link/reference checks); CI N/A; DEPLOY N/A; LIVE N/A.
- **Closed:** `2026-09-02T18:01:24Z`.
- **Git baseline:** ratified documentation сохранена в initial content commit `e0860e4972e486e59fcf3a8499b5da0f2863b96c`.
- **Limitations:** Windows support matrix и numeric performance budgets остаются SPEC gaps.

## BOOTSTRAP-001 — Initial `main` synchronization

- **State:** `DONE` после обязательной final Git/GitHub verification этой task.
- **Authorization:** explicit user instruction от `2026-09-02` выполнить one-time initial commit/push в `main`, затем остановиться перед выбором следующей Goal.
- **Scope:** commit ratified documentation baseline, update checkpoint metadata, выполнить один initial push и доказать equality local/remote HEAD.
- **Non-goals:** feature implementation, CI/repository settings, architecture work, новая Goal.
- **Base content SHA:** `e0860e4972e486e59fcf3a8499b5da0f2863b96c`.
- **Evidence:** local structural validation; final local `HEAD`/`origin/main`/GitHub default-branch verification. Containing metadata commit не self-referenced.
