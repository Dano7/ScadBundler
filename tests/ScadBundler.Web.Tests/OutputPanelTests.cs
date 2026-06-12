using System.Text;
using Bunit;
using ScadBundler.Web.Components;
using Xunit;

namespace ScadBundler.Web.Tests;

/// <summary>
/// bUnit smoke tests for <see cref="OutputPanel"/>: Copy/Download are live <b>iff</b> the bundle is
/// <c>Ok</c> and non-empty (Slice W1 §5), the download name follows the CLI's
/// <c>&lt;rootstem&gt;.bundled.scad</c> shape, and nothing renders before a bundle exists.
/// </summary>
public sealed class OutputPanelTests : TestContext
{
    private static WebBundleResult OkResult(string text) =>
        new(text, true, [], new BundleStats(0, Encoding.UTF8.GetByteCount(text), 0, 0, 0));

    [Fact]
    public void EnablesButtons_AndNamesDownload_WhenOkAndNonEmpty()
    {
        IRenderedComponent<OutputPanel> cut = RenderComponent<OutputPanel>(p => p
            .Add(c => c.Result, OkResult("cube(1);\n"))
            .Add(c => c.RootPath, "/proj/ForkedHolder.scad"));

        Assert.All(cut.FindAll("button"), b => Assert.False(b.HasAttribute("disabled")));
        Assert.Contains("ForkedHolder.bundled.scad", cut.Markup);
    }

    [Fact]
    public void DisablesButtons_WhenNotOk()
    {
        var blocked = new WebBundleResult(string.Empty, false, [], new BundleStats(0, 0, 0, 0, 0));

        IRenderedComponent<OutputPanel> cut = RenderComponent<OutputPanel>(p => p.Add(c => c.Result, blocked));

        Assert.All(cut.FindAll("button"), b => Assert.True(b.HasAttribute("disabled")));
    }

    [Fact]
    public void DisablesButtons_WhenOkButEmpty()
    {
        var empty = new WebBundleResult(string.Empty, true, [], new BundleStats(0, 0, 0, 0, 0));

        IRenderedComponent<OutputPanel> cut = RenderComponent<OutputPanel>(p => p.Add(c => c.Result, empty));

        Assert.All(cut.FindAll("button"), b => Assert.True(b.HasAttribute("disabled")));
    }

    [Fact]
    public void RendersNothing_WhenResultNull()
    {
        IRenderedComponent<OutputPanel> cut = RenderComponent<OutputPanel>();

        Assert.Empty(cut.Markup.Trim());
    }
}
