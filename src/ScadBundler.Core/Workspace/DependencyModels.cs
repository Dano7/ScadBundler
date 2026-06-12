namespace ScadBundler.Core.Workspace;

/// <summary>
/// One node in a <see cref="DependencyTree"/>: a resolved file, or an unresolved/font reference. Nodes
/// mirror the <c>include</c>/<c>use</c> edges of the file they hang under, in source order.
/// </summary>
/// <param name="VirtualPath">The resolved file's canonical virtual path, or — for an unresolved or font
/// reference — the raw <c>&lt;path&gt;</c> exactly as written.</param>
/// <param name="IsRoot"><c>true</c> only for the tree's root node.</param>
/// <param name="Origin">How this node was reached (<see cref="ReferenceOrigin"/>).</param>
/// <param name="Resolved"><c>false</c> ⇒ a "needed" row (a missing, non-font reference); <c>true</c> for
/// resolved files and font pass-throughs.</param>
/// <param name="Children">The <c>include</c>/<c>use</c> edges leaving this file, in source order; empty
/// for leaves, unresolved references, and fonts.</param>
public sealed record DependencyNode(
    string VirtualPath,
    bool IsRoot,
    ReferenceOrigin Origin,
    bool Resolved,
    IReadOnlyList<DependencyNode> Children);

/// <summary>The resolved dependency graph rooted at the chosen entry point.</summary>
/// <param name="Root">The root file's <see cref="DependencyNode"/>.</param>
public sealed record DependencyTree(DependencyNode Root);

/// <summary>
/// A distinct unresolved (non-font) reference with <b>zero</b> uploaded files matching it by basename — a
/// file the user still needs to provide. (A reference matched by exactly one upload is resolved; one
/// matched by two or more is an <see cref="AmbiguousReference"/> instead.)
/// </summary>
/// <param name="RawPath">The <c>&lt;path&gt;</c> exactly as written.</param>
/// <param name="Origin"><see cref="ReferenceOrigin.Include"/> or <see cref="ReferenceOrigin.Use"/>.</param>
/// <param name="NeededBy">Virtual paths of the files that reference it.</param>
public sealed record MissingReference(
    string RawPath,
    ReferenceOrigin Origin,
    IReadOnlyList<string> NeededBy);

/// <summary>
/// An unresolved reference matched by <b>two or more</b> uploaded files by basename (or one basename
/// needed at two different sub-paths). The facade refuses to guess; the UI offers a one-click picker over
/// <see cref="Candidates"/>, which simply re-adds the chosen file with <c>Name = RawPath</c> so the next
/// analysis places it deterministically.
/// </summary>
/// <param name="RawPath">The <c>&lt;path&gt;</c> exactly as written.</param>
/// <param name="Origin"><see cref="ReferenceOrigin.Include"/> or <see cref="ReferenceOrigin.Use"/>.</param>
/// <param name="NeededBy">Virtual paths of the files that reference it.</param>
/// <param name="Candidates">Uploaded virtual paths that match by basename — the picker set.</param>
public sealed record AmbiguousReference(
    string RawPath,
    ReferenceOrigin Origin,
    IReadOnlyList<string> NeededBy,
    IReadOnlyList<string> Candidates);
