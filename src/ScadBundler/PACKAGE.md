# ScadBundler

AST-based OpenSCAD file bundler — combines a multi-file OpenSCAD project into a single `.scad`
file for upload to Thingiverse, MakerWorld, Printables, and other single-file platforms.

## Install (as a .NET global tool)

```
dotnet tool install --global ScadBundler
```

> Requires the .NET runtime. Prefer not to install .NET? Portable executables, a winget package,
> and (soon) a Microsoft Store app are on the
> [GitHub releases page](https://github.com/Dano7/ScadCombiner/releases).

## Use

```
scadbundler bundle myproject.scad -o bundled.scad
```

It resolves `include`/`use` across your `OPENSCADPATH` and the per-user OpenSCAD library folder,
preserves Customizer parameters and license headers, and can `--minify` or `--obfuscate` the
result while keeping the output semantically identical to the original.

See the [documentation](https://github.com/Dano7/ScadCombiner) for the full option set.
