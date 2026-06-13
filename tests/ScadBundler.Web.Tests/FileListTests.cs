using Bunit;
using Microsoft.Extensions.DependencyInjection;
using ScadBundler.Web.Components;
using ScadBundler.Web.State;
using Xunit;

namespace ScadBundler.Web.Tests;

/// <summary>
/// bUnit smoke tests for the W2 <see cref="FileList"/>: entry-point badge + loaded/font/unused icons, the
/// missing-reference rows, the ambiguous entry-point picker, and click-to-promote (<c>★ make main</c>) — all
/// wired to a real <see cref="WorkspaceController"/> + <see cref="ProjectAnalyzer"/> so the rendering matches
/// the live app. JS interop is loose because the hosted <c>MissingRow</c>s register drop zones on render.
/// </summary>
public sealed class FileListTests : TestContext
{
    private (WorkspaceController Controller, IRenderedComponent<FileList> Cut) RenderFor(params UploadedFile[] uploads)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var controller = new WorkspaceController();
        controller.AddOrReplace(uploads);
        Services.AddSingleton(controller);
        IRenderedComponent<FileList> cut = RenderComponent<FileList>(p => p
            .Add(c => c.Analysis, controller.Analysis)
            .Add(c => c.Uploads, controller.Uploads));
        return (controller, cut);
    }

    [Fact]
    public void RendersEntryPointBadge_LoadedIcons_AndFontRow()
    {
        (_, IRenderedComponent<FileList> cut) = RenderFor(
            new UploadedFile("main.scad", "use <lib.scad>\nuse <Helvetica.ttf>\nwidget();\n"),
            new UploadedFile("lib.scad", "module widget() cube(1);\n"));

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
        (_, IRenderedComponent<FileList> cut) = RenderFor(
            new UploadedFile("main.scad", "include <missing.scad>\ncube(1);\n"));

        Assert.Contains("⚠", cut.Markup);            // needed status icon
        Assert.Contains("missing.scad", cut.Markup);
        Assert.Contains("needed by", cut.Markup);
    }

    [Fact]
    public void MarksUnusedUploads()
    {
        (_, IRenderedComponent<FileList> cut) = RenderFor(
            new UploadedFile("main.scad", "use <lib.scad>\nwidget();\n"),
            new UploadedFile("lib.scad", "module widget() cube(1);\n"),
            new UploadedFile("orphan.scad", "module unused() sphere(1);\n"));

        Assert.Contains("orphan.scad", cut.Markup);
        Assert.Contains("unused", cut.Markup);
    }

    [Fact]
    public void MakeMain_RerootsViaController()
    {
        (WorkspaceController controller, IRenderedComponent<FileList> cut) = RenderFor(
            new UploadedFile("main.scad", "use <lib.scad>\nwidget();\n"),
            new UploadedFile("lib.scad", "module widget() cube(1);\n"));
        Assert.Equal("/proj/main.scad", controller.Root);

        // Only the non-root file carries a "make main" affordance; clicking it re-roots.
        cut.Find("button.make-main").Click();

        Assert.Equal("/proj/lib.scad", controller.Root);
    }

    [Fact]
    public void AmbiguousRoot_ListsCandidates_AndPickSetsRoot()
    {
        (WorkspaceController controller, IRenderedComponent<FileList> cut) = RenderFor(
            new UploadedFile("a.scad", "cube(1);\n"),
            new UploadedFile("b.scad", "sphere(1);\n"));
        Assert.Null(controller.Root);                 // two geometry files ⇒ ambiguous entry point

        Assert.Contains("Which file is your model?", cut.Markup);
        cut.FindAll("button.make-main")[0].Click();   // first candidate is a.scad (name-ordered)

        Assert.Equal("/proj/a.scad", controller.Root);
    }

    [Fact]
    public void RendersNothing_WhenAnalysisNull()
    {
        Services.AddSingleton(new WorkspaceController());
        IRenderedComponent<FileList> cut = RenderComponent<FileList>();

        Assert.Empty(cut.Markup.Trim());
    }
}
