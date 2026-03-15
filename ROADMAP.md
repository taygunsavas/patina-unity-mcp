# Patina Roadmap

This is Patina's public roadmap for users, contributors, and maintainers.

Public, GitHub-facing roadmap updates should land here through normal pull requests so changes stay versioned with the codebase. Local AI or maintainer planning notes can continue to live outside the public repository surface.

## Current State

- Phase 1 is the current shipped baseline: console logging, hierarchy inspection, and GameObject creation.
- Patina is maintained as a source-first repository with artifact-first distribution.
- Registry-backed Unity package delivery is the primary install path.
- GitHub Releases remain a secondary release surface for artifacts and notes.
- The local runtime override is a contributor workflow, not an end-user installation mode.

## Near-Term Roadmap

### Phase 1.1 - Artifact Release Hardening

- Run the release workflow end to end with production-like registry credentials.
- Verify that the published Unity package installs cleanly from scoped registries into a fresh Unity project.
- Confirm packaged platform binaries import correctly across supported platforms.
- Validate whether binary-specific `.meta` files are still needed after real registry-backed install cycles.

### Phase 1.2 - Contributor UX Hardening

- Validate Unity window copy and visibility rules for the contributor runtime override.
- Test source-based iteration with a local package checkout and a published dev runtime.
- Clarify the preferred contributor loop for package-only edits versus package-plus-Rust changes.

### Phase 1.3 - Distribution Operations

- Publish stable registry endpoints and installation examples.
- Add a maintainer release checklist covering required secrets, variables, and release steps.
- Prepare Asset Store submission notes without making them the primary release path.

## Longer-Term Roadmap

### Phase 2 - Product Coverage

- Expand tool coverage into scene management, asset operations, and component editing.
- Preserve the install contract: the packaged runtime ships inside the published Unity package artifact.

### Phase 3 - Product Hardening

- Add repository-owned tests for Rust bridge behavior.
- Increase Unity-side validation where practical.
- Improve setup diagnostics and host integration observability.
- Keep public claims aligned with shipped behavior and supported release channels.

## How To Use This Roadmap

- Treat this file as the canonical public roadmap.
- Use issues and pull requests to discuss specific roadmap items.
- Use release notes to describe what has shipped, and this file to describe what is planned next.
