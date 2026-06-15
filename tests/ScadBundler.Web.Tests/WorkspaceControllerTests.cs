using ScadBundler.Core.Inlining;
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

    [Fact]
    public void Dispose_SettlesBusyPhaseToIdle_WhenARecomputeIsStillParked()
    {
        // PhaseHoldMs parks the recompute in the analyze phase; BusyPhase is set synchronously before the
        // first await, so it is observable the instant the intent returns.
        var controller = new WorkspaceController { DebounceMs = 0, PhaseHoldMs = 60_000 };
        controller.AddOrReplace([new UploadedFile("main.scad", "cube(1);\n")]);
        Assert.Equal(BusyPhase.Analyzing, controller.BusyPhase);

        controller.Dispose();                             // cancels the parked recompute (it won't reach Idle itself)

        Assert.Equal(BusyPhase.Idle, controller.BusyPhase);
        Assert.False(controller.IsBusy);
    }

    // ---- Slice W5 §C: large-project deferred bundle + staged options ----

    // A project over the byte threshold (one big file), so AutoBundle is off: it analyzes live but defers the
    // bundle until ApplyOptions. The leading comment + a top-level cube make it a complete, bundleable root.
    private static async Task<WorkspaceController> LargeProjectAsync()
    {
        var controller = new WorkspaceController { DebounceMs = 0 };
        string big = "// " + new string('x', WorkspaceController.LargeProjectByteThreshold) + "\ncube(1);\n";
        controller.AddOrReplace([new UploadedFile("main.scad", big)]);
        await controller.Recomputing;
        return controller;
    }

    [Fact]
    public async Task SmallProject_AutoBundles_AndAppliesOptionsImmediately()
    {
        var controller = new WorkspaceController { DebounceMs = 0 };
        controller.AddOrReplace([new UploadedFile("main.scad", "cube(1);\n")]);
        await controller.Recomputing;

        Assert.False(controller.IsLargeProject);
        Assert.True(controller.AutoBundle);
        Assert.NotNull(controller.Bundle);                       // bundled live
        Assert.False(controller.NeedsBundle);                    // nothing deferred in a small project

        controller.SetOptions(controller.PendingOptions with { Hardening = HardeningProfile.Minify });
        await controller.Recomputing;

        Assert.Equal(HardeningProfile.Minify, controller.Options.Hardening);        // applied immediately
        Assert.Equal(HardeningProfile.Minify, controller.PendingOptions.Hardening);
        Assert.False(controller.OptionsDirty);
    }

    [Fact]
    public async Task LargeProject_AnalyzesButDefersBundle_OnStructuralChange()
    {
        WorkspaceController controller = await LargeProjectAsync();

        Assert.True(controller.IsLargeProject);
        Assert.False(controller.AutoBundle);
        Assert.NotNull(controller.Analysis);                     // analyzed live (drives the file list/tree)
        Assert.True(controller.CanBundle);                       // complete and ready…
        Assert.Null(controller.Bundle);                          // …but not bundled — deferred
        Assert.True(controller.NeedsBundle);                     // the UI prompts a manual Bundle
        Assert.Equal(BusyPhase.Idle, controller.BusyPhase);
    }

    [Fact]
    public async Task LargeProject_SetOptions_StagesWithoutBundling()
    {
        WorkspaceController controller = await LargeProjectAsync();

        controller.SetOptions(controller.PendingOptions with { Hardening = HardeningProfile.Minify });
        await controller.Recomputing;   // no new recompute is scheduled; awaiting the last (completed) task is safe

        Assert.Equal(HardeningProfile.Minify, controller.PendingOptions.Hardening); // staged
        Assert.Equal(HardeningProfile.None, controller.Options.Hardening);          // not applied
        Assert.True(controller.OptionsDirty);
        Assert.Null(controller.Bundle);                                             // still not bundled
    }

    [Fact]
    public async Task LargeProject_ApplyOptions_BundlesOnce_WithStagedOptions()
    {
        WorkspaceController controller = await LargeProjectAsync();
        controller.SetOptions(controller.PendingOptions with { Hardening = HardeningProfile.Minify });

        controller.ApplyOptions();
        await controller.Recomputing;

        Assert.Equal(HardeningProfile.Minify, controller.Options.Hardening);        // staged options applied
        Assert.False(controller.OptionsDirty);
        Assert.NotNull(controller.Bundle);
        Assert.True(controller.Bundle!.Ok);
        Assert.False(controller.NeedsBundle);                                       // up to date now
    }

    [Fact]
    public async Task OptionsOnlyApply_ReusesAnalysis_SkippingTheAnalyzePhase()
    {
        // Slice W5 §C2: with nothing structural changed, an options re-bundle reuses the cached analysis and
        // never re-enters the Analyzing phase.
        var controller = new WorkspaceController { DebounceMs = 0 };
        controller.AddOrReplace([new UploadedFile("main.scad", "cube(1);\n")]);
        await controller.Recomputing;       // initial analyze + bundle; structure now clean

        var phases = new List<BusyPhase>();
        controller.Changed += () => phases.Add(controller.BusyPhase);

        controller.SetOptions(controller.PendingOptions with { Hardening = HardeningProfile.Minify });
        await controller.Recomputing;

        Assert.Equal([BusyPhase.Bundling, BusyPhase.Idle], phases);  // no Analyzing — analysis reused
    }

    [Fact]
    public void IsLargeProject_TrueAboveFileCountThreshold()
    {
        var controller = new WorkspaceController { DebounceMs = 0 };
        var files = new List<UploadedFile>();
        for (int i = 0; i <= WorkspaceController.LargeProjectFileThreshold; i++)  // threshold + 1 files
        {
            files.Add(new UploadedFile($"f{i}.scad", "a = 1;\n"));
        }

        controller.AddOrReplace(files);

        Assert.True(controller.IsLargeProject);
        Assert.False(controller.AutoBundle);
    }

    [Fact]
    public void IsLargeProject_TrueAboveByteThreshold_WithASingleFile()
    {
        var controller = new WorkspaceController { DebounceMs = 0 };

        controller.AddOrReplace(
            [new UploadedFile("main.scad", new string('x', WorkspaceController.LargeProjectByteThreshold + 1))]);

        Assert.Equal(1, controller.UploadCount);   // one file, but over the byte threshold
        Assert.True(controller.IsLargeProject);
    }
}
