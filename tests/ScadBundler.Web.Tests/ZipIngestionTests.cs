using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using ScadBundler.Web.Ingestion;
using Xunit;

namespace ScadBundler.Web.Tests;

/// <summary>
/// <see cref="ZipIngestion"/> is plain BCL (no browser), so the zip ingestion path is testable directly:
/// entry paths must survive into <c>UploadedFile.Name</c> (structure preservation, Spec §3.2) and non-scad
/// entries / directories must be skipped.
/// </summary>
public sealed class ZipIngestionTests
{
    [Fact]
    public void Read_PreservesRelativePaths_AndKeepsOnlyScad()
    {
        byte[] zip = BuildZip(new Dictionary<string, string>
        {
            ["main.scad"] = "include <BOSL2/std.scad>\ncube(1);\n",
            ["BOSL2/std.scad"] = "module std() cube(1);\n",
            ["readme.txt"] = "ignore me",
        });

        IReadOnlyList<UploadedFile> uploads = ZipIngestion.Read(zip);

        Assert.Equal(2, uploads.Count); // the .txt is skipped
        Assert.Contains(uploads, u => u.Name == "main.scad");

        UploadedFile std = uploads.Single(u => u.Name == "BOSL2/std.scad");
        Assert.Contains("module std", std.Text);
    }

    [Fact]
    public void Read_SkipsDirectoryEntries()
    {
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            archive.CreateEntry("libs/");          // explicit directory entry
            WriteEntry(archive, "libs/util.scad", "x = 1;\n");
        }

        IReadOnlyList<UploadedFile> uploads = ZipIngestion.Read(ms.ToArray());

        UploadedFile only = Assert.Single(uploads);
        Assert.Equal("libs/util.scad", only.Name);
    }

    private static byte[] BuildZip(Dictionary<string, string> entries)
    {
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (KeyValuePair<string, string> entry in entries)
            {
                WriteEntry(archive, entry.Key, entry.Value);
            }
        }

        return ms.ToArray();
    }

    private static void WriteEntry(ZipArchive archive, string name, string text)
    {
        ZipArchiveEntry entry = archive.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(text);
    }
}
