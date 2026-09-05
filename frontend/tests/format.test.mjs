import test from 'node:test';
import assert from 'node:assert/strict';
import {
  metricText,
  number,
  dateText,
  utcInput,
  chartSegments,
  errorText,
  label,
} from '../src/lib/format.mjs';
test('missing, unavailable and nonfinite metrics never become zero', () => {
  for (const metric of [
    null,
    undefined,
    { value: null },
    { value: NaN },
    { value: Infinity },
    { value: 0, quality: 'unavailable' },
  ])
    assert.equal(metricText(metric), '—');
  assert.equal(metricText({ value: 0, quality: 'exact', unit: 'tokens' }), '0 ток.');
  assert.equal(metricText({ value: 1048576, quality: 'calculated', unit: 'bytes' }), '1 МиБ');
  assert.equal(number(undefined), '—');
  assert.equal(label('estimated'), 'Оценка');
});
test('invalid date and unsupported error text stay content-free', () => {
  assert.equal(dateText({}), '—');
  assert.equal(dateText('invalid'), '—');
  assert.throws(() => utcInput(''));
  assert.ok(utcInput('2026-09-05T12:00').endsWith('Z'));
  assert.ok(!errorText(new Error('private-secret-canary')).includes('canary'));
  assert.equal(errorText('Недопустимый период'), 'Недопустимый период');
});
test('charts preserve unavailable gaps and exact zero', () => {
  const paths = chartSegments([
    { x: 0, y: 0 },
    { x: 1, y: 2 },
    { x: 2, y: null },
    { x: 3, y: 3 },
    { x: 4, y: undefined },
  ]);
  assert.equal(paths.length, 2);
  assert.match(paths[0], /^M8\.00,122\.00 L/);
  assert.ok(!paths.join('').includes('NaN'));
  assert.deepEqual(chartSegments([{ x: 0, y: null }]), []);
});
