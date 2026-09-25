import {expect, test} from '@playwright/test';

import {DemoProjectPage} from './demo-project.page';

// Regression: the dashboard stats resource is cached per-project, but its
// entry-change subscription used to be tied to the first DashboardView mount.
// Navigating away tore the subscription down while the cached resource lived on,
// so edits made elsewhere never refreshed the counts on a later visit.
test.describe('Dashboard stats stay live across navigation', () => {

  test('total entries count refetches after an entry is deleted in another view', async ({page}) => {
    const projectPage = new DemoProjectPage(page);
    await projectPage.goto();

    const dashboardNav = page.getByRole('button', {name: 'Dashboard'});
    const browseNav = page.getByRole('button', {name: 'Browse'});
    const totalEntries = page.locator('[class*="bg-primary/5"] [data-slot="card-title"]');

    const readTotal = async () => parseInt((await totalEntries.innerText()).replace(/\D/g, ''), 10);

    // Open the dashboard once so its stats resource is created and subscribed.
    await dashboardNav.click();
    await expect(totalEntries).toHaveText(/\d/);
    const initialCount = await projectPage.api.countEntries();
    expect(await readTotal()).toBe(initialCount);

    // Leave the dashboard (unmounts DashboardView), then mutate data elsewhere.
    await browseNav.click();
    await expect(totalEntries).toBeHidden();
    await projectPage.api.deleteEntry(await projectPage.api.getEntryIdAtIndex(0));

    // Returning must show the fresh count. Before the fix this stayed at initialCount
    // because the subscription died with the first DashboardView mount.
    await dashboardNav.click();
    await expect.poll(readTotal).toBe(initialCount - 1);
  });
});
