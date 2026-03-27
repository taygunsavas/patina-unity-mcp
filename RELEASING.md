# Release Checklist

This document covers the steps a maintainer must complete to publish a new Patina release. Follow the steps in order; do not skip validation gates.

## Prerequisites

### Repository secrets and variables

The release workflow reads the following GitHub Actions secrets and variables. Configure them in repository settings before the first registry-backed release.

| Name | Type | Required | Purpose |
|------|------|----------|---------|
| `NPM_TOKEN` | Secret | Optional | Publishes the tarball to the npm-compatible registry. Workflow skips npm publication if absent. |
| `OPENUPM_TOKEN` | Secret | Optional | Publishes the tarball to the OpenUPM-compatible registry. Workflow skips OpenUPM publication if absent. |
| `NPM_REGISTRY_URL` | Variable | Optional | Overrides the npm target registry. Defaults to `https://registry.npmjs.org/` if absent. |
| `OPENUPM_REGISTRY_URL` | Variable | Required for OpenUPM | OpenUPM registry endpoint. Workflow skips OpenUPM publication if absent. |

At least one of `NPM_TOKEN` or `OPENUPM_TOKEN` must be configured for any registry publication to occur. The `github-release` job runs regardless.

### Version sync

Both manifests must carry the same semantic version before tagging:

- `rust-server/Cargo.toml` — `version = "X.Y.Z"`
- `unity-package/package.json` — `"version": "X.Y.Z"`

The `verify` job in the release workflow enforces this automatically via `scripts/assert-version-sync.ps1`. A mismatch fails the workflow before any binary is built.

### Unity validation baseline

Use **Unity 6000.3.5f2 or newer** for release validation. Package Manager signed-package behavior was stabilized at that patch level. Earlier 6000.3 patch releases may emit signature warnings on first import from a scoped registry.

## Release Steps

### 1. Prepare the version bump

1. Update `rust-server/Cargo.toml` and `unity-package/package.json` to the new version.
2. Run `pwsh -File scripts/assert-version-sync.ps1` locally to confirm both files agree.
3. Open a pull request, pass CI, and merge to `main`.

### 2. Trigger the release workflow

Go to **Actions → release → Run workflow** and fill in:

| Input | Value |
|-------|-------|
| `version` | `vX.Y.Z` (must match the version in both manifests after the `v` prefix) |
| `publish_npm` | `true` if `NPM_TOKEN` is configured |
| `publish_openupm` | `true` if both `OPENUPM_TOKEN` and `OPENUPM_REGISTRY_URL` are configured |
| `create_github_release` | `true` for production releases |

### 3. Monitor workflow jobs

The workflow runs the following job groups:

1. **verify** — asserts version sync and resolves the bare version string.
2. **build-binaries** — builds Windows (x86_64), Linux (x86_64), macOS (x86_64 and arm64) binaries in parallel.
3. **package-upm** — downloads all four binaries, stages the full Unity package via `scripts/stage-upm-package.ps1`, runs `npm pack`, and uploads the `.tgz` tarball and `.zip` bundle.
4. **publish-npm** and **publish-openupm** — run in parallel once `package-upm` completes; each publishes the tarball to its configured registry and skips gracefully if credentials are absent.
5. **github-release** — runs once both `build-binaries` and `package-upm` succeed; creates a GitHub Release with the tarball, zip bundle, and all four raw binaries attached.

If any job fails, review the step logs before re-triggering.

### 4. Validate the published package

After publication:

1. Create a fresh Unity project on **Unity 6000.3.5f2+**.
2. Add the Patina scoped registry in Package Manager settings.
3. Install `com.taygunsavas.patina-unity-mcp` at the released version.
4. Confirm all four platform binaries are present under `Plugins/`.
5. Open **Window > Patina Unity MCP** and run **One-Click Setup**. Confirm the server starts and the expected tool count is reported.
6. Run a quick smoke test: log a message, get the hierarchy, create a GameObject.
7. Record any signature warnings, import errors, or unexpected behavior as issues against this repository.

### 5. After the first release

- Update this file with any quirks discovered during the validation pass.
- Add or correct registry endpoint examples in `README.md` once the scoped registry URL is stable.
- Record secret and variable values (not the values themselves) that were required or changed.
