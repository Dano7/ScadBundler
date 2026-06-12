using ScadBundler.Core.Workspace;

namespace ScadBundler.Web.Ingestion;

/// <summary>
/// One item handed up from <c>interop.js</c> after a drop / pick. <see cref="Kind"/> is <c>"text"</c> for a
/// <c>.scad</c> source (<see cref="Content"/> is its text) or <c>"zip"</c> for an archive
/// (<see cref="Content"/> is Base64). <see cref="Name"/> carries the relative path when structure is known.
/// </summary>
/// <param name="Name">The relative path (folder/zip) or bare file name (loose).</param>
/// <param name="Kind"><c>"text"</c> or <c>"zip"</c>.</param>
/// <param name="Content">The file text, or Base64 zip bytes when <see cref="Kind"/> is <c>"zip"</c>.</param>
public sealed record IngestItem(string Name, string Kind, string Content);

/// <summary>Turns the raw <see cref="IngestItem"/>s from JS into <see cref="UploadedFile"/>s.</summary>
public static class IngestItemReader
{
    /// <summary>
    /// Expands every item to its uploads: <c>"zip"</c> items are Base64-decoded and unzipped via
    /// <see cref="ZipIngestion"/>; <c>"text"</c> items become a single upload. Never throws on a malformed
    /// item — it is skipped so one bad file can't abort an otherwise good drop.
    /// </summary>
    /// <param name="items">The raw items from the browser.</param>
    /// <returns>The flattened upload set.</returns>
    public static IReadOnlyList<UploadedFile> ToUploads(IEnumerable<IngestItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var uploads = new List<UploadedFile>();

        foreach (IngestItem item in items)
        {
            if (string.Equals(item.Kind, "zip", StringComparison.Ordinal))
            {
                if (TryDecode(item.Content, out byte[] bytes))
                {
                    uploads.AddRange(ZipIngestion.Read(bytes));
                }
            }
            else
            {
                uploads.Add(new UploadedFile(item.Name, item.Content));
            }
        }

        return uploads;
    }

    private static bool TryDecode(string base64, out byte[] bytes)
    {
        try
        {
            bytes = Convert.FromBase64String(base64);
            return true;
        }
        catch (FormatException)
        {
            bytes = [];
            return false;
        }
    }
}
