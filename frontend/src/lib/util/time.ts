import { browser } from '$app/environment';

export const enum Duration {
  Default = 5000,
  Medium = 10000,
  Long = 15000,
}

export async function delay<T>(ms = Duration.Default): Promise<T> {
  return new Promise<T>(resolve => setTimeout(resolve, ms));
}

const debouncedTimeouts: Record<string, ReturnType<typeof setTimeout> | 'debounce-next'> = {};

export function debounce<T extends Record<string, any>, S>(debounceId: string, fn: () => T,
state?: S, { debounceTime = 400, debounceFirst = false } = {}): Promise<T & { state?: S }> {
  if (!browser) return Promise.resolve({ ...fn(), state });

  if (!debouncedTimeouts[debounceId] && !debounceFirst) {
    debouncedTimeouts[debounceId] = 'debounce-next';
    return Promise.resolve({ ...fn(), state });
  }

  clearTimeout(debouncedTimeouts[debounceId]);

  return new Promise(resolve => {
    debouncedTimeouts[debounceId] = setTimeout(() => {
      resolve({ ...fn(), state });
    }, debounceTime);
  });
}
