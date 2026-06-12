using Bunit;
using ScadBundler.Web.Components;
using Xunit;

namespace ScadBundler.Web.Tests;

/// <summary>
/// bUnit smoke tests for <see cref="FileList"/>: the inferred entry point is badged, resolved files show a
/// loaded icon, font pass-throughs show a font icon, and an unresolved reference is rendered as a "needed"
/// row (Slice W1 §5). The analysis is built by the real <see cref="ProjectAnalyzer"/> so the test exercises
/// the same projection the app uses.
/// </summary>
public sealed class FileListTests : TestContext
{
    [Fact]
    public void RendersEntryPointBadge_LoadedIcons_AndFontRow()
    {
        UploadedFile[] uploads =
        [
            new("main.scad", "use <lib.scad>\nuse <Helvetica.ttf>\nwidget();\n"),
            new("lib.scad", "module widget() cube(1);\n"),
        ];
        (_, ProjectAnalysis analysis) = ProjectAnalyzer.Analyze(uploads);

        IRenderedComponent<FileList> cut = RenderComponent<FileList>(p => p.Add(c => c.Analysis, analysis));

        Assert.Contains("★", cut.Markup);            // entry-point badge
        Assert.Contains("main.scad", cut.Markup);
        Assert.Contains("lib.scad", cut.Markup);
        Assert.Contains("✓", cut.Markup);            // loaded status icon
        Assert.Contains("Helvetica.ttf", cut.Markup);
        Assert.Contains("ⓕ", cut.Markup);            // font status icon
    }

    [Fact]
    public void RendersNeededRow_ForMissingReference()
    {
        UploadedFile[] uploads = [new("main.scad", "include <missing.scad>\ncube(1);\n")];
        (_, ProjectAnalysis analysis) = ProjectAnalyzer.Analyze(uploads);
        Assert.NotEmpty(analysis.Missing);

        IRenderedComponent<FileList> cut = RenderComponent<FileList>(p => p.Add(c => c.Analysis, analysis));

        Assert.Contains("⚠", cut.Markup);            // needed status icon
        Assert.Contains("missing.scad", cut.Markup);
        Assert.Contains("needed by", cut.Markup);
    }

    [Fact]
    public void RendersNothing_WhenAnalysisNull()
    {
        IRenderedComponent<FileList> cut = RenderComponent<FileList>();

        Assert.Empty(cut.Markup.Trim());
    }
}
