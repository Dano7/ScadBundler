using Bunit;
using Microsoft.Extensions.DependencyInjection;
using ScadBundler.Web.Components;
using ScadBundler.Web.State;
using Xunit;

namespace ScadBundler.Web.Tests;

/// <summary>
/// bUnit smoke tests for <see cref="ConflictPicker"/> (Slice W2 §2.7): a basename matched by two uploads
/// surfaces both candidates; picking one (or typing a path) re-adds it deterministically, clearing the
/// ambiguity so the bundle appears. The ambiguity is produced by the real <see cref="ProjectAnalyzer"/>.
/// The recompute is async since Slice W5 §C1, so the helper awaits it and resolve clicks await before asserting.
/// </summary>
public sealed class ConflictPickerTests : TestContext
{
    // main.scad needs <utils.scad>; two uploads carry that basename at different sub-paths ⇒ Ambiguous.
    private async Task<(WorkspaceController Controller, AmbiguousReference Ref)> AmbiguousAsync()
    {
        var controller = new WorkspaceController { DebounceMs = 0 };
        controller.AddOrReplace(
        [
            new UploadedFile("main.scad", "include <utils.scad>\ncube(1);\n"),
            new UploadedFile("extra/utils.scad", "a = 1;\n"),
            new UploadedFile("helpers/utils.scad", "b = 2;\n"),
        ]);
        await controller.Recomputing;
        AmbiguousReference ambiguous = Assert.Single(controller.Analysis!.Ambiguous);
        Assert.Equal(2, ambiguous.Candidates.Count);
        Services.AddSingleton(controller);
        return (controller, ambiguous);
    }

    [Fact]
    public async Task ListsBothCandidates()
    {
        (_, AmbiguousReference ambiguous) = await AmbiguousAsync();

        IRenderedComponent<ConflictPicker> cut = RenderComponent<ConflictPicker>(p =>
            p.Add(c => c.Ambiguous, ambiguous));

        Assert.Contains("utils.scad", cut.Markup);
        Assert.Contains("extra/utils.scad", cut.Markup);
        Assert.Contains("helpers/utils.scad", cut.Markup);
        Assert.Equal(2, cut.FindAll("input[type=radio]").Count);
    }

    [Fact]
    public async Task UseThisFile_ResolvesAndBundles()
    {
        (WorkspaceController controller, AmbiguousReference ambiguous) = await AmbiguousAsync();
        IRenderedComponent<ConflictPicker> cut = RenderComponent<ConflictPicker>(p =>
            p.Add(c => c.Ambiguous, ambiguous));

        cut.FindAll("button")[0].Click();             // "Use this file" — default selection = first candidate
        await controller.Recomputing;

        Assert.Empty(controller.Analysis!.Ambiguous);
        Assert.NotNull(controller.Bundle);
        Assert.True(controller.Bundle!.Ok);
    }

    [Fact]
    public async Task InlinePathField_PlacesSelectedCandidate()
    {
        (WorkspaceController controller, AmbiguousReference ambiguous) = await AmbiguousAsync();
        IRenderedComponent<ConflictPicker> cut = RenderComponent<ConflictPicker>(p =>
            p.Add(c => c.Ambiguous, ambiguous));

        cut.FindAll("input[type=radio]")[1].Change("on");          // select the second candidate (b = 2)
        cut.Find("input.conflict-path").Input("utils.scad");       // type the path it should live at
        cut.FindAll("button")[1].Click();                          // "Set path"
        await controller.Recomputing;

        Assert.Empty(controller.Analysis!.Ambiguous);
        Assert.Contains(controller.Uploads, u => u.Name == "utils.scad" && u.Text == "b = 2;\n");
        Assert.NotNull(controller.Bundle);
    }
}
