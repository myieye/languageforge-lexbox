import {useProjectContext} from '$project/project-context.svelte';
import type {ICommentAuthor} from '$lib/dotnet-types/generated-types/FwLiteShared/Services/ICommentAuthor';

const commentAuthorSymbol = Symbol.for('fw-lite-comment-author');
export function useCommentAuthor() {
  const projectContext = useProjectContext();
  return projectContext.getOrAddAsync<ICommentAuthor | undefined>(commentAuthorSymbol, undefined, api => api.getCommentAuthor());
}

export function isOwnComment(author: ICommentAuthor | undefined, commentAuthorId: string | undefined): boolean {
  return !!commentAuthorId && !!author?.ownIds.includes(commentAuthorId);
}
