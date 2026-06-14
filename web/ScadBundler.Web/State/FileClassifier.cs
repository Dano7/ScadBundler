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

        // Uploads reached from the root, by the upload that OWNS each resolved tree node. A loose upload is
        // placed at an alias path the loader resolves (`<BOSL2/std.scad>` → /proj/BOSL2/std.scad, or a
        // case-folded path), so the tree references that alias, not the upload's own path; ResolvedOwners
        // maps the alias back to the owning upload. For folder/zip uploads it is the identity, so this
        // stays exact (no over-marking a same-basename twin). The set holds owner canonical paths.
        var usedOwners = new HashSet<string>(StringComparer.Ordinal);
        if (analysis.Tree is { } tree)
        {
            CollectUsedOwners(tree.Root, analysis.ResolvedOwners, usedOwners);
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
                : usedOwners.Contains(canonical) ? FileUsage.Used
                : FileUsage.Unused;

            result.Add(new ClassifiedFile(canonical, usage));
        }

        return result;
    }

    private static void CollectUsedOwners(
        DependencyNode node,
        IReadOnlyDictionary<string, string> resolvedOwners,
        HashSet<string> usedOwners)
    {
        if (node.Origin == ReferenceOrigin.Font || !node.Resolved)
        {
            return; // fonts and unresolved references are not "used files"
        }

        // Map the resolved (possibly alias) path back to the upload that owns it; identity if not aliased.
        usedOwners.Add(resolvedOwners.GetValueOrDefault(node.VirtualPath, node.VirtualPath));

        foreach (DependencyNode child in node.Children)
        {
            CollectUsedOwners(child, resolvedOwners, usedOwners);
        }
    }
}
