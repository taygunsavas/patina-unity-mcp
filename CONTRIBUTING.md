# Contributing

## Repository Model

Patina keeps source and release artifacts separate:

- `main` contains the source for `unity-package/` and `rust-server/`.
- CI assembles the distributable Unity package artifact for registry publication.
- End users install from registry-backed Unity Package Manager channels, not from a Git URL to this repository.

## Contributor Runtime Override

Contributors have an optional local runtime override for source-based iteration:

- Packaged runtime: the binary already bundled into the published Unity package.
- Local runtime override: a contributor-managed runtime under `dist/dev-runtime/current/<platform>/`.

The local runtime override exists to avoid writing host configs directly against `rust-server/target/release/patina-server.exe` during local iteration.

## Contributor Loop

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

This writes host configs against the contributor runtime path instead of the packaged runtime when that local runtime is available.

6. When you need to clean up local host registrations, click `Remove Patina From Hosts`.

This removes only the `patina` MCP entry from supported host configs and leaves unrelated MCP servers intact.

## Local Unity Testing

For packaged-flow testing, stage a local Unity package artifact first:

```powershell
pwsh -File scripts/stage-local-upm.ps1
```

Then add this package from disk in Unity:

- `dist/local-upm/com.taygunsavas.patina-unity-mcp/package.json`

## Release Notes For Maintainers

- Keep `rust-server/Cargo.toml` and `unity-package/package.json` on the same version.
- The release workflow assembles the full Unity package artifact and publishes it to registry channels.
- GitHub Releases are secondary artifacts and release notes, not the primary install surface.
- Unity `6000.3.5f2+` is the recommended validation baseline because Package Manager signed-package behavior was stabilized there.

## Notes

- `Use Local Runtime (Contributor)` is intended for contributors only.
- Registry users should stay on the packaged runtime path.
- If a host is still pointing to an older runtime path, re-run `One-Click Setup` after publishing the new dev runtime.
- If you switch between the packaged runtime and the contributor runtime, either rerun `One-Click Setup` immediately or use `Remove Patina From Hosts` before switching.

