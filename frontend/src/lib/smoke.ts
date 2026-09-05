import { tick } from 'svelte';
import { GetHistory } from '../../wailsjs/go/desktop/Facade';
import { history } from '../../wailsjs/go/models';

// Runs only inside the isolated --smoke-test fixture. No discovery, process,
// credentials, autostart, deletion or external-service action is performed.
export async function verifyFrontendSmoke() {
  const deadline = Date.now() + 12000;
  while (!(await GetHistory(new history.Filter({ limit: 1 }))).items.length) {
    if (Date.now() > deadline) throw new Error('Smoke: история не появилась');
    await new Promise((resolve) => setTimeout(resolve, 25));
  }
  for (const [id, title, text] of [
    ['history', 'История', 'Техническая история'],
    ['analytics', 'Аналитика', 'Тенденции и сравнение'],
    ['backends', 'Backend', 'Локальные backend'],
    ['settings', 'Настройки', 'Режим наблюдения'],
    ['overview', 'Обзор', 'Что говорят данные'],
  ]) {
    const button = document.querySelector<HTMLButtonElement>(`[data-navigation="${id}"]`);
    if (!button) throw new Error('Smoke: отсутствует навигация');
    button.click();
    await tick();
    if (
      document.querySelector('h1')?.textContent !== title ||
      !document.querySelector('main')?.textContent?.includes(text)
    )
      throw new Error('Smoke: раздел не отрисован');
    // Give each mounted component its read-only async initialization turn.
    await new Promise((resolve) => setTimeout(resolve, 60));
    if (id === 'history') {
      const until = Date.now() + 5000;
      while (!document.querySelector('tbody')?.textContent?.includes('smoke-model')) {
        if (Date.now() > until) throw new Error('Smoke: запрос не виден в истории');
        await new Promise((resolve) => setTimeout(resolve, 25));
      }
    }
    if (id === 'settings') {
      const custom = document.querySelector<HTMLInputElement>(
        'input[type="radio"][value="custom"]',
      );
      if (!custom) throw new Error('Smoke: отсутствует custom профиль');
      custom.click();
      await tick();
      const interval = document.querySelector<HTMLInputElement>('input[type="number"]');
      if (!interval || interval.min !== '250' || interval.max !== '10000')
        throw new Error('Smoke: custom профиль не работает');
    }
  }
}
