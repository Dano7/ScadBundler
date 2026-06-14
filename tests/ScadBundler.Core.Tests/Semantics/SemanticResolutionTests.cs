using ScadBundler.Core.Ast;
using ScadBundler.Core.Diagnostics;
using ScadBundler.Core.Semantics;
using ScadBundler.Core.Tests.TestSupport;
using Xunit;

namespace ScadBundler.Core.Tests.Semantics;

/// <summary>
/// Single-file reference resolution (§5): top-level reads bind to renameable symbols; locals,
/// parameters, special variables, and built-ins bind to <c>null</c>; and <c>ReferencesTo</c> returns
/// exactly the references bound to a declaration.
/// </summary>
public sealed class SemanticResolutionTests
{
    [Fact]
    public void TopLevelVariableRead_ResolvesToVariableSymbol()
    {
        var (ast, result) = SemanticHelper.AnalyzeFile("WALL = 2;\nx = WALL;");
        var read = (Identifier)((AssignmentStatement)ast.Statements[1]).Value;

        Symbol? symbol = result.Model.Resolve(read);
        Assert.NotNull(symbol);
        Assert.Equal(SymbolKind.Variable, symbol.Kind);
        Assert.Equal("WALL", symbol.Name);
        Assert.Same(ast.Statements[0], symbol.Declaration);
    }

    [Fact]
    public void ParameterRead_ShadowsTopLevel_ResolvesToNull()
    {
        var (ast, result) = SemanticHelper.AnalyzeFile("x = 5;\ny = x;\nmodule m(x) cube(x);");
        var topLevelRead = (Identifier)((AssignmentStatement)ast.Statements[1]).Value;
        var module = (ModuleDefinition)ast.Statements[2];
        var cube = (ModuleInstantiation)module.Body;
        var parameterRead = (Identifier)cube.Arguments[0].Value;

        Assert.NotNull(result.Model.Resolve(topLevelRead)); // top-level x
        Assert.Null(result.Model.Resolve(parameterRead));   // shadowed by the parameter → local
    }

    [Fact]
    public void LetBinding_ShadowsTopLevel_ResolvesToNull()
    {
        var (ast, result) = SemanticHelper.AnalyzeFile("n = 9;\nv = let (n = 1) n;");
        var let = (LetExpression)((AssignmentStatement)ast.Statements[1]).Value;
        var bodyRead = (Identifier)let.Body;

        Assert.Null(result.Model.Resolve(bodyRead));
    }

    [Fact]
    public void LetBoundFunctionLiteral_CalledAsFunction_IsLocal_NoUnknownWarning()
    {
        // A value binding (let/parameter/for) can hold a function literal and be invoked as `name(args)`,
        // so the call resolves as a local (null = unrenameable), not an unknown function — BOSL2's
        // `let(avg = function (a, b) ...) avg(x, y)` pattern.
        var (ast, result) = SemanticHelper.AnalyzeFile("v = let (avg = function (a, b) (a + b) / 2) avg(1, 2);");
        var let = (LetExpression)((AssignmentStatement)ast.Statements[0]).Value;
        var call = (FunctionCallExpression)let.Body;

        Assert.Null(result.Model.Resolve(call.Callee)); // bound locally
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.UnknownReference);
    }

    [Fact]
    public void LetBoundFunctionLiteral_SelfRecursion_ResolvesAsLocal_NoUnknownWarning()
    {
        // A function literal is a closure resolved at call time, so its own name (recursion) is visible
        // inside its body even though the let binding is added after the value is resolved — BOSL2's
        // `strip_left`/`bcs`/`binsearch_fn` pattern (`gears.scad:3499`, `masks.scad:1524`, …).
        var (ast, result) = SemanticHelper.AnalyzeFile(
            "v = let (f = function (n) n <= 0 ? 0 : f(n - 1)) f(5);");
        var innerCall = SemanticHelper.Find<FunctionCallExpression>(
            ast, c => c.Callee is Identifier { Name: "f" });

        Assert.Null(result.Model.Resolve(innerCall.Callee)); // in-literal self-reference is local
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.UnknownReference);
    }

    [Fact]
    public void LetBoundFunctionLiteral_ForwardReference_ResolvesAsLocal_NoUnknownWarning()
    {
        // The literal body references a sibling binding declared *after* it; the whole group is visible
        // to a closure regardless of binding order.
        var (ast, result) = SemanticHelper.AnalyzeFile(
            "h = let (p = function (t) q(t) + 1, q = function (t) t * 2) p(3);");
        var forwardCall = SemanticHelper.Find<FunctionCallExpression>(
            ast, c => c.Callee is Identifier { Name: "q" });

        Assert.Null(result.Model.Resolve(forwardCall.Callee)); // forward reference to a later sibling
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.UnknownReference);
    }

    [Fact]
    public void LetBoundFunctionLiterals_MutualRecursion_ResolveAsLocal_NoUnknownWarning()
    {
        var (ast, result) = SemanticHelper.AnalyzeFile(
            "g = let (a = function (x) b(x), b = function (x) a(x)) g;");
        var callB = SemanticHelper.Find<FunctionCallExpression>(ast, c => c.Callee is Identifier { Name: "b" });
        var callA = SemanticHelper.Find<FunctionCallExpression>(ast, c => c.Callee is Identifier { Name: "a" });

        Assert.Null(result.Model.Resolve(callB.Callee)); // a's body calls b
        Assert.Null(result.Model.Resolve(callA.Callee)); // b's body calls a
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.UnknownReference);
    }

    [Fact]
    public void ComprehensionBoundFunctionLiteral_SelfRecursion_NoUnknownWarning()
    {
        // The deferral covers every binding-group form: a `for`-comprehension binding holding a
        // self-recursive closure must resolve too (the binding group is resolved via ResolveBindings).
        var (ast, result) = SemanticHelper.AnalyzeFile(
            "v = [for (f = function (n) n <= 0 ? 0 : f(n - 1)) f(3)];");
        var innerCall = SemanticHelper.Find<FunctionCallExpression>(
            ast, c => c.Callee is Identifier { Name: "f" });

        Assert.Null(result.Model.Resolve(innerCall.Callee));
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.UnknownReference);
    }

    [Fact]
    public void FunctionLiteralClosingOverComprehensionVariable_InsideLetBinding_NoUnknownWarning()
    {
        // Regression guard for the deferral mechanism: a literal nested inside an intervening
        // comprehension scope (the `for`'s `i`) within a let binding value must still see that scope
        // when its deferred body is resolved — the captured snapshot restores the popped for-frame.
        var (ast, result) = SemanticHelper.AnalyzeFile(
            "v = let (m = [for (i = [0:2]) function () i]) m;");
        var read = SemanticHelper.Find<Identifier>(ast, n => n.Name == "i");

        Assert.Null(result.Model.Resolve(read)); // closes over the comprehension variable (local)
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.UnknownReference);
    }

    [Fact]
    public void EagerLetInitializer_SelfReference_StillWarns()
    {
        // GUARD: an eager (non-closure) initializer sees only the prior bindings, never its own, so a
        // self-reference is a genuine unknown — OpenSCAD agrees. Only function-literal bodies defer.
        var (ast, result) = SemanticHelper.AnalyzeFile("bad = let (w = w + 1) w;");
        Assert.Equal(
            1,
            result.Diagnostics.Count(d => d.Code == DiagnosticCode.UnknownReference && d.Message.Contains("'w'")));
    }

    [Fact]
    public void IsUndefOfBareUnknownIdentifier_DoesNotWarn()
    {
        // OpenSCAD's `is_undef(<bare identifier>)` looks the name up with try_lookup_variable, which
        // never warns on an unknown — the call exists to PROBE for undefinedness (builtin_is_undef).
        // BOSL2's `is_undef(BOSL2_NO_STD_WARNING)` opt-in config check relies on this.
        var (_, result) = SemanticHelper.AnalyzeFile("v = is_undef(NOT_DEFINED) ? 1 : 2;");
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.UnknownReference);
    }

    [Fact]
    public void IsUndefOfKnownIdentifier_StillResolvesToSymbol()
    {
        // The probe still binds a *known* variable to its symbol, so the inliner can rename it.
        var (ast, result) = SemanticHelper.AnalyzeFile("KNOB = 3;\nv = is_undef(KNOB) ? 1 : 2;");
        var read = SemanticHelper.Find<Identifier>(ast, n => n.Name == "KNOB");

        Assert.NotNull(result.Model.Resolve(read));
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.UnknownReference);
    }

    [Fact]
    public void IsUndefOfNonIdentifierExpression_StillWarnsOnUnknown()
    {
        // Only a *bare identifier* argument is special-cased; any other expression evaluates normally
        // and an unknown read inside it still warns (OpenSCAD's `else` branch evaluates the argument).
        var (_, result) = SemanticHelper.AnalyzeFile("v = is_undef(NOT_DEFINED + 1) ? 1 : 2;");
        Assert.Contains(
            result.Diagnostics,
            d => d.Code == DiagnosticCode.UnknownReference && d.Message.Contains("'NOT_DEFINED'"));
    }

    [Fact]
    public void ForBinding_ShadowsTopLevel_ResolvesToNull()
    {
        var (ast, result) = SemanticHelper.AnalyzeFile("i = 9;\nfor (i = [0:2]) cube(i);");
        var loop = (ForStatement)ast.Statements[1];
        var cube = (ModuleInstantiation)loop.Body;
        var loopRead = (Identifier)cube.Arguments[0].Value;

        Assert.Null(result.Model.Resolve(loopRead));
    }

    [Fact]
    public void BlockLocalAssignmentInForBody_ResolvesToNull()
    {
        // A block-level assignment inside a for/let body is a scope-wide local (OpenSCAD assignments
        // are not sequential), so a read of it binds to no renameable symbol — like a for/let binding —
        // and never produces SB3005.
        var (ast, result) = SemanticHelper.AnalyzeFile("for (row = [0:2]) {\n  odd = row % 2;\n  x = odd;\n}");
        var read = SemanticHelper.Find<Identifier>(ast, n => n.Name == "odd");

        Assert.Null(result.Model.Resolve(read));
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "SB3005");
    }

    [Fact]
    public void LetBindingValue_SeesOuterScope_NotItsOwnBinding()
    {
        // In `let (n = n) …` the binding's RHS is the OUTER n (sequential binding), so it must bind to
        // the top-level symbol — the inliner has to rewrite it on rename, but not the body's local n.
        var (ast, result) = SemanticHelper.AnalyzeFile("n = 9;\nv = let (n = n) n;");
        var let = (LetExpression)((AssignmentStatement)ast.Statements[1]).Value;
        var bindingRhs = (Identifier)let.Bindings[0].Value;
        var bodyRead = (Identifier)let.Body;

        Assert.NotNull(result.Model.Resolve(bindingRhs)); // outer n
        Assert.Null(result.Model.Resolve(bodyRead));      // let-local n
    }

    [Fact]
    public void SpecialVariableRead_ResolvesToNull()
    {
        var (ast, result) = SemanticHelper.AnalyzeFile("x = $fn;");
        var read = (Identifier)((AssignmentStatement)ast.Statements[0]).Value;
        Assert.Null(result.Model.Resolve(read));
    }

    [Fact]
    public void BuiltinModuleCall_ResolvesToNull()
    {
        var (ast, result) = SemanticHelper.AnalyzeFile("cube(5);");
        Assert.Null(result.Model.Resolve(ast.Statements[0]));
    }

    [Fact]
    public void BuiltinFunctionCall_ResolvesToNull()
    {
        var (ast, result) = SemanticHelper.AnalyzeFile("x = sin(30);");
        var call = (FunctionCallExpression)((AssignmentStatement)ast.Statements[0]).Value;
        Assert.Null(result.Model.Resolve(call.Callee));
    }

    [Fact]
    public void UserModuleCall_ResolvesToModuleSymbol()
    {
        var (ast, result) = SemanticHelper.AnalyzeFile("module m() cube(1);\nm();");
        Symbol? symbol = result.Model.Resolve(ast.Statements[1]);
        Assert.NotNull(symbol);
        Assert.Equal(SymbolKind.Module, symbol.Kind);
        Assert.Same(ast.Statements[0], symbol.Declaration);
    }

    [Fact]
    public void UserFunctionCall_ResolvesToFunctionSymbol()
    {
        var (ast, result) = SemanticHelper.AnalyzeFile("function f() = 1;\nx = f();");
        var call = (FunctionCallExpression)((AssignmentStatement)ast.Statements[1]).Value;
        Symbol? symbol = result.Model.Resolve(call.Callee);
        Assert.NotNull(symbol);
        Assert.Equal(SymbolKind.Function, symbol.Kind);
        Assert.Same(ast.Statements[0], symbol.Declaration);
    }

    [Fact]
    public void ForwardReference_ResolvesToLaterDefinition()
    {
        // OpenSCAD collects all top-level definitions before evaluation, so calls may precede defs.
        var (ast, result) = SemanticHelper.AnalyzeFile("m();\nmodule m() cube(1);");
        Symbol? symbol = result.Model.Resolve(ast.Statements[0]);
        Assert.NotNull(symbol);
        Assert.Same(ast.Statements[1], symbol.Declaration);
    }

    [Fact]
    public void Recursion_BindsToOwnTopLevelSymbol()
    {
        var (ast, result) = SemanticHelper.AnalyzeFile("module m() m();");
        var module = (ModuleDefinition)ast.Statements[0];
        var selfCall = (ModuleInstantiation)module.Body;
        Symbol? symbol = result.Model.Resolve(selfCall);
        Assert.NotNull(symbol);
        Assert.Same(module, symbol.Declaration);
    }

    [Fact]
    public void NestedDefinition_ShadowsTopLevel_CallResolvesToNull()
    {
        // A module defined inside another's body is local; a call to it is not a renameable symbol.
        var (ast, result) = SemanticHelper.AnalyzeFile("module inner() sphere(1);\nmodule outer() { module inner() cube(1); inner(); }");
        var outer = (ModuleDefinition)ast.Statements[1];
        var block = (BlockStatement)outer.Body;
        var innerCall = (ModuleInstantiation)block.Statements[1];
        Assert.Null(result.Model.Resolve(innerCall));
    }

    [Fact]
    public void ReferencesTo_ReturnsExactlyTheBoundReferences()
    {
        var (ast, result) = SemanticHelper.AnalyzeFile("WALL = 2;\nGAP = 9;\na = WALL;\nb = WALL;\nc = GAP;");
        var firstRead = (Identifier)((AssignmentStatement)ast.Statements[2]).Value;
        Symbol wall = result.Model.Resolve(firstRead)!;

        IReadOnlyList<AstNode> references = result.Model.ReferencesTo(wall);
        Assert.Equal(2, references.Count);
        Assert.All(references, r => Assert.Equal("WALL", Assert.IsType<Identifier>(r).Name));

        var gapRead = (Identifier)((AssignmentStatement)ast.Statements[4]).Value;
        Assert.DoesNotContain(gapRead, references);
    }

    [Fact]
    public void ReferencesTo_UnreferencedDeclaration_IsEmpty()
    {
        var (ast, result) = SemanticHelper.AnalyzeFile("module unused() cube(1);\ncube(2);");
        var unused = (ModuleDefinition)ast.Statements[0];
        var symbol = new Symbol(SymbolKind.Module, "unused", ast.Source, unused);
        Assert.Empty(result.Model.ReferencesTo(symbol));
    }

    [Fact]
    public void ImmediatelyInvokedFunctionLiteral_CalleeWalkedNotResolvedAsName()
    {
        // `(function (x) x)(5)` — the callee is an expression, not a name; analysis must not throw and
        // must not invent a symbol for it.
        var (ast, result) = SemanticHelper.AnalyzeFile("v = (function (x) x)(5);");
        var call = (FunctionCallExpression)((AssignmentStatement)ast.Statements[0]).Value;
        Assert.Null(result.Model.Resolve(call.Callee));
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "SB3005");
    }
}
