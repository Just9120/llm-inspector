<script lang="ts">
  import { onMount } from 'svelte';
  import { history, artifact } from '../../wailsjs/go/models';
  import type { domain } from '../../wailsjs/go/models';
  import * as api from '../../wailsjs/go/desktop/Facade';
  import Filters from '../lib/Filters.svelte';
  import RequestDetail from '../lib/RequestDetail.svelte';
  import Resources from '../lib/Resources.svelte';
  import Operation from '../lib/Operation.svelte';
  import ArtifactDialog from '../lib/ArtifactDialog.svelte';
  import { localInput, utcInput, dateText, metricText, label, errorText } from '../lib/format.mjs';
  let filter = $state({
    client: '',
    backend: '',
    model: '',
    session_id: '',
    operation_id: '',
    outcome: '',
    error_type: '',
  } as history.Filter);
  let from = $state(localInput(new Date(Date.now() - 86400000))),
    to = $state(localInput(new Date(Date.now() + 60000)));
  let result = $state<history.Requests | null>(null),
    selected = $state<history.Request | null>(null),
    samples = $state<domain.ResourceSample[]>([]),
    operation = $state<domain.OperationGraph | null>(null);
  let operationSamples = $state<domain.ResourceSample[]>([]),
    operationTruncated = $state(false);
  let busy = $state(false),
    error = $state(''),
    truncated = $state(false),
    preview = $state<artifact.Artifact | null>(null);
  function query() {
    const f = utcInput(from),
      t = utcInput(to);
    if (f > t) throw new Error('Начало периода должно быть раньше окончания');
    return new history.Filter({ ...filter, from: f, to: t, limit: 200 });
  }
  async function perform(task: () => Promise<void>) {
    if (busy) return;
    busy = true;
    error = '';
    try {
      await task();
    } catch (e) {
      error = errorText(e);
    } finally {
      busy = false;
    }
  }
  async function refresh() {
    await perform(async () => {
      result = null;
      selected = null;
      operation = null;
      result = await api.GetHistory(query());
    });
  }
  async function select(request: history.Request) {
    await perform(async () => {
      selected = request;
      operation = null;
      operationSamples = [];
      operationTruncated = false;
      samples = [];
      const begin = Date.parse(String(request.started_at));
      const detail = await api.GetHistoryDetails(
        new history.Filter({
          from: new Date(begin - 1000).toISOString(),
          to: new Date(begin + request.duration_ms + 1000).toISOString(),
        }),
      );
      samples = detail.resource_samples.filter((s) => s.request_id === request.request_id);
      truncated = detail.resource_samples_truncated;
      if (request.operation_id) {
        const graph = await api.GetOperation(request.operation_id);
        operation = graph?.graph ?? null;
        operationSamples = graph?.resources ?? [];
        operationTruncated = graph?.resources_truncated ?? false;
      }
    });
  }
  async function snapshot(operationOnly = false) {
    await perform(async () => {
      if (operationOnly) {
        if (!operation) throw new Error('Сначала выберите операцию');
        preview = await api.PreviewSnapshot({
          scope: 'operation',
          operation_id: operation.id,
        } as artifact.Selection);
      } else {
        query();
        preview = await api.PreviewSnapshot(
          new artifact.Selection({
            scope: 'time_range',
            from_utc: utcInput(from),
            to_utc: utcInput(to),
          }),
        );
      }
    });
  }
  onMount(() => {
    void refresh();
  });
</script>

<section class="panel">
  <div class="section-heading">
    <div>
      <h2>Техническая история</h2>
      <p>Найти запрос, восстановить последовательность и сопоставить нагрузку.</p>
    </div>
    <span class="badge">Локальная SQLite</span>
  </div>
  <Filters bind:filter bind:from bind:to />
  <div class="actions">
    <button disabled={busy} onclick={refresh}>Показать запросы</button><button
      class="secondary"
      disabled={busy}
      onclick={() => snapshot()}>Снимок периода…</button
    ><button
      class="secondary"
      disabled={busy}
      onclick={() =>
        perform(async () => {
          query();
          preview = await api.PreviewExport(utcInput(from), utcInput(to));
        })}>Экспорт с аналитикой…</button
    >
  </div>
</section>
{#if error}<p class="notice warning" role="alert">{error}</p>{/if}
{#if busy}<p role="status" class="muted">Чтение локальных данных…</p>{/if}
{#if result}<section class="panel">
    {#if result.truncated}<p class="notice warning">
        Показаны первые 200 запросов. Сузьте период или фильтры для полного результата.
      </p>{/if}
    {#if !result.items.length}<div class="empty">
        <h3>За этот период запросов нет</h3>
        <p>Направьте клиент на proxy или выберите другой период.</p>
      </div>{:else}
      <div class="table-scroll">
        <table>
          <thead
            ><tr
              ><th>Время / клиент</th><th>Модель / backend</th><th>Токены вход / выход</th><th
                >TTFT</th
              ><th>Результат</th><th></th></tr
            ></thead
          ><tbody>
            {#each result.items as r}<tr class:selected={selected?.request_id === r.request_id}
                ><td>{dateText(r.started_at)}<small>{label(r.client)}</small></td><td
                  >{r.telemetry.model || 'Нет данных'}<small>{label(r.telemetry.backend)}</small
                  ></td
                ><td
                  >{metricText(r.telemetry.prompt_tokens)} / {metricText(
                    r.telemetry.completion_tokens,
                  )}<small
                    >{label(r.telemetry.prompt_tokens.quality)} / {label(
                      r.telemetry.completion_tokens.quality,
                    )}</small
                  ></td
                ><td>{metricText(r.ttft)}<small>{label(r.ttft.quality)}</small></td><td
                  >{label(r.outcome)}{#if r.error_type !== 'none'}<small class="warning-text"
                      >{label(r.error_type)} · {r.error_occurrences >= 2
                        ? `Повторяется: ${r.error_occurrences}`
                        : 'Единичная ошибка'}</small
                    >{/if}</td
                ><td
                  ><button
                    class="subtle"
                    disabled={busy}
                    onclick={() => select(r)}
                    aria-label={`Подробности запроса ${r.request_id}`}>Открыть →</button
                  ></td
                ></tr
              >{/each}
          </tbody>
        </table>
      </div>{/if}
  </section>{/if}
{#if selected}<section class="panel">
    <RequestDetail request={selected} />
    <details>
      <summary>Нагрузка во время этого запроса</summary>{#if truncated}<p class="notice warning">
          Достигнут лимит временной шкалы; часть samples отсутствует.
        </p>{/if}<Resources {samples} timeline />
    </details>
  </section>{/if}
{#if operation}<section class="panel">
    <details>
      <summary>Нагрузка за всю операцию</summary>
      {#if operationTruncated}<p class="notice warning">
          Временная шкала операции неполна: достигнут лимит samples.
        </p>{/if}
      <Resources samples={operationSamples} timeline />
    </details>
    <Operation graph={operation} /><button
      class="secondary"
      disabled={busy}
      onclick={() => snapshot(true)}>Снимок операции…</button
    >
  </section>{/if}
{#if preview}<ArtifactDialog {preview} onclose={() => (preview = null)} />{/if}
