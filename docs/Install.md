# Installing ScadBundler

ScadBundler runs anywhere OpenSCAD does — Windows, macOS, and Linux (x64 and arm64). Pick the
option that matches you:

| You are… | Best option |
| --- | --- |
| An OpenSCAD maker who just wants the tool | **Portable download** or **winget** (Windows) — no .NET needed |
| On Windows and want one-click + auto-update | **Microsoft Store** *(coming once the project has a Store account)* |
| A developer who already has .NET | **`dotnet tool`** |

All builds read your existing OpenSCAD library layout automatically — `OPENSCADPATH` and the
per-user OpenSCAD library folder — so `include`/`use` resolve with no extra flags.

---

## Windows

### winget
```powershell
winget install ScadBundler
```
Then run `scadbundler bundle myproject.scad -o bundled.scad` in any terminal.

> **If winget can't find it yet:** the package goes live only after the maintainer enables winget
> publishing (a one-time setup) and the first manifest is accepted into the winget community repo.
> Until then, use the **portable download** below — it always works.

### Portable (no install)
1. Download `scadbundler-win-x64.zip` (or `-win-arm64`) from the
   [latest release](https://github.com/Dano7/ScadCombiner/releases/latest).
2. Unzip it anywhere. Either run `scadbundler.exe` directly, or add its folder to your `PATH` to
   call `scadbundler` from any terminal.

#### "Windows protected your PC" (SmartScreen)
ScadBundler's downloads **aren't code-signed yet**, so the first time you run the `.exe`, Windows
SmartScreen may show *"Windows protected your PC."* This is normal for new, unsigned open-source
apps and does **not** mean the file is unsafe. To run it:

1. Click **More info**.
2. Click **Run anyway**.

You can verify your download first against `SHA256SUMS.txt` on the release page (see
[Verifying downloads](#verifying-downloads)).

> **Why isn't it signed — and can you help?** Code-signing certificates cost money this project
> doesn't have yet. Microsoft's **Azure Trusted Signing** (~$10/month) would make this warning
> disappear for **every** Windows user, and Apple notarization (~$99/year) would do the same on
> macOS. If ScadBundler saves you time, please consider **[sponsoring the project](https://github.com/sponsors/Dano7)**
> to fund signing — even one sponsor covering the Windows certificate clears the warning for
> everyone. 🙏

### Microsoft Store
A one-click, auto-updating Store app is planned. The Store signs the package, so there's no
SmartScreen prompt. (This ships once the project has a Partner Center account.)

---

## macOS

1. Download `scadbundler-osx-arm64.zip` (Apple Silicon) or `scadbundler-osx-x64.zip` (Intel) from the
   [latest release](https://github.com/Dano7/ScadCombiner/releases/latest).
2. Unzip it, then make it executable and put it on your `PATH`. The zip is built on a Windows
   runner, so the Unix executable bit needs setting:
   ```bash
   chmod +x ./scadbundler
   sudo mv ./scadbundler /usr/local/bin/
   ```
3. Because the binary isn't notarized yet, Gatekeeper may block the first run. Either right-click it
   in Finder → **Open**, or clear the quarantine flag:
   ```bash
   xattr -d com.apple.quarantine /usr/local/bin/scadbundler
   ```

A Homebrew tap (`brew install dano7/tap/scadbundler`) is planned for one-command installs/updates.

---

## Linux

1. Download `scadbundler-linux-x64.zip` (or `-linux-arm64`) from the
   [latest release](https://github.com/Dano7/ScadCombiner/releases/latest).
2. Unzip, mark executable, and put it on your `PATH`:
   ```bash
   unzip scadbundler-linux-x64.zip && cd scadbundler-linux-x64
   chmod +x scadbundler
   sudo mv scadbundler /usr/local/bin/
   ```

A Homebrew tap is planned here too.

---

## .NET global tool (developers)

Requires the [.NET runtime](https://dotnet.microsoft.com/download). The other options above bundle
their own runtime, so they need no .NET install.

```bash
dotnet tool install --global ScadBundler          # install
scadbundler bundle myproject.scad -o bundled.scad  # use
dotnet tool update  --global ScadBundler          # update
dotnet tool uninstall --global ScadBundler        # remove
```

---

## Verifying downloads

Every release includes `SHA256SUMS.txt`. To check a download:

```bash
# macOS/Linux
shasum -a 256 -c SHA256SUMS.txt        # from the folder containing the downloaded zip

# Windows (PowerShell)
Get-FileHash .\scadbundler-win-x64.zip -Algorithm SHA256
# compare the hash against the matching line in SHA256SUMS.txt
```

---

## Using it

See the [README](README.md) and [UX.md](UX.md) for the full command surface. The essentials:

```bash
scadbundler bundle myproject.scad -o bundled.scad   # combine a multi-file project into one file
scadbundler bundle myproject.scad --minify -o out.scad
scadbundler bundle myproject.scad -p /path/to/libs  # add an extra library search path
```
