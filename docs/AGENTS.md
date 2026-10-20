# Docs site

Docusaurus site with three sections: `fw-lite/` (FieldWorks Lite users), `lexbox/` (lexbox.org and Send/Receive users) and `technical/` (developers). `adr/` and `research/` are not published. Check a change with `pnpm install && pnpm build` in this folder; broken links and anchors fail the build. `pnpm typecheck` covers the React components.

## Which page to update

| You changed | Update |
|---|---|
| FW Lite UI (`frontend/viewer/`) | the `fw-lite/` page that names it; Sync dialog changes also `src/components/SyncExplainer/syncScenarios.ts` |
| When sync runs or which edit wins (`backend/FwLite/`, `backend/FwHeadless/`) | `fw-lite/how-sync-works.mdx`, `syncScenarios.ts`, `lexbox/fw-lite.md`, `technical/sync/` |
| lexbox.org UI, roles, invites, orgs, project requests (`frontend/src/`, `backend/LexBoxApi/`, `backend/LexCore/`) | the `lexbox/` page that names it |
| Workflows, deployment, dev setup | `technical/ci-cd.md`, `technical/development/` |

`git grep` the old label or behavior under `docs/` to find every page that mentions it.

## Writing rules

- Name buttons, menus and tabs exactly as the app shows them: FW Lite text is a `msgid` in `frontend/viewer/src/locales/en.po`, lexbox.org text a value in `frontend/src/lib/i18n/locales/en.json`. `fw-lite/` says FieldWorks Classic (FW Lite's name for the desktop app); `lexbox/` says FieldWorks (lexbox.org's name). No doc-only names.
- `fw-lite/` and `lexbox/` are for non-technical readers: plain words, short sentences, no em-dashes. CRDT, Harmony, Mercurial and FwHeadless belong in `technical/` and the "Under the hood" box.
- Say who can see a feature when not everyone can (role, beta flag, admin only).
- No real project codes, names, people or project data, in text or screenshots. Example project codes use the private-use language code `qaa`. Screenshots come from FW Lite's example project or a local dev Lexbox.
- `technical/` explains how the system works; `AGENTS.md` files keep agent procedure (commands, gotchas) and link here. Don't copy content between them.
- The sync explainer's limits are in the `syncScenarios.ts` header.

## Links must keep working

Support replies link to pages, headings and explainer questions (`/fw-lite/how-sync-works#not-seen`: scenario ids in `syncScenarios.ts` are URLs). The apps are meant to link to `/fw-lite/` (FW Lite help), `/lexbox/` (lexbox.org help), `/fw-lite/how-sync-works` (Sync help) and `/lexbox/fw-lite` (turning on FW Lite).

- Moving or renaming a page: add `@docusaurus/plugin-client-redirects` with a redirect from the old path.
- Rewording a heading changes its anchor. Keep the old one with `## New wording {/* #old-anchor */}` (`{#id}` doesn't build here because of `future.v4`).
- Don't rename a scenario id.

Decisions behind the site: `adr/0002-docs-site.md`.
