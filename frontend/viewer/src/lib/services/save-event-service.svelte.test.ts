import {describe, expect, it} from 'vitest';

import {SaveHandler} from './save-event-service.svelte';

function deferred() {
  let resolve!: () => void;
  let reject!: (e: Error) => void;
  const promise = new Promise<void>((res, rej) => {
    resolve = res;
    reject = rej;
  });
  return {promise, resolve, reject};
}

describe('SaveHandler', () => {
  it('keeps saving until every overlapping save finishes', async () => {
    const handler = new SaveHandler();
    const first = deferred();
    const second = deferred();
    const firstSave = handler.handleSave(() => first.promise);
    const secondSave = handler.handleSave(() => second.promise);

    first.resolve();
    await firstSave;
    expect(handler.saving).toBe(true);

    second.resolve();
    await secondSave;
    expect(handler.saving).toBe(false);
    expect(handler.lastResult).toBe('saved');
  });

  it('a save that started before a failure does not clear it', async () => {
    const handler = new SaveHandler();
    const failing = deferred();
    const overlapping = deferred();
    const failingSave = handler.handleSave(() => failing.promise);
    const overlappingSave = handler.handleSave(() => overlapping.promise);

    failing.reject(new Error('boom'));
    await expect(failingSave).rejects.toThrow('boom');
    overlapping.resolve();
    await overlappingSave;
    expect(handler.lastResult).toBe('failed');

    await handler.handleSave(() => Promise.resolve());
    expect(handler.lastResult).toBe('saved');
  });
});
