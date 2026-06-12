namespace ScadBundler.Core.Workspace;

/// <summary>
/// The output of <see cref="WebBundler.Bundle"/>: the emitted bundle text (or <c>""</c> when blocked by an
/// Error-severity diagnostic, mirroring the CLI's exit-1 path), an <see cref="Ok"/> flag, the projected
/// diagnostics, and display <see cref="Stats"/>.
/// </summary>
/// <param name="Text">The emitted bundle, or <c>""</c> when <see cref="Ok"/> is <c>false</c>.</param>
/// <param name="Ok"><c>true</c> when no diagnostic is Error-severity.</param>
/// <param name="Diagnostics">All bundle diagnostics, projected to <see cref="DiagnosticDto"/>.</param>
/// <param name="Stats">Display statistics for the bundle.</param>
public sealed record WebBundleResult(
    string Text,
    bool Ok,
    IReadOnlyList<DiagnosticDto> Diagnostics,
    BundleStats Stats);
