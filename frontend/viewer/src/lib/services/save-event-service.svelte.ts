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
  #lastResultSaveId = $state(0);
  #lastStarted = 0;
  #lastStartedAtFailure = 0;

  get saving(): boolean {
    return this.#inFlight > 0;
  }

  get startedCount(): number {
    return this.#lastStarted;
  }

  /** A failure sticks until a save that started after it succeeds. */
  get lastResult(): SaveResult {
    return this.#lastResult;
  }

  /** The last result, ignoring saves that started before `startedCount` was `baseline`. */
  resultSince(baseline: number): SaveResult {
    return this.#lastResultSaveId > baseline ? this.#lastResult : 'none';
  }

  async handleSave<T>(saveAction: () => Promise<T>): Promise<T> {
    const id = ++this.#lastStarted;
    this.#inFlight++;
    try {
      const result = await saveAction();
      if (id > this.#lastStartedAtFailure) {
        this.#lastResult = 'saved';
        this.#lastResultSaveId = id;
      }
      return result;
    } catch (e) {
      this.#lastStartedAtFailure = this.#lastStarted;
      this.#lastResult = 'failed';
      this.#lastResultSaveId = id;
      throw e;
    } finally {
      this.#inFlight--;
    }
  }
}
