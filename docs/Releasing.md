# Releasing ScadBundler

Releases are **fully automated from a git tag**. Versions are derived from the tag by
[MinVer](https://github.com/adamralph/minver) (`MinVerTagPrefix=v`), so there is no version number
to bump in source.

## Cut a release

```bash
git tag v0.2.0        # annotated or lightweight; must start with 'v'
git push origin v0.2.0
```

That triggers [`.github/workflows/release.yml`](../.github/workflows/release.yml), which:

1. **Portable binaries** — publishes self-contained, single-file, trimmed executables for
   `win-x64`, `win-arm64`, `osx-x64`, `osx-arm64`, `linux-x64`, `linux-arm64` (cross-compiled from a
   single Linux runner), zips each with `LICENSE`, and writes `SHA256SUMS.txt`.
2. **NuGet `dotnet tool`** — packs and (if `NUGET_API_KEY` is set) pushes to NuGet.org.
3. **GitHub Release** — creates the release for the tag and attaches the zips, the bare Windows
   `.exe`s, the checksums, and the `.nupkg`.
4. **winget** — (if `WINGET_TOKEN` is set) opens a PR to `microsoft/winget-pkgs`.
5. **MSIX** — builds an (unsigned) MSIX and attaches it. *Preview; see below.*

### Dry run first
Use **Actions → Release → Run workflow** with `dry_run = true` to build all artifacts **without**
creating a release or publishing anything. Do this before the first real tag to shake out the
matrix and the MSIX step.

## One-time setup

| Secret / account | Enables | How |
| --- | --- | --- |
| `NUGET_API_KEY` (repo secret) | NuGet publish | nuget.org → API Keys → Push scope `ScadBundler` → add as a repo secret |
| `WINGET_TOKEN` (repo secret) | winget PRs | A classic PAT with `public_repo`, from an account that has forked `microsoft/winget-pkgs` |
| GitHub Sponsors | the signing appeal in [Install.md](Install.md) | Enable Sponsors for the account; `.github/FUNDING.yml` is already in place |
| Partner Center (~$19) | Microsoft Store | Reserve the **ScadBundler** name; then wire Store submission (see below) |

Without `NUGET_API_KEY` / `WINGET_TOKEN`, those steps **skip cleanly** — the release and the
portable binaries still publish. Nothing is published until you add the secrets.

## Pre-publish checklist (first release only)
- Confirm the **`ScadBundler`** package id is free on [nuget.org](https://www.nuget.org/packages/ScadBundler)
  (the command name `scadbundler` is independent of the id). If taken, change `<PackageId>` in
  [`src/ScadBundler/ScadBundler.csproj`](../src/ScadBundler/ScadBundler.csproj).
- First winget submission to `microsoft/winget-pkgs` goes through review; subsequent ones are
  auto-bumped by the action.
- Replace the placeholder MSIX assets in `packaging/msix/Images/` with real branding.

## MSIX & Microsoft Store (preview)

`packaging/msix/` holds the manifest, placeholder logos, and `build-msix.ps1`. The `release.yml`
`msix` job builds an **unsigned** package (marked `continue-on-error` so a hiccup never blocks the
rest of the release). An unsigned MSIX can't be installed by double-click; the clean path is the
**Microsoft Store**, which signs the package for free at ingestion.

To enable the Store once you have a Partner Center account:
1. Reserve the app name and set `Identity/@Name` + `Publisher` in `packaging/msix/AppxManifest.xml`
   to the values Partner Center assigns.
2. Add an Azure AD app + Partner Center association and wire the
   [`microsoft/store-submission`](https://github.com/microsoft/store-submission) action (secrets:
   tenant/client/credential). This is intentionally left dormant until the account exists.

## Native AOT (future optimization)
The portable exe currently ships as self-contained single-file + trim (~11 MB, verified). Native
AOT would shrink it further and speed startup, but needs per-OS native toolchains (MSVC / clang /
Xcode) validated on each runner. It's a drop-in switch via `-p:PublishAot=true` once validated — see
[Distribution.md](Distribution.md).
