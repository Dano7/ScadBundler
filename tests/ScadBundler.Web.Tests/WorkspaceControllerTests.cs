using ScadBundler.Web.State;
using Xunit;

namespace ScadBundler.Web.Tests;

/// <summary>
/// <see cref="WorkspaceController"/> orchestration: a complete set bundles, an incomplete one does not, and
/// supplying the missing file completes the cycle — proving the analyze → gate → bundle wiring (Design §3.2).
/// Since Slice W5 §C1 the recompute is asynchronous and phased, so tests run intents with
/// <see cref="WorkspaceController.DebounceMs"/> at <c>0</c> and <c>await</c> <see cref="WorkspaceController.Recomputing"/>
/// before asserting the settled result; the phase transitions and intent coalescing are covered directly.
/// </summary>
public sealed class WorkspaceControllerTests
{
    [Fact]
    public async Task CompleteSet_ProducesNonEmptyBundle_AndFiresChanged()
    {
        var controller = new WorkspaceController { DebounceMs = 0 };
        int changes = 0;
        controller.Changed += () => changes++;

        controller.AddOrReplace(
        [
            new UploadedFile("main.scad", "use <lib.scad>\nwidget(3);\n"),
            new UploadedFile("lib.scad", "module widget(w) cube(w);\n"),
        ]);
        await controller.Recomputing;

        Assert.True(changes > 0);                          // at least one phase tick fired
        Assert.Equal(BusyPhase.Idle, controller.BusyPhase); // settled
        Assert.NotNull(controller.Analysis);
        Assert.NotNull(controller.Bundle);
        Assert.True(controller.Bundle!.Ok);
        Assert.NotEqual(0, controller.Bundle.Text.Length);
        Assert.Contains("widget", controller.Bundle.Text);
    }

    [Fact]
    public async Task IncompleteSet_LeavesBundleNull_AndReportsMissing()
    {
        var controller = new WorkspaceController { DebounceMs = 0 };

        controller.AddOrReplace([new UploadedFile("main.scad", "include <missing.scad>\ncube(1);\n")]);
        await controller.Recomputing;

        Assert.NotNull(controller.Analysis);
        Assert.NotEmpty(controller.Analysis!.Missing);
        Assert.Null(controller.Bundle);
    }

    [Fact]
    public async Task AddingTheMissingFile_CompletesAndBundles()
    {
        var controller = new WorkspaceController { DebounceMs = 0 };
        controller.AddOrReplace([new UploadedFile("main.scad", "include <lib.scad>\ncube(1);\n")]);
        await controller.Recomputing;
        Assert.Null(controller.Bundle);

        controller.AddOrReplace([new UploadedFile("lib.scad", "wall = 2;\n")]);
        await controller.Recomputing;

        Assert.NotNull(controller.Bundle);
        Assert.True(controller.Bundle!.Ok);
    }

    [Fact]
    public async Task RootText_ReflectsRoot_AndIsNullWithoutOne()
    {
        var controller = new WorkspaceController { DebounceMs = 0 };
        Assert.Null(controller.RootText);

        controller.AddOrReplace([new UploadedFile("main.scad", "cube(1);\n")]);
        await controller.Recomputing;

        Assert.Equal("/proj/main.scad", controller.Root);
        Assert.Equal("cube(1);\n", controller.RootText);
    }

    [Fact]
    public async Task EditMainFile_ReplacesRootText_AndReanalyzes()
    {
        var controller = new WorkspaceController { DebounceMs = 0 };
        controller.AddOrReplace([new UploadedFile("main.scad", "cube(1);\n")]);
        await controller.Recomputing;
        Assert.NotNull(controller.Bundle);

        controller.EditMainFile("include <lib.scad>\ncube(1);\n");
        await controller.Recomputing;

        Assert.Equal("include <lib.scad>\ncube(1);\n", controller.RootText);
        Assert.Contains(controller.Analysis!.Missing, m => m.RawPath == "lib.scad");
        Assert.Null(controller.Bundle);                 // now blocked on the missing library
    }

    [Fact]
    public void EditMainFile_IsNoOp_WithoutARoot()
    {
        var controller = new WorkspaceController { DebounceMs = 0 };

        controller.EditMainFile("cube(1);\n");          // must not throw, must not schedule a recompute

        Assert.Null(controller.Analysis);
        Assert.Equal(BusyPhase.Idle, controller.BusyPhase);
    }

    [Fact]
    public async Task ResolveAmbiguous_PlacesCandidate_AndClearsAmbiguity()
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

        controller.ResolveAmbiguous(ambiguous.Candidates[0], ambiguous.RawPath);
        await controller.Recomputing;

        Assert.Empty(controller.Analysis!.Ambiguous);
        Assert.NotNull(controller.Bundle);
        Assert.True(controller.Bundle!.Ok);
    }

    // ---- Slice W5 §C1: phased recompute, progress, and intent coalescing ----

    [Fact]
    public async Task Recompute_TransitionsThroughAnalyzingThenBundlingThenIdle_ForCompleteProject()
    {
        var controller = new WorkspaceController { DebounceMs = 0 };
        var phases = new List<BusyPhase>();
        controller.Changed += () => phases.Add(controller.BusyPhase);

        controller.AddOrReplace(
        [
            new UploadedFile("main.scad", "use <lib.scad>\nwidget(3);\n"),
            new UploadedFile("lib.scad", "module widget(w) cube(w);\n"),
        ]);
        await controller.Recomputing;

        Assert.Equal([BusyPhase.Analyzing, BusyPhase.Bundling, BusyPhase.Idle], phases);
        Assert.Equal(BusyPhase.Idle, controller.BusyPhase);
        Assert.False(controller.IsBusy);
    }

    [Fact]
    public async Task Recompute_SkipsBundlingPhase_WhenProjectIsIncomplete()
    {
        var controller = new WorkspaceController { DebounceMs = 0 };
        var phases = new List<BusyPhase>();
        controller.Changed += () => phases.Add(controller.BusyPhase);

        controller.AddOrReplace([new UploadedFile("main.scad", "include <missing.scad>\ncube(1);\n")]);
        await controller.Recomputing;

        Assert.Equal([BusyPhase.Analyzing, BusyPhase.Idle], phases);   // no bundle phase without a complete set
        Assert.Equal(BusyPhase.Idle, controller.BusyPhase);
    }

    [Fact]
    public async Task RapidIntents_CoalesceToASingleCompletedRecompute()
    {
        // PhaseHoldMs holds each recompute in-phase so the burst reliably supersedes the in-flight ones —
        // closing the race that plain xUnit (no WASM-style single SynchronizationContext) would otherwise
        // allow by running yield continuations on the thread pool concurrently with the burst.
        var controller = new WorkspaceController { DebounceMs = 0, PhaseHoldMs = 100 };
        int completed = 0;
        controller.Changed += () =>
        {
            if (controller.BusyPhase == BusyPhase.Idle)
            {
                completed++;                              // only a recompute that runs to the end reaches Idle
            }
        };

        // A burst of five intents, as repeated drops or fast typing would produce.
        for (int i = 0; i < 5; i++)
        {
            controller.AddOrReplace([new UploadedFile("main.scad", $"cube({i});\n")]);
        }

        await controller.Recomputing;

        Assert.Equal(1, completed);                       // four were superseded before completing
        Assert.NotNull(controller.Bundle);
        Assert.Contains("cube(4)", controller.Bundle!.Text);   // the last intent's content is the one that lands
    }

    [Fact]
    public async Task SupersedingIntent_CancelsTheInFlightRecompute()
    {
        // PhaseHoldMs holds each busy phase so the first recompute is genuinely mid-flight when superseded.
        var controller = new WorkspaceController { DebounceMs = 0, PhaseHoldMs = 150 };
        int completed = 0;
        controller.Changed += () =>
        {
            if (controller.BusyPhase == BusyPhase.Idle)
            {
                completed++;
            }
        };

        controller.AddOrReplace([new UploadedFile("main.scad", "cube(1);\n")]);   // starts, then holds in Analyzing
        controller.AddOrReplace([new UploadedFile("main.scad", "cube(2);\n")]);   // supersedes the in-flight one
        await controller.Recomputing;

        Assert.Equal(1, completed);                       // the first never reached Idle — it was cancelled
        Assert.Equal(BusyPhase.Idle, controller.BusyPhase);
        Assert.Contains("cube(2)", controller.Bundle!.Text);
    }
}
