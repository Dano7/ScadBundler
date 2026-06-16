# Changelog

All notable changes to ScadBundler are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project aims to follow
[Semantic Versioning](https://semver.org/spec/v2.0.0.html). Versions are cut from git tags
(`vX.Y.Z`) by [MinVer](https://github.com/adamralph/minver).

## [Unreleased]

### Added
- **Distribution pipeline.** Tag-driven `release.yml` produces portable, self-contained
  single-file executables for win-x64/arm64, osx-x64/arm64, and linux-x64/arm64 (no .NET install
  required), publishes the `dotnet tool` to NuGet, and submits the winget manifest — all from one
  `git tag`. See [docs/Distribution.md](docs/Distribution.md) and [docs/Releasing.md](docs/Releasing.md).
- **MSIX packaging** scaffolding for the Microsoft Store (`packaging/msix/`).
- `LICENSE` (MIT), `CHANGELOG.md`, `global.json` (SDK pin), and NuGet package metadata.
- [docs/Install.md](docs/Install.md) — per-platform install guide, the Windows SmartScreen
  "Run anyway" explanation, and a code-signing sponsorship appeal.

[Unreleased]: https://github.com/Dano7/ScadCombiner/commits/main
