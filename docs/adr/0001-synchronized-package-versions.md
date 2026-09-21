# ADR-0001: Synchronized (lockstep) package versions

- **Status:** Accepted
- **Date:** 2026-09-21
- **Deciders:** maintainer + AI assistant (decision delegated to assistant, approved by maintainer)

## Context

The repo ships two NuGet packages from one codebase:

- `TropinAlexey.FluentMigrator.IdempotentExtensions` (core, all providers)
- `TropinAlexey.FluentMigrator.IdempotentExtensions.SqlServer` (SQL Server-only helpers)

They were versioned independently: each bumped only when its own code changed
(core reached `1.7.0` while SqlServer stayed at `1.5.1`). This caused three problems:

1. **User confusion** — mismatched numbers (`1.7.0` vs `1.5.1`) look like the
   SqlServer package is abandoned and raise unanswerable-at-a-glance
   compatibility questions.
2. **Stale core DLL inside the SqlServer package** — the SqlServer package builds
   via `ProjectReference`, so its nupkg embeds the core DLL **as of its own build
   time**. A user installing only `SqlServer 1.5.1` silently gets a 1.5.1-era core
   library without all newer methods. Only republishing the SqlServer package
   fixes that.
3. **Meaningless tags** — release tags (`v*.*.*`) trigger the publish workflow for
   both packages, but with divergent versions a tag no longer answers
   "which version of everything is this?".

## Decision

Both packages **always share one version number** (lockstep), bumped together on
every release, even if one package has no code changes. The release tag
`vX.Y.Z` means "both packages are version X.Y.Z".

- A release with no SqlServer code changes gets a `no changes, version
  alignment` note in the changelog instead of silence.
- A SqlServer-only hotfix still bumps both packages (keeps the invariant simple;
  no exceptions).

This mirrors how FluentMigrator itself versions all of its runner packages.

## Consequences

- Users always take the same version of both packages; README badges match.
- The SqlServer nupkg always embeds the latest core DLL.
- Occasional no-change republishes of one package (accepted trade-off; NuGet
  `--skip-duplicate` in the publish workflow makes re-runs safe).
- Release process: bump `<Version>` in **both** csproj files → commit → tag
  `vX.Y.Z` → push tag. The publish workflow packs and pushes both; already
  published versions are skipped as duplicates.

## Baseline

Alignment established at `1.7.0` (SqlServer `1.5.1` → `1.7.0`, no code changes),
published via tag `v1.7.1`.
