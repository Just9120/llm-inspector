<script lang="ts">
  import { onMount } from 'svelte';
  import { history } from '../../wailsjs/go/models';
  import { Analyze, Compare } from '../../wailsjs/go/desktop/Facade';
  import Filters from '../lib/Filters.svelte';
  import { localInput, utcInput, errorText, label, number, chartSegments } from '../lib/format.mjs';
  let baseline = $state({
    client: '',
    backend: '',
    model: '',
    session_id: '',
    operation_id: '',
    outcome: '',
    error_type: '',
  } as history.Filter);
  let candidate = $state({
    client: '',
    backend: '',
    model: '',
    session_id: '',
    operation_id: '',
    outcome: '',
    error_type: '',
  } as history.Filter);
  let from = $state(localInput(new Date(Date.now() - 7 * 86400000))),
    to = $state(localInput(new Date(Date.now() + 60000)));
  let candidateFrom = $state(localInput(new Date(Date.now() - 86400000))),
    candidateTo = $state(localInput(new Date(Date.now() + 60000)));
  let data = $state<history.Analytics | null>(null),
    comparison = $state<history.Comparison | null>(null),
    busy = $state(false),
    error = $state(''),
    metric = $state('ttft_ms');
  const metrics = [
    'input_tokens',
    'output_tokens',
    'ttft_ms',
    'prompt_tokens_per_second',
    'generation_tokens_per_second',
    'context_usage_tokens',
    'model_load_ms',
    'queue_ms',
    'total_duration_ms',
    'system_cpu_percent',
    'system_memory_percent',
    'error_rate_percent',
  ];
  function query(filter: history.Filter, start: string, end: string) {
    const f = utcInput(start),
      t = utcInput(end);
    if (f > t) throw new Error('Начало периода должно быть раньше окончания');
    return new history.Filter({ ...filter, from: f, to: t });
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
      data = null;
      comparison = null;
      data = await Analyze(query(baseline, from, to));
    });
  }
  onMount(() => {
    void refresh();
  });
</script>

<section class="panel">
  <h2>Тенденции и сравнение</h2>
  <p>Сравнивайте периоды, модели, backend или клиентов. Состав групп задаётся фильтрами.</p>
  <Filters bind:filter={baseline} bind:from bind:to prefix="Базовый период" />
  <div class="actions"><button disabled={busy} onclick={refresh}>Рассчитать аналитику</button></div>
  <details>
    <summary>Сравнить с другой группой</summary><Filters
      bind:filter={candidate}
      bind:from={candidateFrom}
      bind:to={candidateTo}
      prefix="Вторая группа"
    />
    <div class="actions">
      <label
        >Метрика<select bind:value={metric}
          >{#each metrics as key}<option value={key}>{label(key)}</option>{/each}</select
        ></label
      ><button
        class="secondary"
        disabled={busy}
        onclick={() =>
          perform(async () => {
            comparison = null;
            comparison = await Compare(
              query(baseline, from, to),
              query(candidate, candidateFrom, candidateTo),
              metric,
            );
          })}>Сравнить</button
      >
    </div>
  </details>
</section>
{#if error}<p class="notice warning" role="alert">{error}</p>{/if}
{#if busy}<p role="status">Расчёт по локальной истории…</p>{/if}
{#if comparison}<section class="panel">
    <h3>{label(comparison.metric)}</h3>
    <p class:warning-text={comparison.is_confirmed_degradation}>
      {comparison.is_confirmed_degradation
        ? 'Ухудшение подтверждено выбранной метрикой и baseline'
        : !comparison.baseline.is_statistically_sufficient ||
            !comparison.candidate.is_statistically_sufficient
          ? 'Недостаточно samples для вывода'
          : 'Подтверждённого ухудшения нет'}
    </p>
    <p>
      Среднее: {number(comparison.baseline.arithmetic_mean)} → {number(
        comparison.candidate.arithmetic_mean,
      )} · Δ {number(comparison.mean_delta)}. Samples: {comparison.baseline.sample_count} / {comparison
        .candidate.sample_count}.
    </p>
    {#if comparison.recurring_error_frequency.length}<h4>Изменение частоты ошибок</h4>
      {#each comparison.recurring_error_frequency as e}<p>
          {label(e.error_type)}: {number(e.baseline_rate_percent)}% → {number(
            e.candidate_rate_percent,
          )}% · Δ {number(e.rate_delta_percentage_points)} п.п. ({e.baseline_occurrences} / {e.candidate_occurrences})
        </p>{/each}{/if}
  </section>{/if}
{#if data}<section class="panel">
    <div class="section-heading">
      <h3>Показатели периода</h3>
      <span class="badge">Минимум 3 samples</span>
    </div>
    <p class="muted">
      Mean — среднее арифметическое. P95 — nearest rank: ceil(0,95 × n). При n &lt; 3 значения
      видны, но недостаточны для статистического вывода. Единицы заданы метрикой. Агрегаты и
      проценты — вычисленные значения (calculated); «—» — unavailable. Исходные
      exact/calculated/estimated samples и их provenance доступны в истории.
    </p>
    <div class="table-scroll">
      <table>
        <thead
          ><tr
            ><th>Метрика</th><th>n</th><th>Mean</th><th>Median</th><th>P95</th><th>Достаточность</th
            ></tr
          ></thead
        ><tbody
          >{#each Object.entries(data.metrics) as [key, a]}<tr
              ><td>{label(key)}</td><td>{a.sample_count}</td><td>{number(a.arithmetic_mean)}</td><td
                >{number(a.median)}</td
              ><td>{number(a.p95)}</td><td
                >{a.is_statistically_sufficient ? 'Достаточно' : 'Недостаточно'}</td
              ></tr
            >{/each}</tbody
        >
      </table>
    </div>
    <p>
      Холодные запросы: {data.model_loads.cold_requests}; тёплые: {data.model_loads.warm_requests};
      нет данных: {data.model_loads.unavailable_requests}.
    </p>
  </section>
  <section class="panel">
    <div class="section-heading">
      <h3>Динамика по дням</h3>
      <label
        >Метрика<select bind:value={metric}
          >{#each Object.keys(data.metrics) as key}<option value={key}>{label(key)}</option
            >{/each}</select
        ></label
      >
    </div>
    <svg
      viewBox="0 0 600 130"
      class="chart"
      role="img"
      aria-label={`Динамика среднего: ${label(metric)}`}
      ><line
        x1="8"
        x2="592"
        y1="122"
        y2="122"
        class="chart-axis"
      />{#each chartSegments(data.trend.map( (t) => ({ x: Date.parse(t.day), y: t.metrics[metric]?.arithmetic_mean }) )) as path}<path
          d={path}
          class="chart-line"
        />{/each}</svg
    >
    <div class="table-scroll">
      <table>
        <thead><tr><th>День UTC</th><th>Среднее</th><th>Samples</th></tr></thead><tbody
          >{#each data.trend as day}<tr
              ><td>{day.day}</td><td>{number(day.metrics[metric]?.arithmetic_mean)}</td><td
                >{day.metrics[metric]?.sample_count ?? 0}</td
              ></tr
            >{/each}</tbody
        >
      </table>
    </div>
  </section>
  <section class="panel">
    <h3>Ошибки и изменения runtime</h3>
    {#if !data.error_groups.length}<p>Ошибок в выбранной группе нет.</p>{/if}
    {#each data.error_groups as e}<p>
        {label(e.error_type)} · {e.occurrences} ·
        <span class:warning-text={e.is_recurring}
          >{e.is_recurring ? 'Повторяется' : 'Единичная ошибка'}</span
        >
      </p>{/each}
    <p>
      Ошибок без подтверждённой корреляции: {data.uncorrelated_errors}. Не считаем совпадение по
      времени доказательством общей причины.
    </p>
    <details>
      <summary>Подтверждённые связи ошибок</summary>
      <pre>{JSON.stringify(data.error_correlations, null, 2)}</pre>
    </details>
    <p>
      Связь с версиями и конфигурацией: {label(data.runtime_correlation.status)}. Запросов без
      runtime facts: {data.runtime_correlation.missing_facts}.
    </p>
    <details>
      <summary>Группы версий и сравнения</summary>
      <pre>{JSON.stringify(data.runtime_correlation, null, 2)}</pre>
    </details>
  </section>{/if}
