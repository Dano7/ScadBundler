using ScadBundler.Core.Ast;
using ScadBundler.Core.Text;

namespace ScadBundler.Core.Semantics;

/// <summary>
/// The concrete <see cref="ISemanticModel"/>: immutable lookup tables produced by
/// <see cref="SemanticAnalyzer"/>. Declaration lists are per-file in source order; the resolution and
/// references-to side tables use reference identity; <see cref="PrivateConstants(SourceFile)"/> is
/// computed on demand from the recorded file-context reference edges.
/// </summary>
internal sealed class SemanticModel : ISemanticModel
{
    private readonly IReadOnlyDictionary<SourceFile, FileDeclarations> _files;
    private readonly IReadOnlyDictionary<AstNode, Symbol> _resolution;
    private readonly IReadOnlyDictionary<AstNode, IReadOnlyList<AstNode>> _referencesTo;

    /// <summary>For each declaration node, the file-context symbols its body references (own file +
    /// its include closure — the edges that drive <see cref="PrivateConstants(SourceFile)"/>
    /// reachability). Keyed by reference identity.</summary>
    private readonly IReadOnlyDictionary<AstNode, IReadOnlyList<Symbol>> _fileContextReferences;

    public SemanticModel(
        IReadOnlyDictionary<SourceFile, FileDeclarations> files,
        IReadOnlyDictionary<AstNode, Symbol> resolution,
        IReadOnlyDictionary<AstNode, IReadOnlyList<AstNode>> referencesTo,
        IReadOnlyDictionary<AstNode, IReadOnlyList<Symbol>> fileContextReferences)
    {
        _files = files;
        _resolution = resolution;
        _referencesTo = referencesTo;
        _fileContextReferences = fileContextReferences;
    }

    /// <inheritdoc/>
    public IReadOnlyList<ModuleDefinition> Modules(SourceFile file) =>
        _files.TryGetValue(file, out FileDeclarations? d) ? d.Modules : [];

    /// <inheritdoc/>
    public IReadOnlyList<FunctionDefinition> Functions(SourceFile file) =>
        _files.TryGetValue(file, out FileDeclarations? d) ? d.Functions : [];

    /// <inheritdoc/>
    public IReadOnlyList<AssignmentStatement> TopLevelVariables(SourceFile file) =>
        _files.TryGetValue(file, out FileDeclarations? d) ? d.Variables : [];

    /// <inheritdoc/>
    public Symbol? Resolve(AstNode reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return _resolution.GetValueOrDefault(reference);
    }

    /// <inheritdoc/>
    public IReadOnlyList<AstNode> ReferencesTo(Symbol declaration)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        return _referencesTo.GetValueOrDefault(declaration.Declaration, []);
    }

    /// <inheritdoc/>
    public IReadOnlyList<AssignmentStatement> PrivateConstants(SourceFile usedFile)
    {
        ArgumentNullException.ThrowIfNull(usedFile);
        return PrivateConstants([usedFile]);
    }

    /// <inheritdoc/>
    public IReadOnlyList<AssignmentStatement> PrivateConstants(IReadOnlyList<SourceFile> mergedFiles)
    {
        ArgumentNullException.ThrowIfNull(mergedFiles);

        // Reachability closure: start from the exported callables of every file in the merge, follow
        // file-context reference edges through modules/functions/constants, and collect every
        // reachable top-level constant — wherever in the merge it is declared.
        var reached = new HashSet<AssignmentStatement>(ReferenceEqualityComparer.Instance);
        var visited = new HashSet<AstNode>(ReferenceEqualityComparer.Instance);
        var queue = new Queue<AstNode>();
        foreach (SourceFile file in mergedFiles)
        {
            if (!_files.TryGetValue(file, out FileDeclarations? declarations))
            {
                continue;
            }

            foreach (ModuleDefinition module in declarations.Modules)
            {
                queue.Enqueue(module);
            }

            foreach (FunctionDefinition function in declarations.Functions)
            {
                queue.Enqueue(function);
            }
        }

        while (queue.Count > 0)
        {
            AstNode declaration = queue.Dequeue();
            if (!visited.Add(declaration))
            {
                continue;
            }

            foreach (Symbol referenced in _fileContextReferences.GetValueOrDefault(declaration, []))
            {
                if (referenced.Kind == SymbolKind.Variable)
                {
                    reached.Add((AssignmentStatement)referenced.Declaration);
                }

                // Follow modules/functions/constants transitively (a constant may cite another).
                queue.Enqueue(referenced.Declaration);
            }
        }

        // Return grouped by file, in declaration order; geometry, unreferenced vars, $-var settings,
        // and body locals are simply never in a file's top-level Variables list or never reached.
        List<AssignmentStatement> result = [];
        foreach (SourceFile file in mergedFiles)
        {
            if (_files.TryGetValue(file, out FileDeclarations? declarations))
            {
                result.AddRange(declarations.Variables.Where(reached.Contains));
            }
        }

        return result;
    }
}

/// <summary>The top-level declarations of one file, in declaration order.</summary>
/// <param name="Modules">Top-level module definitions.</param>
/// <param name="Functions">Top-level function definitions.</param>
/// <param name="Variables">Top-level variable assignments.</param>
internal sealed record FileDeclarations(
    IReadOnlyList<ModuleDefinition> Modules,
    IReadOnlyList<FunctionDefinition> Functions,
    IReadOnlyList<AssignmentStatement> Variables);
