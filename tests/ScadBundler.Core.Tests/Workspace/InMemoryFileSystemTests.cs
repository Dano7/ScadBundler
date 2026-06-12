using ScadBundler.Core.Inlining;
using ScadBundler.Core.Loading;
using ScadBundler.Core.Workspace;
using Xunit;

namespace ScadBundler.Core.Tests.Workspace;

/// <summary>
/// The <see cref="InMemoryFileSystem"/>: pure exact-path canonicalization and POSIX path semantics, plus a
/// round-trip that drives the real <see cref="SourceLoader"/> over an in-memory tree.
/// </summary>
public sealed class InMemoryFileSystemTests
{
    [Theory]
    [InlineData("/proj/main.scad", "/proj/main.scad")]
    [InlineData("proj/main.scad", "/proj/main.scad")]          // leading '/' ensured
    [InlineData("\\proj\\sub\\a.scad", "/proj/sub/a.scad")]    // '\' → '/'
    [InlineData("/proj/./sub/../main.scad", "/proj/main.scad")] // '.' and '..' collapsed
    [InlineData("/proj//sub///a.scad", "/proj/sub/a.scad")]    // empty segments collapsed
    [InlineData("/../../etc", "/etc")]                          // '..' cannot escape root
    public void GetFullPath_Canonicalizes(string input, string expected)
    {
        var fs = new InMemoryFileSystem();
        Assert.Equal(expected, fs.GetFullPath(input));
    }

    [Fact]
    public void FileExists_TrueForStoredFile_AndItsDirectoryChain()
    {
        var fs = new InMemoryFileSystem();
        fs.AddFile("/proj/sub/a.scad", "x");

        Assert.True(fs.FileExists("/proj/sub/a.scad"));
        Assert.True(fs.FileExists("/proj/sub"));  // a directory in the chain
        Assert.True(fs.FileExists("/proj"));
        Assert.False(fs.FileExists("/proj/missing.scad"));
    }

    [Fact]
    public void DirectoryExists_DistinguishesDirectoriesFromFiles()
    {
        var fs = new InMemoryFileSystem();
        fs.AddFile("/proj/sub/a.scad", "x");

        Assert.True(fs.DirectoryExists("/proj"));
        Assert.True(fs.DirectoryExists("/proj/sub"));
        Assert.True(fs.DirectoryExists("/"));        // root holds files
        Assert.False(fs.DirectoryExists("/proj/sub/a.scad")); // a file is not a directory
        Assert.False(fs.DirectoryExists("/nope"));
    }

    [Fact]
    public void DirectoryExists_Root_FalseWhenEmpty()
    {
        var fs = new InMemoryFileSystem();
        Assert.False(fs.DirectoryExists("/"));
    }

    [Fact]
    public void GetDirectoryName_IsPosix()
    {
        var fs = new InMemoryFileSystem();
        Assert.Equal("/proj/sub", fs.GetDirectoryName("/proj/sub/a.scad"));
        Assert.Equal("/", fs.GetDirectoryName("/a.scad"));  // top-level → root
    }

    [Theory]
    [InlineData("/proj", "lib.scad", "/proj/lib.scad")]
    [InlineData("/proj/", "sub/lib.scad", "/proj/sub/lib.scad")]
    [InlineData("/proj", "/abs/lib.scad", "/abs/lib.scad")] // absolute relative wins
    [InlineData("/proj", "../lib.scad", "/proj/../lib.scad")] // joined raw; GetFullPath collapses '..' later
    public void Combine_IsPosix(string dir, string rel, string expected)
    {
        var fs = new InMemoryFileSystem();
        Assert.Equal(expected, fs.Combine(dir, rel));
    }

    [Fact]
    public void AddRemoveContainsFiles_RoundTrip()
    {
        var fs = new InMemoryFileSystem();
        fs.AddFile("/proj/a.scad", "a");
        fs.AddFile("\\proj\\b.scad", "b"); // canonicalized on insert

        Assert.True(fs.Contains("/proj/a.scad"));
        Assert.True(fs.Contains("/proj/b.scad"));
        Assert.Equal(2, fs.Files.Count);
        Assert.Contains("/proj/a.scad", fs.Files);

        fs.RemoveFile("/proj/a.scad");
        Assert.False(fs.Contains("/proj/a.scad"));
        Assert.Single(fs.Files);
    }

    [Fact]
    public void AddFile_LastWriteWins()
    {
        var fs = new InMemoryFileSystem();
        fs.AddFile("/proj/a.scad", "first");
        fs.AddFile("/proj/a.scad", "second");
        Assert.Equal("second", fs.ReadAllText("/proj/a.scad"));
        Assert.Single(fs.Files);
    }

    [Fact]
    public void ReadAllText_ThrowsForAbsentPath()
    {
        var fs = new InMemoryFileSystem();
        Assert.Throws<FileNotFoundException>(() => fs.ReadAllText("/proj/missing.scad"));
    }

    [Fact]
    public void DrivesSourceLoader_OverAThreeFileTree()
    {
        var fs = new InMemoryFileSystem();
        fs.AddFile("/proj/main.scad", "include <a.scad>\nuse <b.scad>\ncube(1);");
        fs.AddFile("/proj/a.scad", "module a() cube(1);");
        fs.AddFile("/proj/b.scad", "module b() sphere(1);");

        LoadGraph graph = SourceLoader.Load("/proj/main.scad", BundleOptions.Default, fs);

        Assert.Empty(graph.Diagnostics);
        Assert.Equal(3, graph.ByAbsolutePath.Count);
        Assert.NotNull(Assert.Single(graph.Root.Includes).Target);
        Assert.NotNull(Assert.Single(graph.Root.Uses).Target);
    }
}
