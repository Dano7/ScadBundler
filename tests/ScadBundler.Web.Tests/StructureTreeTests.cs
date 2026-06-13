using Bunit;
using ScadBundler.Web.Components;
using Xunit;

namespace ScadBundler.Web.Tests;

/// <summary>
/// bUnit smoke tests for the read-only <see cref="StructureTree"/> (Slice W2 §2.6): it shows the resolved
/// folder layout (folders → files) and is display-only — there are no inputs, buttons, or editable controls.
/// </summary>
public sealed class StructureTreeTests : TestContext
{
    [Fact]
    public void RendersFoldersAndFiles_ReadOnly()
    {
        UploadedFile[] uploads =
        [
            new("main.scad", "cube(1);\n"),
            new("BOSL2/std.scad", "// std\n"),
        ];

        IRenderedComponent<StructureTree> cut = RenderComponent<StructureTree>(p =>
            p.Add(c => c.Uploads, uploads));

        Assert.Contains("main.scad", cut.Markup);
        Assert.Contains("BOSL2", cut.Markup);
        Assert.Contains("std.scad", cut.Markup);
        Assert.Empty(cut.FindAll("button"));            // read-only: no affordances
        Assert.Empty(cut.FindAll("input"));
    }

    [Fact]
    public void RendersNothing_WhenNoUploads()
    {
        IRenderedComponent<StructureTree> cut = RenderComponent<StructureTree>();

        Assert.Empty(cut.Markup.Trim());
    }
}
