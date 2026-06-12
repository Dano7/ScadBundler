using ScadBundler.Web.State;
using Xunit;

namespace ScadBundler.Web.Tests;

/// <summary>
/// <see cref="WorkspaceController"/> orchestration: a complete set bundles, an incomplete one does not, and
/// supplying the missing file completes the cycle — proving the analyze → gate → bundle wiring (Design §3.2).
/// </summary>
public sealed class WorkspaceControllerTests
{
    [Fact]
    public void CompleteSet_ProducesNonEmptyBundle_AndFiresChanged()
    {
        var controller = new WorkspaceController();
        int changes = 0;
        controller.Changed += () => changes++;

        controller.AddOrReplace(
        [
            new UploadedFile("main.scad", "use <lib.scad>\nwidget(3);\n"),
            new UploadedFile("lib.scad", "module widget(w) cube(w);\n"),
        ]);

        Assert.Equal(1, changes);
        Assert.NotNull(controller.Analysis);
        Assert.NotNull(controller.Bundle);
        Assert.True(controller.Bundle!.Ok);
        Assert.NotEqual(0, controller.Bundle.Text.Length);
        Assert.Contains("widget", controller.Bundle.Text);
    }

    [Fact]
    public void IncompleteSet_LeavesBundleNull_AndReportsMissing()
    {
        var controller = new WorkspaceController();

        controller.AddOrReplace([new UploadedFile("main.scad", "include <missing.scad>\ncube(1);\n")]);

        Assert.NotNull(controller.Analysis);
        Assert.NotEmpty(controller.Analysis!.Missing);
        Assert.Null(controller.Bundle);
    }

    [Fact]
    public void AddingTheMissingFile_CompletesAndBundles()
    {
        var controller = new WorkspaceController();
        controller.AddOrReplace([new UploadedFile("main.scad", "include <lib.scad>\ncube(1);\n")]);
        Assert.Null(controller.Bundle);

        controller.AddOrReplace([new UploadedFile("lib.scad", "wall = 2;\n")]);

        Assert.NotNull(controller.Bundle);
        Assert.True(controller.Bundle!.Ok);
    }
}
