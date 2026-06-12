namespace ScadBundler.Core.Workspace;

/// <summary>
/// Display statistics for a bundle (the UI's "N files combined · X KB" line). Cosmetic — never gates
/// output. Counts are derived from the load graph and the bundle's diagnostics.
/// </summary>
/// <param name="FilesInlined">Distinct non-root files pulled into the bundle (as <c>--verbose</c> counts).</param>
/// <param name="OutputBytes">UTF-8 byte length of the emitted bundle text.</param>
/// <param name="Renames">Definitions renamed/namespaced to resolve a collision (<c>SB5004</c>).</param>
/// <param name="DefinitionsRemoved">Definitions tree-shaken by a hardening profile (from the
/// <c>SB5009</c> summary; <c>0</c> when no profile ran).</param>
/// <param name="Normalizations">Deprecated constructs normalized (<c>SB5001</c> <c>assign</c>→<c>let</c>
/// plus <c>SB5002</c> <c>child</c>→<c>children</c>).</param>
public sealed record BundleStats(
    int FilesInlined,
    int OutputBytes,
    int Renames,
    int DefinitionsRemoved,
    int Normalizations);
