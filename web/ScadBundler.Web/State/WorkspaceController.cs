using ScadBundler.Core.Workspace;

namespace ScadBundler.Web.State;

/// <summary>
/// The single DI-registered owner of all mutable UI state (Design §3.2). Components bind to it and raise
/// intents; it is the only thing that calls the <see cref="ProjectAnalyzer"/> / <see cref="WebBundler"/>
/// facade. It never touches the DOM. Every intent schedules a recompute that runs <b>off the synchronous UI
/// render path</b> — phased (analyze → bundle) with an <c>await Task.Yield()</c> between phases so the browser
/// can paint a determinate <see cref="BusyPhase"/> indicator and never shows its "page unresponsive" prompt
/// (Slice W5 §C1). Rapid intents are coalesced: each cancels the prior in-flight recompute (cooperative —
/// there is no preemption on single-threaded WASM, so the token is checked <i>between</i> phases) and, except
/// for the already-debounced <see cref="EditMainFile"/>, waits out a short <see cref="DebounceMs"/> window.
/// </summary>
public sealed class WorkspaceController : IDisposable
{
    // The virtual project root every upload is placed under (mirrors ProjectAnalyzer's convention).
    private const string ProjectRoot = "/proj/";

    // A stateless canonicalizer: GetFullPath is a pure function of its argument, so one shared instance
    // maps an upload Name to the same canonical virtual path the analyzer uses (e.g. Root, candidates).
    private static readonly InMemoryFileSystem Canonicalizer = new();

    // Uploaded files keyed by Name, preserving insertion order so a re-add replaces in place.
    private readonly Dictionary<string, UploadedFile> _uploads = new(StringComparer.Ordinal);
    private string? _explicitRoot;

    // Cancels the in-flight recompute when a newer intent supersedes it (coalescing rapid intents).
    private CancellationTokenSource? _recomputeCts;

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
    /// The phase of the in-flight recompute (Slice W5 §C1). <see cref="State.BusyPhase.Idle"/> when settled;
    /// the UI watches this (via <see cref="Changed"/>) to show a determinate progress indicator.
    /// </summary>
    public BusyPhase BusyPhase { get; private set; } = BusyPhase.Idle;

    /// <summary>Whether a recompute is currently running (a convenience over <see cref="BusyPhase"/>).</summary>
    public bool IsBusy => BusyPhase != BusyPhase.Idle;

    /// <summary>
    /// The in-flight (or most recently completed) recompute. Components fire-and-forget their intents and
    /// re-render off <see cref="Changed"/>; tests <c>await</c> this to observe the settled result. A
    /// superseded recompute completes normally (its cancellation is swallowed), so this is always awaitable.
    /// </summary>
    public Task Recomputing { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// The debounce window (ms) an intent waits before recomputing, to coalesce a flurry of intents (e.g. a
    /// multi-file drop). <see cref="EditMainFile"/> bypasses it (the <c>MainFileEditor</c> already debounces).
    /// Set to <c>0</c> in tests for an immediate recompute.
    /// </summary>
    public int DebounceMs { get; set; } = 100;

    // Test seam: an extra awaited hold (ms) inside each busy phase so a render test can observe the
    // indicator mid-recompute (the handoff's "slow-stubbed recompute"). Zero — a no-op — in production.
    internal int PhaseHoldMs { get; set; }

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

        Schedule(debounce: true);
    }

    /// <summary>Removes the upload with the given <see cref="UploadedFile.Name"/>, then re-analyzes.</summary>
    /// <param name="name">The upload's <see cref="UploadedFile.Name"/>.</param>
    public void Remove(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (_uploads.Remove(name))
        {
            Schedule(debounce: true);
        }
    }

    /// <summary>Pins the entry point to the given canonical virtual path (overrides inference).</summary>
    /// <param name="virtualPath">The chosen root's canonical virtual path.</param>
    public void SetRoot(string virtualPath)
    {
        ArgumentNullException.ThrowIfNull(virtualPath);
        _explicitRoot = virtualPath;
        Schedule(debounce: true);
    }

    /// <summary>Replaces the bundle options, then re-bundles.</summary>
    /// <param name="options">The new options.</param>
    public void SetOptions(WebBundleOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Options = options;
        Schedule(debounce: true);
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
        Schedule(debounce: false); // the MainFileEditor already debounces keystrokes; don't double-debounce
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

    // Supersede any in-flight recompute and start a fresh one. Each intent calls this; the prior token is
    // cancelled so a flurry of intents collapses to one final recompute (its result is the one that lands).
    private void Schedule(bool debounce)
    {
        _recomputeCts?.Cancel();
        _recomputeCts?.Dispose();
        _recomputeCts = new CancellationTokenSource();
        Recomputing = RecomputeAsync(debounce, _recomputeCts.Token);
    }

    // The phased recompute (Slice W5 §C1): analyze → gate on a usable root with no missing/ambiguous
    // references → bundle (mirrors Design §3.2). Each phase sets BusyPhase + fires Changed *before* its
    // blocking call, then `await Task.Yield()`s so the browser paints the new label (resetting its
    // "unresponsive" watchdog) before the synchronous work runs. Cancellation is cooperative: there is no
    // preemption on single-threaded WASM, so the token is only observed between phases — enough to drop a
    // superseded recompute, not to interrupt one already inside a Core call.
    private async Task RecomputeAsync(bool debounce, CancellationToken token)
    {
        try
        {
            if (debounce && DebounceMs > 0)
            {
                await Task.Delay(DebounceMs, token);
            }

            // Phase 1 — analyze (layout inference + load + semantic).
            BusyPhase = BusyPhase.Analyzing;
            Changed?.Invoke();
            await YieldToBrowserAsync(token);

            (InMemoryFileSystem fs, ProjectAnalysis analysis) = ProjectAnalyzer.Analyze(Uploads, _explicitRoot);
            Analysis = analysis;
            Root = analysis.Root;

            if (analysis.Root is not null && analysis.Missing.Count == 0 && analysis.Ambiguous.Count == 0)
            {
                // Phase 2 — bundle (load + inline + transform + emit).
                BusyPhase = BusyPhase.Bundling;
                Changed?.Invoke();
                await YieldToBrowserAsync(token);

                Bundle = WebBundler.Bundle(fs, analysis.Root, Options);
            }
            else
            {
                Bundle = null;
            }

            BusyPhase = BusyPhase.Idle;
            Changed?.Invoke();
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer intent before this recompute finished; that newer recompute now owns the
            // state and will fire its own Changed. Leave BusyPhase as-is — the live recompute overwrites it.
        }
    }

    // Hand control back to the browser event loop so it can paint the just-set phase label between blocking
    // phases. On single-threaded WASM this doesn't parallelize; it resets the "page unresponsive" watchdog.
    // The optional PhaseHoldMs (tests only) additionally holds the busy phase long enough to render-assert.
    private async Task YieldToBrowserAsync(CancellationToken token)
    {
        await Task.Yield();
        if (PhaseHoldMs > 0)
        {
            await Task.Delay(PhaseHoldMs, token);
        }

        token.ThrowIfCancellationRequested();
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

    /// <summary>Cancels any in-flight recompute and releases the cancellation source (DI teardown).</summary>
    public void Dispose()
    {
        _recomputeCts?.Cancel();
        _recomputeCts?.Dispose();
        _recomputeCts = null;
    }
}
