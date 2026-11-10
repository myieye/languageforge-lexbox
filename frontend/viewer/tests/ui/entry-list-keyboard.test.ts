import {expect, test, type Locator, type Page} from '@playwright/test';
import {DemoProjectPage} from './demo-project.page';

function row(page: Page, index: number): Locator {
  return page.locator(`[data-entry-index="${index}"]`);
}

async function focusRowWithKeyboard(page: Page, projectPage: DemoProjectPage, index: number): Promise<void> {
  await projectPage.entriesList.searchInput.click();
  for (let i = 0; i < 25; i++) {
    await page.keyboard.press('Tab');
    if (await row(page, index).evaluate(el => el === document.activeElement).catch(() => false)) return;
  }
  throw new Error(`Tab never reached row ${index}`);
}

test.describe('Entry list keyboard navigation', () => {
  let projectPage: DemoProjectPage;

  test.beforeEach(async ({page}) => {
    projectPage = new DemoProjectPage(page);
    await projectPage.goto();
  });

  test('arrow keys move focus and selection, and the editor follows', async ({page}) => {
    await focusRowWithKeyboard(page, projectPage, 0);

    for (const index of [1, 2, 3]) {
      await page.keyboard.press('ArrowDown');
      await expect(row(page, index)).toBeFocused();
      await expect(row(page, index)).toHaveAttribute('aria-selected', 'true');
    }
    const {headword} = await projectPage.api.getEntryAtIndex(3);
    await expect(await projectPage.entryView.getLexemeInput()).toHaveValue(headword);

    await page.keyboard.press('ArrowUp');
    await expect(row(page, 2)).toBeFocused();
    await expect(row(page, 2)).toHaveAttribute('aria-selected', 'true');
  });

  test('Home and End jump to the first and last entry', async ({page}) => {
    await focusRowWithKeyboard(page, projectPage, 0);
    const last = (await projectPage.api.countEntries()) - 1;

    await page.keyboard.press('End');
    await expect(row(page, last)).toBeFocused();
    await expect(row(page, last)).toHaveAttribute('aria-selected', 'true');

    await page.keyboard.press('Home');
    await expect(row(page, 0)).toBeFocused();
  });

  test('Enter on the selected row moves focus into the editor', async ({page}) => {
    await focusRowWithKeyboard(page, projectPage, 0);
    await page.keyboard.press('ArrowDown');
    await expect(row(page, 1)).toBeFocused();
    const lexemeInput = await projectPage.entryView.getLexemeInput();
    await expect(lexemeInput).toHaveValue((await projectPage.api.getEntryAtIndex(1)).headword);
    await expect(lexemeInput).not.toBeFocused();

    await page.keyboard.press('Enter');
    await expect(lexemeInput).toBeFocused();
  });

  test('clicking a row still focuses the editor', async ({page}) => {
    await row(page, 2).click();
    await expect(await projectPage.entryView.getLexemeInput()).toBeFocused();
  });
});
