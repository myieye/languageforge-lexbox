<script lang="ts">
  import { Badge, BadgeList, MemberBadge } from '$lib/components/Badges';
  import EditableText from '$lib/components/EditableText.svelte';
  import FormatDate from '$lib/components/FormatDate.svelte';
  import { ProjectTypeBadge } from '$lib/components/ProjectType';
  import FormatRetentionPolicy from '$lib/components/FormatRetentionPolicy.svelte';
  import HgLogView from '$lib/components/HgLogView.svelte';
  import DeleteModal from '$lib/components/modals/DeleteModal.svelte';
  import t from '$lib/i18n';
  import { isAdmin } from '$lib/user';
  import { z } from 'zod';
  import type { PageData } from './$types';
  import { _changeProjectDescription, _changeProjectName, _deleteProjectUser, type ProjectUser } from './+page';
  import AddProjectMember from './AddProjectMember.svelte';
  import ChangeMemberRoleModal from './ChangeMemberRoleModal.svelte';
  import { CircleArrowIcon, TrashIcon } from '$lib/icons';
  import { notifySuccess, notifyWarning } from '$lib/notify';
  import { DialogResponse } from '$lib/components/modals';
  import type { ErrorMessage } from '$lib/forms';
  import ResetProjectModal from './ResetProjectModal.svelte';
  import Dropdown from '$lib/components/Dropdown.svelte';
  import { FormField } from '$lib/forms';
  import IconButton from '$lib/components/IconButton.svelte';
  import { delay } from '$lib/util/time';
  import { page } from '$app/stores';
  import ConfirmDeleteModal from '$lib/components/modals/ConfirmDeleteModal.svelte';
  import { _deleteProject } from '$lib/gql/mutations';
  import { goto } from '$app/navigation';
  import MoreSettings from '$lib/components/MoreSettings.svelte';
  import { AdminContent, Page } from '$lib/layout';
  import SvelteMarkdown from 'svelte-markdown';
  import {ProjectMigrationStatus} from '$lib/gql/generated/graphql';
  import {onMount} from 'svelte';
  import Button from '$lib/forms/Button.svelte';
  import Icon from '$lib/icons/Icon.svelte';

  export let data: PageData;
  $: user = data.user;
  let projectStore = data.project;
  $: project = $projectStore;
  $: _project = project as NonNullable<typeof project>;

  $: projectHgUrl = import.meta.env.DEV
    ? `http://hg.${$page.url.host}/${data.code}`
    : `https://hg-${$page.url.host.replace('depot', 'forge')}/${data.code}`;

  let changeMemberRoleModal: ChangeMemberRoleModal;
  async function changeMemberRole(projectUser: ProjectUser): Promise<void> {
    const { response } = await changeMemberRoleModal.open({
      userId: projectUser.user.id,
      name: projectUser.user.name,
      role: projectUser.role,
    });

    if (response === DialogResponse.Submit) {
      notifySuccess(
        $t('project_page.notifications.role_change', {
          name: projectUser.user.name,
          role: projectUser.role.toLowerCase(),
        })
      );
    }
  }

  let resetProjectModal: ResetProjectModal;
  async function resetProject(): Promise<void> {
    await resetProjectModal.open(_project.code);
  }

  let removeUserModal: DeleteModal;
  let userToDelete: ProjectUser | undefined;
  async function deleteProjectUser(projectUser: ProjectUser): Promise<void> {
    userToDelete = projectUser;
    const deleted = await removeUserModal.prompt(async () => {
      const { error } = await _deleteProjectUser(_project.id, projectUser.user.id);
      return error?.message;
    });
    if (deleted) {
      notifyWarning($t('project_page.notifications.user_delete', { name: projectUser.user.name }));
    }
  }

  async function updateProjectName(newName: string): Promise<ErrorMessage> {
    const result = await _changeProjectName({ projectId: _project.id, name: newName });
    if (result.error) {
      return result.error.message;
    }
    notifySuccess($t('project_page.notifications.rename_project', { name: newName }));
  }

  async function updateProjectDescription(newDescription: string): Promise<ErrorMessage> {
    const result = await _changeProjectDescription({
      projectId: _project.id,
      description: newDescription,
    });
    if (result.error) {
      return result.error.message;
    }
    notifySuccess($t('project_page.notifications.describe', { description: newDescription }));
  }

  $: userId = user.id;
  $: canManage = isAdmin(user) || user.projects.find((p) => p.code == project?.code)?.role == 'Manager';

  const projectNameValidation = z.string().min(1, $t('project_page.project_name_empty_error'));

  var getProjectDropdownTrigger: HTMLElement;

  var copyingToClipboard = false;
  var copiedToClipboard = false;

  async function copyProjectUrlToClipboard(): Promise<void> {
    getProjectDropdownTrigger.focus(); // keeps the dropdown open
    copyingToClipboard = true;
    await navigator.clipboard.writeText(projectHgUrl);
    copiedToClipboard = true;
    copyingToClipboard = false;
    await delay();
    copiedToClipboard = false;
  }

  let deleteProjectModal: ConfirmDeleteModal;

  async function softDeleteProject(): Promise<void> {
    const result = await deleteProjectModal.open(_project.name, async () => {
      const { error } = await _deleteProject(_project.id);
      return error?.message;
    });
    if (result.response === DialogResponse.Submit) {
      notifyWarning($t('delete_project_modal.success', { name: _project.name, code: _project.code }));
      await goto(data.home);
    }
  }

  let migrationStatus = project?.migrationStatus ?? ProjectMigrationStatus.Unknown;
  //no need to translate these since it'll only be temporary
  const migrationStatusTable = {
    [ProjectMigrationStatus.Migrated]: 'Migrated',
    [ProjectMigrationStatus.Migrating]: 'Migrating',
    [ProjectMigrationStatus.Unknown]: 'Unknown',
    [ProjectMigrationStatus.PrivateRedmine]: 'Not Migrated (private)',
      [ProjectMigrationStatus.PublicRedmine]: 'Not Migrated (public)',
  } satisfies Record<ProjectMigrationStatus, string>;
  onMount(() => {
      migrationStatus = project?.migrationStatus ?? ProjectMigrationStatus.Unknown;
      if (migrationStatus === ProjectMigrationStatus.Migrating) {
          void watchMigrationStatus();
      }
  });

  async function watchMigrationStatus(): Promise<void> {
    if (!project) return;
    notifyWarning('This project is currently being migrated. Some features may not work as expected.');
    const result = await fetch(`/api/project/awaitMigrated?projectCode=${project.code}`);
    const response = await result.json();
    if (response) {
      migrationStatus = ProjectMigrationStatus.Migrated;
    }
  }

  async function migrateProject(): Promise<void> {
    if (!project) return;
    await fetch(`/api/migrate/migrateRepo?projectCode=${project.code}`);
    migrationStatus = ProjectMigrationStatus.Migrating;
    await watchMigrationStatus();
  }
</script>

<svelte:head>
  <title>{project?.name ?? $t('project_page.not_found', { code: data.code })}</title>
</svelte:head>

<Page wide>
  <div class="space-y-4">
    {#if project}
      <div class="space-y-2 space-x-1">
        <div class="float-right mt-1 sm:mt-2 md:mt-1">
          {#if migrationStatus !== ProjectMigrationStatus.Migrating}
              <Dropdown>
                <!-- svelte-ignore a11y-label-has-associated-control -->
                <label bind:this={getProjectDropdownTrigger} tabindex="-1" class="btn btn-sm md:btn-md btn-success">
                  {$t('project_page.get_project.label')}
                  <span class="i-mdi-dots-vertical text-2xl" />
                </label>
                <div slot="content" class="card w-[calc(100vw-1rem)] sm:max-w-[35rem]">
                  <div class="card-body max-sm:p-4">
                    <div class="prose">
                      <SvelteMarkdown
                        source={$t('project_page.get_project.instructions', {type: _project.type, code: data.code, name: _project.name})}>
                      </SvelteMarkdown>
                    </div>
                    <AdminContent>
                      <FormField label={$t('project_page.get_project.send_receive_url')}>
                        <div class="join">
                          <input
                            value={projectHgUrl}
                            class="input input-bordered join-item w-full focus:input-success"
                            readonly
                          />
                          <div
                            class="join-item tooltip-open"
                            class:tooltip={copiedToClipboard}
                            data-tip={$t('clipboard.copied')}
                          >
                            {#if copiedToClipboard}
                              <IconButton disabled icon="i-mdi-check" style="btn-outline btn-success" />
                            {:else}
                              <IconButton
                                loading={copyingToClipboard}
                                icon="i-mdi-content-copy"
                                style="btn-outline"
                                on:click={copyProjectUrlToClipboard}
                              />
                            {/if}
                          </div>
                        </div>
                      </FormField>
                    </AdminContent>
                  </div>
                </div>
              </Dropdown>
          {/if}
          {#if migrationStatus === ProjectMigrationStatus.PublicRedmine || migrationStatus === ProjectMigrationStatus.PrivateRedmine}
            <AdminContent>
              <Button on:click={migrateProject}>
                Migrate Project
                <Icon icon="i-mdi-source-branch-sync"/>
              </Button>
            </AdminContent>
          {/if}
        </div>
        <div class="text-3xl flex items-center gap-3 gap-y-0 flex-wrap">
          <span>{$t('project_page.project')}:</span>
          <span class="text-primary">
            <EditableText
              disabled={!canManage}
              value={project.name}
              validation={projectNameValidation}
              saveHandler={updateProjectName}
            />
          </span>
        </div>
        <BadgeList>
          <ProjectTypeBadge type={project.type}/>
          <Badge>
            <FormatRetentionPolicy policy={project.retentionPolicy}/>
          </Badge>
          {#if migrationStatus === ProjectMigrationStatus.Migrating}
            <Badge><span class="loading loading-spinner loading-xs"></span> Migrating</Badge>
          {:else}
            <Badge>{migrationStatusTable[migrationStatus]}</Badge>
          {/if}
        </BadgeList>
      </div>

      <div class="divider" />

      <p class="text-2xl mb-4">{$t('project_page.summary')}</p>

      <div class="space-y-2">
        <span class="text-lg">
          {$t('project_page.project_code')}:
          <span class="text-secondary">{project.code}</span>
        </span>
        <div class="text-lg">
          {$t('project_page.last_commit')}:
          <span class="text-secondary"><FormatDate date={project.lastCommit} /></span>
        </div>
        <div class="text-lg">{$t('project_page.description')}:</div>
        <span class="text-secondary">
          <EditableText
            value={project.description}
            disabled={!canManage}
            saveHandler={updateProjectDescription}
            placeholder={$t('project_page.add_description')}
            multiline
          />
        </span>
      </div>

      <div>
        <p class="text-2xl mb-4">
          {$t('project_page.members')}
        </p>

        <BadgeList>
          {#each project.users as member}
            {@const canManageMember = canManage && (member.user.id !== userId  || isAdmin(user))}
            <Dropdown disabled={!canManageMember}>
              <MemberBadge
                member={{ name: member.user.name, role: member.role }}
                canManage={canManageMember}
              />
              <ul slot="content" class="menu">
                <li>
                  <button on:click={() => changeMemberRole(member)}>
                    <span class="i-mdi-account-lock text-2xl" />
                    {$t('project_page.change_role')}
                  </button>
                </li>
                <li>
                  <button class="text-error" on:click={() => deleteProjectUser(member)}>
                    <TrashIcon />
                    {$t('project_page.remove_user')}
                  </button>
                </li>
              </ul>
            </Dropdown>
          {/each}
          {#if canManage}
            <AddProjectMember projectId={project.id} />
          {/if}

          <ChangeMemberRoleModal projectId={project.id} bind:this={changeMemberRoleModal} />

          <DeleteModal
            bind:this={removeUserModal}
            entityName={$t('project_page.remove_project_user_title')}
            isRemoveDialog
          >
            {$t('project_page.confirm_remove', {
              userName: userToDelete?.user.name ?? '',
            })}
          </DeleteModal>
        </BadgeList>
      </div>

      <div class="divider" />
      <div class="space-y-2">
        <p class="text-2xl mb-4 flex gap-4 items-baseline">
          {$t('project_page.history')}
          <a class="btn btn-sm btn-outline btn-info" href="/hg/{project.code}" target="_blank">
            {$t('project_page.hg.open_in_hgweb')}<span class="i-mdi-open-in-new text-2xl" />
          </a>
        </p>

        <!-- <HgWeb code={project.code} /> -->
        <div class="max-h-[75vh] overflow-auto border-b border-base-200">
          <HgLogView json={project.changesets} />
        </div>
      </div>

      {#if canManage}
        <div class="divider" />

        <MoreSettings>
          <button class="btn btn-error" on:click={softDeleteProject}>
            {$t('delete_project_modal.submit')}<TrashIcon />
          </button>
          <AdminContent>
              <button class="btn btn-accent" on:click={() => resetProject()}>
                {$t('project_page.reset_project_modal.title')}<CircleArrowIcon />
              </button>
            <ResetProjectModal bind:this={resetProjectModal} i18nScope="project_page.reset_project_modal" />
          </AdminContent>
        </MoreSettings>
      {/if}

      <ConfirmDeleteModal bind:this={deleteProjectModal} i18nScope="delete_project_modal" />
    {:else}
      <div class="text-center text-error">
        {$t('project_page.not_found', { code: data.code })}
      </div>
    {/if}
  </div>
</Page>
