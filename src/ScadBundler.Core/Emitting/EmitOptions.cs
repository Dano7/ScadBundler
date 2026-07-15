namespace ScadBundler.Core.Emitting;

/// <summary>How the emitter renders one level of indentation.</summary>
public enum IndentStyle
{
    /// <summary>Indent with <see cref="EmitOptions.IndentWidth"/> spaces per level.</summary>
    Spaces,

    /// <summary>Indent with one tab character per level (<see cref="EmitOptions.IndentWidth"/> is ignored).</summary>
    Tabs,
}

/// <summary>Where a block's opening brace is placed relative to its header.</summary>
public enum BraceStyle
{
    /// <summary>K&amp;R: <c>{</c> follows the header on the same line after one space.</summary>
    SameLine,

    /// <summary>Allman: <c>{</c> begins on the next line at the header's indent.</summary>
    NextLine,
}

/// <summary>
/// Configuration for the <see cref="Emitter"/>. The defaults (4-space indent, same-line braces,
/// comments preserved, no wrapping) are what lock the checked-in golden outputs; every setting is
/// otherwise adjustable.
/// </summary>
/// <param name="IndentWidth">Spaces per indent level when <see cref="IndentStyle"/> is <see cref="IndentStyle.Spaces"/>.</param>
/// <param name="IndentStyle">Whether to indent with spaces or tabs.</param>
/// <param name="BraceStyle">Where block opening braces are placed.</param>
/// <param name="MaxLineLength">Hard maximum line length in characters; longer lines are broken at the
/// last safe token boundary (ADR 0003; never inside strings, <c>include</c> paths, comments, or a
/// Customizer-annotated parameter line — a single unbreakable token may still exceed the limit).
/// <c>0</c> disables wrapping. The CLI defaults hardened output (<c>--minify</c>/<c>--obfuscate</c>) to
/// <see cref="DefaultHardenedMaxLineLength"/>.</param>
/// <param name="Minify">When <c>true</c>, drop all comments, blank lines, and optional whitespace.</param>
/// <param name="PreserveComments">When <c>true</c>, comment trivia is emitted; ignored when <see cref="Minify"/> is set.</param>
public sealed record EmitOptions(
    int IndentWidth = 4,
    IndentStyle IndentStyle = IndentStyle.Spaces,
    BraceStyle BraceStyle = BraceStyle.SameLine,
    int MaxLineLength = 0,
    bool Minify = false,
    bool PreserveComments = true)
{
    /// <summary>
    /// The <see cref="MaxLineLength"/> the CLI/web default to under <c>--minify</c>/<c>--obfuscate</c>:
    /// long enough to cost ≈1% size, short enough for the line-buffered <c>.scad</c> parsers some
    /// upload platforms use (ADR 0003). Pass <c>--max-line-length 0</c> for unbounded lines.
    /// </summary>
    public const int DefaultHardenedMaxLineLength = 256;

    /// <summary>The default options (4-space indent, same-line braces, comments preserved, no wrapping).</summary>
    public static readonly EmitOptions Default = new();
}
