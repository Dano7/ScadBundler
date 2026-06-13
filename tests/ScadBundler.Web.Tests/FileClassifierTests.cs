using ScadBundler.Web.State;
using Xunit;

namespace ScadBundler.Web.Tests;

/// <summary>
/// Pure unit tests for <see cref="FileClassifier"/> — the used / unused / root partition that drives the
/// file list's emphasis (Slice W2 §2.4). No browser; the analysis is built by the real
/// <see cref="ProjectAnalyzer"/> so the labels match what the bundle actually inlines.
/// </summary>
public sealed class FileClassifierTests
{
    [Fact]
    public void LabelsRoot_Used_AndUnused()
    {
        UploadedFile[] uploads =
        [
            new("main.scad", "use <lib.scad>\nwidget();\n"),
            new("lib.scad", "module widget() cube(1);\n"),
            new("orphan.scad", "module unused() sphere(1);\n"),
        ];
        (_, ProjectAnalysis analysis) = ProjectAnalyzer.Analyze(uploads);

        IReadOnlyList<ClassifiedFile> classified = FileClassifier.Classify(uploads, analysis);

        Assert.Equal(FileUsage.Root, Usage(classified, "/proj/main.scad"));
        Assert.Equal(FileUsage.Used, Usage(classified, "/proj/lib.scad"));
        Assert.Equal(FileUsage.Unused, Usage(classified, "/proj/orphan.scad"));
    }

    [Fact]
    public void Used_MatchesCaseInsensitively_ForBasenameAliasedFiles()
    {
        // Loose upload with sloppy-case reference (the ForkedHolder case): the analyzer aliases the file at
        // the lower-cased path the loader resolves, but the upload is still "used".
        UploadedFile[] uploads =
        [
            new("Main.scad", "include <mylib.scad>\ncube(1);\n"),
            new("MyLib.scad", "wall = 2;\n"),
        ];
        (_, ProjectAnalysis analysis) = ProjectAnalyzer.Analyze(uploads);

        IReadOnlyList<ClassifiedFile> classified = FileClassifier.Classify(uploads, analysis);

        Assert.Equal(FileUsage.Root, Usage(classified, "/proj/Main.scad"));
        Assert.Equal(FileUsage.Used, Usage(classified, "/proj/MyLib.scad"));
    }

    [Fact]
    public void AllUnused_WhenNoRoot()
    {
        // Two geometry-bearing files referencing nothing ⇒ ambiguous root (Tree null) ⇒ nothing is "used".
        UploadedFile[] uploads = [new("a.scad", "cube(1);\n"), new("b.scad", "sphere(1);\n")];
        (_, ProjectAnalysis analysis) = ProjectAnalyzer.Analyze(uploads);
        Assert.Null(analysis.Root);

        IReadOnlyList<ClassifiedFile> classified = FileClassifier.Classify(uploads, analysis);

        Assert.All(classified, c => Assert.Equal(FileUsage.Unused, c.Usage));
    }

    private static FileUsage Usage(IReadOnlyList<ClassifiedFile> classified, string virtualPath) =>
        classified.Single(c => c.VirtualPath == virtualPath).Usage;
}
