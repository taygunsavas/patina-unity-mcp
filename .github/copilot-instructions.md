# GitHub Copilot review instructions for Patina

Use these instructions when reviewing pull requests in this repository.

## Review priorities

- Focus on correctness, regressions, upgrade safety, and contributor-facing workflow risk.
- Prioritize comments on Rust server behavior, Unity package install/runtime contracts, host setup changes, release workflow changes, and repository governance.
- Flag changes that break the source-only repository model or the artifact-first distribution model.

## What good feedback looks like here

- Call out API compatibility risks in `rust-server/`, especially dependency upgrades and transport changes.
- Call out Unity package layout mistakes in `unity-package/`, especially anything that would break Package Manager import or packaged runtime expectations.
- Call out workflow or release changes that would break validation, artifact assembly, or registry publication.
- Call out repository policy gaps for open-source contribution flow, including missing docs updates when contributor behavior changes.

## Review boundaries

- Do not nitpick formatting, naming, or minor style issues unless they hide a correctness or maintainability problem.
- Do not ask for speculative refactors unrelated to the pull request.
- Prefer high-signal comments over broad style commentary.

## Repository-specific expectations

- `docs/` is local-only planning context and is not part of the GitHub-facing repository surface.
- Public contributor guidance lives in tracked files such as `README.md`, `CONTRIBUTING.md`, `ROADMAP.md`, and `.github/*`.
- Pull requests should stay focused on one concern and include validation notes when behavior changes.
- Treat `validate / lint` and `validate / rust` as the baseline required checks for pull requests targeting `main`.

## High-risk areas

- Changes to `.github/workflows/*`
- Changes to `rust-server/src/*`
- Changes to `unity-package/Editor/*`
- Changes that alter installation, release, or host configuration behavior
