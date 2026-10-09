import {expect, test} from '@playwright/test';
import {DemoProjectPage} from './demo-project.page';

test('add at end, baseline read right after skeletons resolve', async ({page}) => {
  const p = new DemoProjectPage(page);
  await page.goto('/testing/project-view');
  await page.waitForFunction(() => window.__PLAYWRIGHT_UTILS__?.demoApi);
  await expect.poll(() => p.entriesList.entryRows.count()).toBeGreaterThan(5);
  await expect(p.entriesList.skeletons).toHaveCount(0);
  const old = await p.entriesList.getScrollHeight();
  const {headword} = await p.api.getLastEntry();
  await p.api.createEntryWithHeadword(headword + 'z-inserted');
  await expect.poll(() => p.entriesList.getScrollHeight(), {timeout: 5000}).toBeGreaterThan(old);
});
