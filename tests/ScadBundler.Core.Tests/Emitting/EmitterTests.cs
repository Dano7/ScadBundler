using ScadBundler.Core;
using ScadBundler.Core.Ast;
using ScadBundler.Core.Diagnostics;
using ScadBundler.Core.Emitting;
using ScadBundler.Core.Parsing;
using ScadBundler.Core.Tests.TestSupport;
using ScadBundler.Core.Text;
using Xunit;

namespace ScadBundler.Core.Tests.Emitting;

/// <summary>
/// Unit coverage for the <see cref="Emitter"/>: per-node default formatting (§5), precedence-correct
/// parenthesization, <c>--minify</c>, comment/trivia placement (EM-001), idempotence (EM-002), and the
/// structural round-trip self-check (SB6001 guard) over every node kind and the whole on-disk corpus.
/// </summary>
public sealed class EmitterTests
{
    // ---------------------------------------------------------------------------------------------
    // Per-node default formatting
    // ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("WALL = 2;", "WALL = 2;")]
    [InlineData("x=1+2*3;", "x = 1 + 2 * 3;")]
    [InlineData("cube([10, 20, 30], center = true);", "cube([10, 20, 30], center = true);")]
    [InlineData("translate([0,0,5]) rotate([0,0,45]) cube(10);", "translate([0, 0, 5]) rotate([0, 0, 45]) cube(10);")]
    [InlineData("module ring(d) circle(d);", "module ring(d) circle(d);")]
    [InlineData("function clamp(x, lo, hi) = x < lo ? lo : x;", "function clamp(x, lo, hi) = x < lo ? lo : x;")]
    [InlineData("r = [0:2:10];", "r = [0:2:10];")]
    [InlineData("a = [];", "a = [];")]
    [InlineData("m = v.x;", "m = v.x;")]
    [InlineData("i = v[0];", "i = v[0];")]
    [InlineData("#%cube(1);", "#%cube(1);")]
    [InlineData("n = -a;", "n = -a;")]
    [InlineData("p = (a + b);", "p = (a + b);")]
    [InlineData("lit = function (z) z * 2;", "lit = function(z) z * 2;")]
    [InlineData("e = each_v;", "e = each_v;")]
    public void EmitsSingleStatement_InCanonicalForm(string source, string expected)
    {
        string emitted = Emitter.Emit(ParseHelper.Parse(source).Root).TrimEnd('\n');
        Assert.Equal(expected, emitted);
    }

    [Fact]
    public void EmitsBlockBody_WithIndentedChildren()
    {
        string emitted = Emitter.Emit(ParseHelper.Parse("module wrapper(){children(0);children(1);}").Root);
        Assert.Equal(
            "module wrapper() {\n    children(0);\n    children(1);\n}\n",
            emitted);
    }

    [Fact]
    public void EmitsIfElseIfChain_OnOneLine()
    {
        string emitted = Emitter.Emit(ParseHelper.Parse("if (n==0) a(); else if (n==1) b(); else c();").Root).TrimEnd('\n');
        Assert.Equal("if(n == 0) a(); else if(n == 1) b(); else c();", emitted);
    }

    // ---------------------------------------------------------------------------------------------
    // Precedence-correct parenthesization (author parens preserved; gotchas re-emit unchanged)
    // ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("v = -2 ^ 2;", "v = -2 ^ 2;")]          // ^ binds tighter than unary minus
    [InlineData("v = 2 ^ 3 ^ 2;", "v = 2 ^ 3 ^ 2;")]   // ^ right-associative
    [InlineData("v = 2 ^ -1;", "v = 2 ^ -1;")]          // ^ right operand may be unary
    [InlineData("v = a || b && c;", "v = a || b && c;")] // && tighter than ||
    [InlineData("v = a | b & c + d;", "v = a | b & c + d;")] // bitwise/shift precedence
    [InlineData("v = (a + b) * c;", "v = (a + b) * c;")] // author parens preserved
    [InlineData("v = a - (b - c);", "v = a - (b - c);")] // needed paren preserved
    [InlineData("v = a ? b : c ? d : e;", "v = a ? b : c ? d : e;")] // ternary right-assoc
    public void Parenthesization_MatchesPrecedence(string source, string expected)
    {
        string emitted = Emitter.Emit(ParseHelper.Parse(source).Root).TrimEnd('\n');
        Assert.Equal(expected, emitted);
    }

    [Fact]
    public void Parenthesization_SynthesizedSubtree_InsertsMinimalParens()
    {
        // A hand-built (non-parsed) tree: -(a * b). The emitter must add the parens the parser would
        // otherwise omit, so the meaning survives a re-parse.
        var tree = new UnaryExpression(
            UnaryOperator.Negate,
            new BinaryExpression(BinaryOperator.Multiply, new Identifier("a"), new Identifier("b")));
        var file = new ScadFile(
            new SourceFile("t.scad", string.Empty),
            [new AssignmentStatement("v", tree)]);

        Assert.Equal("v = -(a * b);", Emitter.Emit(file).TrimEnd('\n'));
    }

    // ---------------------------------------------------------------------------------------------
    // Trivia / Customizer (EM-001)
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void Customizer_TriviaRoundTrips_AndStaysAttached()
    {
        const string source = "/* [Dimensions] */\n// Outer diameter of the part\ndiameter = 20; // [5:50]";
        ScadFile root = ParseHelper.Parse(source).Root;

        string emitted = Emitter.Emit(root).TrimEnd('\n');

        Assert.Equal(
            "/* [Dimensions] */\n// Outer diameter of the part\ndiameter = 20;  // [5:50]",
            emitted);

        // Re-parsing keeps all three comments attached to the same assignment.
        var assignment = Assert.IsType<AssignmentStatement>(ParseHelper.Parse(emitted).Root.Statements[0]);
        Assert.Equal(2, assignment.LeadingTrivia.Count);
        Assert.Single(assignment.TrailingTrivia);
    }

    [Fact]
    public void BlankLineBefore_RendersExactlyOneBlankLine()
    {
        string emitted = Emitter.Emit(ParseHelper.Parse("a = 1;\n\n\nb = 2;").Root);
        Assert.Equal("a = 1;\n\nb = 2;\n", emitted);
    }

    [Fact]
    public void LeadingBlankLine_IsSuppressedAtTopOfFile()
    {
        string emitted = Emitter.Emit(ParseHelper.Parse("\n\na = 1;").Root);
        Assert.Equal("a = 1;\n", emitted);
    }

    // ---------------------------------------------------------------------------------------------
    // Minify
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void Minify_DropsCommentsAndWhitespace_KeepsTokenSeparators()
    {
        const string source = "// header\nmodule ring(d) circle(d);\nx = 1 + 2;";
        string emitted = Emitter.Emit(ParseHelper.Parse(source).Root, new EmitOptions(Minify: true));

        Assert.DoesNotContain("//", emitted, StringComparison.Ordinal);
        // Comments and inner whitespace are dropped, but each top-level statement keeps its own line so
        // OpenSCAD's line-based Customizer extraction still works (Slice 7).
        Assert.Equal("module ring(d)circle(d);\nx=1+2;\n", emitted);
    }

    [Fact]
    public void Minify_KeepsSpaceBetweenAdjacentWords()
    {
        // `module` and the name must stay separated; `let`/keyword forms keep their needed spaces.
        string emitted = Emitter.Emit(ParseHelper.Parse("a = b ? c : d;").Root, new EmitOptions(Minify: true));
        Assert.Equal("a=b?c:d;\n", emitted);
    }

    [Fact]
    public void PreserveCommentsFalse_DropsComments_ButKeepsLayout()
    {
        const string source = "// header\nx = 1; // trailing";
        string emitted = Emitter.Emit(ParseHelper.Parse(source).Root, new EmitOptions(PreserveComments: false));
        Assert.Equal("x = 1;\n", emitted);
    }

    [Fact]
    public void StickyTrailingComment_SurvivesStripping_OrdinaryOneDropped()
    {
        // The inliner marks a hoisted parameter's inline Customizer annotation sticky so it rides the
        // minified line (mirroring sticky leading trivia); a non-sticky trailing comment still drops.
        AssignmentStatement annotated = Assignment("width", new CommentTrivia("// [1:100]", CommentKind.Line)
        {
            Span = SourceSpan.Synthetic,
            Sticky = true,
        });
        AssignmentStatement plain = Assignment("x", new CommentTrivia("// note", CommentKind.Line)
        {
            Span = SourceSpan.Synthetic,
        });
        var file = new ScadFile(SourceFile.Synthesized, [annotated, plain]);

        Assert.Equal("width=10;  // [1:100]\nx=10;\n", Emitter.Emit(file, new EmitOptions(Minify: true)));
        Assert.Equal("width = 10;  // [1:100]\nx = 10;\n", Emitter.Emit(file, new EmitOptions(PreserveComments: false)));
    }

    private static AssignmentStatement Assignment(string name, CommentTrivia trailing) =>
        new(name, new NumberLiteral(10, "10") { Span = SourceSpan.Synthetic })
        {
            Span = SourceSpan.Synthetic,
            TrailingTrivia = [trailing],
        };

    // ---------------------------------------------------------------------------------------------
    // Configurable formatting (brace style, indent style/width, EOF trivia)
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void BraceStyle_NextLine_PutsBraceOnItsOwnLine()
    {
        string emitted = Emitter.Emit(
            ParseHelper.Parse("module a() { cube(1); }").Root,
            new EmitOptions(BraceStyle: BraceStyle.NextLine));
        Assert.Equal("module a()\n{\n    cube(1);\n}\n", emitted);
    }

    [Fact]
    public void IndentStyle_Tabs_IndentsWithTabs()
    {
        string emitted = Emitter.Emit(
            ParseHelper.Parse("module a() { cube(1); }").Root,
            new EmitOptions(IndentStyle: IndentStyle.Tabs));
        Assert.Equal("module a() {\n\tcube(1);\n}\n", emitted);
    }

    [Fact]
    public void IndentWidth_IsConfigurable()
    {
        string emitted = Emitter.Emit(
            ParseHelper.Parse("module a() { cube(1); }").Root,
            new EmitOptions(IndentWidth: 2));
        Assert.Equal("module a() {\n  cube(1);\n}\n", emitted);
    }

    [Fact]
    public void EndOfFileComment_IsPreserved()
    {
        string emitted = Emitter.Emit(ParseHelper.Parse("cube(1);\n// trailing eof").Root);
        Assert.Equal("cube(1);\n// trailing eof\n", emitted);
    }

    [Fact]
    public void EmitOptions_Default_MatchesSpecifiedDefaults()
    {
        EmitOptions options = EmitOptions.Default;
        Assert.Equal(4, options.IndentWidth);
        Assert.Equal(IndentStyle.Spaces, options.IndentStyle);
        Assert.Equal(BraceStyle.SameLine, options.BraceStyle);
        Assert.Equal(0, options.MaxLineLength); // no wrapping by default; the CLI opts hardened output into 256
        Assert.False(options.Minify);
        Assert.True(options.PreserveComments);
        Assert.Equal(options, new EmitOptions()); // record value-equality
    }

    // ---------------------------------------------------------------------------------------------
    // Line wrapping (EmitOptions.MaxLineLength, ADR 0003)
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void Wrap_Minify_KeepsEveryLineWithinTheLimit()
    {
        const string source =
            "module grid(w) { translate([0,1,0]) cube([w,1,1]); translate([0,2,0]) cube([w,1,2]); "
            + "translate([0,3,0]) cube([w,1,3]); translate([0,4,0]) cube([w,1,4]); }";
        var options = new EmitOptions(Minify: true, MaxLineLength: 40);

        string emitted = Emitter.Emit(ParseHelper.Parse(source).Root, options);

        AssertLinesWithin(emitted, 40);
        Assert.True(Emitter.RoundTripsStructurally(ParseHelper.Parse(source).Root, options));
    }

    [Fact]
    public void Wrap_Minify_InsertsOnlyNewlines()
    {
        // A wrapped emit is the unwrapped emit plus line breaks — nothing else moves — so stripping
        // every newline from both recovers identical text.
        ScadFile root = ParseHelper.Parse(RichScad.Source).Root;
        string unwrapped = Emitter.Emit(root, new EmitOptions(Minify: true));
        string wrapped = Emitter.Emit(root, new EmitOptions(Minify: true, MaxLineLength: 60));

        Assert.Equal(
            unwrapped.Replace("\n", string.Empty, StringComparison.Ordinal),
            wrapped.Replace("\n", string.Empty, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Wrap_RoundTripsStructurally_AndIsIdempotent(bool minify)
    {
        var options = new EmitOptions(Minify: minify, MaxLineLength: 48);
        ScadFile root = ParseHelper.Parse(RichScad.Source).Root;

        Assert.True(Emitter.RoundTripsStructurally(root, options));

        string once = Emitter.Emit(root, options);
        string twice = Emitter.Emit(ParseHelper.Parse(once).Root, options);
        Assert.Equal(once, twice);
    }

    [Fact]
    public void Wrap_NeverBreaksAnAnnotatedParameterLine()
    {
        // The Customizer reads a parameter's annotation off the line its assignment STARTS on
        // (CommentParser.cc getComment), so the whole statement stays on one line even over the limit.
        var parsed = (AssignmentStatement)ParseHelper.Parse("width = [1111, 2222, 3333, 4444, 5555, 6666];").Root.Statements[0];
        AssignmentStatement annotated = parsed with
        {
            TrailingTrivia =
            [
                new CommentTrivia("// [1:100]", CommentKind.Line) { Span = SourceSpan.Synthetic, Sticky = true },
            ],
        };
        var file = new ScadFile(SourceFile.Synthesized, [annotated]);

        string emitted = Emitter.Emit(file, new EmitOptions(Minify: true, MaxLineLength: 30));

        string firstLine = emitted.Split('\n')[0];
        Assert.Contains("width=[1111,", firstLine, StringComparison.Ordinal);
        Assert.EndsWith("// [1:100]", firstLine, StringComparison.Ordinal);
    }

    [Fact]
    public void Wrap_BreaksAnUnannotatedTopLevelAssignment()
    {
        // Without an annotation there is nothing line-based for the Customizer to lose (description
        // and group comments are read relative to the assignment's FIRST line, which wrapping keeps).
        string emitted = Emitter.Emit(
            ParseHelper.Parse("width = [1111, 2222, 3333, 4444, 5555, 6666];").Root,
            new EmitOptions(Minify: true, MaxLineLength: 30));

        AssertLinesWithin(emitted, 30);
        Assert.Contains('\n', emitted.TrimEnd('\n'));
    }

    [Fact]
    public void Wrap_NeverSplitsAStringLiteral()
    {
        string longString = new('a', 60);
        string emitted = Emitter.Emit(
            ParseHelper.Parse($"s = \"{longString}\";").Root,
            new EmitOptions(Minify: true, MaxLineLength: 20));

        Assert.Contains($"\"{longString}\"", emitted, StringComparison.Ordinal); // over the limit but intact
    }

    [Fact]
    public void Wrap_NeverSplitsAnIncludePath()
    {
        const string source = "include <a/very/long/library/path/to/lib.scad>";
        string emitted = Emitter.Emit(ParseHelper.Parse(source).Root, new EmitOptions(Minify: true, MaxLineLength: 10));

        Assert.Contains(source, emitted, StringComparison.Ordinal);
    }

    [Fact]
    public void Wrap_LeavesStickyCommentLinesAlone()
    {
        // Comment trivia (the aggregated license header, the Customizer fence) is emitted verbatim —
        // an over-limit comment line is the author's, not the wrapper's.
        string text = "// " + new string('h', 60);
        var parsed = (AssignmentStatement)ParseHelper.Parse("x = 1;").Root.Statements[0];
        AssignmentStatement statement = parsed with
        {
            LeadingTrivia = [new CommentTrivia(text, CommentKind.Line) { Span = SourceSpan.Synthetic, Sticky = true }],
        };
        var file = new ScadFile(SourceFile.Synthesized, [statement]);

        string emitted = Emitter.Emit(file, new EmitOptions(Minify: true, MaxLineLength: 20));

        Assert.Equal(text, emitted.Split('\n')[0]);
    }

    [Fact]
    public void Wrap_PrettyMode_IndentsContinuationLines()
    {
        // Opt-in for pretty output: continuation lines sit two levels past the statement's indent, and
        // the break lands after the operator with its separator space dropped.
        string emitted = Emitter.Emit(
            ParseHelper.Parse("x = 1000 + 2000 + 3000;").Root,
            new EmitOptions(MaxLineLength: 12));

        Assert.Equal("x = 1000 +\n        2000 +\n        3000;\n", emitted);
    }

    [Fact]
    public void Wrap_PrettyMode_TabIndent_ContinuesWithTabs()
    {
        string emitted = Emitter.Emit(
            ParseHelper.Parse("x = 1000 + 2000 + 3000;").Root,
            new EmitOptions(MaxLineLength: 12, IndentStyle: IndentStyle.Tabs));

        Assert.Equal("x = 1000 +\n\t\t2000 +\n\t\t3000;\n", emitted);
    }

    private static void AssertLinesWithin(string emitted, int limit)
    {
        foreach (string line in emitted.Split('\n'))
        {
            Assert.True(line.Length <= limit, $"line exceeds {limit} chars: '{line}'");
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Structural round-trip (SB6001 self-check) over every node kind + the corpus
    // ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public void RichScad_RoundTripsStructurally(bool minify, bool preserveComments)
    {
        ScadFile root = ParseHelper.Parse(RichScad.Source).Root;
        Assert.True(Emitter.RoundTripsStructurally(root, new EmitOptions(Minify: minify, PreserveComments: preserveComments)));
    }

    [Fact]
    public void RichScad_IsIdempotent_UnderDefaultOptions()
    {
        ScadFile root = ParseHelper.Parse(RichScad.Source).Root;
        string once = Emitter.Emit(root);
        string twice = Emitter.Emit(ParseHelper.Parse(once).Root);
        Assert.Equal(once, twice);
    }

    public static TheoryData<string> CleanCorpusFiles()
    {
        var data = new TheoryData<string>();
        string corpus = Path.Combine(CorpusLocator.RepoRoot, "tests", "Corpus");
        foreach (string path in Directory.EnumerateFiles(corpus, "*.scad", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.Ordinal))
        {
            data.Add(path);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(CleanCorpusFiles))]
    public void CorpusFile_RoundTripsStructurally(string path)
    {
        var source = new SourceFile(path, File.ReadAllText(path));
        ParseResult parsed = Parser.Parse(source);
        if (parsed.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            return; // deliberately-malformed fixtures (e.g. the unterminated-string lexer case) are skipped
        }

        Assert.True(Emitter.RoundTripsStructurally(parsed.Root), $"default emit did not round-trip: {path}");
        Assert.True(Emitter.RoundTripsStructurally(parsed.Root, new EmitOptions(Minify: true)), $"minified emit did not round-trip: {path}");
    }
}
