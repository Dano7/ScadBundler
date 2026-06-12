using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using ScadBundler.Web.Ingestion;
using Xunit;

namespace ScadBundler.Web.Tests;

/// <summary>
/// <see cref="IngestItemReader"/> is the managed boundary the JS layer hands files to: <c>"text"</c> items
/// become uploads verbatim; <c>"zip"</c> items are Base64-decoded and expanded; malformed items are skipped
/// rather than aborting the whole drop.
/// </summary>
public sealed class IngestItemReaderTests
{
    [Fact]
    public void ToUploads_TextItem_BecomesUploadVerbatim()
    {
        var items = new[] { new IngestItem("a/main.scad", "text", "cube(1);\n") };

        UploadedFile only = Assert.Single(IngestItemReader.ToUploads(items));

        Assert.Equal("a/main.scad", only.Name);
        Assert.Equal("cube(1);\n", only.Text);
    }

    [Fact]
    public void ToUploads_ZipItem_IsDecodedAndExpanded()
    {
        string base64 = Convert.ToBase64String(BuildZip(("lib.scad", "x = 1;\n")));
        var items = new[] { new IngestItem("proj.zip", "zip", base64) };

        UploadedFile only = Assert.Single(IngestItemReader.ToUploads(items));

        Assert.Equal("lib.scad", only.Name);
    }

    [Fact]
    public void ToUploads_MalformedZip_IsSkipped_NotThrown()
    {
        var items = new[]
        {
            new IngestItem("bad.zip", "zip", "not-base64!!!"),
            new IngestItem("ok.scad", "text", "cube(1);\n"),
        };

        UploadedFile only = Assert.Single(IngestItemReader.ToUploads(items));

        Assert.Equal("ok.scad", only.Name);
    }

    private static byte[] BuildZip(params (string Name, string Text)[] entries)
    {
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string name, string text) in entries)
            {
                ZipArchiveEntry entry = archive.CreateEntry(name);
                using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
                writer.Write(text);
            }
        }

        return ms.ToArray();
    }
}
