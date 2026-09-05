# Windows portable release runbook

## Назначение

Runbook описывает выпуск финальной unsigned portable self-contained single-file `win-x64` application
через GitHub Releases. Это distribution flow, а не server/runtime deployment: `DEPLOY` и `LIVE` для
него `N/A`. Microsoft Store, MSIX, trusted signing и automatic update сюда не входят.

Canonical product/release constraints находятся в [`../project-spec.md`](../project-spec.md), safety
rules — в [`../ci-cd-rules.md`](../ci-cd-rules.md). Workflow: `.github/workflows/release.yml`.

## Version и branch policy

- `main` — единственная development и release source branch.
- До первой финальной публикации product version остаётся `1.0`.
- Новые prerelease versions и отдельные version/release branches не создаются.
- Первый следующий public stable tag — `v1.0.0`; после его публикации development version становится
  `1.1` в том же `main`.
- Historical `v1.0.0-rc.1`, `v1.0.0-rc.2` и `v1.0.0-rc.3` immutable и не переиспользуются.

## Preconditions

1. Release source merged в `main`; local `main`, `origin/main` и intended SHA совпадают.
2. Exact-main `CI / windows-go` на intended SHA завершён success без required skips.
3. Все согласованные обязательные automated, manual Windows и integration gates для публикации
   зафиксированы с exact source/artifact identity; unresolved required gate блокирует tag.
4. Intended tag имеет exact final SemVer form `vMAJOR.MINOR.PATCH`, без prerelease suffix, и ещё не
   существует local, remote или в GitHub Releases.
5. GitHub Actions включены, repository public, а actor имеет право создать tag/release.

## Historical prerelease Evidence

`v1.0.0-rc.1` — immutable failed-delivery tag: build и attestations прошли, Release не был создан.
`v1.0.0-rc.2` опубликован, но провалил Pro manual gate. `v1.0.0-rc.3` опубликован из exact source
`821b17abf68bb63dd09f83a834d2d3bdec2e899c`; release pipeline и доступные Windows Pro flows прошли,
но Windows Home/full matrix не завершена. Эти tags/releases остаются audit Evidence, а не текущим
versioning template или финальным `v1.0.0`.

## Запуск trusted flow

Создание final tag является authorization на немедленную публикацию workflow. Выполнять блок можно
только после Preconditions. `<FINAL_SEMVER_TAG>` и `<EXACT_VALIDATED_MAIN_SHA>` заменяются фактически
проверенными значениями; для первой stable публикации tag должен быть `v1.0.0`.

```powershell
git fetch origin main --tags
git switch main
git merge --ff-only origin/main
$releaseTag = '<FINAL_SEMVER_TAG>'
$sourceSha = '<EXACT_VALIDATED_MAIN_SHA>'
if ($releaseTag -notmatch '^v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') {
    throw 'Only a final SemVer release tag is allowed.'
}
if ((git rev-parse HEAD) -ne $sourceSha) { throw 'Local main does not match the validated source SHA.' }
git tag -a $releaseTag $sourceSha -m "LLM Inspector $releaseTag"
git push origin "refs/tags/$releaseTag"
```

Tag считается immutable. Не перемещать и не переиспользовать опубликованный version. Если defect
обнаружен после финальной публикации, исправление проходит новым PR в `main` и получает новый final
patch/minor version только после повторной required validation.

## Что обязан сделать workflow

1. Fail closed проверить exact final SemVer без prerelease suffix и достижимость tagged SHA из
   `origin/main`.
2. На ephemeral `windows-2025` выполнить `scripts/build-windows.ps1`: exact Go/Node/npm pins, readonly Go modules, locked npm install, format/vet/Svelte checks, полный Go/Node test suite и native smoke.
3. Один раз собрать single-file `LlmInspector-<version>-win-x64.exe` и smoke-test именно его.
4. Сформировать SHA-256, `portable-release-v1` manifest, SPDX 2.3 SBOM и русскоязычное предупреждение
   об unsigned/SmartScreen/manual-update boundary.
5. Передать exact payload в отдельный publish job, перепроверить checksums/source identity, создать
   Sigstore build-provenance и SBOM attestations и только затем создать финальный GitHub Release.

Build job имеет только `contents: read`. Publish job не checkout-ит repository и получает только
`contents: write`, `id-token: write`, `attestations: write`; `GH_TOKEN` передаётся только в final
`gh release create` step.

## Проверка результата

После публикации exact tag и asset проверяются следующей template-процедурой:

```powershell
$releaseTag = '<FINAL_SEMVER_TAG>'
$releaseVersion = $releaseTag.Substring(1)
$verificationDirectory = '.\artifacts\release-verification'
gh release view $releaseTag --json url,isPrerelease,targetCommitish,assets
gh release download $releaseTag --dir $verificationDirectory
$executable = Join-Path $verificationDirectory "LlmInspector-$releaseVersion-win-x64.exe"
Get-FileHash $executable -Algorithm SHA256
gh attestation verify $executable --repo Just9120/llm-inspector
```

Observed hash обязан совпасть с `SHA256SUMS.txt`, manifest и exact pre-publication candidate Evidence.
Если public artifact identity отличается, manual gate не переносится автоматически и release не
получает READY. Зафиксировать tag, source SHA, workflow run ID, release URL, artifact SHA-256,
attestation URLs/verification и terminal statuses.

## Manual Windows gate

До final tag один exact candidate hash из intended `main` SHA отдельно проверяется на Windows 11
`25H2` Home x64 и Pro x64: запуск без Go/Node/.NET и admin rights, с установленным WebView2 Runtime (без автоматического скачивания), SmartScreen
guidance, tray/background, local proxy, SQLite restart/recovery и critical supported backend/client
flow. После публикации identity public artifact сверяется с pre-publication candidate. До полного
Evidence `E01-AC01` не получает credit, а final release tag не создаётся.

### Ограничение текущего Go candidate

На code SHA `49d1e9aaf48c8b6780803dcc115100e3a2a5b5f7` local root и independent clean checkout дали одинаковый executable SHA-256, но hosted CI — другой. Значения и exact run записаны в [delivery checkpoint](../delivery-plan.md). Cross-host byte identity не доказана; ни локальный smoke, ни hosted CI не являются pre-publication manual Evidence. До начала manual phase нужно выбрать exact source/artifact candidate; при последующем несовпадении hashes проверка не переносится. Это фиксация открытого gate, не изменение release policy.

## Failure policy

- Failed/skipped/cancelled build, tests, payload verification, attestation или publish — не success.
- Не выполнять speculative rerun. Сначала установить причину и сохранить run/SHA Evidence.
- Не удалять и не перемещать tag/release автоматически. Destructive correction требует отдельного
  explicit owner decision; обычный путь — reviewed fix в `main` и новый final SemVer после validation.
- Не публиковать вручную локально пересобранный replacement под тем же tag.
