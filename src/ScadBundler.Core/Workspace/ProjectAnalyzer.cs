using ScadBundler.Core.Ast;
using ScadBundler.Core.Diagnostics;
using ScadBundler.Core.Inlining;
using ScadBundler.Core.Loading;
using ScadBundler.Core.Parsing;
using ScadBundler.Core.Semantics;
using ScadBundler.Core.Text;

namespace ScadBundler.Core.Workspace;

/// <summary>
/// Turns a set of <see cref="UploadedFile"/>s into an <see cref="InMemoryFileSystem"/> plus a
/// <see cref="ProjectAnalysis"/>: it builds the virtual layout (with basename inference so the bundle
/// resolves exactly as the analysis predicted), infers or accepts the entry point, and reports the
/// dependency tree, missing/ambiguous references, and (SB4001-filtered) diagnostics. Never throws; adds no
/// new <c>SBxxxx</c> codes.
/// </summary>
public static class ProjectAnalyzer
{
    private const string ProjectRoot = "/proj";

    /// <summary>
    /// Builds the layout, infers (or accepts <paramref name="explicitRoot"/> as) the root, and reports the
    /// dependency status for <paramref name="uploads"/>.
    /// </summary>
    /// <param name="uploads">The files the user provided.</param>
    /// <param name="explicitRoot">A user-chosen root (canonical virtual path); overrides inference.</param>
    /// <returns>The populated in-memory file system and the analysis.</returns>
    public static (InMemoryFileSystem Fs, ProjectAnalysis Analysis) Analyze(
        IReadOnlyList<UploadedFile> uploads,
        string? explicitRoot = null)
    {
        ArgumentNullException.ThrowIfNull(uploads);

        var fs = new InMemoryFileSystem();
        var parseCache = new Dictionary<string, ParsedFile>(StringComparer.Ordinal);

        // 1. Place each upload at /proj/<Name>. Track originals (distinct, in upload order) + content +
        //    the basename index used by layout inference.
        var originals = new List<string>();
        var contentByPath = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (UploadedFile upload in uploads)
        {
            string canonical = fs.GetFullPath(ProjectRoot + "/" + upload.Name);
            if (!contentByPath.ContainsKey(canonical))
            {
                originals.Add(canonical);
            }

            contentByPath[canonical] = upload.Text; // last-wins on a duplicate Name
            fs.AddFile(canonical, upload.Text);
        }

        // aliasOwner maps every placed path back to the upload that owns its content (originals map to
        // themselves; aliases to their source) so the reference graph counts in-degree against uploads.
        var aliasOwner = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string original in originals)
        {
            aliasOwner[original] = original;
        }

        // 2. Layout inference (basename fixpoint): place an alias for every unresolved reference that maps
        //    to exactly one uploaded basename, so it resolves at the path the loader will look for. Folder
        //    and zip uploads resolve verbatim, so this never fires for them.
        ResolveByBasename(fs, parseCache, originals, contentByPath, aliasOwner);

        // 3. Reference graph over the uploads (for entry-point inference).
        (Dictionary<string, HashSet<string>> edges, Dictionary<string, bool> hasGeometry) =
            BuildReferenceGraph(fs, parseCache, originals, aliasOwner);

        IReadOnlyList<string> candidates = OrderedCandidates(originals, edges, hasGeometry);
        string? inferredRoot = InferRoot(candidates, hasGeometry);
        string? root = explicitRoot is not null ? fs.GetFullPath(explicitRoot) : inferredRoot;

        if (root is null)
        {
            // No usable root: report missing/ambiguous from a raw scan; no tree, no diagnostics.
            (IReadOnlyList<MissingReference> rawMissing, IReadOnlyList<AmbiguousReference> rawAmbiguous) =
                ClassifyUnresolved(ScanUnresolved(fs, parseCache, originals), originals);
            return (fs, new ProjectAnalysis(
                candidates, inferredRoot, null, null, rawMissing, rawAmbiguous, [], aliasOwner));
        }

        // 4. Dependency report from the load graph.
        LoadGraph graph = SourceLoader.Load(root, BundleOptions.Default, fs);
        var canonicalByFile = new Dictionary<LoadedFile, string>(ReferenceEqualityComparer.Instance);
        foreach (KeyValuePair<string, LoadedFile> entry in graph.ByAbsolutePath)
        {
            canonicalByFile[entry.Value] = entry.Key;
        }

        string rootCanonical = canonicalByFile.TryGetValue(graph.Root, out string? rc) ? rc : root;
        DependencyTree tree = new(BuildNode(fs, graph.Root, rootCanonical, ReferenceOrigin.Root, true, canonicalByFile, []));

        (IReadOnlyList<MissingReference> missing, IReadOnlyList<AmbiguousReference> ambiguous) =
            ClassifyUnresolved(GraphUnresolved(fs, graph, canonicalByFile), originals);

        IReadOnlyList<DiagnosticDto> diagnostics = ProjectDiagnostics(graph);

        // Count distinct non-root loaded files (== --verbose's "files inlined") off the graph we already
        // have, so the bundle phase can reuse it instead of re-loading just to count (Slice W5 §C2).
        int filesInlined = FilesInlined(graph);

        return (fs, new ProjectAnalysis(
            candidates, inferredRoot, root, tree, missing, ambiguous, diagnostics, aliasOwner, filesInlined));
    }

    // ---------------------------------------------------------------------------------------------
    // Layout inference (basename fixpoint)
    // ---------------------------------------------------------------------------------------------

    private static void ResolveByBasename(
        InMemoryFileSystem fs,
        Dictionary<string, ParsedFile> parseCache,
        IReadOnlyList<string> originals,
        Dictionary<string, string> contentByPath,
        Dictionary<string, string> aliasOwner)
    {
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (string filePath in fs.Files)
            {
                string includerDir = fs.GetDirectoryName(filePath) ?? "/";
                foreach (Ref reference in ParseInfo(fs, parseCache, filePath).Refs)
                {
                    if (reference.IsFont || ResolveRef(fs, reference.RawPath, includerDir) is not null)
                    {
                        continue;
                    }

                    IReadOnlyList<string> matches = BasenameCandidates(reference.RawPath, originals);
                    if (matches.Count != 1)
                    {
                        continue; // 0 ⇒ missing, ≥2 ⇒ ambiguous: neither is placed automatically
                    }

                    string? target = AliasTarget(fs, reference.RawPath, includerDir);
                    if (target is null || fs.Contains(target))
                    {
                        continue;
                    }

                    fs.AddFile(target, contentByPath[matches[0]]);
                    aliasOwner[target] = matches[0];
                    changed = true;
                }
            }
        }
    }

    // The canonical path the loader would look up for rawPath from includerDir (where an alias must go).
    private static string? AliasTarget(InMemoryFileSystem fs, string rawPath, string includerDir)
    {
        string normalized = rawPath.Replace('\\', '/');
        return IsAbsolute(normalized) ? null : fs.GetFullPath(fs.Combine(includerDir, normalized));
    }

    // Uploaded files whose basename matches rawPath's (case-insensitively — makers author on
    // case-insensitive file systems and reference with sloppy case).
    private static IReadOnlyList<string> BasenameCandidates(string rawPath, IReadOnlyList<string> originals)
    {
        string wanted = Basename(rawPath);
        return [.. originals
            .Where(o => string.Equals(Basename(o), wanted, StringComparison.OrdinalIgnoreCase))
            .OrderBy(o => o, StringComparer.Ordinal)];
    }

    private static string Basename(string path)
    {
        string normalized = path.Replace('\\', '/');
        int slash = normalized.LastIndexOf('/');
        return slash < 0 ? normalized : normalized[(slash + 1)..];
    }

    // ---------------------------------------------------------------------------------------------
    // Entry-point inference
    // ---------------------------------------------------------------------------------------------

    private static (Dictionary<string, HashSet<string>> Edges, Dictionary<string, bool> HasGeometry)
        BuildReferenceGraph(
            InMemoryFileSystem fs,
            Dictionary<string, ParsedFile> parseCache,
            IReadOnlyList<string> originals,
            Dictionary<string, string> aliasOwner)
    {
        var edges = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var hasGeometry = new Dictionary<string, bool>(StringComparer.Ordinal);

        foreach (string original in originals)
        {
            ParsedFile parsed = ParseInfo(fs, parseCache, original);
            hasGeometry[original] = parsed.HasGeometry;
            var targets = new HashSet<string>(StringComparer.Ordinal);
            string includerDir = fs.GetDirectoryName(original) ?? "/";

            foreach (Ref reference in parsed.Refs)
            {
                if (reference.IsFont)
                {
                    continue;
                }

                string? resolved = ResolveRef(fs, reference.RawPath, includerDir);
                if (resolved is not null
                    && aliasOwner.TryGetValue(resolved, out string? owner)
                    && !string.Equals(owner, original, StringComparison.Ordinal))
                {
                    targets.Add(owner);
                }
            }

            edges[original] = targets;
        }

        return (edges, hasGeometry);
    }

    private static IReadOnlyList<string> OrderedCandidates(
        List<string> originals,
        IReadOnlyDictionary<string, HashSet<string>> edges,
        Dictionary<string, bool> hasGeometry)
    {
        if (originals.Count == 0)
        {
            return [];
        }

        var inDegree = originals.ToDictionary(o => o, _ => 0, StringComparer.Ordinal);
        foreach (HashSet<string> targets in edges.Values)
        {
            foreach (string target in targets)
            {
                inDegree[target]++;
            }
        }

        List<string> candidates = [.. originals.Where(o => inDegree[o] == 0)];
        if (candidates.Count == 0)
        {
            // All-cycle upload: fall back to geometry-bearing files, else every file.
            candidates = [.. originals.Where(o => hasGeometry[o])];
            if (candidates.Count == 0)
            {
                candidates = [.. originals];
            }
        }

        return [.. candidates
            .OrderByDescending(c => hasGeometry[c])
            .ThenByDescending(c => ReachableCount(c, edges))
            .ThenBy(c => c, StringComparer.Ordinal)];
    }

    private static int ReachableCount(string start, IReadOnlyDictionary<string, HashSet<string>> edges)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<string>();
        stack.Push(start);
        while (stack.Count > 0)
        {
            string current = stack.Pop();
            if (!edges.TryGetValue(current, out HashSet<string>? targets))
            {
                continue;
            }

            foreach (string target in targets)
            {
                if (visited.Add(target))
                {
                    stack.Push(target);
                }
            }
        }

        visited.Remove(start);
        return visited.Count;
    }

    private static string? InferRoot(IReadOnlyList<string> candidates, Dictionary<string, bool> hasGeometry)
    {
        if (candidates.Count == 1)
        {
            return candidates[0];
        }

        List<string> geometry = [.. candidates.Where(c => hasGeometry[c])];
        return geometry.Count == 1 ? geometry[0] : null;
    }

    // ---------------------------------------------------------------------------------------------
    // Dependency tree
    // ---------------------------------------------------------------------------------------------

    private static DependencyNode BuildNode(
        InMemoryFileSystem fs,
        LoadedFile file,
        string canonical,
        ReferenceOrigin origin,
        bool isRoot,
        Dictionary<LoadedFile, string> canonicalByFile,
        HashSet<LoadedFile> onPath)
    {
        onPath.Add(file);
        string includerDir = fs.GetDirectoryName(canonical) ?? "/";
        var children = new List<DependencyNode>();
        int includeIndex = 0;
        int useIndex = 0;

        foreach (Statement statement in file.Ast.Statements)
        {
            switch (statement)
            {
                case IncludeStatement:
                    IncludeEdge include = file.Includes[includeIndex++];
                    children.Add(BuildEdge(
                        fs, include.Statement.RawPath, include.Target, ReferenceOrigin.Include,
                        includerDir, canonicalByFile, onPath));
                    break;
                case UseStatement:
                    UseEdge use = file.Uses[useIndex++];
                    children.Add(use.FontPassthrough
                        ? new DependencyNode(use.Statement.RawPath, false, ReferenceOrigin.Font, true, [])
                        : BuildEdge(
                            fs, use.Statement.RawPath, use.Target, ReferenceOrigin.Use,
                            includerDir, canonicalByFile, onPath));
                    break;
            }
        }

        onPath.Remove(file);
        return new DependencyNode(canonical, isRoot, origin, true, children);
    }

    private static DependencyNode BuildEdge(
        InMemoryFileSystem fs,
        string rawPath,
        LoadedFile? target,
        ReferenceOrigin origin,
        string includerDir,
        Dictionary<LoadedFile, string> canonicalByFile,
        HashSet<LoadedFile> onPath)
    {
        if (target is not null)
        {
            string childCanonical = canonicalByFile.TryGetValue(target, out string? c) ? c : rawPath;
            return onPath.Contains(target)
                ? new DependencyNode(childCanonical, false, origin, true, []) // back-edge: stop, don't recurse
                : BuildNode(fs, target, childCanonical, origin, false, canonicalByFile, onPath);
        }

        // Target null: a cycle (the file exists but was on the active stack) is resolved; otherwise the
        // reference is genuinely unresolved (a "needed" or ambiguous row).
        bool isCycle = ResolveRef(fs, rawPath, includerDir) is not null;
        return new DependencyNode(rawPath, false, origin, isCycle, []);
    }

    // ---------------------------------------------------------------------------------------------
    // Missing / ambiguous classification
    // ---------------------------------------------------------------------------------------------

    private static List<Unresolved> GraphUnresolved(
        InMemoryFileSystem fs,
        LoadGraph graph,
        Dictionary<LoadedFile, string> canonicalByFile)
    {
        var result = new List<Unresolved>();
        foreach (LoadedFile file in graph.ByAbsolutePath.Values)
        {
            string canonical = canonicalByFile.TryGetValue(file, out string? c) ? c : file.Source.Path;
            string includerDir = fs.GetDirectoryName(canonical) ?? "/";

            foreach (IncludeEdge edge in file.Includes)
            {
                AddIfUnresolved(fs, edge.Statement.RawPath, edge.Target, ReferenceOrigin.Include, includerDir, canonical, result);
            }

            foreach (UseEdge edge in file.Uses)
            {
                if (!edge.FontPassthrough)
                {
                    AddIfUnresolved(fs, edge.Statement.RawPath, edge.Target, ReferenceOrigin.Use, includerDir, canonical, result);
                }
            }
        }

        return result;
    }

    private static void AddIfUnresolved(
        InMemoryFileSystem fs,
        string rawPath,
        LoadedFile? target,
        ReferenceOrigin origin,
        string includerDir,
        string owner,
        List<Unresolved> result)
    {
        // Resolved, or an unresolved-because-cyclic edge (the file exists): not "missing".
        if (target is not null || ResolveRef(fs, rawPath, includerDir) is not null)
        {
            return;
        }

        result.Add(new Unresolved(rawPath, origin, owner));
    }

    private static List<Unresolved> ScanUnresolved(
        InMemoryFileSystem fs,
        Dictionary<string, ParsedFile> parseCache,
        IReadOnlyList<string> originals)
    {
        var result = new List<Unresolved>();
        foreach (string original in originals)
        {
            string includerDir = fs.GetDirectoryName(original) ?? "/";
            foreach (Ref reference in ParseInfo(fs, parseCache, original).Refs)
            {
                if (!reference.IsFont && ResolveRef(fs, reference.RawPath, includerDir) is null)
                {
                    result.Add(new Unresolved(reference.RawPath, reference.Origin, original));
                }
            }
        }

        return result;
    }

    private static (IReadOnlyList<MissingReference> Missing, IReadOnlyList<AmbiguousReference> Ambiguous)
        ClassifyUnresolved(IReadOnlyList<Unresolved> unresolved, IReadOnlyList<string> originals)
    {
        var missing = new List<MissingReference>();
        var ambiguous = new List<AmbiguousReference>();

        IEnumerable<IGrouping<(string RawPath, ReferenceOrigin Origin), Unresolved>> groups = unresolved
            .GroupBy(u => (u.RawPath, u.Origin))
            .OrderBy(g => g.Key.RawPath, StringComparer.Ordinal)
            .ThenBy(g => g.Key.Origin);

        foreach (IGrouping<(string RawPath, ReferenceOrigin Origin), Unresolved> group in groups)
        {
            IReadOnlyList<string> neededBy = [.. group
                .Select(u => u.Owner)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(o => o, StringComparer.Ordinal)];
            IReadOnlyList<string> candidates = BasenameCandidates(group.Key.RawPath, originals);

            if (candidates.Count >= 2)
            {
                ambiguous.Add(new AmbiguousReference(group.Key.RawPath, group.Key.Origin, neededBy, candidates));
            }
            else
            {
                // 0 candidates ⇒ genuinely missing. A single-candidate reference would already have been
                // placed by the basename fixpoint and resolved — unless it is an absolute path, which
                // cannot be satisfied by basename placement; reporting it missing keeps it from vanishing.
                missing.Add(new MissingReference(group.Key.RawPath, group.Key.Origin, neededBy));
            }
        }

        return (missing, ambiguous);
    }

    // The number of distinct non-root files in the load graph (exactly what --verbose / BundleStats report).
    // Loading is independent of the collision/licence/hardening options — only LibraryPaths could change the
    // resolved file set, and the web sandbox has none — so this equals what WebBundler would recount, letting
    // the bundle phase skip a redundant SourceLoader.Load (Slice W5 §C2).
    private static int FilesInlined(LoadGraph graph)
    {
        string rootPath = graph.Root.Source.Path;
        return graph.ByAbsolutePath.Values
            .Select(f => f.Source.Path)
            .Where(p => !string.Equals(p, rootPath, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .Count();
    }

    // ---------------------------------------------------------------------------------------------
    // Diagnostics
    // ---------------------------------------------------------------------------------------------

    private static IReadOnlyList<DiagnosticDto> ProjectDiagnostics(LoadGraph graph)
    {
        SemanticResult semantics = SemanticAnalyzer.Analyze(graph);
        return [.. graph.Diagnostics
            .Concat(semantics.Diagnostics)
            .Where(d => d.Code != DiagnosticCode.IncludeUseNotFound) // SB4001 drives the missing-file UI
            .OrderBy(d => d.Span.File.Path, StringComparer.Ordinal)
            .ThenBy(d => d.Span.Start.Offset)
            .ThenBy(d => d.Code, StringComparer.Ordinal)
            .Select(d => new DiagnosticDto(
                d.Code, d.Severity.ToString(), d.Message, d.Span.File.Path, d.Span.Start.Line, d.Span.Start.Column))];
    }

    // ---------------------------------------------------------------------------------------------
    // Reference resolution (mirrors SourceLoader.ResolvePath with no library paths)
    // ---------------------------------------------------------------------------------------------

    private static string? ResolveRef(InMemoryFileSystem fs, string rawPath, string includerDir)
    {
        if (rawPath.Length == 0)
        {
            return null;
        }

        string normalized = rawPath.Replace('\\', '/');
        if (IsAbsolute(normalized))
        {
            return ExistsAsFile(fs, normalized) ? fs.GetFullPath(normalized) : null;
        }

        string fromIncluder = fs.Combine(includerDir, normalized);
        return ExistsAsFile(fs, fromIncluder) ? fs.GetFullPath(fromIncluder) : null;
    }

    private static bool ExistsAsFile(InMemoryFileSystem fs, string path) =>
        fs.FileExists(path) && !fs.DirectoryExists(path);

    private static bool IsAbsolute(string path) => path.StartsWith('/') || Path.IsPathRooted(path);

    private static bool IsFont(string rawPath) =>
        rawPath.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)
        || rawPath.EndsWith(".otf", StringComparison.OrdinalIgnoreCase);

    // ---------------------------------------------------------------------------------------------
    // Parsing
    // ---------------------------------------------------------------------------------------------

    private static ParsedFile ParseInfo(
        InMemoryFileSystem fs,
        Dictionary<string, ParsedFile> cache,
        string canonicalPath)
    {
        string text = fs.ReadAllText(canonicalPath);
        if (cache.TryGetValue(text, out ParsedFile? cached))
        {
            return cached;
        }

        ParseResult parse = Parser.Parse(new SourceFile(canonicalPath, text));
        var refs = new List<Ref>();
        bool hasGeometry = false;
        foreach (Statement statement in parse.Root.Statements)
        {
            switch (statement)
            {
                case IncludeStatement include:
                    refs.Add(new Ref(include.RawPath, ReferenceOrigin.Include, IsFont(include.RawPath)));
                    break;
                case UseStatement use:
                    refs.Add(new Ref(use.RawPath, ReferenceOrigin.Use, IsFont(use.RawPath)));
                    break;
                case ModuleInstantiation:
                    hasGeometry = true; // a top-level call (cube(), translate(), echo(), …) — not a pure def
                    break;
            }
        }

        var result = new ParsedFile(refs, hasGeometry);
        cache[text] = result;
        return result;
    }

    private sealed record ParsedFile(IReadOnlyList<Ref> Refs, bool HasGeometry);

    private readonly record struct Ref(string RawPath, ReferenceOrigin Origin, bool IsFont);

    private readonly record struct Unresolved(string RawPath, ReferenceOrigin Origin, string Owner);
}
