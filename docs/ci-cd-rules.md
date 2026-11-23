# Настройка и исправление CI/CD

Читай этот документ при первоначальной настройке CI/CD, изменении pipeline и исправлении его проблем. Он задаёт требования к workflows, triggers, checks, runners, permissions, artifacts, CD и связанным настройкам. Повседневный flow агента и Goal определяет корневой [AGENTS.md](../AGENTS.md); команды и процедуры проекта находятся по его routing.

Адаптируй правила к фактическому стеку и согласованному scope. Перечень возможностей не требует внедрять все инструменты. Workflows/scripts/runbooks можно исправлять внутри разрешённой Goal; изменение общей политики, модели доступа и safety exceptions требует соответствующего решения пользователя. Обновлённые инструкции не изменяют CI/CD автоматически: в порученной адаптации сначала сверь actual workflows/settings/scripts, используй уже работающие selection/reuse/full profiles и сохрани согласованные проектные исключения.

## 1. Границы CI, CD и окружений

- Базовый flow — локальный preflight → CI и применимое review актуальной revision PR/MR → merge → применимый CD → проверка работающей версии. До merge соблюдай обязательные checks, review и repository protections. После каждого merge, требующего deployment, поставь точную merge revision/artifact на целевую платформу проекта (VPS, Cloudflare, Google Cloud или другую) и подтверди работающую версию обязательными post-checks.
- Разделяй проверку кода и изменение окружения по jobs, triggers, credentials и permissions. Отдельные workflow-файлы необязательны. Проверка PR не должна незаметно запускать production deploy.
- Различай runtime environment, GitHub Environment, вариант продукта и операцию delivery. Для каждого GitHub Environment установи назначение, jobs, targets, configuration и access/protection rules; укажи связь с runtime environment, если она есть. Не вводи обязательный набор dev/staging/production. Вариант продукта требует отдельного окружения только при самостоятельной цели deployment.
- Для каждого изменения определи применимость deployment. Изменение документации без влияния на runtime не требует искусственного CD. Неизвестный target или отсутствующий доступ не означают N/A.
- Для переносимых jobs используй Linux по умолчанию. Если сборка или проверка требует конкретной ОС, запускай её на целевой ОС. Выделяй платформенно-зависимую проверку в отдельный job, когда это сохраняет охват без несоразмерного повторного setup. Основание выбора другой ОС фиксируй в проектной процедуре.

## 2. Validation и merge gates

Минимальный CI входит в первую implementation Goal нового проекта: настрой его в первом PR с кодом, до merge обеспечь успешные применимые build/static checks и необходимые tests реализованного поведения. Развивай проверки вместе с функциональностью. Отсутствие pipeline не разрешает merge первого PR с кодом без CI. Аудит/bootstrap сами по себе не поручают его настройку; CD на целевую платформу проекта внедряй в согласованном delivery scope.

Используй Validation Plan проекта и canonical scripts без второй копии команд/AC. Перед push воспроизведи весь локально выполнимый профиль штатного PR CI на актуальном кандидате. Используй тот же selector или правила выбора CI для точных base/head и проверяемой ревизии. В Validation Plan укажи состав, общие с CI команды и нужное окружение. Полный регресс запускай отдельно. При несовместимой ОС выполни переносимую часть и используй доступное совместимое окружение для остальных обязательных локальных проверок. Недоступная или упавшая REQUIRED проверка до push блокирует push. Проверки, заранее отнесённые в Validation Plan к CI-only из-за требований платформы или среды CI, выполняются в обязательном CI до merge. Сначала используй существующие tests; новые нужны для существенного непокрытого поведения, важных рисков и содержательных регрессий. Один сценарий может подтверждать несколько AC; test на каждый AC/метод и обязательный TDD не требуются. Выбирай unit/integration/E2E по затронутым требованиям и риску. Coverage выявляет gaps, но не доказывает AC; CI обнаруживает только проверяемые нарушения.

Выбирай проверки по назначению, риску и стоимости, а не только по метке `non-PG`, `postgres` или `E2E`. Простое изменение текста требует применимой проверки документации, а не искусственной E2E suite.

Пример лёгкого профиля — изменение только `AGENTS.md` и `ci-cd-rules.md`, когда полный diff подтверждает отсутствие влияния на runtime, сборку и входы выпуска. До дорогого setup выбери проверки документов, ссылок и затронутых контрактов с сохранением применимого required gate. Если документы влияют на launcher, генерацию, выпуск или поставку самих документов, проверь затронутых потребителей и выполни нужные шаги. При неполном diff или неизвестном влиянии используй более широкий применимый набор по правилам affected selection.

Разделяй полноту поддерживаемого test inventory и частоту запусков. Профили используют одни suites/команды, без второй реализации:

| Профиль | Состав и запуск |
| --- | --- |
| Обычный CI PR | Применимые static/build, unit/component/API/DB/integration и критичное общее ядро по зависимостям и риску; действующие проектные исключения сохраняются. |
| Полная регрессия main | По отдельному поручению на exact main SHA запускай весь поддерживаемый применимый набор, включая browser/native/recovery по проекту. По умолчанию локально. На CI-провайдере запускай по выбору владельца или по отдельному согласованию, если нужная проверка недоступна локально. Не запускает CD, полный аудит или пересчёт готовности. Не запускается автоматически после Goal или аудита. |
| Специализированные | Load/stress/soak, large-data/resource qualification, fault/backup/restore и другие проверки по требованиям и выбранному scope в подходящей изолированной среде. |

Property-based/stateful tests рассматривай для инвариантов, комбинаций входов и последовательностей действий; mutation testing — для силы существующих assertions; fuzzing — для парсеров, импортов, протоколов и другого недоверенного ввода. Выбирай по риску и стоимости, без обязательного инструмента или целевого mutation score. Небольшие полезные проверки могут входить в обычный CI; дорогие кампании относятся к специализированному scope и автоматически не запускаются. Для кампаний определи scope, лимиты времени/ресурсов и условия остановки; сохраняй seed/минимальный контрпример либо другой способ воспроизведения failure вместе с revision. Mutation score не подтверждает READY. Новый тестовый профиль и обязательный workflow этим не вводятся.

Команды, inventory, prerequisites, target revision и условия запуска держи в canonical scripts/README/процедурах проекта. Предоставь preflight PR и полную регрессию как отдельные команды или режимы общего скрипта без deployment. Сохраняй результаты по фактически выполненным suites, включая skips/ограничения. Отсутствующий профиль или сценарий — gap; присутствие config не доказывает запуск. Actions-версия полного профиля тоже расходует ресурсы. Обязательные автоматические расписания не вводи.

Дополнительные некритичные tests можно отложить. Проверки текущих AC, безопасного merge и существенных рисков (права, деньги, миграции, целостность данных) не откладывай; сохраняй обязательные tests/assertions/gates и согласованные исключения по разделу 9. Полная suite не компенсирует отсутствующие сценарии.

Playwright внедряй лишь для небольшой обоснованной browser гарантии сверх unit/component/API; наличие frontend/PWA само по себе не требует framework или широкой device/browser matrix. Обычный запуск — headless, без UI/debug/watch, открытия окон/HTML-отчёта; диагностика по failure. Визуальный аудит и воспроизводимые assertions не заменяют друг друга; существующие обязательные browser gates сохраняются.

Для существенных рисков проверяй настоящий entrypoint и execution model: user/group/permissions, isolated child process, версии зависимостей, concurrency, размер данных и CPU/RAM/IO по применимости. Wrapper/mock или маленький fixture не доказывают результат production профиля. Дорогую qualification запускай отдельно по scope; не используй production как тестовый стенд.

Для affected selection начни с понятных групп по компонентам и зависимостям; усложняй selector только при измеримой пользе. Учитывай общие config/lockfiles, migrations, test infrastructure и динамические связи. Неизвестный impact, ошибка selector или неполный diff требуют более широкого подходящего набора; пустая выборка должна иметь объяснимое основание.

Настрой точную проверяемую revision, изолированный workspace и воспроизводимую установку dependencies с lockfiles, когда применимо. Required command должна завершаться ошибкой при failure; test discovery должен находить ожидаемые tests. Silent fallback, безусловный success и `continue-on-error` не должны скрывать обязательную ошибку.

Required checks и их источник устанавливай по фактическим rulesets/branch protection. Уточни revision model: PR head, test merge commit или merge group. После изменения revision требуется соответствующая validation; reuse после merge допустим только по условиям раздела 3. При merge queue настрой `merge_group` trigger. Self-review не заменяет обязательное approval, а зелёный YAML job не доказывает выполнение repository gate.

Required workflow не должен зависать в ожидании из-за path/branch filters. Для выборочных jobs настрой итоговый required gate, который запускается после dependencies и проверяет результат каждого применимого required job, в том числе при failures/cancellations зависимостей.

GitHub допускает успешное завершение отдельных checks со статусом `skipped` или `neutral`. По этому контракту skip допустим при заранее определённой неприменимости либо подтверждённом reuse по разделу 3. Reuse оформляй отдельным основанием с исходным run, не как неприменимость самих tests. Ошибочный filter, dependency failure или недоступность сервиса не являются N/A. Разбирай `failure`, `cancelled`, `timed_out`, отсутствие результата и неожиданный `action_required`, сохраняя обязательные gates.

## 3. Время CI и GitHub Actions minutes

Избегай дублирующих запусков одной suite по `push` и `pull_request` без отдельной цели. Используй безопасные caches и обоснованную параллельность; не дроби быстрые команды на отдельные jobs без пользы. Отменяй устаревшие CI runs в пределах того же PR/workflow, сохраняя stateful операции и независимые проверки. Timeouts задавай по реальным операциям и поведению зависаний.

Определи selection/reuse до дорогого setup. Поднимай PostgreSQL, браузеры и другие сервисы и устанавливай специальные dependencies только в jobs, которым они нужны для оставшихся checks/build. Пропуск test step после безусловного setup не устраняет его стоимость.

После merge допускается reuse успешной PR validation вместо повторной suite, если автоматически подтверждены merged PR, связь head/base/tested revision с текущим main, совпадение проверенного Git tree и влияющих inputs: dependencies, test/workflow/config и environment. Учитывай фактический способ интеграции PR: merge commit, squash/rebase или merge queue; не навязывай два parent commit там, где их нет. Одинаковый tree сам по себе недостаточен: temporal/runtime и изменившиеся inputs требуют своих проверок. Проверь trusted repository/workflow и результаты всех required jobs последнего релевантного run/attempt для этой PR revision. Старый PASS не перекрывает более новый неуспешный, отменённый или незавершённый релевантный запуск.

Неизвестные или изменившиеся inputs, недоступные records либо неподтверждённые условия reuse требуют выполнения необходимых checks; не подменяй их success. Для текущей revision сохраняй проверяемое решение reuse и ID исходного run. Reuse tests не отменяет проверку нового artifact и применимый CD; cache не заменяет эту процедуру.

Измеряй длительность CI и суммарный runner time отдельно от billing. Сравнивай сопоставимые изменения до/после оптимизации: выбранные tests, причины fallback, setup/build и повторы. Параллельность сама по себе не означает экономии minutes. Проектные бюджеты и цели устанавливай по измерениям; универсальный лимит времени или coverage target не вводи. Стоимость не разрешает ослаблять обязательные проверки.

При оптимизации используй доступную историю PR/runs/jobs за заданный период, сверяя pagination, attempts и отменённые jobs. Раздели PR validation, main validation, build, CD и manual; manual workflow-ref SHA сам по себе не доказывает поставленного candidate. Измеряй setup, tests, build, ожидание и CD раздельно, насколько позволяют records. Runner sum по jobs, elapsed до результата и billable usage — разные величины; billing требует runner type, visibility и доступных billing records. Ранжируй предложения по подтверждённому baseline, цене/риску и сохраняемым гарантиям; численные затраты/экономию давай только с расчётом, недостаток данных обозначай. Новые speculative runs ради измерения не нужны.

Цепочки hotfix связывай с основной работой через PR descriptions, project records, commits и diff, не по соседству номеров/слову fix. Различай регрессию, окружение, ошибку проверки, новый scope и плановое продолжение. При разборе выбранной Goal учитывай число CI/CD runs и суммарный runner time основных PR и подтверждённо связанных исправлений до завершения applicable delivery либо указанного момента проверки. Одни и те же jobs/attempts не считай повторно; период, неполную историю и неизвестные затраты обозначай. Подбери минимальный test реальной причины и проверь совокупный результат Goal; известные связанные CI failures исправляй batch в текущем PR по AGENTS.md, срочный safety hotfix не задерживай ради пакета. Исправленные исторические дефекты не выдавай за текущие.

Flaky tests отличай от воспроизводимой регрессии и сбоев окружения по доступным logs/attempts. Исправляй подтверждённую причину; повторный зелёный run сам по себе не доказывает исправления. Не заменяй исправление ослаблением assertions, отключением tests или бесконтрольным увеличением retries; обязательные gates сохраняются.

Длительный monitoring/observation и speculative reruns на GitHub-hosted Actions требуют отдельного owner approval и предварительной проверки остатка included minutes. Неизвестный остаток сообщи. Ограниченные обязательные post-checks и обоснованный retry подтверждённой transient ошибки отличаются от наблюдения без конкретной проверяемой причины.

Обычный CI проверяет repository. Auto-fix commits/push и self-modifying workflows допускаются только как отдельно согласованная узкая automation с trusted trigger, минимальными permissions, allowlist изменений и защитой от циклов.

## 4. Trust boundaries и supply chain

Задавай `permissions` явно: read-only/none по умолчанию, повышение только для нужного job/target. Используй short-lived credentials/OIDC, когда доступны, с ограниченной identity. Reusable workflows передавай только необходимые inputs/secrets.

Untrusted PR/fork code не должен получать production secrets, write-capable token, privileged persistent runner или доступ к внутренним ресурсам. Для PR используй подходящую изоляцию; deploy runners отделяй от общего PR execution. На self-hosted runners учитывай очистку и возможность сохранения чужого состояния.

`pull_request_target` и privileged `workflow_run` не должны исполнять untrusted PR code или без проверки принимать его artifacts. PR metadata обрабатывай как данные. Не вставляй untrusted expressions в shell source; безопасно передавай аргументы/environment variables и проверяй формат.

Внешние Actions/reusable workflows фиксируй полным commit SHA из проверенного repository. Проверяй источник и требуемые полномочия зависимости, lock integrity, registries и install scripts. Vulnerability/license scans, SBOM, signing и attestations применяй по требованиям и риску, без автоматического внедрения всей инфраструктуры.

Cache — оптимизация, а не доверенный artifact. Учитывай OS/runtime/lockfile и trust context в ключах; privileged job не должен потреблять cache, который может отравить untrusted job. Не помещай secrets в repository, artifacts/cache/logs. При изменении runner, trigger или credential model перепроверь trust boundaries.

## 5. Build и identity поставки

Свяжи image/package/archive с source revision, build run и immutable digest/version. `latest` или branch tag без неизменяемой identity недостаточны. Для передачи между workflows проверяй repository, источник и provenance artifact по модели проекта.

Production artifact должен пройти required validation в допустимом trust context. Успешный untrusted PR run сам по себе не делает artifact доверенным. Для release build после merge свяжи фактический merge SHA, применимую validation и созданный artifact. CD не должен зависеть от существования удаляемой feature/fix branch.

По возможности строй один раз и продвигай тот же artifact. Если модель проекта требует пересборки, зафиксируй её, обеспечь точную source revision, воспроизводимость inputs и проверку нового artifact; не выдавай его за ранее проверенный бинарный результат. Publishing — явный разрешённый этап, а не побочный эффект tests.

### Версии и release notes

Сохраняй принятую схему версий; при её отсутствии в порученном внедрении выбери простую схему по контексту проекта, без единого обязательного формата. Свяжи версию с точным SHA/artifact; опубликованный тег не переноси на другой код. Принятую схему и порядок выпуска отражай в существующей проектной процедуре.

При ведении releases обычно оформляй release после завершения applicable delivery Goal. Два основных PR не требуют двух releases; каждая промежуточная поставка сохраняет SHA/artifact независимо от наличия release notes. Отдельно поставляемое исправление опубликованной версии получает новую версию.

Храни короткие release notes в GitHub Releases: существенные изменения, исправления, несовместимость и действия при обновлении — по применимости. Основу получай из PR/commits между выбранными revisions, проверяя содержание по diff. Отдельный CHANGELOG.md не обязателен. Release не подтверждает READY, успешный deployment или безопасность rollback.

Не создавай отдельный PR и не повторяй CI/build/CD ради release notes. Не вводи обязательный workflow выпуска; учитывай существующие tag/release triggers, чтобы публикация не запускала лишние checks или повторную поставку. Если номер нужен в manifest приложения, обновляй его в соответствующем implementation PR.

Ретроспективно оформляй только значимые версии по отдельному поручению, без переписывания истории и переноса опубликованных тегов. Помечай описание как восстановленное, различай историческую revision и время оформления; неизвестную прошлую поставку не выдавай за подтверждённую.

## 6. CD и recovery

Проверь trusted trigger, expected repository/ref, exact revision/artifact, target host/account, directory, service/deployment unit и config/credential owners. Настрой обязательные preconditions, environment protections, allowed branches/tags, approvals и stop criteria. Не выбирай неизвестный target по догадке и не обходи protections.

Сериализуй поставки в один target. Перед изменением окружения повторно проверяй допустимость candidate по release policy: отложенный job не должен затереть более новую поставленную версию. Одна очередь не гарантирует правильный порядок версий. Намеренный rollback выполняй по recovery procedure. Не отменяй migration/deploy, если это может оставить неконсистентный state; retry требует известной idempotency или безопасной точки продолжения.

До переключения версии проверь config и stateful preconditions, изменяй только intended deployment unit. Успех CD должен зависеть от проверки фактически запущенной версии, health/readiness и необходимых прикладных smoke checks. Статус процесса или доступный endpoint не доказывают успешный бизнес-сценарий. Failed post-check останавливает дальнейшее продвижение; сохраняй Evidence и применяй согласованную recovery strategy.

Для критичного риска с предусмотренным эксплуатационным сигналом укажи в существующей процедуре поставки связь проверки и сигнала с revision/artifact, владельца реакции и действие по runbook. При поэтапной поставке используй согласованные критерии продолжения, паузы и восстановления. При canary сравнивай candidate и контроль по сопоставимым условиям. Учитывай согласованное окно наблюдения, объём и состав трафика. При недостатке данных укажи ограничение Evidence и следуй проектной процедуре паузы или эскалации. Используй имеющиеся источники сигналов и предусмотренные прогоны.

Для managed/serverless-платформ установи account/project, service/worker и регион по применимости, deployment version/artifact, bindings/config/secrets и способ проверки активной версии/маршрутизации. Адаптируй поставку и recovery к фактической платформе, сохраняя общие trust, stateful и post-check gates.

### VPS, Docker Compose и IaC по применимости

Проверь SSH host identity, deploy directory, remote/ref/exact commit, worktree, Compose project, allowlisted services и persistent volumes. Не отключай host verification ради исправления доступа. Git-based deploy использует безопасный fast-forward либо exact revision в чистой release directory, сохраняя неизвестные изменения на host.

`reset --hard`, broad `clean`, `docker compose down`, volume removal и system-wide prune не являются стандартной стратегией CD. Bootstrap host, users/SSH/firewall, массовые permission changes и перенос данных требуют согласованного setup/maintenance scope. Разделяй credential чтения repository/artifact с VPS и credential подключения CI к VPS.

Для IaC apply подтверди target account/workspace, plan diff, revision, полномочия и изменения persistent resources. Нужны state locking и recovery procedure; защити state/plan artifacts, которые могут содержать secrets. Пересоздание или удаление ресурсов не маскируй под routine deploy.

## 7. Runtime configuration и stateful changes

Установи canonical config owner: platform settings, secret manager, environment secrets или host files. Schema/examples содержат безопасные значения. CD сохраняет existing runtime values и не заменяет production `.env` шаблоном. Missing required value блокирует соответствующий deploy.

Проверяй наличие и формат config без печати secret values, resolved secret-bearing config и authorization headers. Rotation и recovery выполняются в разрешённом scope, без скрытой смены секретов при обычной поставке.

Для DB, очередей, object/file storage, volumes и других persistent systems определи класс изменения:

- `NONE` — persistent schema/data не меняются.
- `BACKWARD_COMPATIBLE_AUTOMATED` — versioned migration совместима в rollout window; известны retry, locking, duration/failure behavior, необходимые backup/recovery preconditions и post-check.
- `EXPLICITLY_GATED` — destructive, несовместимое, необратимое или privileged изменение; нужны явные scope/authorization, target, preconditions, recovery/forward-fix и stop criteria.

Обязательный operation gate оставляет соответствующий delivery stage незавершённым до выполнения. Его не подменяет отложенное Ad-hoc тестирование. Если migration требует backup, проверь пригодность восстановления по принятой процедуре: наличия файла недостаточно. Restore, удаление volumes, перенос данных и broad cleanup не становятся разрешёнными автоматически.

Automatic rollback допустим только при проверенной совместимости artifact, config и уже изменённой schema/state. Откат приложения не равен откату данных. Если безопасный rollback не определён, используй согласованный forward-fix или внешний gate; destructive recovery не импровизируй.

## 8. Records и проверка конфигурации

Сохраняй первичные records независимо от сессии агента: CI run/check ID и проверяемую revision с результатами required jobs; build run и artifact identity; CD run/deployment ID, target environment, фактически запущенную revision/artifact, время, migration result и обязательные post-checks. Настрой retention/access по потребностям восстановления контекста и recovery, без secrets в artifacts. Для оптимизации отражай selection/reuse/fallback reason и существенные phase timings в существующем job summary/records, без нового monitoring workflow; сравнивай сопоставимые scopes/inputs до и после изменения.

Успешный CD подтверждает выполненные проверки на момент поставки. Наличие конфигурации не доказывает запуск; raw log без target/revision не заменяет Evidence. Недоступный результат остаётся неподтверждённым. Pre-merge CI не подтверждает будущую поставку.

Отдельные статусы DEPLOY/LIVE, обязательная post-merge запись delivery metadata в main и metadata-only follow-up PR не требуются. Источник результата поставки — первичные records. Не вводи automation для переписывания metadata без отдельного поручения.

После настройки проверь синтаксис и затронутые trigger/revision/selection/reuse/trust/failure paths, required checks и protections доступным безопасным способом. Для selector/reuse проверь неизвестный impact, неполные records, изменённые inputs и более новый неуспешный run; они не должны давать ложный green. Сохрани результат и ограничения. Непроверенная гарантия не становится PASS из-за наличия YAML; production/destructive сценарий не запускай только ради проверки конфигурации.

## 9. Проектная адаптация

Применяй общий flow к новым и существующим репозиториям. Адаптируй команды, окружение, выбор проверок и условия поставки к проекту в рамках этих правил. Фактические workflows/settings/scripts и runbooks — canonical источники команд и процедур. Реальные пути и навигацию держи в README. Репозиторный AGENTS.md остаётся универсальным контрактом, без проектных сведений. Согласованные исключения фиксируй в соответствующей проектной процедуре с основанием и scope.

В существующем README или runbook укажи команду локального preflight, обязательные проверки перед merge, основание отдельной проверки `main` или reuse и условия выпуска и выкладки. Ссылайся на canonical команды и процедуры, чтобы обычный delivery/recovery можно было восстановить по проекту.

При внедрении сначала сверь действующие workflows, защиты и доступные CI/CD records. Если схема уже соблюдается, обнови копию правил в ближайшем согласованном PR. Если синхронизация сама запускает лишние тяжёлые проверки, сборку или CD, включи её в тот же PR, что и необходимую настройку workflow. Если кандидат уже проверен и готов к merge, перенеси обновление только копии правил на следующую согласованную поставку.

До push используй существующие records и локально сверь выбор профиля для правки только инструкций, затронутого кода и неполного diff. Сохраняй действующие merge gates и требуемое проектом подтверждение exact main SHA. После подготовленного push пройди штатный CI актуальной revision. Для приёмки укажи выбранный профиль, причину выбора, обязательный результат и фактический исход после merge. Отдельные прогоны Actions только ради сверки правил не запускай.

Указывай источники и время/revision проверки; неизвестное — UNSET, неприменимое — N/A с основанием. При изменении stack, settings или окружений обновляй затронутые процедуры. Сохраняй доступную версию шаблона и основания проектных отличий, не выдумывая provenance.

Исключение из safety contract требует решения пользователя/уполномоченного владельца: правило, причина, scope/срок, риск, compensating checks и stop/recovery criteria. Уже разрешённое исключение действует в своих границах и не меняет универсальный шаблон. Стоимость, flaky test или ожидание доступа не разрешают обходить обязательный gate.

## 10. Project CI/CD profile

Профиль ниже заполнен подтверждёнными repository facts и explicit user decisions от `2026-09-02`–`2026-09-04`: LLM Inspector — Windows desktop application без runtime deployment target, поэтому server/runtime CD отключён; `GOAL-002` утвердила design stack, `GOAL-003` реализовала reproducible repository/CI foundation, а `GOAL-005` реализовала portable GitHub Releases flow. После исторических immutable `v1.0.0-rc.*` пользователь отменил отдельную maintenance line: development и release source имеют одну trusted line `main`, prerelease больше не публикуются, а первый следующий public version должен быть финальным `v1.0.0` только после обязательной validation. Фактический PR/run/artifact Evidence фиксируется в `docs/delivery-plan.md`.

```yaml
profile_version: 1
status: CONFIGURED # current repository/CI profile; disabled release/CD fields are N/A

architecture_design:
  runtime: Go 1.27.1
  desktop_ui: Wails v2.15.0 + Svelte 5.57.0 / TypeScript 6.0.3; installed WebView2 prerequisite, no automatic download
  package_manager: Go modules with go.sum + npm 12.0.2 ci with package-lock.json; Node 22.23.1
  solution_format: Go module + embedded frontend
  target_rid: win-x64
  runtime_publish: single-file embedded Go runtime/frontend; external installed WebView2 Runtime
  state_store: SQLite WAL, local per-user, single application writer

repository:
  expected_repository: https://github.com/Just9120/llm-inspector
  production_branch: main
  release_branches: []
  release_tag_policy: exact final vMAJOR.MINOR.PATCH reachable from main; prerelease publication disabled

ci:
  workflow: .github/workflows/ci.yml
  events: [pull_request, push:main]
  runner: GitHub-hosted ubuntu-24.04 for selection/docs/policy/gate; windows-2025 x64 for native build/test/smoke
  install_command: go mod download + npm --prefix frontend ci --ignore-scripts --no-audit --no-fund
  lint_command: ./scripts/validate-go.ps1 + npm --prefix frontend run check
  typecheck_command: go vet ./... + npm --prefix frontend run check (after initial binding generation)
  test_command: go test ./... -count=1 -timeout 60s + npm --prefix frontend test
  build_command: ./scripts/build-windows.ps1 + ./eng/release/Test-ReleaseTools.ps1
  workflow_check: CI / windows-go # always-running aggregate; requires selected profile success
  required_checks: NONE_ENFORCED # no branch protection/rulesets at 2026-09-02 checkpoint
  lockfile: go.mod/go.sum + frontend/package-lock.json; exact toolchain files .go-version/.node-version/.npm-version
  package_source: Go module proxy/checksum database + registry.npmjs.org locked package integrity
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
  version_boundary: one main line; product remains v1.0 before publication; first stable tag is v1.0.0, then development advances to v1.1
  historical_candidates: v1.0.0-rc.1 failed publication; v1.0.0-rc.2 was rejected; v1.0.0-rc.3 remains immutable historical prerelease evidence
  trusted_trigger: exact final SemVer tag reachable from main after required validation; prerelease tags fail closed
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

### Применимость и сохранённые гарантии LLM Inspector

Сверка шаблонов: 2026-11-23; Drive file IDs `1mq36WrP2tWNjDt7pint76S_mF--JAUUf` (редакция 19:21 МСК, raw 30 175 bytes, SHA-256 `956f4f8d1e097c6dccf2295376eeb52fcd7268e64888e8033aac15d8dfd80e5f`) и `17x73acxyIs4TTdLe1BiZWj6wVItmEw3B` (modified 20:15:55Z, raw 43 046 bytes). CRLF/LF нормализованы. База проекта: `77a30342b182fa63686d09d327ed708c232b49ce`. Владелец разрешил в PR #45 синхронизацию инструкций и внедрение выбора профиля CI; это не изменение продукта, release gates или CD.

- Canonical продуктовый контракт — [project-spec.md](project-spec.md); execution state — [delivery-plan.md](delivery-plan.md), первичные PR/CI records и решения владельца. Upstream/архив не расширяют scope и не авторизуют реализацию. Задача синхронизации правил не меняет продуктовые AC, readiness или обязательное Evidence.
- Обязательные exact-artifact Windows Home/Pro release проверки, E12 measurements и B02 encrypted two-host LIVE из product contract не являются необязательным Ad-hoc. Их отсутствие нельзя превратить в PASS, READY или N/A обновлением общих правил. Release gates и ограничения [Windows release](runbooks/windows-release.md), [Windows validation](runbooks/windows-validation.md), [secure remote access](runbooks/secure-remote-access.md) сохраняются.
- Переносимые selection/docs/policy/aggregate jobs используют Linux. Native Windows/Wails/WebView2 smoke, process/DPAPI/tray и сборка `windows/amd64` остаются на Windows в job `go-validation`. Его команды, toolchain pins и проверки не сокращены.
- Фактические workflows: [CI](../.github/workflows/ci.yml), [release](../.github/workflows/release.yml). На 2026-11-23 main protected=false, rulesets=[]; `windows-go` всё равно REQUIRED перед merge. Теперь это always-running aggregate: требует успешного preflight и успешного Windows job для профиля `windows`; skip Windows допустим только при успешном `instructions` или `pipeline`. Failure/cancelled/skipped preflight и неизвестный профиль не дают green.
- Canonical команды и prerequisites — [README](../README.md), [build script](../scripts/build-windows.ps1), [release checks](../eng/release/Test-ReleaseTools.ps1). Сохраняются exact pins, `GOTOOLCHAIN=local`, `GOFLAGS=-mod=readonly`, locked npm install, локальный pinned Wails CLI, `CGO_ENABLED=0` и installed WebView2 без автоматической загрузки.
- Перед push REQUIRED весь локально воспроизводимый выбранный профиль на точных base/head и чистом кандидате. Общий selector/проверки находятся в отмеченном блоке `LOCAL PREFLIGHT` workflow; локальная команда ниже исполняет тот же код. Полный проверенный diff только `AGENTS.md`/`docs/ci-cd-rules.md` выбирает `instructions`: UTF-8, размер AGENTS ≤30 KiB, fences, относительные ссылки, diff whitespace и регрессии selector/gate. Эти файлы не потребляются launcher/build/release, что проверено поиском callers. Изменения самого `.github/workflows/ci.yml` вместе с этими инструкциями выбирают `pipeline`: те же проверки плюс существующие `go test ./internal/domain -count=1 -timeout 60s` (CI/release/build trust contracts) и self-review workflow. Другие paths, включая scripts, pins, frontend, release workflow, README и код, выбирают `windows`: исходные `./scripts/build-windows.ps1` → `./eng/release/Test-ReleaseTools.ps1` на Windows x64 с pinned Go/Node/npm и installed WebView2. Неполный/пустой diff, неизвестные SHA или несоответствие checkout выбирают `windows`, не лёгкий профиль. Rename учитывает оба пути через `--no-renames`; удаления обязательных документов завершаются ошибкой. Перед merge нужен CI актуального SHA, прежний green его не заменяет.
- Полная регрессия main — только по отдельному поручению на exact main SHA, по умолчанию локально. Запуск у CI-провайдера требует отдельного выбора/согласования; автоматический PR CI не является таким отдельным запуском. Самостоятельный полный профиль/режим регрессии сейчас не выделен: это gap, не разрешение запускать полный регресс в задаче синхронизации.
- Серверный CD отключён, final release/WinGet требуют отдельного разрешения и всех обязательных release gates. Изменения инструкций/selection CI не требуют build artifact, deployment или ручного запуска Actions. Никаких новых tokens, permissions, protections, runners или metadata writers.
- Существующий профиль выше сохранён как проектная адаптация с сохранением обязательных гарантий; его исторические поля не означают свежую runtime/billing проверку. Primary CI/PR records подтверждают поставку; pre-merge checkpoint допустим, metadata-only follow-up PR не нужен. Датированный checkpoint GOAL-007 не возобновляет остановленную владельцем работу автоматически.

### Локальный preflight текущего CI

Из чистого checkout точного кандидата задайте проверенный base SHA (для PR — актуальный base, для main push — previous SHA), затем выполните общий блок selector. Python 3 — prerequisite переносимой проверки; для `pipeline` нужен pinned Go, для `windows` — Windows prerequisites из [README](../README.md). CI проверяет PR merge checkout или exact main SHA; неизвестный/недоступный diff расширяет профиль. Hosted orchestration, triggers/permissions и GitHub review/protections — CI-only.

```powershell
$env:BASE_SHA = '<verified-base-sha>'
$env:HEAD_SHA = (git rev-parse HEAD).Trim()
$workflow = Get-Content .github/workflows/ci.yml -Raw
$block = [regex]::Match($workflow, '(?ms)^          # BEGIN LOCAL PREFLIGHT\r?\n(.*?)^          # END LOCAL PREFLIGHT').Groups[1].Value
if (-not $block) { throw 'Shared preflight block missing' }
$script = [regex]::Replace($block, '(?m)^          ', '')
$env:GITHUB_OUTPUT = Join-Path $env:TEMP ('llmi-profile-' + [guid]::NewGuid() + '.txt')
try {
    python -c $script
    if ($LASTEXITCODE -ne 0) { throw 'Preflight failed' }
    $profile = (Get-Content $env:GITHUB_OUTPUT | Where-Object { $_ -like 'profile=*' }) -replace '^profile=', ''
    $env:GOTOOLCHAIN = 'local'
    $env:GOFLAGS = '-mod=readonly'
    if ($profile -eq 'pipeline') {
        if ((go env GOVERSION) -ne ('go' + (Get-Content .go-version -Raw).Trim())) { throw 'Go pin mismatch' }
        go test ./internal/domain -count=1 -timeout 60s
        if ($LASTEXITCODE -ne 0) { throw 'Pipeline contracts failed' }
    } elseif ($profile -eq 'windows') {
        ./scripts/build-windows.ps1
        ./eng/release/Test-ReleaseTools.ps1
    } elseif ($profile -ne 'instructions') { throw 'Unknown profile' }
} finally {
    Remove-Item -LiteralPath $env:GITHUB_OUTPUT -ErrorAction SilentlyContinue
    Remove-Item Env:GITHUB_OUTPUT
}
```

До push дополнительно обязательны scoped self-review, сверка YAML/профилей с diff и неизменности Windows build/smoke/release commands. До merge — `windows-go` на актуальной revision; main push выполняет тот же selector на фактическом merge SHA. Отдельный полный main regression и release/CD не запускаются автоматически.

Checkpoint PR #45: исходный широкий локальный профиль на `f76c03b` останавливался на Go `TempDir RemoveAll cleanup: directory is not empty`; причина не установлена, исправление не заявлено. Сохранённое Evidence: два прогона, повтор пяти затронутых tests (count=3), synthetic file cleanup 100/100 PASS. Для текущих изменений инструкций/workflow consumer analysis выбирает `pipeline`, поэтому native DB/runtime tests неприменимы. При следующем изменении кода/сборки Windows профиль остаётся обязательным; повторный сбой нужно диагностировать, а не обходить. Финальный результат поставки подтверждают primary records PR #45.
