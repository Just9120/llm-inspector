# LLM Inspector

LLM Inspector — Windows-first desktop-приложение для локального мониторинга, аналитики и диагностики работы LLM. Согласованный продуктовый scope и acceptance criteria находятся в `docs/project-spec.md`.

## Текущее состояние

Проект имеет реализованную repository/CI foundation поверх выбранной architecture baseline:

- solution содержит девять production boundaries и шесть test projects из `docs/architecture.md`;
- минимальный Avalonia shell компилируется и запускается, но proxy, telemetry, storage и product UI ещё не реализованы;
- выбран design stack: C# / `.NET 10 LTS`, Avalonia UI, embedded loopback-only Kestrel proxy и SQLite WAL;
- initial support matrix: Windows 11 `25H2` Home/Pro, `x64`, с актуальным cumulative update;
- SDK зафиксирован exact version `10.0.400`, NuGet packages — через Central Package Management, 15 normal и 9 `win-x64` committed lock files;
- локально подтверждены locked restore, formatting, Release build, 10 foundation/policy tests, self-contained `win-x64` publish и shell smoke;
- product contract ратифицирован: initial release содержит 139 atomic AC, полный согласованный roadmap — 164 AC;
- PR/`main` CI определён на ephemeral GitHub-hosted `windows-2025` runner с read-only token и SHA-pinned actions; фактический run Evidence см. в `docs/delivery-plan.md`;
- server/runtime CD явно не используется: приложение устанавливается на Windows PC, а не deploy-ится на runtime host.

Foundation не засчитывается как выполнение product acceptance criteria: текущая product readiness остаётся `0/139 = 0%` для initial release и `0/164 = 0%` для полного roadmap.

## Быстрый старт

Prerequisite: Windows x64 и exact [.NET SDK `10.0.400`](https://dotnet.microsoft.com/en-us/download/dotnet/10.0). `global.json` запрещает автоматический roll-forward на другой SDK.

Проверить SDK и восстановить dependency graph:

```powershell
dotnet --version
dotnet restore LlmInspector.slnx --locked-mode
```

Первая команда должна вывести `10.0.400`. Запустить пустой development shell:

```powershell
dotnet run --project src/LlmInspector.App/LlmInspector.App.csproj --no-restore
```

Shell показывает только честный placeholder. Он не открывает proxy port, не создаёт database и не собирает telemetry.

## Проверки и сборка

```powershell
dotnet format LlmInspector.slnx --verify-no-changes --no-restore
dotnet build LlmInspector.slnx -c Release --no-restore
dotnet test LlmInspector.slnx -c Release --no-build --logger "console;verbosity=minimal"
dotnet restore src/LlmInspector.App/LlmInspector.App.csproj --locked-mode -r win-x64
dotnet publish src/LlmInspector.App/LlmInspector.App.csproj -c Release -r win-x64 --self-contained true --no-restore -o artifacts/win-x64
.\artifacts\win-x64\LlmInspector.App.exe --smoke-test
```

`artifacts/` — локальный disposable output и не коммитится. CI выполняет ту же последовательность в [`.github/workflows/ci.yml`](.github/workflows/ci.yml), не публикует artifacts и не содержит deployment steps.

## Навигация

- [`AGENTS.md`](AGENTS.md) — главный repository router, authority model и execution boundaries.
- [`docs/project-spec.md`](docs/project-spec.md) — ратифицированный canonical product contract с initial release/backlog epics, atomic AC, readiness и Evidence.
- [`docs/delivery-plan.md`](docs/delivery-plan.md) — текущий delivery dashboard, readiness snapshots, blockers и candidate next Goal.
- [`docs/delivery-plan-archive.md`](docs/delivery-plan-archive.md) — исторический архив; не используется для текущей readiness.
- [`docs/ci-cd-rules.md`](docs/ci-cd-rules.md) — обязательный CI/CD и production safety contract.
- [`docs/architecture.md`](docs/architecture.md) — выбранный stack, runtime/data/privacy boundaries, backend capability matrix, test strategy и Windows release design.
- [Upstream requirements](https://docs.google.com/document/d/1r4o0UiJohJf34j3nL56LWnOxGRi7WDC3jqoDIIjDTnA/edit) — provenance source ратифицированных требований; текущим source of truth остаётся `docs/project-spec.md`.

Optional contracts для `Context Bundle Builder`, AI delivery infrastructure и runbooks не созданы: соответствующие workstreams и operations отсутствуют.

## Рабочий процесс

1. Перед работой прочитать root и применимые nested `AGENTS.md` / `AGENTS.override.md`.
2. Для product scope использовать только ратифицированную нормативную часть `docs/project-spec.md`.
3. Реализацию начинать только по explicit user instruction либо approved Current Goal в `docs/delivery-plan.md`.
4. Для CI/CD, secrets, deployment, production или migrations обязательно применять `docs/ci-cd-rules.md`.
5. Проценты готовности выводить только из выполненных atomic acceptance criteria с явным denominator и подтверждённым Evidence.

## Repository

- GitHub: <https://github.com/Just9120/llm-inspector>
- Ожидаемая production/default branch: `main`.
- На baseline-аудите `2026-09-02` remote repository был пуст; initial documentation bootstrap создал `main`.
- Architecture baseline merged через [PR #1](https://github.com/Just9120/llm-inspector/pull/1) в commit `00ca8c3ef727d784ca2e0c9d837231be7f68c5e4`; текущая foundation развивается отдельной PR-веткой от этого base.
