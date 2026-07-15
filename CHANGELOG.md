# Changelog

All notable changes to ScadBundler are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project aims to follow
[Semantic Versioning](https://semver.org/spec/v2.0.0.html). Versions are cut from git tags
(`vX.Y.Z`) by [MinVer](https://github.com/adamralph/minver).

## [Unreleased]

### Changed
- **ScadBundler Live** intro sets clearer expectations that WASM performance on large,
  library-heavy projects is much slower than the native CLI, and points users there when they
  hit that wall.
- Fixed lingering references to the project's former name (`ScadCombiner`) throughout docs,
  package metadata, and the web app — all now point at `ScadBundler`.

### Added
- **`--max-line-length <n>`** ([ADR 0003](docs/adr/0003-max-line-length-wrapping.md)): hard-wraps
  emitted lines longer than `n` characters at safe token boundaries — geometry unchanged, proven
  byte-identical against the official OpenSCAD binary. Defaults to **256** under
  `--minify`/`--obfuscate` (the very long single lines minification produces overflow the fixed
  line buffers some platforms' custom `.scad` parsers use; capping them costs ≈1% of the size
  savings) and off otherwise; `--max-line-length 0` removes the cap for maximal minification.
  Breaks never land inside strings, `include` paths, comments, or a Customizer-annotated
  parameter line. Also available in ScadBundler Live under Advanced options.
- **Distribution pipeline.** Tag-driven `release.yml` produces portable, self-contained
  single-file executables for win-x64/arm64, osx-x64/arm64, and linux-x64/arm64 (no .NET install
  required), publishes the `dotnet tool` to NuGet, and submits the winget manifest — all from one
  `git tag`. See [docs/Distribution.md](docs/Distribution.md) and [docs/Releasing.md](docs/Releasing.md).
- **MSIX packaging** scaffolding for the Microsoft Store (`packaging/msix/`).
- `LICENSE` (MIT), `CHANGELOG.md`, `global.json` (SDK pin), and NuGet package metadata.
- [docs/Install.md](docs/Install.md) — per-platform install guide, the Windows SmartScreen
  "Run anyway" explanation, and a code-signing sponsorship appeal.

[Unreleased]: https://github.com/Dano7/ScadBundler/commits/main
