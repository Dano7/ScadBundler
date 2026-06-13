using Bunit;
using ScadBundler.Web.Components;
using Xunit;

namespace ScadBundler.Web.Tests;

/// <summary>
/// bUnit smoke tests for <see cref="ProblemsPanel"/> (Slice W2 §2.5): real syntax/semantic diagnostics show
/// <c>file : line : col</c> + message + a friendly per-code line, and <b>SB4001 never appears</b> (it is the
/// file list's ⚠ rows, not a "problem").
/// </summary>
public sealed class ProblemsPanelTests : TestContext
{
    [Fact]
    public void RendersLocation_Message_AndFriendlyLine()
    {
        DiagnosticDto[] diagnostics =
        [
            new("SB3004", "Warning", "module 'widget' is defined more than once", "/proj/lib.scad", 3, 5),
        ];

        IRenderedComponent<ProblemsPanel> cut = RenderComponent<ProblemsPanel>(p =>
            p.Add(c => c.Diagnostics, diagnostics));

        Assert.Contains("lib.scad : 3 : 5", cut.Markup);
        Assert.Contains("module 'widget' is defined more than once", cut.Markup);
        Assert.Contains("the later one is used", cut.Markup);    // friendly explanation for SB3004
    }

    [Fact]
    public void NeverShowsSb4001()
    {
        DiagnosticDto[] diagnostics =
        [
            new("SB4001", "Warning", "MISSING_FILE_SENTINEL", "/proj/main.scad", 1, 1),
            new("SB3003", "Warning", "variable 'x' reassigned", "/proj/main.scad", 2, 1),
        ];

        IRenderedComponent<ProblemsPanel> cut = RenderComponent<ProblemsPanel>(p =>
            p.Add(c => c.Diagnostics, diagnostics));

        Assert.DoesNotContain("MISSING_FILE_SENTINEL", cut.Markup);
        Assert.Contains("variable 'x' reassigned", cut.Markup);
    }

    [Fact]
    public void RendersNothing_WhenNoDiagnostics()
    {
        IRenderedComponent<ProblemsPanel> empty = RenderComponent<ProblemsPanel>(p =>
            p.Add(c => c.Diagnostics, []));
        Assert.Empty(empty.Markup.Trim());

        IRenderedComponent<ProblemsPanel> nul = RenderComponent<ProblemsPanel>();
        Assert.Empty(nul.Markup.Trim());
    }
}
