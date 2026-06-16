# packaging/

Deployment/packaging assets, kept out of `src/` so the core projects carry no platform concerns.

## `msix/`
Microsoft Store / MSIX packaging for the Windows build.

- `AppxManifest.xml` — package manifest. Declares ScadBundler as a full-trust desktop app with a
  `windows.appExecutionAlias`, so installing it makes `scadbundler` available in any terminal.
  `Identity/@Name`, `Publisher`, and `Version` are placeholders; the Store assigns the first two
  (Partner Center), and `build-msix.ps1` injects the version at build time.
- `Images/` — tile/logo assets. **These are solid-color placeholders** — replace them with real
  branding before submitting to the Store. (`makeappx` packs them as-is; the Store validates
  dimensions at submission.)
- `build-msix.ps1` — stages the published `scadbundler.exe` + manifest + images and runs
  `makeappx` to produce an MSIX. Invoked by the `msix` job in
  [`.github/workflows/release.yml`](../../.github/workflows/release.yml).

### Status
The `release.yml` MSIX job builds an **unsigned** package (best-effort, non-blocking). An unsigned
MSIX cannot be installed by double-click — the clean path is the **Microsoft Store**, which signs it
for free at ingestion. See [docs/Releasing.md](../docs/Releasing.md) for enabling Store submission
once a Partner Center account exists.
