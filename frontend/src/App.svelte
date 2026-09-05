<script lang="ts">
  import { onMount, tick } from 'svelte';
  import type { desktop, main } from '../wailsjs/go/models';
  import { GetState, HideWindow, Exit } from '../wailsjs/go/desktop/Facade';
  import { GetShellState, ReportFrontendReady } from '../wailsjs/go/main/Host';
  import { EventsOn } from '../wailsjs/runtime/runtime';
  import Metric from './lib/Metric.svelte';
  import RequestDetail from './lib/RequestDetail.svelte';
  import Resources from './lib/Resources.svelte';
  import Operation from './lib/Operation.svelte';
  import History from './screens/History.svelte';
  import Analytics from './screens/Analytics.svelte';
  import Backends from './screens/Backends.svelte';
  import Settings from './screens/Settings.svelte';
  import { dateText, label, errorText } from './lib/format.mjs';
  const navigation = [
    { id: 'overview', title: 'Обзор', icon: '◉', hint: 'Что происходит сейчас' },
    { id: 'history', title: 'История', icon: '≡', hint: 'Запросы и операции' },
    { id: 'analytics', title: 'Аналитика', icon: '↗', hint: 'Тенденции и сравнение' },
    { id: 'backends', title: 'Backend', icon: '▣', hint: 'Локальные runtime' },
    { id: 'settings', title: 'Настройки', icon: '⚙', hint: 'Данные и фоновая работа' },
  ];
  let page = $state('overview'),
    viewState = $state<desktop.ViewState | null>(null),
    shell = $state<main.ShellState | null>(null),
    error = $state(''),
    bridge = $state(false);
  let initialized = false,
    visible = true,
    disposed = false,
    polling = false,
    timer: ReturnType<typeof setTimeout> | undefined;
  const active = $derived(viewState?.live.active ?? []);
  const currentPage = $derived(navigation.find((n) => n.id === page) ?? navigation[0]);
  async function refresh() {
    if (disposed || polling || !bridge) return;
    polling = true;
    try {
      shell = await GetShellState();
      if (shell.ready) {
        viewState = await GetState();
        error = '';
        if (!initialized) {
          visible = shell.visible;
          if (shell.smoke) {
            const { verifyFrontendSmoke } = await import('./lib/smoke');
            await verifyFrontendSmoke();
          }
          await tick();
          await ReportFrontendReady(
            document.documentElement.lang,
            document.querySelectorAll('[data-navigation]').length,
            'desktop-ui-v1',
          );
          initialized = true;
        }
      }
    } catch (e) {
      error = errorText(e);
    } finally {
      polling = false;
      if (!disposed && (visible || !initialized)) timer = setTimeout(refresh, 1000);
    }
  }
  onMount(() => {
    bridge = Object.hasOwn(window, 'go');
    if (!bridge) return;
    const offVisibility = EventsOn('inspector:visibility', (value: boolean) => {
      visible = value;
      if (timer) clearTimeout(timer);
      if (value) void refresh();
    });
    const offNavigate = EventsOn('inspector:navigate', (value: string) => {
      if (navigation.some((n) => n.id === value)) page = value;
    });
    const visibility = () => {
      visible = document.visibilityState !== 'hidden';
      if (timer) clearTimeout(timer);
      if (visible) void refresh();
    };
    document.addEventListener('visibilitychange', visibility);
    void refresh();
    return () => {
      disposed = true;
      if (timer) clearTimeout(timer);
      offVisibility();
      offNavigate();
      document.removeEventListener('visibilitychange', visibility);
    };
  });
  async function windowAction(action: () => Promise<void>) {
    try {
      await action();
    } catch (e) {
      error = errorText(e);
    }
  }
</script>

<div class="app-shell">
  <aside class="sidebar" aria-label="Навигация">
    <a
      class="brand"
      href="#overview"
      onclick={() => (page = 'overview')}
      aria-label="LLM Inspector — обзор"
    >
      <svg viewBox="0 0 36 36" aria-hidden="true"
        ><rect x="1" y="1" width="34" height="34" rx="10" fill="currentColor" /><path
          d="M9 23V13m6 13V10m6 11v-6m6 9V12"
          stroke="#102325"
          stroke-width="3"
          stroke-linecap="round"
        /></svg
      >
      <span>LLM Inspector<small>Локальная диагностика</small></span>
    </a>
    <nav>
      {#each navigation as item}<button
          data-navigation={item.id}
          class:active={page === item.id}
          aria-current={page === item.id ? 'page' : undefined}
          onclick={() => (page = item.id)}
          ><span class="nav-icon" aria-hidden="true">{item.icon}</span><span
            >{item.title}<small>{item.hint}</small></span
          ></button
        >{/each}
    </nav>
    <div class="sidebar-bottom">
      <p><span class="status-dot"></span> Данные остаются здесь</p>
      <small>Go · Windows · v{viewState?.version ?? '1.0.0'}</small>{#if bridge}<div
          class="window-actions"
        >
          <button
            class="subtle"
            disabled={!shell?.tray_available}
            onclick={() => windowAction(HideWindow)}>В tray</button
          ><button class="subtle" onclick={() => windowAction(Exit)}>Выйти</button>
        </div>{/if}
    </div>
  </aside>
  <main id="main-content">
    <header class="page-header">
      <div>
        <p class="eyebrow">ВАШ ЛОКАЛЬНЫЙ RUNTIME</p>
        <h1>{currentPage.title}</h1>
      </div>
      <div class="connection">
        <span class:online={viewState?.status.proxy_running} class="status-dot"></span>{viewState
          ?.status.proxy_running
          ? 'Proxy работает'
          : viewState
            ? 'Proxy не запущен'
            : bridge
              ? 'Инициализация'
              : 'Предпросмотр UI'}
      </div>
    </header>
    {#if !bridge}<p class="notice">
        Это браузерный предпросмотр интерфейса без runtime. Для наблюдения и действий откройте
        Windows executable LlmInspector.exe.
      </p>{/if}
    {#if error}<p role="alert" class="notice warning">{error}</p>{/if}
    {#if bridge && !viewState}<p role="status" class="notice">
        {shell?.message ?? 'Подключение к Go runtime…'}
      </p>{/if}
    {#if viewState?.status.settings_message}<p class="notice compact-notice">
        {viewState.status.settings_message}
      </p>{/if}
    {#if viewState && !viewState.status.history_available}<p class="notice warning">
        {viewState.status.history_message}
      </p>{/if}

    {#if page === 'overview'}
      <section class="hero panel">
        <div class="hero-copy">
          <span class="eyebrow">СОСТОЯНИЕ СЕЙЧАС</span>
          <h2>{active.length ? 'Наблюдаем за выполнением' : 'Готов к следующему запросу'}</h2>
          <p>
            {active.length
              ? 'Каждый запрос отделён по ID. Прогресс и ETA появляются только при достаточных сигналах.'
              : 'Направьте LLM-клиент на локальный proxy. Здесь появятся скорость, контекст и объяснение задержек — без текста ваших разговоров.'}
          </p>
          <div class="endpoint">
            <code>{viewState?.status.listener ?? 'http://127.0.0.1:5117'}/v1</code><span
              >Base URL клиента</span
            >
          </div>
        </div>
        <div class="hero-stat">
          <strong>{viewState?.active_count ?? '—'}</strong><span>активных запросов</span><small
            >{viewState ? label(viewState.status.backend) : 'Ожидаем подключение'}</small
          >
        </div>
      </section>
      {#if viewState && !viewState.status.proxy_running}<p class="notice warning">
          {viewState.status.message}
        </p>{/if}
      <div class="summary-grid">
        <Metric title="До первого токена" metric={viewState?.latest?.ttft} /><Metric
          title="Скорость генерации"
          metric={viewState?.latest?.telemetry.generation_speed}
        /><Metric
          title="Токены на входе"
          metric={viewState?.latest?.telemetry.prompt_tokens}
        /><Metric title="Контекст: занято" metric={viewState?.latest?.telemetry.context_usage} />
      </div>
      <p class="muted summary-caption">
        Последний завершённый запрос: {viewState?.latest
          ? dateText(viewState.latest.started_at)
          : 'пока нет данных'}. Это не показатели текущей незавершённой генерации.
      </p>
      {#if active.length}<section class="panel">
          <div class="section-heading">
            <h3>Выполняются сейчас</h3>
            <span class="badge">{viewState?.active_count}</span>
          </div>
          {#each active as request}<article class="live-row">
              <div>
                <h4>{label(request.stage.stage)} · {label(request.client)}</h4>
                <small><code>{request.request_id}</code> · {label(request.stage.evidence)}</small>
              </div>
              <Metric title="Прошло" metric={request.elapsed} compact /><Metric
                title="Прогресс"
                metric={request.progress}
                compact
              /><Metric title="ETA · оценка" metric={request.eta} compact />
            </article>{/each}{#if viewState?.live.omitted}<p class="notice warning">
              Часть запросов не входит в bounded live snapshot: {viewState.live.omitted}. Счётчик
              активных запросов не ограничен этим лимитом.
            </p>{/if}
        </section>{/if}
      <section class="panel">
        <div class="section-heading">
          <div>
            <h3>Что говорят данные</h3>
            <p>Факты отдельно от гипотез. Причина не угадывается.</p>
          </div>
          <span class="badge">Диагностика</span>
        </div>
        {#if !viewState?.diagnostics.length}<p class="empty compact-empty">
            Пока недостаточно наблюдений для диагностического вывода.
          </p>{/if}
        {#if viewState?.diagnostic_resource}<p class="muted">
            Resource evidence последнего завершённого запроса:
            {dateText(viewState.diagnostic_resource.captured_at)} ·
            <code>{viewState.diagnostic_resource.request_id}</code>. Это историческое измерение, не
            текущая нагрузка.
          </p>{/if}
        {#each viewState?.diagnostics ?? [] as conclusion}<article class="conclusion">
            <span class:warning={conclusion.kind === 'hypothesis'} class="badge"
              >{label(conclusion.kind)}</span
            >
            <div>
              <p>{conclusion.explanation}</p>
              <details>
                <summary>На чём основано</summary>
                <p>Правило: <code>{conclusion.rule}</code> · {conclusion.rule_version}</p>
                {#each conclusion.evidence as evidence}{#if evidence.metric}<Metric
                      title={label(evidence.kind)}
                      metric={evidence.metric}
                      compact
                    />{:else}<pre>{JSON.stringify(
                        evidence,
                        null,
                        2,
                      )}</pre>{/if}{/each}{#if !conclusion.evidence.length}<p>
                    Supporting evidence отсутствует.
                  </p>{/if}
              </details>
            </div>
          </article>{/each}
      </section>
      <section class="panel">
        <details>
          <summary>Системные ресурсы и GPU</summary><Resources
            samples={viewState?.resources ?? []}
          />
        </details>
      </section>
      {#if viewState?.latest}<section class="panel">
          <details>
            <summary>Последний запрос: токены, контекст, timings и tools</summary><RequestDetail
              request={viewState.latest}
            />
          </details>
        </section>{/if}
      {#if viewState?.operation}<section class="panel">
          <details>
            <summary>Последняя связанная операция</summary><Operation graph={viewState.operation} />
          </details>
        </section>{/if}
      <section class="panel">
        <details>
          <summary>Как подключить клиент и проверить сбор данных</summary>
          <p>
            Backend: <code>{viewState?.status.backend_url ?? 'http://127.0.0.1:11434'}</code>. Proxy
            не запускает его автоматически.
          </p>
          <p>Для достоверной идентификации клиента используйте endpoint:</p>
          <ul>
            {#each ['opencode', 'hermes', 'cline', 'open-webui'] as client}<li>
                {label(client)}:
                <code
                  >{viewState?.status.listener ?? 'http://127.0.0.1:5117'}/clients/{client}/v1</code
                >
              </li>{/each}
          </ul>
          <p>
            Поддерживаются <code>/v1/models</code> и <code>/v1/chat/completions</code>. Выберите
            OpenAI-compatible provider; для OpenCode — <code>@ai-sdk/openai-compatible</code>.
            Значение API key задаётся требованиями backend; Inspector не требует local application
            token.
          </p>
          {#if viewState}<h4>Состояние внутренних очередей</h4>
            <p>Потеря наблюдений означает неполную историю, а не успешную запись.</p>
            <pre>{JSON.stringify(
                {
                  gateway_dropped: viewState.gateway_dropped,
                  hub: viewState.hub_health,
                  writer: viewState.writer_health,
                  collectors: viewState.collector_health,
                  notifications: viewState.notification_health,
                  tray_failures: shell?.tray_failures,
                },
                null,
                2,
              )}</pre>{/if}
        </details>
      </section>
    {:else if viewState}
      {#if page === 'history'}<History />{:else if page === 'analytics'}<Analytics
        />{:else if page === 'backends'}<Backends
          activeCount={viewState.active_count}
          proxyEndpoint={viewState.status.backend_url}
        />{:else if page === 'settings'}<Settings
          {viewState}
          trayAvailable={shell?.tray_available ?? false}
        />{/if}
    {:else}<section class="panel empty">
        <h2>{currentPage.hint}</h2>
        <p>Этот раздел работает с локальным Go runtime. Действия недоступны до подключения.</p>
      </section>{/if}
  </main>
</div>
