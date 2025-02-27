﻿<script lang="ts">
import t from '$lib/i18n';
import {getProjectTypeI18nKey, ProjectTypeIcon} from '$lib/components/ProjectType';
import type {AdminSearchParams, Project, User} from './+page';
import {_deleteProject} from '$lib/gql/mutations';
import {DialogResponse} from '$lib/components/modals';
import {notifyWarning} from '$lib/notify';
import ConfirmDeleteModal from '$lib/components/modals/ConfirmDeleteModal.svelte';
import Dropdown from '$lib/components/Dropdown.svelte';
import TrashIcon from '$lib/icons/TrashIcon.svelte';
import FormatDate from '$lib/components/FormatDate.svelte';
import ProjectTypeSelect from '$lib/forms/ProjectTypeSelect.svelte';
import FormField from '$lib/forms/FormField.svelte';
import {ActiveFilter, FilterBar} from '$lib/components/FilterBar';
import Badge from '$lib/components/Badges/Badge.svelte';
import {getSearchParams, queryParam} from '$lib/util/query-params';
import type {ProjectType} from '$lib/gql/types';
import AuthenticatedUserIcon from '$lib/icons/AuthenticatedUserIcon.svelte';
import IconButton from '$lib/components/IconButton.svelte';
import {bubbleFocusOnDestroy} from '$lib/util/focus';
import Button from '$lib/forms/Button.svelte';
export let projects: Project[];
export let users: User[];

export function setUserFilter(email: string): void {
    $queryParams.userEmail = email;
}

export let defaultFilterLimit: number;
const {queryParams, defaultQueryParams} = getSearchParams<AdminSearchParams>({
    showDeletedProjects: queryParam.boolean<boolean>(false),
    projectType: queryParam.string<ProjectType | undefined>(undefined),
    userEmail: queryParam.string(undefined),
    projectSearch: queryParam.string<string>(''),
});
function getFilteredUser(userEmail: string | undefined): User | undefined {
    if (!userEmail) {
        return undefined;
    }
    if (filteredUser?.email == userEmail) {
        return filteredUser;
    }
    return users.find(user => user.email === userEmail);
}

let hasActiveProjectFilter: boolean;
$: projectSearchLower = $queryParams.projectSearch.toLocaleLowerCase();
let projectFilterLimit = defaultFilterLimit;
$: projectLimit = hasActiveProjectFilter ? projectFilterLimit : 10;
$: filteredUser = getFilteredUser($queryParams.userEmail);
$: userProjects = filteredUser?.projects.map(({projectId}) => projects.find(p => p.id === projectId) as Project);
$: filteredProjects = (userProjects ?? projects).filter(
    (p) =>
        (!$queryParams.projectSearch ||
            p.name.toLocaleLowerCase().includes(projectSearchLower) ||
            p.code.toLocaleLowerCase().includes(projectSearchLower)) &&
        (!$queryParams.projectType || p.type === $queryParams.projectType));
$: shownProjects = filteredProjects.slice(0, projectLimit);
$: {
    // Reset limit if search is changed
    hasActiveProjectFilter;
    projectFilterLimit = defaultFilterLimit;
}

let deleteProjectModal: ConfirmDeleteModal;
async function softDeleteProject(project: Project): Promise<void> {
    const result = await deleteProjectModal.open(project.name, async () => {
        const {error} = await _deleteProject(project.id);
        return error?.message;
    });
    if (result.response === DialogResponse.Submit) {
        notifyWarning($t('delete_project_modal.success', {name: project.name, code: project.code}));
    }
}
</script>
<ConfirmDeleteModal bind:this={deleteProjectModal} i18nScope="delete_project_modal"/>
<div>
    <div class="flex justify-between items-center">
        <span class="text-xl flex gap-4">
          {$t('admin_dashboard.project_table_title')}
            <Badge>
            <span class="inline-flex gap-2">
              {hasActiveProjectFilter ? filteredProjects.length : shownProjects.length}
                <span>/</span>
                {projects.length}
            </span>
          </Badge>
        </span>
        <a href="/project/create" class="btn btn-sm btn-success">
          <span class="max-sm:hidden">
            {$t('project.create.title')}
          </span>
            <span class="i-mdi-plus text-2xl"/>
        </a>
    </div>

    <FilterBar bind:search={$queryParams.projectSearch} filters={queryParams} defaultValues={defaultQueryParams}
               bind:hasActiveFilter={hasActiveProjectFilter} autofocus>
        <svelte:fragment slot="activeFilters" let:activeFilters>
            {#each activeFilters as filter}
                {#if filter.key === 'projectType'}
                    <ActiveFilter {filter}>
                        <ProjectTypeIcon type={filter.value}/>
                    </ActiveFilter>
                {:else if filter.key === 'showDeletedProjects'}
                    <ActiveFilter {filter}>
                        <TrashIcon color="text-error"/>
                        {$t('admin_dashboard.project_filter.show_deleted')}
                    </ActiveFilter>
                {:else if filter.key === 'userEmail' && filter.value}
                    <ActiveFilter {filter}>
                        <AuthenticatedUserIcon/>
                        {filter.value}
                    </ActiveFilter>
                {/if}
            {/each}
        </svelte:fragment>
        <svelte:fragment slot="filters">
            <h2 class="card-title">Project filters</h2>
            <FormField label={$t('admin_dashboard.project_filter.project_member')}>
                {#if $queryParams.userEmail}
                    <div class="join" use:bubbleFocusOnDestroy>
                        <input class="input input-bordered join-item flex-grow"
                               placeholder={$t('admin_dashboard.project_filter.all_users')} readonly
                               value={$queryParams.userEmail}/>
                        <div class="join-item isolate">
                            <IconButton
                                    icon="i-mdi-close"
                                    style="btn-outline"
                                    on:click={() => $queryParams.userEmail = undefined}
                            />
                        </div>
                    </div>
                {:else}
                    <div class="alert alert-info gap-2">
                        <span class="i-mdi-info-outline text-xl"></span>
                        <div class="flex_ items-center gap-2">
                            <span class="mr-1">{$t('admin_dashboard.project_filter.select_user_from_table')}</span>
                            <span class="btn btn-sm btn-square pointer-events-none">
                    <span class="i-mdi-dots-vertical"></span>
                  </span>
                            <span class="i-mdi-chevron-right"></span>
                            <span class="btn btn-sm pointer-events-none normal-case font-normal">
                    <span class="i-mdi-filter-outline mr-1"></span>
                                {$t('admin_dashboard.filter_projects')}
                  </span>
                        </div>
                    </div>
                {/if}
            </FormField>
            <div class="form-control">
                <ProjectTypeSelect bind:value={$queryParams.projectType}
                                   undefinedOptionLabel={$t('project_type.any')}/>
            </div>
            <div class="form-control">
                <label class="cursor-pointer label gap-4">
                    <span class="label-text">{$t('admin_dashboard.show_delete_projects')}</span>
                    <input
                            bind:checked={$queryParams.showDeletedProjects}
                            type="checkbox"
                            class="toggle toggle-error"
                    />
                </label>
            </div>
        </svelte:fragment>
    </FilterBar>

    <div class="divider"/>
    <div class="overflow-x-auto">
        <table class="table table-lg">
            <thead>
            <tr class="bg-base-200">
                <th>{$t('admin_dashboard.column_name')}</th>
                <th>{$t('admin_dashboard.column_code')}</th>
                <th>{$t('admin_dashboard.column_users')}</th>
                <th>
                    {$t('admin_dashboard.column_last_change')}
                    <span class="i-mdi-sort-ascending text-xl align-[-5px] ml-2"/>
                </th>
                <th>{$t('admin_dashboard.column_type')}</th>
                <th/>
            </tr>
            </thead>
            <tbody>
            {#each shownProjects as project}
                <tr>
                    <td>
                        {#if project.deletedDate}
                    <span class="flex gap-2 text-error items-center">
                      {project.name}
                        <TrashIcon pale/>
                    </span>
                        {:else}
                            <a class="link" href={`/project/${project.code}`}>
                                {project.name}
                            </a>
                        {/if}
                    </td>
                    <td>{project.code}</td>
                    <td>{project.userCount}</td>
                    <td>
                        {#if project.deletedDate}
                    <span class="text-error">
                      <FormatDate date={project.deletedDate}/>
                    </span>
                        {:else}
                            <FormatDate date={project.lastCommit}/>
                        {/if}
                    </td>
                    <td>
                  <span class="tooltip align-bottom" data-tip={$t(getProjectTypeI18nKey(project.type))}>
                    <ProjectTypeIcon type={project.type}/>
                  </span>
                    </td>
                    <td class="p-0">
                        {#if !project.deletedDate}
                            <Dropdown>
                                <!-- svelte-ignore a11y-label-has-associated-control -->
                                <label tabindex="-1" class="btn btn-ghost btn-square">
                                    <span class="i-mdi-dots-vertical text-lg"/>
                                </label>
                                <ul slot="content" class="menu">
                                    <li>
                                        <button class="text-error whitespace-nowrap"
                                                on:click={() => softDeleteProject(project)}>
                                            <TrashIcon/>
                                            {$t('delete_project_modal.submit')}
                                        </button>
                                    </li>
                                </ul>
                            </Dropdown>
                        {/if}
                    </td>
                </tr>
            {/each}
            </tbody>
        </table>
        {#if hasActiveProjectFilter && projectFilterLimit < filteredProjects.length}
            <Button class="float-right mt-2" on:click={() => (projectFilterLimit = Infinity)}>
                {$t('admin_dashboard.load_more')}
            </Button>
        {/if}
    </div>
</div>
