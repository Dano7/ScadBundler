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
}
