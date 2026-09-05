<script lang="ts">
  import type { domain } from '../../wailsjs/go/models';
  import Metric from './Metric.svelte';
  import { chartSegments, dateText, label, metricText } from './format.mjs';
  let { samples, timeline = false }: { samples: domain.ResourceSample[]; timeline?: boolean } =
    $props();
  let device = $state('');
  let key = $state('cpu');
  const keys = [
    'cpu',
    'memory_percent',
    'memory_used',
    'process_cpu',
    'process_memory',
    'disk_read',
    'disk_write',
    'client_to_backend',
    'backend_to_client',
    'gpu_utilization',
    'gpu_vram_used',
    'gpu_vram_total',
    'gpu_temperature',
    'gpu_power',
  ] as const;
  const devices = $derived([...new Set(samples.map((s) => s.gpu_device_id || ''))]);
  const selected = $derived(devices.includes(device) ? device : (devices[0] ?? ''));
  const rows = $derived(
    samples
      .filter((s) => (s.gpu_device_id || '') === selected)
      .sort((a, b) => String(a.captured_at).localeCompare(String(b.captured_at))),
  );
  const latest = $derived(rows.at(-1));
  const points = $derived(
    rows.map((s) => ({
      x: Date.parse(String(s.captured_at)),
      y: (s[key as keyof domain.ResourceSample] as domain.Metric)?.value,
    })),
  );
</script>

{#if !samples.length}<p class="empty compact-empty">
    Системные метрики появятся во время запроса. Фоновое отсутствие измерений не означает нулевую
    нагрузку.
  </p>
{:else}
  <div class="toolbar">
    <label
      >Устройство<select bind:value={device}
        >{#each devices as id}<option value={id}>{id || 'Система · GPU не определён'}</option
          >{/each}</select
      ></label
    >
    {#if timeline}<label
        >Метрика<select bind:value={key}
          >{#each keys as k}<option value={k}>{label(k)}</option>{/each}</select
        ></label
      >{/if}
  </div>
  {#if timeline}
    <svg
      class="chart"
      viewBox="0 0 600 130"
      role="img"
      aria-label={`Динамика ${label(key)}; разрывы означают отсутствие данных`}
      ><line
        x1="8"
        x2="592"
        y1="122"
        y2="122"
        class="chart-axis"
      />{#each chartSegments(points) as path}<path d={path} class="chart-line" />{/each}</svg
    >
    <div class="chart-range">
      <span>{dateText(rows[0]?.captured_at)}</span><span>{dateText(latest?.captured_at)}</span>
    </div>
    <details>
      <summary>Значения, request и stage на временной шкале ({rows.length})</summary>
      <div class="table-scroll">
        <table>
          <thead
            ><tr
              ><th>Время</th><th>Request</th><th>Стадия</th><th>{label(key)}</th><th>Качество</th
              ></tr
            ></thead
          ><tbody
            >{#each rows as s}<tr
                ><td>{dateText(s.captured_at)}</td><td
                  ><code>{s.request_id || 'не установлен'}</code></td
                ><td>{label(s.stage?.stage)}<br /><small>{label(s.stage?.evidence)}</small></td><td
                  >{metricText(s[key as keyof domain.ResourceSample] as domain.Metric)}</td
                ><td>{label((s[key as keyof domain.ResourceSample] as domain.Metric)?.quality)}</td
                ></tr
              >{/each}</tbody
          >
        </table>
      </div>
    </details>
  {/if}
  {#if latest}
    <p class="muted">
      Измерено: {dateText(latest.captured_at)}. Нагрузка GPU — всего устройства; attribution
      конкретного workload недоступна.
    </p>
    <div class="metrics-grid">
      {#each keys as k}<Metric title={label(k)} metric={latest[k]} compact />{/each}
    </div>
    <details>
      <summary>Process association и потери samples</summary>
      {#if latest.process}<p>
          PID {latest.process.pid} · {latest.process.image_name} · запущен {dateText(
            latest.process.started_at,
          )}<br />Источник: {latest.process.source_version}
        </p>{:else}<p>Достоверная связь с процессом не установлена.</p>{/if}
      <p>
        Driver: {latest.gpu_driver_version || 'нет данных'}. Пропущено samples: {Math.max(
          ...rows.map((r) => r.dropped_samples),
        )}.
      </p>
    </details>
  {/if}
{/if}
