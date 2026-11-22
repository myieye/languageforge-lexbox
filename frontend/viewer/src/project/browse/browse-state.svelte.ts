import type {IPartOfSpeech, IPublication, ISemanticDomain} from '$lib/dotnet-types';
import {usePartsOfSpeech, usePublications, useSemanticDomains} from '$project/data';
import {useProjectContext} from '$project/project-context.svelte';
import type {MissingOption, Op, SelectedField} from './filter/filter-types';
import type {SortChoice} from './sort/options';

type ItemList<T> = {readonly current: T[]; readonly loaded: boolean};

const symbol = Symbol.for('fw-lite-browse-state');
/** Search, filters and sort of the browse view, kept for as long as the project is open. */
export function useBrowseState(): BrowseState {
  const projectContext = useProjectContext();
  const semanticDomains = useSemanticDomains();
  const partsOfSpeech = usePartsOfSpeech();
  const publications = usePublications();
  return projectContext.getOrAdd(symbol, () => new BrowseState(semanticDomains, partsOfSpeech, publications));
}

export class BrowseState {
  search = $state('');
  sortChoice = $state<SortChoice>();
  /** Writing system to sort/display by, chosen separately from the sort field/direction. Undefined = the current view's first vernacular. */
  sortWs = $state<string>();
  missingField = $state<MissingOption | null>(null);
  selectedField = $state<SelectedField | null>(null);
  /** Undefined = all of the selected field's writing systems */
  selectedWs = $state<string[]>();
  fieldFilterValue = $state('');
  filterOp = $state<Op>('contains');
  includeSubDomains = $state(false);
  hasComments = $state(false);
  unreadComments = $state(false);
  openComments = $state(false);
  #semanticDomain = $state<ISemanticDomain>();
  #partOfSpeech = $state<IPartOfSpeech>();
  #publication = $state<IPublication>();

  constructor(
    private semanticDomains: ItemList<ISemanticDomain>,
    private partsOfSpeech: ItemList<IPartOfSpeech>,
    private publications: ItemList<IPublication>,
  ) {}

  // These read through the current list, so a pick deleted (e.g. by sync) since it was made is dropped.
  get semanticDomain() { return stillListed(this.#semanticDomain, this.semanticDomains); }
  set semanticDomain(value) { this.#semanticDomain = value; }
  get partOfSpeech() { return stillListed(this.#partOfSpeech, this.partsOfSpeech); }
  set partOfSpeech(value) { this.#partOfSpeech = value; }
  get publication() { return stillListed(this.#publication, this.publications); }
  set publication(value) { this.#publication = value; }

  resetFilters() {
    this.search = '';
    this.missingField = null;
    // null tells FieldSelect to fall back to its default field
    this.selectedField = null;
    this.selectedWs = undefined;
    this.fieldFilterValue = '';
    this.filterOp = 'contains';
    this.includeSubDomains = false;
    this.semanticDomain = undefined;
    this.partOfSpeech = undefined;
    this.publication = undefined;
    this.hasComments = false;
    this.unreadComments = false;
    this.openComments = false;
  }
}

function stillListed<T extends {id: string}>(selected: T | undefined, list: ItemList<T>): T | undefined {
  if (!selected) return undefined;
  // read current first: it starts the lazy fetch that `loaded` waits on
  const items = list.current;
  if (!list.loaded) return selected;
  return items.find(item => item.id === selected.id);
}
