using AngleSharp.Dom;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using ScadBundler.Web.Components;
using ScadBundler.Web.State;
using Xunit;

namespace ScadBundler.Web.Tests;

/// <summary>
/// bUnit tests for the Slice W5 §C1 busy indicator on <see cref="EngineStatus"/>: when idle it shows "Engine
/// ready" with no progress bar; while a recompute runs it announces the phase ("Analyzing… → Bundling…") with
/// a determinate <c>&lt;progress&gt;</c> bar via its <c>role="status"</c> live region. The recompute is held
/// mid-phase (<see cref="WorkspaceController.PhaseHoldMs"/>) so the transient busy state is observable.
/// </summary>
public sealed class EngineStatusTests : TestContext
{
    [Fact]
    public void Idle_ShowsEngineReady_AsAStatusRegion_WithNoProgressBar()
    {
        Services.AddSingleton(new WorkspaceController());

        IRenderedComponent<EngineStatus> cut = RenderComponent<EngineStatus>();

        IElement status = cut.Find(".engine-status");
        Assert.Equal("status", status.GetAttribute("role"));     // a polite live region for assistive tech
        Assert.Equal("polite", status.GetAttribute("aria-live"));
        Assert.Contains("Engine ready", cut.Markup);
        Assert.Empty(cut.FindAll("progress"));                   // determinate bar shown only while busy
    }

    [Fact]
    public async Task WhileRecomputing_ShowsPhaseLabel_AndDeterminateProgress_ThenSettles()
    {
        // Hold each phase so the transient "Analyzing…" state is reliably observable by WaitForAssertion.
        var controller = new WorkspaceController { DebounceMs = 0, PhaseHoldMs = 200 };
        Services.AddSingleton(controller);
        IRenderedComponent<EngineStatus> cut = RenderComponent<EngineStatus>();
        Assert.Contains("Engine ready", cut.Markup);

        controller.AddOrReplace(
        [
            new UploadedFile("main.scad", "use <lib.scad>\nwidget(3);\n"),
            new UploadedFile("lib.scad", "module widget(w) cube(w);\n"),
        ]);

        // The indicator surfaces the busy phase with a determinate <progress> bar.
        cut.WaitForAssertion(
            () =>
            {
                Assert.Contains("Analyzing", cut.Markup);
                Assert.NotEmpty(cut.FindAll("progress"));
            },
            TimeSpan.FromSeconds(10));

        // When the recompute completes it settles back to "ready" with the bar gone.
        await controller.Recomputing;
        cut.WaitForAssertion(
            () =>
            {
                Assert.Contains("Engine ready", cut.Markup);
                Assert.Empty(cut.FindAll("progress"));
            },
            TimeSpan.FromSeconds(10));
    }
}
