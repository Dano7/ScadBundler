using ScadBundler.Core.Workspace;

namespace ScadBundler.Web.State;

/// <summary>How an uploaded file relates to the current dependency tree (drives the file list's emphasis).</summary>
public enum FileUsage
{
    /// <summary>The chosen entry point (badged "★ main").</summary>
    Root,

    /// <summary>Reachable from the root — inlined into the bundle.</summary>
    Used,

    /// <summary>Uploaded but not reached from the root — de-emphasized, safe to ignore or remove.</summary>
    Unused,
}

/// <summary>An uploaded file with its computed <see cref="FileUsage"/> for display.</summary>
/// <param name="VirtualPath">The upload's canonical virtual path (<c>/proj/…</c>).</param>
/// <param name="Usage">Root, used, or unused relative to the current root's dependency tree.</param>
public sealed record ClassifiedFile(string VirtualPath, FileUsage Usage);

/// <summary>
/// Partitions the uploaded set into root / used / unused relative to a <see cref="ProjectAnalysis"/> — the
/// "highlight as used / unused" view model (Slice W2 §2.4). Pure and presentation-only: it adds no
/// behavior, it just labels what the analyzer already resolved, so it is unit-tested directly (no browser).
/// </summary>
public static class FileClassifier
{
    private const string ProjectRoot = "/proj/";
    private static readonly InMemoryFileSystem Canonicalizer = new();

    /// <summary>
    /// Labels each distinct upload (first occurrence wins, upload order preserved) as
    /// <see cref="FileUsage.Root"/>, <see cref="FileUsage.Used"/>, or <see cref="FileUsage.Unused"/>.
    /// </summary>
    /// <param name="uploads">The uploaded files.</param>
    /// <param name="analysis">The current analysis (its root + resolved tree define "used").</param>
    /// <returns>One <see cref="ClassifiedFile"/> per distinct upload, in upload order.</returns>
    public static IReadOnlyList<ClassifiedFile> Classify(
        IReadOnlyList<UploadedFile> uploads,
        ProjectAnalysis analysis)
    {
        ArgumentNullException.ThrowIfNull(uploads);
        ArgumentNullException.ThrowIfNull(analysis);

        // Resolved files reachable from the root. A basename alias can place a used file at a case-folded
        // path (e.g. uploaded ForkedHolderLib.scad referenced as <forkedholderlib.scad>), so we also match
        // case-insensitively on the full path — exact for folder/zip uploads, case-tolerant for loose ones.
        var used = new HashSet<string>(StringComparer.Ordinal);
        var usedLower = new HashSet<string>(StringComparer.Ordinal);
        if (analysis.Tree is { } tree)
        {
            CollectResolved(tree.Root, used, usedLower);
        }

        var result = new List<ClassifiedFile>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (UploadedFile upload in uploads)
        {
            string canonical = Canonicalizer.GetFullPath(ProjectRoot + upload.Name);
            if (!seen.Add(canonical))
            {
                continue; // a duplicate Name resolves to the same file — list it once
            }

            FileUsage usage =
                string.Equals(canonical, analysis.Root, StringComparison.Ordinal) ? FileUsage.Root
                : used.Contains(canonical) || usedLower.Contains(canonical.ToLowerInvariant()) ? FileUsage.Used
                : FileUsage.Unused;

            result.Add(new ClassifiedFile(canonical, usage));
        }

        return result;
    }

    private static void CollectResolved(DependencyNode node, HashSet<string> used, HashSet<string> usedLower)
    {
        if (node.Origin == ReferenceOrigin.Font || !node.Resolved)
        {
            return; // fonts and unresolved references are not "used files"
        }

        if (used.Add(node.VirtualPath))
        {
            usedLower.Add(node.VirtualPath.ToLowerInvariant());
        }

        foreach (DependencyNode child in node.Children)
        {
            CollectResolved(child, used, usedLower);
        }
    }
}
