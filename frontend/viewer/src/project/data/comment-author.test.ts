import {describe, expect, it} from 'vitest';
import {isOwnComment} from './comment-author';

describe('isOwnComment', () => {
  const author = {id: 'lexbox-user', ownIds: ['lexbox-user', 'anonymous:device']};

  it('is true for any of the author ids', () => {
    expect(isOwnComment(author, 'lexbox-user')).toBe(true);
    expect(isOwnComment(author, 'anonymous:device')).toBe(true);
  });

  it('is false for someone else', () => {
    expect(isOwnComment(author, 'other-user')).toBe(false);
  });

  it('is false while the author is unknown or the comment has no author', () => {
    expect(isOwnComment(undefined, 'lexbox-user')).toBe(false);
    expect(isOwnComment(author, undefined)).toBe(false);
  });
});
