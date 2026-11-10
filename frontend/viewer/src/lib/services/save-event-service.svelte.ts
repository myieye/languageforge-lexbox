import {useProjectContext} from '$project/project-context.svelte';

export type SaveResult = 'none' | 'saved' | 'failed';

const symbol = Symbol.for('fw-lite-save-event-service');
export function useSaveHandler(): SaveHandler {
  const projectContext = useProjectContext();
  return projectContext.getOrAdd(symbol, () => new SaveHandler());
}

export class SaveHandler {
  #inFlight = $state(0);
  #lastResult = $state<SaveResult>('none');
  #lastStarted = 0;
  #lastStartedAtFailure = 0;

  get saving(): boolean {
    return this.#inFlight > 0;
  }

  /** A failure sticks until a save that started after it succeeds. */
  get lastResult(): SaveResult {
    return this.#lastResult;
  }

  async handleSave<T>(saveAction: () => Promise<T>): Promise<T> {
    const id = ++this.#lastStarted;
    this.#inFlight++;
    try {
      const result = await saveAction();
      if (id > this.#lastStartedAtFailure) this.#lastResult = 'saved';
      return result;
    } catch (e) {
      this.#lastStartedAtFailure = this.#lastStarted;
      this.#lastResult = 'failed';
      throw e;
    } finally {
      this.#inFlight--;
    }
  }
}
