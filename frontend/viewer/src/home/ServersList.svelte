<script lang="ts">
  import type {ILexboxServer, IProjectModel, IServerProjects} from '$lib/dotnet-types';
  import {useAuthService, useProjectsService} from '$lib/services/service-provider';
  import {SvelteSet} from 'svelte/reactivity';
  import Server from './Server.svelte';

  interface Props {
    localProjects: IProjectModel[];
    refreshProjects: () => Promise<void>;
  }

  const { localProjects, refreshProjects }: Props = $props();

  const projectsService = useProjectsService();
  const authService = useAuthService();

  const remoteProjects: { [server: string]: IServerProjects } = $state({});
  let loadingRemoteProjects = $state(false);

  async function fetchRemoteProjects(): Promise<void> {
    loadingRemoteProjects = true;
    try {
      let result = await projectsService.remoteProjects();
      for (let serverProjects of result) {
        remoteProjects[serverProjects.server.id] = serverProjects;
      }
    } finally {
      loadingRemoteProjects = false;
    }
  }

  const loadingServerProjects = new SvelteSet<string>();

  async function refreshServerProjects(server: ILexboxServer, force: boolean = false) {
    loadingServerProjects.add(server.id);
    try {
      const projects = await projectsService.serverProjects(server.id, force);
      if (projects) remoteProjects[server.id] = projects;
    } finally {
      loadingServerProjects.delete(server.id);
    }
  }

  fetchRemoteProjects().catch((error) => {
    console.error('Failed to fetch remote projects', error);
    throw error;
  });

  async function refreshProjectsAndServers() {
    await refreshProjects();
    await fetchRemoteProjects();
    serversPromise = authService.servers();
    await serversPromise;
  }

  function retryUnreachableServers() {
    for (const serverProjects of Object.values(remoteProjects)) {
      if (serverProjects.unreachable) void refreshServerProjects(serverProjects.server);
    }
  }


  let serversPromise = $state(authService.servers());

</script>

<svelte:window ononline={retryUnreachableServers}/>

{#await serversPromise}
  <Server status={undefined} projects={[]} localProjects={[]} loading={true}/>
{:then serversStatus}
  {#each serversStatus as status (status.server.id)}
    {@const server = status.server}
    {@const serverProjects = remoteProjects[server.id]?.projects.filter(p => p.crdt) ?? []}
    {@const canDownloadByCode = remoteProjects[server.id]?.canDownloadByCode}
    <Server {status}
            projects={serverProjects}
            {canDownloadByCode}
            unreachable={remoteProjects[server.id]?.unreachable}
            {localProjects}
            loading={loadingServerProjects.has(server.id) || loadingRemoteProjects}
            refreshProjects={() => refreshServerProjects(server, true)}
            refreshAll={() => refreshProjectsAndServers()}/>
  {/each}
{/await}
