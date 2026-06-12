namespace ScadBundler.Core.Workspace;

/// <summary>
/// Why a file (or unresolved reference) appears in a <see cref="DependencyTree"/>: it is the root, or it
/// was reached by an <c>include</c>, a <c>use</c>, or a font <c>use</c> pass-through.
/// </summary>
public enum ReferenceOrigin
{
    /// <summary>The bundle's entry point (the file inference picked or the user designated).</summary>
    Root,

    /// <summary>Reached via an <c>include &lt;path&gt;</c> edge.</summary>
    Include,

    /// <summary>Reached via a <c>use &lt;path&gt;</c> edge.</summary>
    Use,

    /// <summary>A font <c>use</c> (<c>.ttf</c>/<c>.otf</c>): registered, never inlined, never "missing".</summary>
    Font,
}
