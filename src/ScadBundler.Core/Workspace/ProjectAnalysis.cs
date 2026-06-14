namespace ScadBundler.Core.Workspace;

/// <summary>
/// The result of <see cref="ProjectAnalyzer.Analyze"/>: entry-point inference plus a dependency report for
/// the uploaded set. A bundle is producible only when both <see cref="Missing"/> and <see cref="Ambiguous"/>
/// are empty and <see cref="Root"/> is non-<c>null</c>.
/// </summary>
/// <param name="EntryPointCandidates">In-degree-0 files (or the fall-back set when none), ordered
/// geometry-first; the picker list when inference is ambiguous.</param>
/// <param name="InferredRoot">The single best entry-point guess, or <c>null</c> when inference is
/// ambiguous or there are no files.</param>
/// <param name="Root">The root actually used: the explicit override when supplied, else
/// <see cref="InferredRoot"/>. <c>null</c> ⇒ no dependency tree yet.</param>
/// <param name="Tree">The resolved dependency tree, or <c>null</c> when <see cref="Root"/> is <c>null</c>.</param>
/// <param name="Missing">Distinct unresolved (non-font) references with no basename candidate.</param>
/// <param name="Ambiguous">Basename collisions awaiting a one-click pick.</param>
/// <param name="Diagnostics">Parse/semantic problems, projected to <see cref="DiagnosticDto"/>, with
/// <c>SB4001</c> (missing include/use — surfaced as <see cref="Missing"/> instead) filtered out.</param>
/// <param name="ResolvedOwners">Maps every placed virtual path (each <see cref="DependencyTree"/> node's
/// path) back to the canonical path of the upload that owns its content. For a folder/zip upload this is
/// the identity (files resolve verbatim); for a loose upload the basename fixpoint places a referenced file
/// at the <i>alias</i> path the loader looks for (<c>&lt;BOSL2/std.scad&gt;</c> → <c>/proj/BOSL2/std.scad</c>,
/// or a case-folded path), and this maps that alias back to the real upload (<c>/proj/std.scad</c>) — so the
/// used/unused view can tell an aliased-but-reached upload from a genuinely orphaned one.</param>
public sealed record ProjectAnalysis(
    IReadOnlyList<string> EntryPointCandidates,
    string? InferredRoot,
    string? Root,
    DependencyTree? Tree,
    IReadOnlyList<MissingReference> Missing,
    IReadOnlyList<AmbiguousReference> Ambiguous,
    IReadOnlyList<DiagnosticDto> Diagnostics,
    IReadOnlyDictionary<string, string> ResolvedOwners);
