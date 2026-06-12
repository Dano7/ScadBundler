using System.IO.Compression;
using System.Text;
using ScadBundler.Core.Workspace;

namespace ScadBundler.Web.Ingestion;

/// <summary>
/// Expands an uploaded <c>.zip</c> into <see cref="UploadedFile"/>s using the BCL
/// <see cref="ZipArchive"/> (works under WASM — <b>no JS library</b>). Each entry's path is preserved in
/// <see cref="UploadedFile.Name"/>, so a zipped project resolves its <c>include</c>/<c>use</c> references
/// deterministically (Spec §3.2 / §6.3). Only <c>.scad</c> entries are surfaced; directories and other
/// files are skipped (fonts are <c>use</c> pass-throughs the loader never reads).
/// </summary>
public static class ZipIngestion
{
    /// <summary>Reads a zip from raw bytes.</summary>
    /// <param name="zipBytes">The complete zip archive.</param>
    /// <returns>The <c>.scad</c> entries as uploads, in archive order.</returns>
    public static IReadOnlyList<UploadedFile> Read(byte[] zipBytes)
    {
        ArgumentNullException.ThrowIfNull(zipBytes);
        using var stream = new MemoryStream(zipBytes, writable: false);
        return Read(stream);
    }

    /// <summary>Reads a zip from a stream.</summary>
    /// <param name="zipStream">A readable stream positioned at the start of the archive.</param>
    /// <returns>The <c>.scad</c> entries as uploads, in archive order.</returns>
    public static IReadOnlyList<UploadedFile> Read(Stream zipStream)
    {
        ArgumentNullException.ThrowIfNull(zipStream);
        var uploads = new List<UploadedFile>();
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);

        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            // A directory entry has an empty Name; skip it and anything that is not a .scad source.
            if (entry.Name.Length == 0
                || !entry.FullName.EndsWith(".scad", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            using Stream entryStream = entry.Open();
            using var reader = new StreamReader(entryStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            uploads.Add(new UploadedFile(entry.FullName, reader.ReadToEnd()));
        }

        return uploads;
    }
}
