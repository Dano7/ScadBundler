using ScadBundler.Core.Diagnostics;
using ScadBundler.Core.Inlining;
using ScadBundler.Core.Parsing;
using ScadBundler.Core.Tests.TestSupport;
using Xunit;

namespace ScadBundler.Core.Tests.Inlining;

/// <summary>
/// Per-node-kind tests for the SB5008 free-read walk (<see cref="ForwardReferenceChecker"/>), run
/// directly over parsed statements: every eagerly-evaluated expression position must surface a
/// forward read, while binder-introduced names (let/for/comprehensions), lazy positions
/// (function-literal bodies and defaults), and callee identifiers must not. Assembled-bundle SB5008
/// shapes (hoisting, last-wins emission) live in <see cref="Slice5BundleTests"/>.
/// </summary>
public sealed class ForwardReferenceCheckerTests
{
    [Fact]
    public void Conditional_WalksConditionThenAndElse()
    {
        AssertForwardReads("a = c ? t : e;\nc = true;\nt = 1;\ne = 2;", "c", "t", "e");
    }

    [Fact]
    public void Parenthesized_WalksInner()
    {
        AssertForwardReads("a = (fwd);\nfwd = 1;", "fwd");
    }

    [Fact]
    public void Index_WalksTargetAndIndex()
    {
        AssertForwardReads("a = vec[idx];\nvec = [1, 2];\nidx = 0;", "vec", "idx");
    }

    [Fact]
    public void Member_WalksTarget()
    {
        AssertForwardReads("a = pos.x;\npos = [1, 2];", "pos");
    }

    [Fact]
    public void ParenthesizedCallee_IsAVariableRead()
    {
        // Only a *bare identifier* callee is a scope-wide function reference; `(fn)` is an
        // expression evaluating to a function value, so `fn` is an order-sensitive variable read.
        AssertForwardReads("a = (fn)(arg);\nfn = function (x) x;\narg = 1;", "fn", "arg");
    }

    [Fact]
    public void AssertExpression_WalksArgumentsAndBody()
    {
        AssertForwardReads("a = assert(cond, msg) val;\ncond = true;\nmsg = \"m\";\nval = 1;", "cond", "msg", "val");
    }

    [Fact]
    public void AssertExpression_WithoutBody_WalksArguments()
    {
        AssertForwardReads("a = assert(cond);\ncond = true;", "cond");
    }

    [Fact]
    public void EchoExpression_WalksArgumentsAndBody()
    {
        AssertForwardReads("a = echo(msg) val;\nmsg = \"m\";\nval = 1;", "msg", "val");
    }

    [Fact]
    public void EchoExpression_WithoutBody_WalksArguments()
    {
        AssertForwardReads("a = echo(msg);\nmsg = \"m\";", "msg");
    }

    [Fact]
    public void FunctionLiteral_BodyAndDefaults_AreLazy_NeverWarn()
    {
        // Both the default and the body evaluate at call time, when the file scope is complete.
        AssertForwardReads("a = function (x = fwd) x + fwd;\nfwd = 1;");
    }

    [Fact]
    public void ForComprehension_WalksSourceAndBody_LoopVariableIsBound()
    {
        AssertForwardReads("a = [for (j = src) j + fwd];\nsrc = [1, 2];\nfwd = 3;", "src", "fwd");
    }

    [Fact]
    public void CStyleForComprehension_WalksInitConditionUpdateAndBody_LoopVariableIsBound()
    {
        // `j` is bound by the init clause, so `j < limit` and `j = j + step` read only the free names.
        AssertForwardReads(
            "a = [for (j = init; j < limit; j = j + step) j + extra];\ninit = 0;\nlimit = 3;\nstep = 1;\nextra = 10;",
            "init", "limit", "step", "extra");
    }

    [Fact]
    public void IfComprehension_WalksConditionThenAndElse()
    {
        AssertForwardReads("a = [for (j = [0]) if (cnd) thn else els];\ncnd = true;\nthn = 1;\nels = 2;", "cnd", "thn", "els");
    }

    [Fact]
    public void IfComprehension_FilterForm_WalksConditionAndThen()
    {
        AssertForwardReads("a = [for (j = [0]) if (cnd) thn];\ncnd = true;\nthn = 1;", "cnd", "thn");
    }

    [Fact]
    public void LetComprehension_WalksBindingValuesAndGeneratorBody_BindingIsBound()
    {
        // A `let` whose body is a generator parses as LetComprehension (vs. LetExpression);
        // `n` is bound for both the range and the yielded expression.
        AssertForwardReads("a = [let (n = src) for (i = [0:n]) i + fwd];\nsrc = 1;\nfwd = 2;", "src", "fwd");
    }

    [Fact]
    public void Each_WalksValue()
    {
        AssertForwardReads("a = [each fwd];\nfwd = [1, 2];", "fwd");
    }

    /// <summary>Parses the snippet (asserting it is clean) and runs only the checker over it.</summary>
    private static IReadOnlyList<Diagnostic> Check(string source)
    {
        ParseResult result = ParseHelper.Parse(source);
        Assert.Empty(result.Diagnostics);

        var bag = new DiagnosticBag();
        ForwardReferenceChecker.Check(result.Root.Statements, bag);
        return bag.ToList();
    }

    /// <summary>Asserts the checker reports exactly the given forward reads, in walk order.</summary>
    private static void AssertForwardReads(string source, params string[] expectedReads)
    {
        IReadOnlyList<Diagnostic> diagnostics = Check(source);
        Assert.Equal(expectedReads.Length, diagnostics.Count);
        for (int i = 0; i < expectedReads.Length; i++)
        {
            Assert.Equal(DiagnosticCode.ForwardReference, diagnostics[i].Code);
            Assert.Equal(DiagnosticSeverity.Warning, diagnostics[i].Severity);
            Assert.Contains($"reads '{expectedReads[i]}'", diagnostics[i].Message, StringComparison.Ordinal);
        }
    }
}
