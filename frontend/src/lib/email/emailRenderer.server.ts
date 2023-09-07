import { parse, walk } from 'svelte/compiler';

import type { ComponentType } from 'svelte';
import type { TemplateNode } from 'svelte/types/compiler/interfaces';
import mjml2html from 'mjml';
import type {EmailTemplateProps} from '../../routes/email/emails';

type RenderResult = { head: string, html: string, css: string };
export type RenderEmailResult = { subject: string, html: string };

function getSubject(head: string): string {
  // using the Subject.svelte component a title should have been placed in the head.
  // we need to parse the head and find the title and extract the text
  const headAst = parse(head, { filename: 'file.html' });
  let subject: string | undefined;
  walk(headAst.html, {
    enter(node: TemplateNode, ..._) {
      if (node.type === 'Element' && node.name === 'title') {
        subject = node.children?.[0].data as string;
      }
    }
  } as Parameters<typeof walk>[1]);
  if (!subject) throw new Error('subject not found');
  return subject;
}

export function render(emailComponent: ComponentType, props: Omit<EmailTemplateProps, 'template'>): RenderEmailResult {
  // eslint-disable-next-line
  const result: RenderResult = (emailComponent as any).render(props) as RenderResult;
  return {
    html: mjml2html(result.html, { validationLevel: 'strict' }).html,
    subject: getSubject(result.head)
  };
}
