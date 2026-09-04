# CI/CD Rules

> Universal safety contract: `goal-driven-v1`

## 1. Назначение

Этот документ — universal safety contract для CI, build/artifact pipelines, CD/deployment и связанных production operations.

Он задаёт обязательные boundaries, но не является готовым pipeline recipe и не авторизует изменения сам по себе. Scope задаётся current explicit user instruction или approved Current Goal; каждый adopted project должен заполнить **Project CI/CD profile** в конце документа либо вынести его в один явно указанный canonical file.

Читать документ нужно при изменении workflows, runners, artifacts, secrets, environments, deploy, migrations, rollback, runtime configuration или post-deploy automation. Изменять contract — только по explicit CI/CD policy task.

---

## 2. Universal invariants

1. **CI и CD разделены.** Standard CI проверяет revision и не deploy-ит; CD запускается только от trusted trigger.
2. **Least privilege.** Tokens, Actions permissions, credentials, runner access и environment access минимальны по scope/time.
3. **Untrusted code не получает trusted capability.** PR/fork content не исполняется с production secrets, write token или privileged runner.
4. **Exact identity.** Build/deploy всегда связывается с exact repository, revision/artifact, target и deployment unit.
5. **Fail closed.** Unknown input, identity mismatch, unresolved secret и failed/skipped required gate останавливают flow.
6. **Build once where applicable.** Deploy использует идентифицированный artifact, прошедший required validation.
7. **Stateful work is explicit.** Destructive migration, backup/restore, cleanup и persistent-data operation не скрываются в standard CD.
8. **No secret disclosure.** Secret values не попадают в code, docs, logs, artifacts, caches или generated context.
9. **Auditable outcome.** Run IDs, revision/artifact identity, target environment и post-check result восстанавливаются без raw secret values.
10. **Success after verification.** Deployment success не объявляется до required health/LIVE check.

---

## 3. Required project inputs

До создания или изменения pipeline установи по repository/settings или safe diagnostics:

### CI

- repository и production/default branch;
- supported events и trust model;
- stack, package manager, lockfiles;
- install, lint, typecheck, test и build commands;
- required checks и runner model;
- build outputs/artifacts, если есть.

### CD

- trusted trigger и deploy branch/tag;
- target environment/account/host/cluster;
- target directory/namespace и expected remote/registry, когда применимо;
- expected branch/tag/release и deploy model;
- intended deployment unit;
- exact commit/artifact identity model;
- credential и runtime-config owner;
- environment protection/approval rules;
- health/LIVE checks;
- concurrency/cancellation policy;
- stateful services и migration class;
- rollback/forward-fix policy;
- post-deploy metadata mechanism.

Неизвестные значения не придумывай. Используй `UNSET`, safe diagnostic или blocker.

---

## 4. Trust boundaries и GitHub Actions security

### 4.1. Untrusted pull requests

Workflow, исполняющий untrusted PR/fork code, не получает:

- production secrets/credentials;
- write-capable repository token без narrowly justified job;
- production environment access;
- privileged persistent self-hosted runner;
- право публиковать production-trusted artifact без отдельной trusted validation.

`pull_request_target` и аналогичный privileged context запрещено сочетать с checkout/execute/build untrusted PR code. Для labels/comments/metadata обрабатывай PR values как untrusted data.

### 4.2. Permissions и dependencies

- Задавай `permissions` явно на workflow/job уровне; default — read-only или none.
- Write permissions и `id-token: write` выдавай только нужному job.
- Не передавай write token в steps, которым он не нужен.
- External actions/reusable workflows фиксируй по полному immutable commit SHA; tag допустим только как комментарий.
- Оцени owner, source, permissions, maintenance и supply-chain risk новой dependency.
- Inputs/secrets reusable workflow объявляются явно; broad secret inheritance не используется без необходимости.

### 4.3. Script injection

Не вставляй untrusted GitHub expression напрямую в shell/program source. Передавай значение через quoted environment variable/structured input и валидируй формат. `eval` и dynamic command construction из untrusted data запрещены.

### 4.4. Runners

- Для untrusted PR предпочитай ephemeral GitHub-hosted runner.
- Self-hosted runner должен иметь isolation, patching, cleanup и ограниченный repository/network access.
- Untrusted public-fork code не запускается на runner с internal network, production credentials или persistent sensitive state.
- Deploy runner не используется как общий PR runner.

### 4.5. Credentials, environments и logs

- Предпочитай short-lived/OIDC credentials long-lived static secrets, если provider это поддерживает.
- Production jobs используют protected Environment или эквивалентный gate.
- Allowed branches/tags, required reviewers и approvals не обходятся.
- Не печатай `.env`, resolved secret-bearing config, tokens, private keys или authorization headers.
- Persistent debug, раскрывающий environment/credentials, запрещён.

### 4.6. Concurrency, timeout и retry

- CI может отменять stale runs, если это безопасно.
- Production deploy сериализуется по target environment.
- Cancellation in-progress production deploy задаётся явно; unsafe cancellation запрещена.
- Jobs имеют разумный timeout.
- Retry допустим только для idempotent/transient operations и не скрывает deterministic failure.

---

## 5. CI contract

CI должен:

- запускаться на project-approved events;
- использовать intended revision и clean isolated workspace;
- устанавливать dependencies reproducibly с lockfile при наличии;
- выполнять existing relevant checks;
- валидировать build/configuration, если это часть current Goal или project Definition of Done;
- иметь однозначные required check names;
- завершаться non-zero при required failure;
- сохранять только необходимые artifacts/results.

CI не должен:

- deploy-ить;
- использовать production credentials без отдельного narrowly scoped security job;
- менять protected branch или создавать auto-fix commits по умолчанию;
- ослаблять tests/lint/type gates ради green status;
- считать skipped/cancelled/timed-out required job успешным;
- выполнять unrelated cleanup, migrations или infrastructure operations.

Если конкретный check отсутствует, используй smallest available useful validation и зафиксируй gap. Не добавляй heavy infrastructure только ради формального соответствия.

---

## 6. Build и artifact contract

Если проект deploy-ит package/image/archive:

- artifact создаётся в trusted build context;
- связывается с source SHA и build run ID;
- получает immutable digest/checksum, когда формат это поддерживает;
- не пересобирается молча при promotion между environments;
- не содержит secrets, runtime state или unintended source files;
- имеет подходящие retention и access controls;
- provenance/attestation применяется, когда этого требует risk/profile.

Mutable tag (`latest`, branch tag) не является достаточной identity без immutable digest/version. Artifact untrusted PR не становится production-trusted только из-за успешного workflow.

---

## 7. CD contract

CD запускается только от trusted event/revision согласно Project CI/CD profile.

До изменения target state deployment проверяет:

- expected repository и exact source revision/artifact;
- intended branch/tag/release;
- target environment/account/host/cluster;
- target directory/namespace и expected remote/registry, когда применимо;
- deployment unit/service;
- credentials и runtime configuration presence;
- отсутствие unsafe local tracked changes для git-based deploy;
- migration/stateful preconditions.

CD должен:

- использовать minimal permissions;
- изменять только intended deployment unit;
- быть idempotent или иметь documented safe retry boundary;
- сериализовать production deploy;
- сохранять existing runtime secrets;
- выполнять required post-deploy health/LIVE check;
- публиковать deployment Evidence;
- сообщать success только после required post-check.

CD не должен:

- deploy-ить unreviewed/unverified revision;
- автоматически выбирать неизвестный target;
- выполнять broad cleanup, hardening или bootstrap;
- менять firewall/users/SSH policy без отдельной task;
- удалять persistent data/volumes;
- запускать uncontrolled migration;
- маскировать failed post-check;
- импровизировать destructive rollback.

---

## 8. Runtime configuration и secrets

Canonical runtime-config owner указывается в profile: Environment secrets, secret manager, platform config, target-host file или иной mechanism.

Rules:

- real secret values не коммитятся и не копируются в docs/tests/prompts/bundles;
- `.env.example`, `.env.sample`, `.env.template` содержат только safe schema/examples;
- production `.env` не перезаписывается template-файлом целиком;
- missing non-secret keys можно добавлять только documented idempotent mechanism без изменения existing values;
- unresolved required placeholder блокирует deploy;
- validation проверяет presence/shape без раскрытия value;
- long-lived credentials имеют rotation/revocation procedure.

Не используй команды, способные вывести resolved secrets, только ради validation.

---

## 9. Stateful services и migrations

Stateful services включают databases, queues, Redis, vector/object/file storage, persistent volumes и другие owners невосстанавливаемых данных.

Migration class:

```text
NONE
BACKWARD_COMPATIBLE_AUTOMATED
MANUAL_GATED
```

`BACKWARD_COMPATIBLE_AUTOMATED` допустима в CD только если migration versioned/reviewable, совместима на rollout window, safe on retry, имеет известные timeout/locking/failure behavior, выполненные backup/recovery preconditions и post-check.

`MANUAL_GATED` требует отдельной explicit task со scope/owner, preconditions, backup/recovery plan, downtime/compatibility expectation, validation и stop/rollback/forward-fix criteria.

Standard CD не выполняет backup/restore, volume recreation, destructive cleanup, data move, reindex или irreversible migration без такого contract.

---

## 10. Rollback и forward-fix

Automatic rollback разрешён только когда documented strategy безопасна для deployed artifact, schema и persistent state.

Если rollback safety не доказана:

- останови flow после failed post-check;
- сохрани Evidence;
- не выполняй destructive recovery;
- используй approved forward-fix или manual gated procedure.

Rollback не удаляет/recreate persistent data и не разворачивает application version, несовместимую с уже применённой migration.

---

## 11. Git-based VPS / Docker Compose profile

Этот раздел применяется только к mutable Git checkout + Docker Compose на VPS/server.

До deploy проверь deploy directory, remote URL, branch, target commit, worktree, runtime config, intended Compose project/services и stateful volumes. Для SSH access используй явную host-key verification policy; отключение проверки host identity запрещено.

Code update должен быть fast-forward/checkout exact reviewed revision или эквивалентной безопасной операцией. Broad `reset --hard`/`clean` не является normal deploy strategy.

Deployment изменяет только allowlisted application services. `docker compose down`, volume removal и system-wide prune не входят в standard CD.

Initial bootstrap, deploy-user/SSH setup, directory migration, firewall/hardening и repository access model требуют отдельной setup/maintenance task.

Не путай:

```text
Deploy Key / target credential = target получает repository/artifact
DEPLOY_* workflow secret = GitHub Actions получает доступ к target/provider
```

---

## 12. Forbidden by default

Без отдельной explicit task и safety plan запрещены:

- deploy из обычного CI job;
- production credentials в untrusted workflow;
- direct/force push в protected production branch;
- workflow self-modification или auto-fix commits;
- broad variable-path delete/reset/clean;
- Compose down, volume prune/removal, system-wide prune;
- recursive broad ownership/permission changes;
- printing `.env` или resolved secret-bearing config;
- uncontrolled migration, backup/restore или reindex;
- hidden bootstrap, hardening, cleanup или access-model change;
- destructive rollback без verified recovery path.

Команда оценивается по effect и scope, а не только по имени. Narrow reviewed operation может быть допустима в отдельной maintenance task; broad mutable path остаётся blocker.

---

## 13. Post-deploy metadata mechanism

Автоматическая synchronization status/Evidence после LIVE допустима только если mechanism:

- запускается от trusted deployment result exact revision;
- пишет только allowlisted metadata paths/fields;
- использует minimal write permission;
- не изменяет durable requirements/acceptance criteria;
- защищён от recursive runs;
- создаёт auditable commit/status record;
- не используется для произвольных code changes.

При отсутствии mechanism deployment может быть `LIVE_VERIFIED`, но Current Goal не может быть `DONE`, если metadata synchronization обязательна по её DoD или project contract. Если synchronization явно неприменима, используй `N/A`. Direct push в обход protection rules запрещён.

---

## 14. Evidence contract

### CI Evidence

- workflow/check name и run ID/URL;
- event и exact head SHA;
- required jobs и terminal statuses.

### Build Evidence

- source SHA и build run;
- artifact name/version;
- digest/checksum и provenance reference, если применимо.

### Deployment/LIVE Evidence

- deployment/run ID и target environment;
- deployed commit/artifact identity;
- migration class/result;
- terminal status и post-check result;
- endpoint/service, timestamp и limitations проверки.

Raw logs без exact identity не заменяют Evidence.

---

## 15. Exceptions

Исключение допустимо только по explicit owner/user decision и содержит:

```text
Rule being overridden
Reason
Scope and duration
Risk
Compensating controls
Authorization source
Validation and rollback/stop criteria
```

Исключение narrow и временное, если иное не утверждено явно; оно не становится universal precedent автоматически.

---

## 16. Project CI/CD profile

Профиль ниже заполнен подтверждёнными repository facts и explicit user decisions от `2026-09-02`–`2026-09-04`: LLM Inspector — Windows desktop application без runtime deployment target, поэтому server/runtime CD отключён; `GOAL-002` утвердила design stack, `GOAL-003` реализовала reproducible repository/CI foundation, а текущая `GOAL-005` реализовала portable GitHub Releases flow. После immutable observation-only `v1.0.0-rc.2` и последующего merge lifecycle-кода в `main` пользователь явно утвердил отдельную maintenance line `release/v1.0`: только она может быть source для новых `v1.0.x` tags, тогда как другие release lines по умолчанию остаются привязаны к `main`. Фактический PR/run/artifact Evidence фиксируется в `docs/delivery-plan.md`.

```yaml
profile_version: 1
status: CONFIGURED # current repository/CI profile; disabled release/CD fields are N/A

architecture_design:
  runtime: .NET 10 LTS
  desktop_ui: Avalonia UI
  package_manager: NuGet PackageReference with Central Package Management and lock files
  solution_format: slnx
  target_rid: win-x64
  runtime_publish: self-contained
  state_store: SQLite WAL, local per-user, single application writer

repository:
  expected_repository: https://github.com/Just9120/llm-inspector
  production_branch: main
  release_branches: [release/v1.0]
  release_tag_policy: exact vMAJOR.MINOR.PATCH[-prerelease]; v1.0.x reachable from release/v1.0, other lines reachable from main

ci:
  workflow: .github/workflows/ci.yml
  events: [pull_request, push:main, push:release/v1.0]
  runner: GitHub-hosted windows-2025 x64 ephemeral standard runner
  install_command: dotnet restore LlmInspector.slnx --locked-mode
  lint_command: dotnet format LlmInspector.slnx --verify-no-changes --no-restore
  typecheck_command: dotnet build LlmInspector.slnx -c Release --no-restore
  test_command: dotnet test LlmInspector.slnx -c Release --no-build --logger "console;verbosity=minimal"
  build_command: locked win-x64 restore + self-contained publish + Avalonia initialization smoke
  workflow_check: CI / windows-dotnet
  required_checks: NONE_ENFORCED # no branch protection/rulesets at 2026-09-02 checkpoint
  lockfile: 15 normal packages.lock.json + 9 RID-specific packages.win-x64.lock.json files
  package_source: nuget.org only via NuGet.Config source mapping
  untrusted_pr_policy: contents:read; no secrets, environments, write token, privileged runner or deploy
  action_pinning: full immutable commit SHA; tag only in comments; enforced by unit policy test
  concurrency: per workflow and PR/ref; stale runs cancel safely
  timeout_minutes: 20
  usage: standard runner in public repository; no billable Actions minutes

artifacts:
  enabled: false
  type: N/A # publish output stays only in ephemeral CI workspace
  identity: N/A
  registry_or_storage: N/A
  provenance_required: N/A # required if artifact publishing is enabled later

windows_release:
  enabled: true
  workflow: .github/workflows/release.yml
  approved_target: portable unsigned self-contained single-file win-x64 executable
  validated_build_unit: exact hashed self-contained win-x64 publish output
  version_boundary: observation-only v1.0; lifecycle starts v1.1
  first_candidate: v1.0.0-rc.1 failed publication; v1.0.0-rc.2 published but failed Pro manual gate; forward-fix candidate is v1.0.0-rc.3
  trusted_trigger: exact SemVer tag reachable from mapped trusted line (v1.0.x=release/v1.0; otherwise main)
  signing_identity: N/A # unsigned portable channel; trusted signing belongs to separate Store/MSIX backlog
  distribution_channel: GitHub Releases
  update_channel: manual download # automatic Store updates are deferred
  package_validation: SHA-256 + SBOM + provenance + SmartScreen disclosure + Windows 11 25H2 Home/Pro exact-artifact run
  applicable_evidence: BUILD_PACKAGE_INSTALL # DEPLOY/LIVE remain N/A

deployment:
  cd_enabled: false
  trusted_trigger: N/A
  target_environment: N/A
  target_host_or_account: N/A
  target_directory_or_namespace: N/A
  expected_remote_or_registry: N/A
  expected_branch_tag_or_release: N/A
  environment_protection: N/A
  host_identity_verification: N/A
  deploy_model: N/A
  deploy_command_or_workflow: N/A
  deployment_unit: N/A
  concurrency_group: N/A
  cancel_in_progress_policy: N/A
  health_check: N/A
  live_check: N/A

credentials:
  model: N/A # CD credentials; application/runtime secrets are a separate future design concern
  runtime_config_owner: N/A # no runtime config implemented in foundation
  required_secret_names: N/A # CD secret names only

stateful:
  services: local per-user SQLite WAL in %LOCALAPPDATA%/LLM Inspector/data/inspector.db
  migration_class: forward schema migrations through v5; single application writer
  backup_recovery_contract: startup quick_check plus normal/process-kill committed-history recovery tests; user backup workflow remains deferred

recovery:
  rollback_or_forward_fix: N/A # server deployment recovery
  failed_post_check_action: N/A

metadata_sync:
  enabled: false
  mechanism: N/A
  allowlisted_paths_or_fields: N/A
  loop_protection: N/A
```

`status: CONFIGURED` допустим только после того, как все применимые поля перестали быть `UNSET` и были сверены с repository/settings или safe diagnostics. Для `cd_enabled: false` CD-only поля должны быть `N/A`, а не фиктивно заполнены.

Profile может быть вынесен в отдельный canonical file, но не дублируется в competing sources.

---

## 17. Completion gates для Current Goal

### CI

- trust boundary, permissions и intended events явны;
- exact checks воспроизводимы настолько, насколько позволяет проект;
- production secrets/deploy отсутствуют;
- required failures не маскируются.

### CD

- trusted trigger, target и exact revision/artifact подтверждены;
- credentials/runtime config обрабатываются безопасно;
- stateful/migration, concurrency и failure policy соблюдены;
- post-check и Evidence присутствуют;
- success объявлен только после required validation.

### Maintenance/migration

- есть отдельный scope/owner, preconditions, backup/recovery и stop criteria;
- destructive surface минимальна;
- result и residual risk подтверждены Evidence.

Current Goal может быть `DONE` только после выполнения всех применимых gates этого contract; неприменимые gates должны быть явно отмечены `N/A`, а не пропущены молча.
