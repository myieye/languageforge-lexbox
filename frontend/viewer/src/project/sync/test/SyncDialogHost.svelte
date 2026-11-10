<script lang="ts">
  import {Router} from 'svelte-routing';
  import {UserProjectRole, type ILexboxServer, type IMiniLcmJsInvokable} from '$lib/dotnet-types';
  import type {SyncStatus} from '$lib/dotnet-types/generated-types/LexCore/Sync/SyncStatus';
  import type {ISyncServiceJsInvokable} from '$lib/dotnet-types/generated-types/FwLiteShared/Services/ISyncServiceJsInvokable';
  import {initProjectContext} from '$project/project-context.svelte';
  import SyncDialog from '../../SyncDialog.svelte';
  import {untrack} from 'svelte';

  const testServer: ILexboxServer = {id: 'test-server', displayName: 'Lexbox', authority: 'https://test-server.com'};
  const {syncService, supportedFeatures, syncStatus, server = testServer}: {
    syncService: ISyncServiceJsInvokable,
    supportedFeatures: IMiniLcmJsInvokable['supportedFeatures'],
    syncStatus?: SyncStatus,
    server?: ILexboxServer | null,
  } = $props();

  initProjectContext({
    api: {supportedFeatures: untrack(() => supportedFeatures)} as unknown as IMiniLcmJsInvokable,
    projectName: 'test-project',
    projectCode: 'test-project',
    server: untrack(() => server) ?? undefined,
    projectData: {
      name: 'test-project', code: 'test-project', id: 'test-project-id', clientId: 'test-client',
      role: UserProjectRole.Editor, serverId: untrack(() => server)?.id,
    },
    syncService: untrack(() => syncService),
  });
  let dialog = $state<SyncDialog>();
  export function open() {
    dialog?.open();
  }
</script>

<Router>
  <SyncDialog bind:this={dialog} {syncStatus} />
</Router>
