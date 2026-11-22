import {expect, test, type Page} from '@playwright/test';

import {DemoProjectPage} from './demo-project.page';

async function setPageHidden(page: Page, hidden: boolean, via: 'visibilitychange' | 'pagehide'): Promise<void> {
  await page.evaluate(({hidden, via}) => {
    Object.defineProperty(document, 'visibilityState', {value: hidden ? 'hidden' : 'visible', configurable: true});
    Object.defineProperty(document, 'hidden', {value: hidden, configurable: true});
    if (via === 'visibilitychange') document.dispatchEvent(new Event('visibilitychange'));
    else window.dispatchEvent(new PageTransitionEvent(hidden ? 'pagehide' : 'pageshow'));
  }, {hidden, via});
}

test.describe('Pending edits are saved when the app is hidden', () => {
  for (const via of ['visibilitychange', 'pagehide'] as const) {
    test(`unblurred lexeme form is saved on ${via}`, async ({page}) => {
      const projectPage = new DemoProjectPage(page);
      await projectPage.goto();
      const {entryId, headword} = await projectPage.api.getEntryAtIndex(10);
      await projectPage.selectEntryByFilter(headword);

      const input = await projectPage.entryView.getLexemeInput();
      const newLexeme = `${headword}-hidden-${via}`;
      await input.fill(newLexeme);
      await expect(input).toBeFocused();
      await page.waitForTimeout(300);
      expect(await projectPage.api.getEntryLexeme(entryId)).toBe(headword);

      await setPageHidden(page, true, via);

      await expect.poll(() => projectPage.api.getEntryLexeme(entryId)).toBe(newLexeme);
      await expect(input).toBeFocused();

      await setPageHidden(page, false, via);
      await input.fill(`${newLexeme}-again`);
      await setPageHidden(page, true, via);
      await expect.poll(() => projectPage.api.getEntryLexeme(entryId)).toBe(`${newLexeme}-again`);
    });
  }

  test('unblurred rich-text definition is saved on visibilitychange', async ({page}) => {
    const projectPage = new DemoProjectPage(page);
    await projectPage.goto();
    const {entryId, headword} = await projectPage.api.getEntryWithEnglishGloss();
    await projectPage.selectEntryByFilter(headword);

    const definitionField = page.locator('[data-field-id="definition"]').first();
    const editor = definitionField.locator('[contenteditable="true"]').first();
    await editor.click();
    await page.keyboard.press('ControlOrMeta+a');
    const newDefinition = 'defined-while-hidden';
    await page.keyboard.type(newDefinition);

    function getDefinition(): Promise<(string | undefined)[]> {
      return page.evaluate(async (id) => {
        const entry = await window.__PLAYWRIGHT_UTILS__.demoApi.getEntry(id);
        return Object.values(entry?.senses[0]?.definition ?? {})
          .map(richString => richString?.spans.map(span => span.text).join(''));
      }, entryId);
    }
    await page.waitForTimeout(300);
    expect(await getDefinition()).not.toContain(newDefinition);

    await setPageHidden(page, true, 'visibilitychange');

    await expect.poll(getDefinition).toContain(newDefinition);
  });
});
