# AGENTS.md

> Repository instruction contract: `goal-driven-v1`

## 1. Назначение и scope

Этот файл — repository-specific router и durable execution kernel для coding agents. Он определяет canonical documents, source authority, Goal authorization, readiness, checkpoint/recovery, repository boundaries и project commands.

Session orchestration задаётся текущей user instruction; отдельный общий coding-workflow document не используется. Этот файл не заменяет product contract, delivery state, architecture, runbooks или CI/CD safety contract.

Root `AGENTS.md` действует на весь repository. Перед изменением пути учитывай все применимые `AGENTS.md` / `AGENTS.override.md` от root до затрагиваемого subtree.

Nested instructions могут уточнять local commands, conventions и process details только внутри subtree. Они не расширяют user-authorized scope, не меняют durable product/CI-CD contracts и не разрешают bypass safety gates. Неразрешимый conflict — blocker для затронутого действия.

---

## 2. Document router

| Документ / источник | Роль | Читать когда |
|---|---|---|
| User-provided upstream requirements | Сырые/несогласованные requirements и идеи; supporting input | Audit, reconciliation, explicit proposal task |
| `README.md` | Русскоязычный entry point, quickstart и navigation | Первый вход; неизвестны commands/structure |
| `AGENTS.md` | Repository instructions и routing | Всегда: root + applicable nested files |
| `docs/project-spec.md` | Canonical согласованный product contract, epics и atomic product AC | Scope, behavior, business rules, data, integrations, constraints, readiness |
| `docs/delivery-plan.md` | Current Goal, durable authorization, checkpoint и readiness snapshots | Active work, resume/recovery, Goal/delivery-state change |
| `docs/delivery-plan-archive.md` | Historical delivery context | Только history/reconciliation или cleanup active plan |
| `docs/ci-cd-rules.md` | CI/CD, deployment и production safety | Audit или работа с workflows, artifacts, secrets, environments, migrations, runtime |
| `docs/architecture.md` | Optional current architecture map | Boundaries, ownership, data flow, integrations, deployment topology |
| `docs/runbooks/*` | Approved operational procedures | Только затронутая operation/surface |
| `docs/utility/context-bundle-builder.md` | Optional scoped Builder contract | Только Builder Goal/workstream |
| `docs/ai-delivery-infrastructure-plan.md` | Optional AI tooling/infrastructure workstream | Только если файл и реальный workstream существуют |

Если referenced document отсутствует, не придумывай его содержание. Optional document не создаётся только ради соответствия router.

Generated bundles, exports, chats, logs, issues/trackers, temporary reports и archive — supporting evidence/context, но не active source of truth и не implementation authorization.

---

## 3. Authority и Evidence

### Normative authority

1. Текущая explicit user instruction.
2. `docs/project-spec.md` — durable requirements, business rules, product AC и constraints.
3. Approved `Current Goal` в `docs/delivery-plan.md` — execution authorization только в пределах product contract и согласованного scope.
4. Applicable repository `AGENTS.md` — process, routing и repository boundaries.
5. `docs/architecture.md`, relevant runbooks и scoped utility contracts — supporting contracts внутри своей surface.

`docs/ci-cd-rules.md` — обязательный safety contract для применимой CI/CD/production работы; изменять его можно только по explicit CI/CD policy task.

Upstream requirements, ideas, issues и historical notes не меняют `docs/project-spec.md` автоматически. Расхождение фиксируется как requirement drift, `SPEC` gap или proposal; implementation и изменение denominator требуют решения пользователя.

### Evidence strength для actual-state claims

```text
LIVE/runtime observation
→ deployment record exact revision/artifact
→ CI/check exact revision
→ automated/manual test
→ code/config exact revision
→ documentation/historical claim
```

Intended behavior и actual state не смешиваются. Conflict между ними описывается как drift.

---

## 4. Старт, resume и recovery

На старте:

1. Установи root и applicable instructions.
2. Проверь branch, `HEAD`, worktree, remotes и фактическую base branch.
3. Если существует `docs/delivery-plan.md`, прочитай Current Goal и checkpoint.
4. `APPROVED`/`IN_PROGRESS` Goal с подтверждённым checkpoint продолжай как `RESUME`.
5. При расхождении checkpoint с Git/GitHub/CI/CD state сначала выполни `RECOVERY`.
6. Если active approved Goal отсутствует, следуй текущей user instruction; proposed/candidate Goal implementation не авторизует.
7. Читай только relevant source-of-truth sections, code, tests и configuration; broad context используй для audit/reconciliation/architecture/release work.

При recovery actual state имеет приоритет над checkpoint как evidence, но не меняет requirements или Goal scope автоматически. Не повторяй implementation «на всякий случай», не создавай duplicate PR и не удаляй unknown changes.

---

## 5. Goal contract и authorization

Goal states:

```text
PROPOSED | APPROVED | IN_PROGRESS | BLOCKED | PENDING_EXTERNAL_GATE | DONE
```

Implementation авторизована только current explicit user instruction либо Current Goal со state `APPROVED` / `IN_PROGRESS`, durable authorization source и неизменённым согласованным scope.

Current Goal фиксирует: stable ID/title, authorization source, scope, non-goals, Goal AC, required Evidence, known blockers/dependencies, state и stop condition.

Внутри согласованной Goal агент действует самостоятельно: выбирает implementation strategy, декомпозирует работу, добавляет необходимые tests и исправляет небольшие связанные defects, без которых Goal нельзя безопасно довести до DoD.

Новая authorization требуется для material change scope/non-goals/Goal AC/required Evidence, изменения durable product contract, новой architecture boundary/production dependency, destructive/privileged operation или перехода к следующей Goal.

Audit findings, backlog items, recommendations, TODO, issues, upstream ideas и `Candidate next Goals` сами по себе не авторизуют implementation.

После `DONE`, `BLOCKED` или `PENDING_EXTERNAL_GATE` остановись. К следующей Goal без явного согласования пользователя не переходи.

---

## 6. Product readiness и Goal DoD

Product/epic readiness считается только по canonical atomic product AC из `docs/project-spec.md`. Goal DoD считается по Goal AC из `docs/delivery-plan.md`.

Goal AC не добавляются автоматически в product denominator и не меняют durable requirements. Они могут подтверждать existing product AC; отсутствующий requirement/AC фиксируется как `SPEC` gap/proposal.

```text
Product status: ⬜ BACKLOG | 🟦 IN PROGRESS | 🟩 READY | ⛔ BLOCKED (modifier)
Evidence: SPEC | CODE | TEST | CI | DEPLOY | LIVE
Evidence status: ✅ confirmed | ◐ partial | ❌ failed | — absent | N/A not required
```

`READY` означает `100%` in-scope product AC и `✅` для всех required Evidence. Completion требует явного denominator; subjective partial percentage запрещён — criterion декомпозируется без изменения смысла либо считается невыполненным.

После commit пересчитывай затронутую readiness только если изменилось выполнение product AC. Общую readiness пересчитывай перед PR, после material change scope/denominator, при выполнении product AC и при закрытии Goal. Evidence синхронизируй после фактических TEST/CI/DEPLOY/LIVE событий.

---

## 7. Delivery plan и checkpoint

`docs/delivery-plan.md` — operational dashboard, durable Current Goal contract и verified execution state. Он должен содержать:

- **Current Goal:** ID/title, state, authorization source, scope, non-goals, Goal AC, required Evidence, blockers.
- **Active execution checkpoint:** updated UTC, base branch/SHA, working branch, last verified revision, worktree state, completed work, current step, one `Next exact action`, validation/Evidence, PR, CI, deployment, blockers, unverified assumptions и preserved pre-existing changes.
- **Project readiness:** только current и previous independently calculated snapshots.
- **Candidate next Goals:** proposals без implementation authorization.

Checkpoint хранит только facts, decisions и exact identifiers; не хранит chain of thought, secrets, credentials или raw logs. `Last verified revision` — последний commit, состояние которого покрывает checkpoint; не создавай metadata-only commit loop ради ссылки на собственный containing commit.

Обновляй checkpoint после base/branch change, commit, push/PR, CI/review, merge, deployment/LIVE, blocker/external gate и interruption.

`docs/delivery-plan-archive.md` создаётся при первой необходимости. Переноси туда closed Goals, obsolete checkpoints, старые snapshots и длинные PR/CI/deployment chains. Archive не является current source of truth, authorization или readiness input.

---

## 8. Git и execution boundary

Перед записью зафиксируй root, base branch/SHA, working branch и исходный worktree state.

```text
git fetch
→ если local base clean и допускает safe fast-forward — sync
→ иначе isolated branch/worktree от verified origin/<base>
→ focused changes в отдельной feature/fix branch
```

Не смешивай изменения агента с unrelated pre-existing user changes и не выполняй destructive reset/clean, checkout-overwrite, force push или удаление unknown state без explicit authorization.

После завершённой узкой задачи выполняй relevant checks и создавай reviewable commit. После согласованного implementation scope создавай/обновляй PR, анализируй required checks и продолжай исправления внутри той же Goal.

Merge и applicable delivery выполняй самостоятельно только если это входит в Goal, обязательные gates выполнены и права позволяют. DEPLOY/LIVE подтверждай только фактическим Evidence; неприменимые dimensions получают `N/A` по Goal DoD.

Post-deploy metadata write без отдельного PR допустим только через approved path/field-scoped mechanism с minimal permissions, exact deployed revision и loop protection. При отсутствии required mechanism зафиксируй blocker/technical debt; protection rules не обходи.

После Goal closure безопасно обнови local base и удали только созданные этой работой merged branches/worktrees после проверки отсутствия unique commits или unrelated state. Затем остановись.

---

## 9. Documentation write policy

| Документ | Разрешённое update без изменения durable scope |
|---|---|
| `README.md` | Quickstart, commands, structure и canonical navigation при фактическом изменении |
| `docs/project-spec.md` | Только отделённые operational fields: status, completion, Evidence, verified IDs, blocker, timestamp |
| `docs/delivery-plan.md` | Current Goal, checkpoint, current/previous snapshots, blockers и next action |
| `docs/delivery-plan-archive.md` | Closed/obsolete delivery history |
| `docs/architecture.md` | Фактические architecture/runtime/data-flow changes |
| `docs/runbooks/*` | Изменение соответствующей approved procedure |
| `docs/ci-cd-rules.md` | Только explicit CI/CD policy task |
| `AGENTS.md` | Только explicit repository-agent-policy task |
| Scoped utility/tooling contract | Только соответствующий authorized workstream |

Для agent-writable operational metadata в `docs/project-spec.md` используй визуально отделённый block. Без explicit user instruction не меняй durable scope, requirements, business rules, product AC, data ownership, public behavior или security/runtime constraints. Upstream requirements не синхронизируются в canonical contract автоматически.

Все pre-merge documentation changes включай в текущий PR. Отдельный follow-up PR только ради delivery metadata не создавай, если synchronization должен выполнить approved post-deploy mechanism.

---

## 10. CI/CD, validation и repository commands

При audit или работе с workflows, artifacts, secrets, environments, deployment, production, migrations, stateful systems или post-deploy automation прочитай `docs/ci-cd-rules.md` и фактический Project CI/CD profile.

Не меняй CI/CD safety contract, credential model, deployment topology или production operations без explicit scope. Failed, skipped, cancelled, timed-out, unavailable и not-run required checks не являются success.

`UNSET` означает «определить по repository configuration», а не «придумать».

| Назначение | Команда |
|---|---|
| Install | `UNSET` |
| Format/lint | `UNSET` |
| Typecheck | `UNSET` |
| Focused tests | `UNSET` |
| Full tests | `UNSET` |
| Build | `UNSET` |
| Run locally | `UNSET` |
| CI-equivalent | `UNSET` |

Не добавляй heavy testing infrastructure только ради заполнения таблицы.

В итоговом отчёте укажи changed files, checks и terminal statuses, Current Goal state, PR/CI/deployment identifiers, limitations, blockers и remaining risks. Не заявляй merge, deployment, LIVE или Goal `DONE` без required Evidence.
