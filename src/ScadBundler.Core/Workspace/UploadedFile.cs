namespace ScadBundler.Core.Workspace;

/// <summary>
/// One file the user provided to ScadBundler Live. <see cref="Name"/> is its relative path when known
/// (a folder drop / <c>webkitRelativePath</c> / a <c>.zip</c> entry path), e.g. <c>"BOSL2/std.scad"</c>;
/// otherwise just the bare file name, e.g. <c>"main.scad"</c>. The facade is unaffected by <i>how</i> the
/// file arrived — only by whether <see cref="Name"/> carries directory structure.
/// </summary>
/// <param name="Name">The relative path (when structure is known) or bare file name.</param>
/// <param name="Text">The full file text, already decoded to a .NET (UTF-16) string.</param>
public sealed record UploadedFile(string Name, string Text);
