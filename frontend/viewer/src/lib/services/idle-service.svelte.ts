import {IsIdle, useEventListener} from 'runed';
import {getContext, hasContext, setContext} from 'svelte';

const symbol = Symbol.for('fw-lite-idle-service');
const IDLE_TIMEOUT_MS = 5 * 60 * 1000;

export function useIdleService(): IdleService {
  if (hasContext(symbol)) return getContext(symbol);
  const service = new IdleService();
  setContext(symbol, service);
  return service;
}

export class IdleService {
  constructor() {
    this.#isIdle = new IsIdle({ timeout: IDLE_TIMEOUT_MS });
    const updateHidden = () => this.#hidden = document.visibilityState === 'hidden';
    useEventListener(() => document, 'visibilitychange', updateHidden);
    useEventListener(() => window, 'pageshow', updateHidden);
    useEventListener(() => window, 'pagehide', () => this.#hidden = true);
  }

  #isIdle: IsIdle;
  #hidden = $state(false);

  // Hidden counts as idle: a backgrounded app can be killed (e.g. on Android) without inputs ever blurring
  get isIdle() { return this.#isIdle.current || this.#hidden; }
}
