using ScadBundler.Core.Loading;

namespace ScadBundler.Core.Workspace;

/// <summary>
/// An <see cref="IFileSystem"/> over an in-memory set of files, addressed by virtual POSIX paths rooted at
/// <c>"/"</c>. Drives the whole pipeline from uploaded files with no disk or network access.
/// </summary>
/// <remarks>
/// <para>
/// <b>Dumb on purpose — pure exact-path semantics.</b> There is no basename magic, layout inference, or
/// case folding here; all of that smart resolution lives in <see cref="ProjectAnalyzer"/>, which builds the
/// file system deterministically so the loader resolves exactly what the analysis predicted. Keeping this
/// type trivial keeps it trivially testable.
/// </para>
/// <para>
/// <see cref="GetFullPath"/> is the canonicalizer the loader uses as its cache and cycle-detection key:
/// it replaces <c>\</c> with <c>/</c>, ensures a leading <c>/</c>, and collapses <c>.</c>/<c>..</c>
/// segments, so equivalent paths canonicalize identically.
/// </para>
/// </remarks>
public sealed class InMemoryFileSystem : IFileSystem
{
    private readonly Dictionary<string, string> _files = new(StringComparer.Ordinal);

    /// <summary>Adds (or replaces) the file at <paramref name="virtualPath"/>, canonicalized on insert.</summary>
    /// <param name="virtualPath">The file's virtual path (canonicalized via <see cref="GetFullPath"/>).</param>
    /// <param name="text">The file text.</param>
    public void AddFile(string virtualPath, string text)
    {
        ArgumentNullException.ThrowIfNull(virtualPath);
        ArgumentNullException.ThrowIfNull(text);
        _files[GetFullPath(virtualPath)] = text;
    }

    /// <summary>Removes the file at <paramref name="virtualPath"/> if present.</summary>
    /// <param name="virtualPath">The file's virtual path.</param>
    public void RemoveFile(string virtualPath)
    {
        ArgumentNullException.ThrowIfNull(virtualPath);
        _files.Remove(GetFullPath(virtualPath));
    }

    /// <summary>True when a file is stored at the canonical form of <paramref name="virtualPath"/>.</summary>
    /// <param name="virtualPath">The file's virtual path.</param>
    /// <returns><c>true</c> when the file exists.</returns>
    public bool Contains(string virtualPath)
    {
        ArgumentNullException.ThrowIfNull(virtualPath);
        return _files.ContainsKey(GetFullPath(virtualPath));
    }

    /// <summary>Every stored file's canonical virtual path (a snapshot).</summary>
    public IReadOnlyCollection<string> Files => _files.Keys.ToArray();

    /// <inheritdoc/>
    public string GetFullPath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        string rooted = path.Replace('\\', '/');
        if (!rooted.StartsWith('/'))
        {
            rooted = "/" + rooted;
        }

        var segments = new List<string>();
        foreach (string segment in rooted.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
            {
                continue;
            }

            if (segment == ".." && segments.Count > 0)
            {
                segments.RemoveAt(segments.Count - 1);
            }
            else if (segment != "..")
            {
                segments.Add(segment);
            }
        }

        return "/" + string.Join('/', segments);
    }

    /// <inheritdoc/>
    public bool FileExists(string path) => _files.ContainsKey(GetFullPath(path)) || DirectoryExists(path);

    /// <inheritdoc/>
    public bool DirectoryExists(string path)
    {
        string full = GetFullPath(path);
        string prefix = full == "/" ? "/" : full + "/";
        return _files.Keys.Any(key => key.StartsWith(prefix, StringComparison.Ordinal));
    }

    /// <inheritdoc/>
    public string ReadAllText(string path) =>
        _files.TryGetValue(GetFullPath(path), out string? content)
            ? content
            : throw new FileNotFoundException(null, path);

    /// <inheritdoc/>
    public string? GetDirectoryName(string path)
    {
        string full = GetFullPath(path);
        int slash = full.LastIndexOf('/');
        return slash <= 0 ? "/" : full[..slash];
    }

    /// <inheritdoc/>
    public string Combine(string directory, string relative)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(relative);
        return relative.Replace('\\', '/').StartsWith('/')
            ? relative
            : directory.TrimEnd('/') + "/" + relative;
    }
}
