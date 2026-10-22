---
title: Technical documentation
sidebar_position: 1
---

How FieldWorks Lite and Lexbox work under the hood, and how they are built and deployed.
For using the apps, see the [FieldWorks Lite](/fw-lite/) and [Lexbox](/lexbox/) guides.

## Architecture

- [System overview](/technical/architecture/overview): the deployed services, tech stack
  and repo layout.
- [Integrations](/technical/architecture/integrations): how FieldWorks Classic, FieldWorks
  Lite, FwHeadless and Platform.Bible connect to Lexbox, plus its APIs, Crowdin and
  observability.

## Sync

- [The sync chain](/technical/sync/): the four hops between FieldWorks Lite and FieldWorks
  Classic (FLEx), and what triggers each one.
- [CRDT sync](/technical/sync/crdt): how FieldWorks Lite syncs with Lexbox.
- [The FwHeadless merge](/technical/sync/fwheadless-merge): how the FieldWorks Lite and
  FieldWorks Classic copies are merged on the server.

## Development

- [Local development](/technical/development/): run the stack locally, with setup guides
  for [Windows](/technical/development/setup-windows),
  [Linux](/technical/development/setup-linux) and
  [macOS](/technical/development/setup-macos).
- [CI/CD](/technical/ci-cd): workflows, releases and deployment.

Design decisions are recorded as ADRs in
[`docs/adr/`](https://github.com/sillsdev/languageforge-lexbox/tree/develop/docs/adr) in
the repo.
