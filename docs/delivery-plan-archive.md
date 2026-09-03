# Delivery plan archive

Этот файл не является current source of truth, implementation authorization или входом для текущего расчёта readiness. Текущее состояние находится в [`delivery-plan.md`](delivery-plan.md).

## Replaced readiness snapshots

- `2026-09-03T05:10:51Z`: initial release `70/139 = 50.4%`, full roadmap `70/164 = 42.7%`; terminal EPIC-08 state before the bounded EPIC-04 fix. Archived when GOAL-004 reached terminal `72/72` with exact-main CI.
- `2026-09-02T21:05:52Z`: initial release `0/139 = 0%`, full roadmap `0/164 = 0%`; start-of-GOAL-004 foundation had no completed product AC. Snapshot archived when it ceased to be one of the current/previous independently calculated states.
- `2026-09-02T21:43:13Z`: initial release `13/139 = 9.4%`, full roadmap `13/164 = 7.9%`; only EPIC-09 core AC were complete after PR #3. Archived when EPIC-02 terminal CI produced a newer snapshot without changing its already credited AC.
- `2026-09-02T22:15:32Z`: initial release `28/139 = 20.1%`, full roadmap `28/164 = 17.1%`; EPIC-02 product AC were complete but terminal CI Evidence was not. Archived after EPIC-03 produced a newer independent readiness calculation.
- `2026-09-02T22:48:15Z`: initial release `28/139 = 20.1%`, full roadmap `28/164 = 17.1%`; EPIC-02 was `READY` before EPIC-03 implementation. Archived after initial PR #6 CI produced a newer Evidence snapshot.
- `2026-09-02T23:02:05Z`: initial release `41/139 = 29.5%`, full roadmap `41/164 = 25.0%`; EPIC-03 had local code/test Evidence and pending CI. Archived after terminal EPIC-03 delivery Evidence.
- `2026-09-02T23:12:59Z`: initial release `41/139 = 29.5%`, full roadmap `41/164 = 25.0%`; initial PR #6 CI had exposed an inherited abort-fixture race and the deterministic fix was still local. Archived after the grouped follow-up passed.
- `2026-09-02T23:21:16Z`: initial release `41/139 = 29.5%`, full roadmap `41/164 = 25.0%`; EPIC-03 exact-merge `main` CI was terminal success. Archived after EPIC-04 produced a newer independently calculated product state.
- `2026-09-03T00:03:30Z`: initial release `51/139 = 36.7%`, full roadmap `51/164 = 31.1%`; EPIC-04 partial `10/12` was merged, while EPIC-08 and `E09-AC06` were not implemented. Archived after terminal EPIC-08 delivery produced a newer current/previous pair.

## GOAL-004 — 72-AC core product tranche

- **State:** `DONE`; `72/72` selected product AC and all required Evidence completed.
- **Authorization:** explicit user instruction от `2026-09-03` to implement an approximately 70-AC unified Goal through separate epic PRs and continue fixes despite product blockers.
- **Scope/outcome:** EPIC-02 `15/15`, EPIC-03 `13/13`, EPIC-04 `12/12`, EPIC-08 `18/18`, EPIC-09 `14/14`; all five epics `READY` with SPEC/CODE/TEST/CI `✅`, DEPLOY/LIVE `N/A`.
- **Terminal fix PR:** [#9](https://github.com/Just9120/llm-inspector/pull/9), head `74024d15e7556f4deac4f30f2bb4a1001e725d95`, PR CI `33720248633` success, merged as `7110c70b6975c939915273b005f05eedfcd2eb14`.
- **Exact-main Evidence:** CI [33720428488](https://github.com/Just9120/llm-inspector/actions/runs/33720428488) success on merge SHA; every required step completed successfully.
- **Product readiness at closure:** initial release `72/139 = 51.8%`; full roadmap `72/164 = 43.9%`.
- **Cleanup:** local `main` and `origin/main` synchronized; merged branch safely removed before GOAL-005 branch creation.
- **Metadata limitation:** terminal Evidence was recorded in the merged PR comment and recovered in the next substantive PR because no approved post-merge writer exists.

## GOAL-004 / EPIC-08 — History, analytics и retention

- **Product outcome:** `READY 18/18`; its real-schema/runtime-canary Evidence also completed `E09-AC06`, making EPIC-09 `READY 14/14`.
- **Product PR:** [#8](https://github.com/Just9120/llm-inspector/pull/8), head `9d4651f883a4fac843510ac378346da39214932f`.
- **Local Evidence:** exact SDK `10.0.400`, locked normal/RID restores, format, Release build `0 warnings / 0 errors`, `112/112` tests, self-contained `win-x64` publish and smoke.
- **Terminal CI:** PR run `33702613336` success; merged as `5757652943753e549ef85f81308c0fc0c6d83686`; exact-main run `33702791561` success with all required steps.
- **Cleanup:** merged branch had zero unique commits and was removed locally/remotely; local `main` was fast-forwarded before the EPIC-04 fix branch.
- **Metadata limitation:** repository has no approved post-merge writer; terminal Evidence was recorded in the merged PR comment and recovered into the next substantive fix PR.

## GOAL-004 / EPIC-03 — Live state, quality и deterministic abort fixture

- **Product outcome:** `READY 13/13`; SPEC/CODE/TEST/CI `✅`, DEPLOY/LIVE `N/A`.
- **Product PR:** [#6](https://github.com/Just9120/llm-inspector/pull/6), initial head `d1e8f313b6652cfe1656f1de0c4fac99bd852103`.
- **Detected gate failure:** PR CI `33693671440` failed the inherited `BackendBodyAbortKeepsOriginalStatusAndRecordsRelayFailure` fixture because the backend could abort before the proxy had observed response headers; required downstream steps correctly did not supply success Evidence.
- **Grouped fix:** fixture now releases backend abort only after the client receives exact `200` and the partial body through the proxy; validated head `eb83e98df4fb130257e7f1cc298af1e2b9b99c8c` passed follow-up PR CI `33694308639`.
- **Terminal CI:** merged as `ba63d0b219e61527d3d81994638dee39a11c14bf`; exact-merge `main` run `33694559218` succeeded with all required steps.
- **Cleanup:** merged branch had zero unique commits and was removed locally/remotely; local `main` was fast-forwarded before EPIC-04 branch creation.

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
