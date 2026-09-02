# Delivery plan archive

Этот файл не является current source of truth, implementation authorization или входом для текущего расчёта readiness. Текущее состояние находится в [`delivery-plan.md`](delivery-plan.md).

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
