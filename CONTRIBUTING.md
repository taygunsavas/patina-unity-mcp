# Contributing

Thanks for helping improve Patina. This repository is maintained as an open-source project, so clear issue reports, focused pull requests, and reproducible validation are all important.

## Repository Model

Patina keeps source and release artifacts separate:

- `main` contains the source for `unity-package/` and `rust-server/`.
- CI assembles the distributable Unity package artifact for registry publication.
- End users install from registry-backed Unity Package Manager channels, not from a Git URL to this repository.

## Before You Start

- Read `README.md` for the product overview and install model.
- Check existing issues before starting larger work.
- If you plan to change behavior, release flow, or public tooling contracts, open or comment on an issue first so the approach can be aligned early.
- For support questions or setup trouble, prefer the GitHub support-oriented issue form instead of opening an unfocused bug.

## Contributor Runtime Override

Contributors have an optional local runtime override for source-based iteration only:

- Packaged runtime: the binary already bundled into the published Unity package.
- Local runtime override: a contributor-managed runtime under `dist/dev-runtime/current/<platform>/`.

The local runtime override exists to avoid writing host configs directly against `rust-server/target/release/patina-server.exe` during local iteration. It is not the normal install path for registry users.

## Contributor Loop

Use this loop when you are changing source files in this repository:

1. Point a Unity project at the local package checkout.

- `file:<your-clone-path>/unity-package`

2. Build the Rust server.

```powershell
cd rust-server
cargo build --release
```

3. Publish the current binary into the development runtime path.

```powershell
pwsh -File scripts/publish-dev-runtime.ps1
```

4. In Unity, open `Window > Patina Unity MCP` and enable `Use Local Runtime (Contributor)`.

5. Click `One-Click Setup`.

This writes host configs against the contributor runtime path instead of the packaged runtime when that local runtime is available. Re-run steps 2 through 5 after every source change that affects the Rust binary or the Unity bridge.

6. When you need to clean up local host registrations, click `Remove Patina From Hosts`.

This removes only the `patina` MCP entry from supported host configs and leaves unrelated MCP servers intact.

## Local Unity Testing

Use a staged local package when you want to verify the package exactly as it will be published:

```powershell
pwsh -File scripts/stage-local-upm.ps1
```

Then add this package from disk in Unity:

- `dist/local-upm/com.taygunsavas.patina-unity-mcp/package.json`

Prefer the staged local package for package-layout checks, import checks, and release-candidate validation. Prefer the source checkout flow when you need the shortest edit-build-setup loop.

## Validation Expectations

Run the checks that match your change before opening a pull request:

```powershell
cd rust-server
cargo fmt --all
cargo test
```

- If you changed packaging or runtime publishing behavior, also run `pwsh -File scripts/publish-dev-runtime.ps1`.
- If you changed the Unity package layout, also run `pwsh -File scripts/stage-local-upm.ps1`.
- If your change affects Unity editor behavior, include manual validation notes from a real Unity project in the pull request.
- If you changed the local runtime selection behavior, also rerun `Remove Patina From Hosts` and `One-Click Setup` after republishing the dev runtime.

## Pull Request Guidelines

- Keep each pull request focused on a single concern.
- Link the related issue or explain why no issue exists.
- Update `README.md` or `.github/` guidance when contributor or user-facing behavior changes.
- Include the commands you ran and any Unity-side manual checks in the PR description.
- Add screenshots or logs when changing setup UX, package import behavior, or editor window output.
- Pull requests to `main` should be prepared for required CI, CODEOWNERS review, and automatic Copilot review.

## Reporting Paths

- Use the `Bug Report` issue form for reproducible defects.
- Use the `Feature Request` issue form for product or workflow proposals.
- Use the `Usage Question` issue form for help requests and setup questions.
- Follow `.github/SECURITY.md` for vulnerabilities; do not open public issues for security reports.
- Follow `.github/CODE_OF_CONDUCT.md` in all project spaces.

## Release Notes For Maintainers

- Keep `rust-server/Cargo.toml` and `unity-package/package.json` on the same version.
- The release workflow assembles the full Unity package artifact and publishes it to registry channels.
- GitHub Releases are secondary artifacts and release notes, not the primary install surface.
- Unity `6000.3.5f2+` is the validated minimum for release verification; Package Manager signed-package behavior was stabilized at that patch level.
- Keep release runbooks in maintainer-owned channels and update this section when public release behavior changes.

### Asset Store

The Unity Asset Store is a planned manual secondary distribution channel. It does not share automation with the registry release path and is not a blocker for registry delivery.

When preparing an Asset Store submission:
- Stage the package locally with `pwsh -File scripts/stage-local-upm.ps1`, then export from `dist/local-upm/com.taygunsavas.patina-unity-mcp/` as a `.unitypackage` via the Unity Editor asset export flow.
- The submission requires a display name, description, category, screenshots, and a support URL. Use the README product copy as a starting point.
- Asset Store review timelines are independent of registry releases; plan submissions separately.

## Notes

- `Use Local Runtime (Contributor)` is intended for contributors only.
- Registry users should stay on the packaged runtime path.
- If a host is still pointing to an older runtime path, re-run `One-Click Setup` after publishing the new dev runtime.
- If you switch between the packaged runtime and the contributor runtime, either rerun `One-Click Setup` immediately or use `Remove Patina From Hosts` before switching.
