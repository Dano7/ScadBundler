using ScadBundler.Core.Ast;
using ScadBundler.Core.Text;

namespace ScadBundler.Core.Semantics;

/// <summary>
/// The queryable result of semantic analysis: per-file declarations, reference→declaration bindings,
/// and the transitive private-constant sets the inliner needs. Built over the <b>pre-inline</b> graph
/// (the inliner consumes it before flattening). Resolution and references-to are keyed by reference
/// identity (see <c>AST-Reference.md</c> §15.6), so structurally-identical nodes never collide.
/// </summary>
public interface ISemanticModel
{
    /// <summary>The top-level module definitions of <paramref name="file"/>, in declaration order.</summary>
    /// <param name="file">The file to query.</param>
    /// <returns>The file's top-level modules (empty if the file is not in the model).</returns>
    IReadOnlyList<ModuleDefinition> Modules(SourceFile file);

    /// <summary>The top-level function definitions of <paramref name="file"/>, in declaration order.</summary>
    /// <param name="file">The file to query.</param>
    /// <returns>The file's top-level functions (empty if the file is not in the model).</returns>
    IReadOnlyList<FunctionDefinition> Functions(SourceFile file);

    /// <summary>The top-level variable assignments of <paramref name="file"/>, in declaration order.</summary>
    /// <param name="file">The file to query.</param>
    /// <returns>The file's top-level variables (empty if the file is not in the model).</returns>
    IReadOnlyList<AssignmentStatement> TopLevelVariables(SourceFile file);

    /// <summary>
    /// The top-level constants of <paramref name="usedFile"/> transitively referenced by its exported
    /// modules/functions — the "private constants" to carry when inlining a <c>use</c>. Excludes
    /// geometry, unreferenced variables, and <c>$</c>-variable settings. Returned in declaration order.
    /// Sees only constants declared in the file itself; a <c>use</c> import must query the file's whole
    /// include closure via <see cref="PrivateConstants(IReadOnlyList{SourceFile})"/>.
    /// </summary>
    /// <param name="usedFile">The used library file.</param>
    /// <returns>The transitively-reachable private constants.</returns>
    IReadOnlyList<AssignmentStatement> PrivateConstants(SourceFile usedFile);

    /// <summary>
    /// The private constants to carry when importing the include-merge of a <c>use</c>d file: the
    /// top-level constants declared anywhere in <paramref name="mergedFiles"/> (a used file plus its
    /// transitive includes — its <c>FileContext</c>) that are transitively referenced by any of those
    /// files' exported modules/functions. A used definition may read a constant its file pulls in via
    /// <c>include</c> (<c>ScopeContext.cc</c> include-merge), so reachability runs over the whole
    /// closure. Exclusions match the single-file form; returned grouped by file in
    /// <paramref name="mergedFiles"/> order, then declaration order.
    /// </summary>
    /// <param name="mergedFiles">The used file's include closure (conventionally itself first).</param>
    /// <returns>The transitively-reachable private constants across the closure.</returns>
    IReadOnlyList<AssignmentStatement> PrivateConstants(IReadOnlyList<SourceFile> mergedFiles);

    /// <summary>
    /// Binds a reference (an <see cref="Identifier"/> in value position, a function call's callee
    /// identifier, or a <see cref="ModuleInstantiation"/>) to the top-level <see cref="Symbol"/> it
    /// resolves to, or <c>null</c> for a local binding, parameter, built-in, special variable, or
    /// unresolved reference.
    /// </summary>
    /// <param name="reference">The reference node (matched by identity).</param>
    /// <returns>The bound symbol, or <c>null</c>.</returns>
    Symbol? Resolve(AstNode reference);

    /// <summary>
    /// Every reference bound to <paramref name="declaration"/> (for the inliner's rename rewriting).
    /// </summary>
    /// <param name="declaration">The declaration whose references are wanted.</param>
    /// <returns>The reference nodes bound to it (empty if none).</returns>
    IReadOnlyList<AstNode> ReferencesTo(Symbol declaration);
}
