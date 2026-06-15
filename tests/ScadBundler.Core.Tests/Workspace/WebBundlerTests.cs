using System.Linq;
using System.Text;
using ScadBundler.Core.Inlining;
using ScadBundler.Core.Workspace;
using Xunit;

namespace ScadBundler.Core.Tests.Workspace;

/// <summary>
/// <see cref="WebBundler"/>: the <see cref="WebBundleOptions"/> → <see cref="BundleOptions"/> /
/// <see cref="Emitting.EmitOptions"/> mapping (observed behaviorally), Error-gating, and the stats
/// projection. Byte-level CLI parity is proven separately in <see cref="BundleParityTests"/>.
/// </summary>
public sealed class WebBundlerTests
{
    private static WebBundleResult Bundle(WebBundleOptions options, params UploadedFile[] uploads)
    {
        (InMemoryFileSystem fs, ProjectAnalysis analysis) = ProjectAnalyzer.Analyze(uploads);
        return WebBundler.Bundle(fs, analysis.Root!, options);
    }

    // ----- Option mapping (behavioral) -------------------------------------------------------------

    // A mid-file comment (not the leading line) is an ordinary comment — the leading comment of a file is
    // instead hoisted as a sticky header by the attribution pass and survives hardening.
    private const string MidComment = "size = 1;\n// MARKER\ncube(size);";

    [Fact]
    public void Normal_PreservesComments()
    {
        WebBundleResult result = Bundle(new WebBundleOptions(), new UploadedFile("main.scad", MidComment));

        Assert.True(result.Ok);
        Assert.Contains("// MARKER", result.Text);
    }

    [Fact]
    public void Normal_NoPreserveComments_DropsComments()
    {
        WebBundleResult result = Bundle(
            new WebBundleOptions(PreserveComments: false),
            new UploadedFile("main.scad", MidComment));

        Assert.DoesNotContain("MARKER", result.Text);
    }

    [Fact]
    public void Minify_DropsCommentsAndCollapsesWhitespace()
    {
        var upload = new UploadedFile("main.scad", MidComment);
        WebBundleResult normal = Bundle(new WebBundleOptions(), upload);
        WebBundleResult minified = Bundle(new WebBundleOptions(Hardening: HardeningProfile.Minify), upload);

        Assert.DoesNotContain("MARKER", minified.Text);
        Assert.True(minified.Text.Length < normal.Text.Length);
        Assert.DoesNotContain("size = 1", minified.Text); // intra-statement whitespace collapsed
    }

    [Fact]
    public void Obfuscate_DropsOrdinaryCommentsButKeepsFormatting()
    {
        WebBundleResult result = Bundle(
            new WebBundleOptions(Hardening: HardeningProfile.Obfuscate),
            new UploadedFile("main.scad", MidComment));

        Assert.True(result.Ok);
        Assert.DoesNotContain("MARKER", result.Text);
        Assert.Contains("\n", result.Text); // not collapsed like minify
    }

    [Fact]
    public void Obfuscate_KeepsLicenseHeader_UnlessStripped()
    {
        var upload = new UploadedFile(
            "main.scad",
            "// Copyright 2026 ACME — MIT License\n// permission is hereby granted\nmodule keep() cube(1);\nkeep();");

        WebBundleResult kept = Bundle(new WebBundleOptions(Hardening: HardeningProfile.Obfuscate), upload);
        WebBundleResult stripped = Bundle(
            new WebBundleOptions(Hardening: HardeningProfile.Obfuscate, StripLicense: true), upload);

        Assert.Contains("MIT License", kept.Text);
        Assert.DoesNotContain("MIT License", stripped.Text);
    }

    // ----- Error gating ----------------------------------------------------------------------------

    [Fact]
    public void Cycle_GatesOutput()
    {
        (InMemoryFileSystem fs, _) = ProjectAnalyzer.Analyze(
        [
            new UploadedFile("x.scad", "include <y.scad>\ncube(1);"),
            new UploadedFile("y.scad", "include <x.scad>\nsphere(1);"),
        ]);

        WebBundleResult result = WebBundler.Bundle(fs, "/proj/x.scad", new WebBundleOptions());

        Assert.False(result.Ok);
        Assert.Equal(string.Empty, result.Text);
        Assert.Contains(result.Diagnostics, d => d.Code == "SB4002");
    }

    [Fact]
    public void OnCollisionError_GatesOutput()
    {
        WebBundleResult result = Bundle(
            new WebBundleOptions(OnCollision: CollisionStrategy.Error),
            new UploadedFile("main.scad", "include <a.scad>\ninclude <b.scad>\nfoo();"),
            new UploadedFile("a.scad", "module foo() cube(1);"),
            new UploadedFile("b.scad", "module foo() cube(2);"));

        Assert.False(result.Ok);
        Assert.Equal(string.Empty, result.Text);
        Assert.Contains(result.Diagnostics, d => d.Code == "SB5006");
    }

    // ----- Stats -----------------------------------------------------------------------------------

    [Fact]
    public void Stats_OutputBytes_IsUtf8Length()
    {
        WebBundleResult result = Bundle(new WebBundleOptions(), new UploadedFile("main.scad", "cube(1);"));
        Assert.Equal(Encoding.UTF8.GetByteCount(result.Text), result.Stats.OutputBytes);
    }

    [Fact]
    public void Stats_FilesInlined_CountsDistinctNonRootFiles()
    {
        WebBundleResult result = Bundle(
            new WebBundleOptions(),
            new UploadedFile("main.scad", "include <a.scad>\nuse <b.scad>\ncube(1);\nb();"),
            new UploadedFile("a.scad", "x = 1;"),
            new UploadedFile("b.scad", "module b() sphere(1);"));

        Assert.Equal(2, result.Stats.FilesInlined);
    }

    [Fact]
    public void Stats_FilesInlined_PrecomputedReusesAnalyzerCount_ByteIdentical()
    {
        // Slice W5 §C2: the analyzer already loaded the graph, so the bundle phase can take its FilesInlined
        // count instead of re-loading. The precomputed path must match the self-recount exactly — same count,
        // same bytes.
        (InMemoryFileSystem fs, ProjectAnalysis analysis) = ProjectAnalyzer.Analyze(
        [
            new UploadedFile("main.scad", "include <a.scad>\nuse <b.scad>\ncube(1);\nb();"),
            new UploadedFile("a.scad", "x = 1;"),
            new UploadedFile("b.scad", "module b() sphere(1);"),
        ]);

        WebBundleResult recount = WebBundler.Bundle(fs, analysis.Root!, new WebBundleOptions());
        WebBundleResult reused = WebBundler.Bundle(fs, analysis.Root!, new WebBundleOptions(), analysis.FilesInlined);

        Assert.Equal(2, analysis.FilesInlined);
        Assert.Equal(recount.Stats.FilesInlined, reused.Stats.FilesInlined);
        Assert.Equal(recount.Text, reused.Text);
    }

    [Fact]
    public void Stats_DefinitionsRemoved_ReadsTreeShakenCountUnderMinify()
    {
        WebBundleResult result = Bundle(
            new WebBundleOptions(Hardening: HardeningProfile.Minify),
            new UploadedFile("main.scad", "module unused() cube(9);\ncube(1);"));

        Assert.True(result.Stats.DefinitionsRemoved >= 1);
        Assert.Equal(0, Bundle(new WebBundleOptions(), new UploadedFile("main.scad", "cube(1);"))
            .Stats.DefinitionsRemoved); // 0 when no hardening profile ran
    }

    [Fact]
    public void Stats_RenamesAndNormalizations_MatchDiagnosticCounts()
    {
        // A deprecated assign(...) normalizes to let(...) (SB5001) → Normalizations.
        WebBundleResult result = Bundle(
            new WebBundleOptions(),
            new UploadedFile("main.scad", "assign(x = 1) cube(x);"));

        int sb5001 = result.Diagnostics.Count(d => d.Code == "SB5001");
        int sb5002 = result.Diagnostics.Count(d => d.Code == "SB5002");
        int sb5004 = result.Diagnostics.Count(d => d.Code == "SB5004");
        Assert.Equal(sb5001 + sb5002, result.Stats.Normalizations);
        Assert.Equal(sb5004, result.Stats.Renames);
        Assert.True(result.Stats.Normalizations >= 1);
    }

    [Fact]
    public void Diagnostics_AreProjectedToDtoFields()
    {
        WebBundleResult result = Bundle(
            new WebBundleOptions(),
            new UploadedFile("main.scad", "assign(x = 1) cube(x);"));

        DiagnosticDto dto = Assert.Single(result.Diagnostics, d => d.Code == "SB5001");
        Assert.Equal("Warning", dto.Severity);
        Assert.False(string.IsNullOrEmpty(dto.Message));
        Assert.True(dto.Line >= 1 && dto.Column >= 1);
    }
}
