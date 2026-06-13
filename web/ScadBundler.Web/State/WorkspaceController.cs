using ScadBundler.Core.Workspace;

namespace ScadBundler.Web.State;

/// <summary>
/// The single DI-registered owner of all mutable UI state (Design §3.2). Components bind to it and raise
/// intents; it is the only thing that calls the <see cref="ProjectAnalyzer"/> / <see cref="WebBundler"/>
/// facade. It never touches the DOM. Every intent runs <see cref="Recompute"/> and fires
/// <see cref="Changed"/>; <c>Recompute</c> is synchronous and fast (maker-scale inputs).
/// </summary>
public sealed class WorkspaceController
{
    // The virtual project root every upload is placed under (mirrors ProjectAnalyzer's convention).
    private const string ProjectRoot = "/proj/";

    // A stateless canonicalizer: GetFullPath is a pure function of its argument, so one shared instance
    // maps an upload Name to the same canonical virtual path the analyzer uses (e.g. Root, candidates).
    private static readonly InMemoryFileSystem Canonicalizer = new();

    // Uploaded files keyed by Name, preserving insertion order so a re-add replaces in place.
    private readonly Dictionary<string, UploadedFile> _uploads = new(StringComparer.Ordinal);
    private string? _explicitRoot;

    /// <summary>The current uploaded set, in first-seen order.</summary>
    public IReadOnlyList<UploadedFile> Uploads => [.. _uploads.Values];

    /// <summary>The latest analysis, or <c>null</c> before the first upload.</summary>
    public ProjectAnalysis? Analysis { get; private set; }

    /// <summary>The latest bundle, or <c>null</c> when the dependency set is incomplete.</summary>
    public WebBundleResult? Bundle { get; private set; }

    /// <summary>The active bundle options.</summary>
    public WebBundleOptions Options { get; private set; } = new();

    /// <summary>The root actually used (explicit override or inferred); <c>null</c> when none is usable.</summary>
    public string? Root { get; private set; }

    /// <summary>
    /// The current root file's text, or <c>null</c> when there is no usable root — the seed for the
    /// <c>MainFileEditor</c> textarea.
    /// </summary>
    public string? RootText => Root is not null && NameForCanonical(Root) is { } name ? _uploads[name].Text : null;

    /// <summary>Raised after every recompute so subscribed components can re-render.</summary>
    public event Action? Changed;

    /// <summary>Adds (or replaces, by <see cref="UploadedFile.Name"/>) files, then re-analyzes.</summary>
    /// <param name="files">The files to merge into the workspace.</param>
    public void AddOrReplace(IEnumerable<UploadedFile> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        foreach (UploadedFile file in files)
        {
            _uploads[file.Name] = file;
        }

        Recompute();
    }

    /// <summary>Removes the upload with the given <see cref="UploadedFile.Name"/>, then re-analyzes.</summary>
    /// <param name="name">The upload's <see cref="UploadedFile.Name"/>.</param>
    public void Remove(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (_uploads.Remove(name))
        {
            Recompute();
        }
    }

    /// <summary>Pins the entry point to the given canonical virtual path (overrides inference).</summary>
    /// <param name="virtualPath">The chosen root's canonical virtual path.</param>
    public void SetRoot(string virtualPath)
    {
        ArgumentNullException.ThrowIfNull(virtualPath);
        _explicitRoot = virtualPath;
        Recompute();
    }

    /// <summary>Replaces the bundle options, then re-bundles.</summary>
    /// <param name="options">The new options.</param>
    public void SetOptions(WebBundleOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Options = options;
        Recompute();
    }

    /// <summary>
    /// Replaces the current root file's text in place (the <c>MainFileEditor</c> intent) and re-analyzes, so
    /// newly-referenced libraries light up as needed and now-unused ones de-emphasize. A no-op when there is
    /// no usable root.
    /// </summary>
    /// <param name="newText">The edited root-file text.</param>
    public void EditMainFile(string newText)
    {
        ArgumentNullException.ThrowIfNull(newText);
        if (Root is null || NameForCanonical(Root) is not { } name)
        {
            return;
        }

        _uploads[name] = new UploadedFile(name, newText);
        Recompute();
    }

    /// <summary>
    /// Resolves an <see cref="AmbiguousReference"/> (the <c>ConflictPicker</c> intent): re-adds the chosen
    /// candidate's content under <paramref name="asPath"/> (the reference as written, or a user-typed
    /// sub-path) so the next analysis places it deterministically and the ambiguity clears. A no-op when the
    /// candidate is unknown.
    /// </summary>
    /// <param name="candidateVirtualPath">The picked candidate's canonical virtual path (from
    /// <see cref="AmbiguousReference.Candidates"/>).</param>
    /// <param name="asPath">The <see cref="UploadedFile.Name"/> to re-add it under (the raw reference path or
    /// a user-typed sub-path).</param>
    public void ResolveAmbiguous(string candidateVirtualPath, string asPath)
    {
        ArgumentNullException.ThrowIfNull(candidateVirtualPath);
        ArgumentNullException.ThrowIfNull(asPath);
        if (TextForVirtualPath(candidateVirtualPath) is { } text)
        {
            AddOrReplace([new UploadedFile(asPath, text)]);
        }
    }

    /// <summary>The text of the upload at the given canonical virtual path, or <c>null</c> when none matches
    /// (used by the <c>ConflictPicker</c> to show candidate size/snippet).</summary>
    /// <param name="virtualPath">A canonical <c>/proj/…</c> virtual path.</param>
    /// <returns>The upload's text, or <c>null</c>.</returns>
    public string? TextForVirtualPath(string virtualPath)
    {
        ArgumentNullException.ThrowIfNull(virtualPath);
        return NameForCanonical(virtualPath) is { } name ? _uploads[name].Text : null;
    }

    // Analyze → gate on a usable root with no missing/ambiguous references → bundle. Mirrors Design §3.2.
    private void Recompute()
    {
        (InMemoryFileSystem fs, ProjectAnalysis analysis) = ProjectAnalyzer.Analyze(Uploads, _explicitRoot);
        Analysis = analysis;
        Root = analysis.Root;

        Bundle = analysis.Root is not null
            && analysis.Missing.Count == 0
            && analysis.Ambiguous.Count == 0
                ? WebBundler.Bundle(fs, analysis.Root, Options)
                : null;

        Changed?.Invoke();
    }

    // The upload Name whose placed virtual path (/proj/<Name>, canonicalized) equals canonical, or null.
    // Lets the edit/resolve intents map a canonical path (Root, a candidate) back to the keyed upload.
    private string? NameForCanonical(string canonical)
    {
        foreach (string name in _uploads.Keys)
        {
            if (string.Equals(Canonicalizer.GetFullPath(ProjectRoot + name), canonical, StringComparison.Ordinal))
            {
                return name;
            }
        }

        return null;
    }
}
