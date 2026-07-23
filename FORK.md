# Staging fork

This is myieye/languageforge-lexbox — Tim's staging fork of sillsdev/languageforge-lexbox.
AI agents (Claude, Devin, CodeRabbit) churn on PRs here so the noise never reaches the real repo.
Local remote names: `origin` = sillsdev, `sandbox` = this fork.

## Rules

- PRs here target `develop` and are **never merged**. When a PR is polished, its branch is
  pushed to sillsdev and a real PR is opened there ("promote"); the staging PR is closed.
  Branch protection on `develop` blocks accidental merges.
- `develop` here = upstream `develop` plus `[fork-only]` commits. Fork-only commits touch
  **only files that don't exist upstream** (this file, `.github/workflows/fork-*.yaml`,
  `.coderabbit.yaml`), so syncing from upstream never conflicts.
- **Cut feature branches from upstream `develop`, never from this fork's `develop`.**
  Otherwise the fork-only commits ride along at promote time. Before promoting, verify:
  `git cat-file -e HEAD:FORK.md` must FAIL on the branch.
- Never rebase or force-push this fork's `develop` — it would corrupt the diffs of open
  staging PRs.

## CI

Upstream workflows are disabled through the GitHub API (zero diff, so zero merge conflicts),
not by editing them. Only the `fork-*.yaml` workflows run: the build/test subset of upstream
CI (FwLite, API, UI) on Linux runners, with no publishing, deploys, k8s, or secrets.

After a develop sync, workflow files newly added upstream arrive **enabled** — re-disable
everything that isn't `fork-*`:

```bash
gh api repos/myieye/languageforge-lexbox/actions/workflows --paginate \
  -q '.workflows[] | select((.path | startswith(".github/workflows/fork-")) | not) | select(.state == "active") | .id' |
  xargs -I{} gh api -X PUT repos/myieye/languageforge-lexbox/actions/workflows/{}/disable
```

## Review loop

1. Push the branch to this fork and open a PR against `develop` here.
2. Run the Devin + CI loop on the staging PR (make-devin-and-ci-green with the PR URL)
   until both are clean at the same HEAD.
3. Only then spend CodeRabbit quota: comment `@coderabbitai review` on the PR
   (auto-review is off via `.coderabbit.yaml` to protect the free-tier quota).
4. Promote: push the branch to sillsdev, open the real PR there, close the staging PR
   with a link.

## Syncing develop

```bash
git fetch origin develop
git push sandbox origin/develop:develop
```

The push fast-forwards only until the next `[fork-only]` commit lands here. After that,
merge instead: check out `sandbox/develop` (detached), `git merge origin/develop`
(always clean — fork-only files don't exist upstream), push, then re-disable new
workflows (see CI above). Tim's local `fork-sync` skill does all of this.
