namespace ScadBundler.Core.Workspace;

/// <summary>
/// A browser-/JSON-friendly projection of a <see cref="Diagnostics.Diagnostic"/>: the displayable fields
/// only, with no <see cref="Text.SourceSpan"/> or <see cref="Text.SourceFile"/> leakage (in particular the
/// file's full text is never serialized).
/// </summary>
/// <param name="Code">The <c>SBnnnn</c> code, e.g. <c>"SB3005"</c>.</param>
/// <param name="Severity">The severity name (<c>"Error"</c>/<c>"Warning"</c>/<c>"Info"</c>).</param>
/// <param name="Message">The fully-rendered, human-facing message.</param>
/// <param name="File">The file path the diagnostic refers to (<c>Span.File.Path</c>).</param>
/// <param name="Line">The 1-based line number (<c>Span.Start.Line</c>).</param>
/// <param name="Column">The 1-based column number (<c>Span.Start.Column</c>).</param>
public sealed record DiagnosticDto(
    string Code,
    string Severity,
    string Message,
    string File,
    int Line,
    int Column);
