# Delivery plan archive

Этот файл не является current source of truth, implementation authorization или входом для текущего расчёта readiness. Текущее состояние находится в [`delivery-plan.md`](delivery-plan.md).

## GOAL-001 — Ратифицировать product contract и CD applicability

- **State:** `DONE`.
- **Authorization:** explicit user instruction от `2026-09-02`: все requirements upstream документа согласованы; перенести их в `project-spec.md` и декомпозировать; Windows desktop product не использует runtime CD.
- **Scope:** canonicalize 72 upstream requirement bullets, сохранить 9 `[Backlog]` items как canonical backlog, сформировать atomic AC/Evidence/readiness, синхронизировать documentation и CI/CD project profile.
- **Non-goals:** architecture selection, product code, dependencies, CI workflows, remote Git writes, Windows package publication.
- **Outcome:** 12 initial-release epics / 139 AC; 6 backlog epics / 25 AC; full roadmap 164 AC; server/runtime CD disabled.
- **Evidence:** SPEC ✅; CODE N/A; TEST ✅ (local structural/count/link/reference checks); CI N/A; DEPLOY N/A; LIVE N/A.
- **Closed:** `2026-09-02T18:01:24Z`.
- **Limitations:** Windows support matrix и numeric performance budgets остаются SPEC gaps; documents остаются local/uncommitted из-за отсутствия base SHA и read-only `.git`.
