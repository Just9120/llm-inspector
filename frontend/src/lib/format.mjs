/** @type {Record<string,string>} */
export const labels = {
  exact: 'Точно',
  calculated: 'Вычислено',
  estimated: 'Оценка',
  unavailable: 'Нет данных',
  unknown: 'Неизвестно',
  fact: 'Факт',
  hypothesis: 'Гипотеза',
  insufficient_data: 'Недостаточно данных',
  ollama: 'Ollama',
  'llama-cpp': 'llama.cpp',
  'lm-studio': 'LM Studio',
  generic: 'Другой / неизвестный',
  opencode: 'OpenCode',
  hermes: 'Hermes',
  cline: 'Cline',
  'open-webui': 'Open WebUI',
  model_loading: 'Загрузка модели',
  queue_waiting: 'Ожидание в очереди',
  prompt_processing: 'Обработка prompt',
  reasoning_generation: 'Генерация',
  tool_wait: 'Ожидание tools',
  completed: 'Завершён',
  cancelled: 'Отменён',
  error: 'Ошибка',
  running: 'Работает',
  stopped: 'Остановлен',
  starting: 'Запускается',
  stopping: 'Останавливается',
  crashed: 'Аварийно завершён',
  faulted: 'Сбой',
  not_configured: 'Не настроен',
  pending_confirmation: 'Ожидает подтверждения',
  cold: 'Холодный запуск',
  warm: 'Модель уже загружена',
  none: 'Нет',
  not_applicable: 'Не применимо',
  protocol_observed: 'Наблюдение протокола',
  backend_reported: 'Сообщено backend',
  connection_refused: 'Соединение отклонено',
  timeout: 'Тайм-аут',
  context_overflow: 'Переполнение контекста',
  client_cancelled: 'Отмена клиентом',
  client_cancellation: 'Отмена клиентом',
  relay_failed: 'Ошибка relay',
  backend_crash: 'Сбой backend',
  http_api_error: 'Ошибка HTTP/API',
  backend_unavailable: 'Backend недоступен',
  inspector: 'Inspector',
  client: 'Клиент',
  backend: 'Backend',
  model: 'Модель',
  input_tokens: 'Входные токены',
  output_tokens: 'Выходные токены',
  total_tokens: 'Всего токенов',
  cached_tokens: 'Токены cache',
  reasoning_tokens: 'Reasoning tokens',
  context_usage_tokens: 'Контекст: занято',
  context_limit_tokens: 'Лимит контекста',
  context_history_tokens: 'Контекст: история',
  context_tool_tokens: 'Контекст: tools',
  prompt_tokens_per_second: 'Обработка prompt, токен/с',
  generation_tokens_per_second: 'Генерация, токен/с',
  ttft_ms: 'TTFT, мс',
  model_load_ms: 'Загрузка модели, мс',
  queue_ms: 'Очередь, мс',
  total_duration_ms: 'Длительность, мс',
  error_rate_percent: 'Доля ошибок, %',
  system_cpu_percent: 'CPU, %',
  system_memory_percent: 'RAM, %',
  system_memory_used_bytes: 'RAM, байт',
  cpu: 'CPU',
  memory_percent: 'RAM',
  memory_used: 'Занято RAM',
  process_cpu: 'CPU процесса',
  process_memory: 'RAM процесса',
  disk_read: 'Чтение диска',
  disk_write: 'Запись диска',
  client_to_backend: 'К backend',
  backend_to_client: 'От backend',
  gpu_utilization: 'GPU',
  gpu_vram_used: 'Занято VRAM',
  gpu_vram_total: 'Всего VRAM',
  gpu_temperature: 'Температура GPU',
  gpu_power: 'Мощность GPU',
  available: 'Доступен',
  probing: 'Проверяется',
  insufficient: 'Недостаточно данных',
  correlation_only: 'Корреляция, не причина',
};
/** @param {string | null | undefined} value */
export function label(value) {
  return value ? (labels[value] ?? value) : 'Нет данных';
}
/** @param {number | null | undefined} value */
export function number(value) {
  return typeof value === 'number' && Number.isFinite(value)
    ? new Intl.NumberFormat('ru-RU', { maximumFractionDigits: 2 }).format(value)
    : '—';
}
/** @param {{value?:number|null,unit?:string,quality?:string}|null|undefined} metric */
export function metricText(metric) {
  if (
    !metric ||
    metric.quality === 'unavailable' ||
    metric.value == null ||
    !Number.isFinite(metric.value)
  )
    return '—';
  let value = metric.value;
  /** @type {Record<string,string>} */
  const units = {
    tokens: 'ток.',
    token_delta: 'ток.',
    count: '',
    milliseconds: 'мс',
    nanoseconds: 'нс',
    tokens_per_second: 'ток./с',
    percent: '%',
    bytes: 'Б',
    celsius: '°C',
    watts: 'Вт',
  };
  let unit = units[metric.unit ?? ''] ?? metric.unit ?? '';
  if (metric.unit === 'bytes') {
    const n = Math.min(3, Math.max(0, Math.floor(Math.log2(Math.max(1, value)) / 10)));
    value /= 1024 ** n;
    unit = ['Б', 'КиБ', 'МиБ', 'ГиБ'][n];
  }
  return `${number(value)}${unit ? ' ' + unit : ''}`;
}
/** @param {unknown} value */
export function dateText(value) {
  if (typeof value !== 'string' || !Number.isFinite(Date.parse(value))) return '—';
  return new Date(value).toLocaleString('ru-RU');
}
/** @param {Date} date */
export function localInput(date) {
  return new Date(date.getTime() - date.getTimezoneOffset() * 60000).toISOString().slice(0, 16);
}
/** @param {string} value */
export function utcInput(value) {
  const date = new Date(value);
  if (!value || !Number.isFinite(date.getTime()))
    throw new Error('Укажите корректные дату и время');
  return date.toISOString();
}
/** @param {unknown} value */
export function errorText(value) {
  const text = typeof value === 'string' ? value : value instanceof Error ? value.message : '';
  return /[а-яё]/i.test(text)
    ? text.slice(0, 500)
    : 'Действие не выполнено. Проверьте доступность runtime и корректность выбранных параметров.';
}
/** Missing samples break lines, never become zero or bridged observations.
 * @param {{x:number,y:number|null|undefined}[]} points
 * @param {number} [width]
 * @param {number} [height]
 */
export function chartSegments(points, width = 600, height = 130) {
  const valid = points.filter(
    (p) => Number.isFinite(p.x) && typeof p.y === 'number' && Number.isFinite(p.y),
  );
  if (!valid.length) return [];
  const minX = Math.min(...valid.map((p) => p.x)),
    maxX = Math.max(...valid.map((p) => p.x));
  const minY = Math.min(0, ...valid.map((p) => p.y ?? 0)),
    maxY = Math.max(1, ...valid.map((p) => p.y ?? 0));
  /** @type {string[]} */ const result = [];
  let segment = '';
  for (const p of points) {
    if (!Number.isFinite(p.x) || p.y == null || !Number.isFinite(p.y)) {
      if (segment) result.push(segment);
      segment = '';
      continue;
    }
    const x = 8 + ((p.x - minX) / Math.max(1, maxX - minX)) * (width - 16),
      y = height - 8 - ((p.y - minY) / (maxY - minY)) * (height - 16);
    segment += `${segment ? ' L' : 'M'}${x.toFixed(2)},${y.toFixed(2)}`;
  }
  if (segment) result.push(segment);
  return result;
}
