# Windows portable release runbook

## Назначение

Runbook описывает выпуск unsigned portable self-contained single-file `win-x64` application через
GitHub Releases. Это distribution flow, а не server/runtime deployment: `DEPLOY` и `LIVE` для него
`N/A`. Microsoft Store, MSIX, trusted signing и automatic update сюда не входят.

Canonical product/release constraints находятся в [`../project-spec.md`](../project-spec.md), safety
rules — в [`../ci-cd-rules.md`](../ci-cd-rules.md). Workflow: `.github/workflows/release.yml`.

## Preconditions

1. Release source уже merged в `main`; local `main`, `origin/main` и intended SHA совпадают.
2. Exact-main `CI / windows-dotnet` на intended SHA завершён success без required skips.
3. Intended tag имеет exact SemVer form `vMAJOR.MINOR.PATCH[-prerelease]` и ещё не существует local,
   remote или в GitHub Releases.
4. Для `v1.0.0-rc.*` source остаётся observation-only: BACKLOG-01 lifecycle code ещё не merged.
5. GitHub Actions включены, repository public, а actor имеет право создать tag/release.

## Запуск trusted flow

`v1.0.0-rc.1` уже является immutable failed-delivery tag: его build и attestations прошли, но Release не был создан. Не переиспользовать этот version. Пример для следующего forward-fix candidate; `<EXACT_MAIN_SHA>` заменяется только фактически проверенным SHA:

```powershell
git fetch origin main --tags
git switch main
git merge --ff-only origin/main
git tag -a v1.0.0-rc.2 <EXACT_MAIN_SHA> -m "LLM Inspector v1.0.0-rc.2"
git push origin refs/tags/v1.0.0-rc.2
```

Tag считается immutable. Не перемещать и не переиспользовать опубликованный version. При defect
исправление проходит новым PR и получает следующий prerelease version, например `v1.0.0-rc.2`.

## Что обязан сделать workflow

1. Fail closed проверить SemVer и достижимость tagged SHA из `origin/main`.
2. На ephemeral `windows-2025` выполнить locked restore, format, Release build и полный test suite.
3. Один раз собрать single-file `LlmInspector-<version>-win-x64.exe` и smoke-test именно его.
4. Сформировать SHA-256, `portable-release-v1` manifest, SPDX 2.3 SBOM и русскоязычное предупреждение
   об unsigned/SmartScreen/manual-update boundary.
5. Передать exact payload в отдельный publish job, перепроверить checksums/source identity, создать
   Sigstore build-provenance и SBOM attestations и только затем создать GitHub prerelease/release.

Build job имеет только `contents: read`. Publish job не checkout-ит repository и получает только
`contents: write`, `id-token: write`, `attestations: write`; `GH_TOKEN` передаётся только в final
`gh release create` step.

## Проверка результата

```powershell
gh release view v1.0.0-rc.2 --json url,isPrerelease,targetCommitish,assets
gh release download v1.0.0-rc.2 --dir artifacts/release-verification
Get-FileHash .\artifacts\release-verification\LlmInspector-1.0.0-rc.2-win-x64.exe -Algorithm SHA256
gh attestation verify .\artifacts\release-verification\LlmInspector-1.0.0-rc.2-win-x64.exe --repo Just9120/llm-inspector
```

Observed hash обязан совпасть с `SHA256SUMS.txt` и manifest. Зафиксировать tag, source SHA, workflow
run ID, release URL, artifact SHA-256, attestation URLs/verification и terminal statuses.

## Manual Windows gate

Один exact artifact hash отдельно проверяется на Windows 11 `25H2` Home x64 и Pro x64: запуск без
установленного .NET/runtime и admin rights, SmartScreen guidance, tray/background, local proxy,
SQLite restart/recovery и critical supported backend/client flow. До обеих записей `E01-AC01` не
получает credit.

## Failure policy

- Failed/skipped/cancelled build, tests, payload verification, attestation или publish — не success.
- Не выполнять speculative rerun. Сначала установить причину и сохранить run/SHA Evidence.
- Не удалять и не перемещать tag/release автоматически. Destructive correction требует отдельного
  explicit owner decision; обычный путь — reviewed forward-fix и новый SemVer prerelease.
- Не публиковать вручную локально пересобранный replacement под тем же tag.
