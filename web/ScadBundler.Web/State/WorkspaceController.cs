using ScadBundler.Core.Workspace;

namespace ScadBundler.Web.State;

/// <summary>
/// The single DI-registered owner of all mutable UI state (Design §3.2). Components bind to it and raise
/// intents; it is the only thing that calls the <see cref="ProjectAnalyzer"/> / <see cref="WebBundler"/>
/// facade. It never touches the DOM. Every intent schedules a recompute that runs <b>off the synchronous UI
/// render path</b> — phased (analyze → bundle) with an <c>await Task.Yield()</c> between phases so the browser
/// can paint a determinate <see cref="BusyPhase"/> indicator and stay responsive across the phase boundary
/// (Slice W5 §C1). It is still single-threaded: one very large phase can run long enough to trip the browser's
/// "page unresponsive" prompt — the §C3 Web Worker is the full fix. Rapid intents are coalesced: each cancels
/// the prior in-flight recompute (cooperative —
/// there is no preemption on single-threaded WASM, so the token is checked <i>between</i> phases) and, except
/// for the already-debounced <see cref="EditMainFile"/>, waits out a short <see cref="DebounceMs"/> window.
/// <para>
/// For <b>large projects</b> (<see cref="IsLargeProject"/>) the bundle is the slow phase, so it is <b>not</b>
/// run on every intent: structural changes still re-analyze live (the cheap phase that drives the file
/// list/tree), but the bundle is deferred until the user clicks Bundle (<see cref="ApplyOptions"/>), and an
/// option change only <i>stages</i> into <see cref="PendingOptions"/> rather than re-bundling. This lets the
/// user set every option before paying for one bundle, instead of freezing the page once per toggle. Small
/// projects keep the live, bundle-on-every-change behaviour (<see cref="AutoBundle"/>). An options-only
/// re-bundle reuses the last analysis (no re-load), per Slice W5 §C2.
/// </para>
/// </summary>
public sealed class WorkspaceController : IDisposable
{
    // The virtual project root every upload is placed under (mirrors ProjectAnalyzer's convention).
    private const string ProjectRoot = "/proj/";

    // Above either threshold, auto-bundling on every intent would freeze single-threaded WASM, so the project
    // switches to manual-bundle mode (analyze live, bundle on demand). Tuned to separate BOSL2-scale projects
    // (tens of files / megabytes) from ordinary maker projects (a handful of small files).
    internal const int LargeProjectFileThreshold = 12;
    internal const int LargeProjectByteThreshold = 256 * 1024;

    // A stateless canonicalizer: GetFullPath is a pure function of its argument, so one shared instance
    // maps an upload Name to the same canonical virtual path the analyzer uses (e.g. Root, candidates).
    private static readonly InMemoryFileSystem Canonicalizer = new();

    // Uploaded files keyed by Name, preserving insertion order so a re-add replaces in place.
    private readonly Dictionary<string, UploadedFile> _uploads = new(StringComparer.Ordinal);
    private string? _explicitRoot;

    // Cancels the in-flight recompute when a newer intent supersedes it (coalescing rapid intents).
    private CancellationTokenSource? _recomputeCts;

    // The file system from the most recent *completed* analyze, reused by an options-only re-bundle so it can
    // skip the analyze load (Slice W5 §C2). Valid to reuse only while !_structureDirty.
    private InMemoryFileSystem? _analyzedFs;

    // True when uploads/root changed since the last analyze, so the next recompute must re-analyze before it
    // can bundle. Starts true so the first recompute always analyzes.
    private bool _structureDirty = true;

    /// <summary>The current uploaded set, in first-seen order.</summary>
    public IReadOnlyList<UploadedFile> Uploads => [.. _uploads.Values];

    /// <summary>The number of uploaded files — a non-allocating alternative to <c>Uploads.Count</c> (which
    /// materializes a new list) for hot paths like the busy-phase status line.</summary>
    public int UploadCount => _uploads.Count;

    /// <summary>The latest analysis, or <c>null</c> before the first upload.</summary>
    public ProjectAnalysis? Analysis { get; private set; }

    /// <summary>The latest bundle, or <c>null</c> when the dependency set is incomplete.</summary>
    public WebBundleResult? Bundle { get; private set; }

    /// <summary>The <b>applied</b> bundle options — the ones the current <see cref="Bundle"/> was produced
    /// with. Equal to <see cref="PendingOptions"/> except, in a large project, between an option edit and the
    /// next <see cref="ApplyOptions"/>.</summary>
    public WebBundleOptions Options { get; private set; } = new();

    /// <summary>The options currently shown in (and edited by) the options panel. In a small project these
    /// apply immediately; in a large one they are staged here until <see cref="ApplyOptions"/> bundles with
    /// them. <see cref="OptionsDirty"/> reports whether they differ from the applied <see cref="Options"/>.</summary>
    public WebBundleOptions PendingOptions { get; private set; } = new();

    /// <summary>Whether the panel's <see cref="PendingOptions"/> differ from the applied <see cref="Options"/>
    /// — i.e. the shown <see cref="Bundle"/> is stale with respect to the user's current option choices.</summary>
    public bool OptionsDirty => PendingOptions != Options;

    /// <summary>
    /// Whether the project is large enough that bundling on every intent would freeze the single-threaded
    /// WASM UI. Such projects analyze live but defer the bundle to an explicit <see cref="ApplyOptions"/>
    /// (Slice W5 §C — set options first, then pay for one bundle).
    /// </summary>
    public bool IsLargeProject =>
        _uploads.Count > LargeProjectFileThreshold || TotalUploadChars > LargeProjectByteThreshold;

    /// <summary>Whether intents bundle live (small project) or defer to <see cref="ApplyOptions"/> (large).</summary>
    public bool AutoBundle => !IsLargeProject;

    /// <summary>Whether the current set could be bundled right now (a usable root, nothing missing/ambiguous).</summary>
    public bool CanBundle =>
        Root is not null && Analysis is { } a && a.Missing.Count == 0 && a.Ambiguous.Count == 0;

    /// <summary>
    /// Whether a (re-)bundle is warranted and possible: the set is complete but the shown bundle is missing or
    /// out of date (stale options), and no recompute is already running. Drives the manual "Bundle" button in
    /// a large project; always <c>false</c> in a small one (it auto-bundles, so nothing is ever stale).
    /// </summary>
    public bool NeedsBundle => !IsBusy && CanBundle && (Bundle is null || OptionsDirty);

    /// <summary>The root actually used (explicit override or inferred); <c>null</c> when none is usable.</summary>
    public string? Root { get; private set; }

    // The total length of all upload texts, used (with the file count) to decide IsLargeProject. char-length
    // is a close-enough proxy for byte size at the threshold; `||` short-circuits past this on file count.
    private long TotalUploadChars
    {
        get
        {
            long total = 0;
            foreach (UploadedFile file in _uploads.Values)
            {
                total += file.Text.Length;
            }

            return total;
        }
    }

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

    /// <summary>Adds (or replaces, by <see cref="UploadedFile.Name"/>) files, then re-analyzes (and, in a
    /// small project, re-bundles).</summary>
    /// <param name="files">The files to merge into the workspace.</param>
    public void AddOrReplace(IEnumerable<UploadedFile> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        foreach (UploadedFile file in files)
        {
            _uploads[file.Name] = file;
        }

        _structureDirty = true;
        Schedule(debounce: true, bundle: AutoBundle);
    }

    /// <summary>Removes the upload with the given <see cref="UploadedFile.Name"/>, then re-analyzes.</summary>
    /// <param name="name">The upload's <see cref="UploadedFile.Name"/>.</param>
    public void Remove(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (_uploads.Remove(name))
        {
            _structureDirty = true;
            Schedule(debounce: true, bundle: AutoBundle);
        }
    }

    /// <summary>Pins the entry point to the given canonical virtual path (overrides inference).</summary>
    /// <param name="virtualPath">The chosen root's canonical virtual path.</param>
    public void SetRoot(string virtualPath)
    {
        ArgumentNullException.ThrowIfNull(virtualPath);
        _explicitRoot = virtualPath;
        _structureDirty = true;
        Schedule(debounce: true, bundle: AutoBundle);
    }

    /// <summary>
    /// Edits the bundle options. In a small project this applies immediately and re-bundles; in a large one it
    /// only <i>stages</i> into <see cref="PendingOptions"/> (no bundle) so the user can set every option before
    /// paying for one bundle via <see cref="ApplyOptions"/>.
    /// </summary>
    /// <param name="options">The new options.</param>
    public void SetOptions(WebBundleOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        PendingOptions = options;
        if (AutoBundle)
        {
            Options = options;
            Schedule(debounce: true, bundle: true);
        }
        else
        {
            Changed?.Invoke(); // stage only: re-render the panel + its "unapplied changes" affordance
        }
    }

    /// <summary>
    /// Applies <see cref="PendingOptions"/> and bundles once (the large-project "Bundle" / "Apply &amp; bundle"
    /// button). Reuses the last analysis when nothing structural changed, so an options-only apply skips the
    /// analyze load (Slice W5 §C2). Harmless in a small project (options already applied; just re-bundles).
    /// </summary>
    public void ApplyOptions()
    {
        Options = PendingOptions;
        Schedule(debounce: false, bundle: true);
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
        _structureDirty = true;

        // The MainFileEditor already debounces keystrokes; don't double-debounce. In a large project this still
        // only re-analyzes (bundle deferred) so live typing never triggers the slow bundle phase.
        Schedule(debounce: false, bundle: AutoBundle);
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
    // `bundle` is false for a large project's structural change (analyze only — the bundle is deferred).
    private void Schedule(bool debounce, bool bundle)
    {
        _recomputeCts?.Cancel();
        _recomputeCts?.Dispose();
        _recomputeCts = new CancellationTokenSource();
        Recomputing = RecomputeAsync(debounce, bundle, _recomputeCts.Token);
    }

    // The phased recompute (Slice W5 §C1): analyze → gate on a usable root with no missing/ambiguous
    // references → bundle (mirrors Design §3.2). Each phase sets BusyPhase + fires Changed *before* its
    // blocking call, then `await Task.Yield()`s so the browser paints the new label (resetting its
    // "unresponsive" watchdog) before the synchronous work runs. Cancellation is cooperative: there is no
    // preemption on single-threaded WASM, so the token is only observed between phases — enough to drop a
    // superseded recompute, not to interrupt one already inside a Core call.
    //
    // Two phases are now independently skippable: the analyze is skipped when nothing structural changed since
    // the last one (an options-only re-bundle reuses the cached fs/analysis — Slice W5 §C2), and the bundle is
    // skipped when `bundle` is false (a large project's deferred bundle) or the set is incomplete.
    private async Task RecomputeAsync(bool debounce, bool bundle, CancellationToken token)
    {
        try
        {
            if (debounce && DebounceMs > 0)
            {
                await Task.Delay(DebounceMs, token);
            }

            // Phase 1 — analyze (layout inference + load + semantic), unless an unchanged structure lets us
            // reuse the last analysis. The first recompute always analyzes (_structureDirty starts true).
            if (_structureDirty || _analyzedFs is null)
            {
                BusyPhase = BusyPhase.Analyzing;
                Changed?.Invoke();
                await YieldToBrowserAsync(token);

                (InMemoryFileSystem fs, ProjectAnalysis analysis) = ProjectAnalyzer.Analyze(Uploads, _explicitRoot);
                _analyzedFs = fs;
                Analysis = analysis;
                Root = analysis.Root;
                _structureDirty = false;
            }

            // Phase 2 — bundle (load + inline + transform + emit), reusing the analyzer's file count so it
            // doesn't re-load just to count (Slice W5 §C2). A deferred or blocked recompute leaves the bundle
            // null so the UI prompts (a manual "Bundle" button, or the missing/ambiguous rows).
            if (bundle && CanBundle)
            {
                BusyPhase = BusyPhase.Bundling;
                Changed?.Invoke();
                await YieldToBrowserAsync(token);

                Bundle = WebBundler.Bundle(_analyzedFs!, Root!, Options, Analysis!.FilesInlined);
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

        // A recompute parked at a phase boundary is cancelled above but won't reach Idle on its own, so settle
        // the phase here — leaving it Analyzing/Bundling post-teardown is a misleading state to observe.
        BusyPhase = BusyPhase.Idle;
    }
}
