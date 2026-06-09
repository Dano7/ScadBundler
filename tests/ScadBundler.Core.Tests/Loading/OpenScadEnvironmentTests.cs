using ScadBundler.Core.Loading;
using Xunit;

namespace ScadBundler.Core.Tests.Loading;

/// <summary>
/// Unit coverage for <see cref="OpenScadEnvironment"/>: the OpenSCAD-faithful parsing of
/// <c>OPENSCADPATH</c> (absolutize each entry; an empty entry is the current directory) and the
/// per-user library path shape.
/// </summary>
public sealed class OpenScadEnvironmentTests
{
    [Fact]
    public void ParsePathList_NullOrEmpty_ReturnsEmpty()
    {
        Assert.Empty(OpenScadEnvironment.ParsePathList(null, ';', "BASE"));
        Assert.Empty(OpenScadEnvironment.ParsePathList(string.Empty, ';', "BASE"));
    }

    [Fact]
    public void ParsePathList_EmptyEntry_ResolvesToCurrentDirectory()
    {
        // ";".Split(';') yields two empty entries; each becomes the supplied current directory verbatim.
        Assert.Equal(["BASE", "BASE"], OpenScadEnvironment.ParsePathList(";", ';', "BASE"));
    }

    [Fact]
    public void ParsePathList_RelativeEntry_IsMadeAbsoluteAgainstCurrentDirectory()
    {
        string baseDir = Directory.GetCurrentDirectory();
        List<string> result = OpenScadEnvironment.ParsePathList("sub", ';', baseDir);
        Assert.Equal(Path.GetFullPath("sub", baseDir), Assert.Single(result));
    }

    [Fact]
    public void ParsePathList_TrimsSurroundingWhitespace()
    {
        string baseDir = Directory.GetCurrentDirectory();
        List<string> result = OpenScadEnvironment.ParsePathList("  sub  ", ';', baseDir);
        Assert.Equal(Path.GetFullPath("sub", baseDir), Assert.Single(result));
    }

    [Fact]
    public void ParsePathList_PreservesOrderAndAbsolutizesEachEntry()
    {
        string baseDir = Directory.GetCurrentDirectory();
        List<string> result = OpenScadEnvironment.ParsePathList("a;b", ';', baseDir);
        Assert.Equal([Path.GetFullPath("a", baseDir), Path.GetFullPath("b", baseDir)], result);
    }

    [Fact]
    public void UserLibraryPath_WhenKnown_EndsWithOpenScadLibraries()
    {
        string path = OpenScadEnvironment.UserLibraryPath();
        if (path.Length > 0) // empty only when the platform home/documents folder is unavailable
        {
            Assert.EndsWith(Path.Combine("OpenSCAD", "libraries"), path);
        }
    }
}
