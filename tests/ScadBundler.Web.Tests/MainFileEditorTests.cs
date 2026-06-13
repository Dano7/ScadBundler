using Bunit;
using Microsoft.Extensions.DependencyInjection;
using ScadBundler.Web.Components;
using ScadBundler.Web.State;
using Xunit;

namespace ScadBundler.Web.Tests;

/// <summary>
/// bUnit smoke tests for <see cref="MainFileEditor"/> (Slice W2 §2.3): editing the textarea re-analyzes the
/// project (debounced), and the textarea reloads when the root <i>file</i> changes (promote/replace) without
/// clobbering in-progress typing.
/// </summary>
public sealed class MainFileEditorTests : TestContext
{
    [Fact]
    public void EditingTextarea_ReanalyzesAfterDebounce()
    {
        var controller = new WorkspaceController();
        controller.AddOrReplace([new UploadedFile("main.scad", "cube(1);\n")]);
        Services.AddSingleton(controller);

        IRenderedComponent<MainFileEditor> cut = RenderComponent<MainFileEditor>(p => p
            .Add(c => c.Root, controller.Root)
            .Add(c => c.RootText, controller.RootText)
            .Add(c => c.DebounceMs, 1));

        // Add a reference to a not-yet-uploaded library: after the debounce, analysis must report it missing.
        cut.Find("textarea").Input("include <extra.scad>\ncube(1);\n");

        // Generous window: the debounce is a real timer, and this assembly may run alongside the CPU-heavy
        // OpenSCAD integration suite — WaitForAssertion polls, so it returns as soon as the edit lands.
        cut.WaitForAssertion(
            () => Assert.Contains(controller.Analysis!.Missing, m => m.RawPath == "extra.scad"),
            TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void ReloadsTextarea_WhenRootFileChanges()
    {
        var controller = new WorkspaceController();
        Services.AddSingleton(controller);

        IRenderedComponent<MainFileEditor> cut = RenderComponent<MainFileEditor>(p => p
            .Add(c => c.Root, "/proj/a.scad")
            .Add(c => c.RootText, "// file A\n"));
        Assert.Contains("// file A", cut.Markup);

        cut.SetParametersAndRender(p => p
            .Add(c => c.Root, "/proj/b.scad")
            .Add(c => c.RootText, "// file B\n"));

        Assert.Contains("// file B", cut.Markup);
        Assert.DoesNotContain("// file A", cut.Markup);
    }
}
