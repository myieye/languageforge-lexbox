<script lang="ts">
  import Hotkey from '$lib/components/hotkey/hotkey.svelte';
  import MasterDetailView from '$lib/components/master-detail/MasterDetailView.svelte';
  import {IsMobile} from '$lib/hooks/is-mobile.svelte';
  import EntryView from './EntryView.svelte';
  import SearchFilter from './SearchFilter.svelte';
  import EntriesList from './EntriesList.svelte';
  import {t} from 'svelte-i18n-lingui';
  import SidebarPrimaryAction from '../SidebarPrimaryAction.svelte';
  import {useDialogsService} from '$lib/services/dialogs-service';
  import {useFeatures} from '$lib/services/feature-service';
  import PrimaryNewEntryButton from '../PrimaryNewEntryButton.svelte';
  import {BrowseParam} from '$lib/utils/search-params';
  import {pt} from '$lib/views/view-text';
  import {useViewService} from '$lib/views/view-service.svelte';
  import {SortField} from '$lib/dotnet-types';
  import SortMenu from './sort/SortMenu.svelte';
  import SortWritingSystemMenu from './sort/SortWritingSystemMenu.svelte';
  import type {SortConfig} from './sort/options';
  import {useProjectContext} from '$project/project-context.svelte';
  import type {EntryListViewMode} from './EntryListViewOptions.svelte';
  import EntryListViewOptions from './EntryListViewOptions.svelte';
  import {useProjectStorage} from '$lib/storage/project-storage.svelte';
  import ViewErrorBoundary from '$lib/layout/ViewErrorBoundary.svelte';
  import UnreadCommentBadge from '$project/browse/filter/UnreadCommentBadge.svelte';
  import {QueryParamStateBool} from '$lib/utils/url.svelte';
  import {watch} from 'runed';
  import {useWritingSystemService} from '$project/data';
  import {useBrowseState} from './browse-state.svelte';

  const projectContext = useProjectContext();
  const viewService = useViewService();
  const writingSystemService = useWritingSystemService();
  const dialogsService = useDialogsService();
  const features = useFeatures();
  const entryListViewMode = useProjectStorage().entryListViewMode;
  const browseState = useBrowseState();

  let selectedId = $state('');
  let gridifyFilter = $state<string>();
  // Same history strategy as the master-detail open state: on mobile the comments take
  // half the screen, so back should close them rather than leave the view. Nothing else
  // pushes for them now that the panel is a pane rather than a drawer.
  const commentsOpen = new QueryParamStateBool({
    key: BrowseParam.CommentsOpen,
    allowBack: IsMobile.value,
    replaceOnDefaultValue: IsMobile.value,
  }, false);
  let sort = $state<SortConfig>();
  // The user's sortWs pick is kept, but only applies while the current view shows that writing system.
  const effectiveSortWs = $derived.by(() => {
    const viewWritingSystems = writingSystemService.viewVernacularNoAudio(viewService.currentView);
    return viewWritingSystems.find(ws => ws.wsId === browseState.sortWs)?.wsId ?? viewWritingSystems[0]?.wsId;
  });
  const sortWithWs = $derived<SortConfig | undefined>(sort ? {...sort, writingSystem: effectiveSortWs} : undefined);
  const entryMode: EntryListViewMode = $derived(entryListViewMode.current === 'preview' ? 'preview' : 'simple');

  // Turning the filter on means the comments are what the user came for, so open the
  // sidebar once. Deliberately only on the transition (and not when the view remounts with the
  // filter already on), so closing it stays closed.
  watch(() => browseState.unreadComments, (filterOn, wasOn) => {
    if (filterOn && !wasOn) commentsOpen.current = true;
  }, {lazy: true});

  async function newEntry() {
    const {publication, semanticDomain, partOfSpeech} = browseState;
    const entry = await dialogsService.createNewEntry(undefined, {
      publishIn: publication ? [publication] : [],
    }, {
      semanticDomains: semanticDomain ? [semanticDomain] : undefined,
      partOfSpeech,
    });
    if (!entry) return;
    selectedId = entry.id;
  }

  let entriesList: EntriesList | undefined = $state();

  async function onClose() {
    // Selected id is preserved by MasterDetailView so we can scroll back to it.
    await entriesList?.tryToScrollToEntry(selectedId);
  }
</script>
<Hotkey key="e" disabled={!features.write} onHotkey={() => void newEntry()} />
<SidebarPrimaryAction>
  {#snippet children(isOpen: boolean)}
    <PrimaryNewEntryButton active={!IsMobile.value && isOpen} onclick={newEntry}/>
  {/snippet}
</SidebarPrimaryAction>
<ViewErrorBoundary class="flex flex-col h-full" title={$t`Browse view failed`}>
  <MasterDetailView
    idParam={BrowseParam.EntryId}
    openParam={BrowseParam.EntryOpen}
    bind:selectedId
    onClose={onClose}
  >
    {#snippet master({selectedId: masterSelectedId, select})}
      <div class="flex flex-col h-full p-2 md:p-4 md:pr-0">
        <div class="md:mr-3">
          <SearchFilter bind:gridifyFilter />
          <div class="my-2 flex items-center gap-2">
            <SortMenu bind:value={sort} bind:choice={browseState.sortChoice}
              autoSelector={() => browseState.search ? SortField.SearchRelevance : SortField.Headword} />
            <SortWritingSystemMenu bind:value={() => effectiveSortWs, (v) => browseState.sortWs = v} />
            {#if features.comments}
              <UnreadCommentBadge bind:unreadComments={browseState.unreadComments}/>
            {/if}
            <div class="ms-auto">
              <EntryListViewOptions bind:entryMode={() => entryMode, (v) => void entryListViewMode.set(v)} />
            </div>
          </div>
        </div>
        <EntriesList bind:this={entriesList}
                     search={browseState.search}
                     selectedEntryId={masterSelectedId}
                     sort={sortWithWs}
                     {gridifyFilter}
                     publication={browseState.publication}
                     partOfSpeech={browseState.partOfSpeech}
                     semanticDomain={browseState.semanticDomain}
                     onSelectEntry={(e) => select(e?.id ?? '')}
                     previewDictionary={entryMode === 'preview'}/>
      </div>
    {/snippet}
    {#snippet detail({selectedId: detailSelectedId, close, showClose})}
      {#if projectContext.maybeApi}
        <div class="md:p-4 md:pl-4 h-full">
          <EntryView
            entryId={detailSelectedId}
            onClose={close}
            {showClose}
            bind:showComments={() => commentsOpen.current, (v) => commentsOpen.current = v}
          />
        </div>
      {/if}
    {/snippet}
    {#snippet empty()}
      <div class="flex items-center justify-center h-full text-muted-foreground text-center m-2">
        <p>{$t`Select ${pt($t`an entry`, $t`a word`, viewService.currentView)} to view details`}</p>
      </div>
    {/snippet}
  </MasterDetailView>
</ViewErrorBoundary>
