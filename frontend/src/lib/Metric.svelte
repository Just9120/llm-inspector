<script lang="ts">
  import type { domain } from '../../wailsjs/go/models';
  import { label, metricText } from './format.mjs';
  let {
    title,
    metric,
    compact = false,
  }: { title: string; metric?: domain.Metric | null; compact?: boolean } = $props();
</script>

<div class:compact class="metric">
  <span class="metric-label">{title}</span>
  <strong>{metricText(metric)}</strong>
  <details class="provenance">
    <summary class:estimated={metric?.quality === 'estimated'}
      >{label(metric?.quality ?? 'unavailable')}</summary
    >
    <div>
      Источник: {metric?.source ?? 'нет данных'}<br />Версия: {metric?.source_version ??
        '—'}{#if metric?.derivation_version}<br />Расчёт: {metric.derivation_version}{/if}
    </div>
  </details>
</div>
