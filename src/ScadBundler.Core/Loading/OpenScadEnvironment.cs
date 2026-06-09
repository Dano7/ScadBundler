namespace ScadBundler.Core.Loading;

/// <summary>
/// Reconstructs the library search paths the OpenSCAD executable itself uses (<c>parsersettings.cc</c>
/// <c>parser_init</c>), so the bundler resolves <c>include</c>/<c>use</c> the way OpenSCAD would: the
/// <c>OPENSCADPATH</c> entries (each made absolute; an empty entry means the current directory),
/// followed by the per-user library folder. These are appended after any explicit <c>-p</c> paths and,
/// per <see cref="SourceLoader"/>, are consulted only after the including file's own directory. The
/// install's bundled libraries (<c>resourcePath("libraries")</c>) are not added — their location is
/// install-specific and can be supplied via <c>OPENSCADPATH</c> when needed.
/// </summary>
public static class OpenScadEnvironment
{
    /// <summary>
    /// The OpenSCAD-equivalent library search paths, in priority order: the absolutized
    /// <c>OPENSCADPATH</c> entries, then the per-user library folder when one is known. Never throws.
    /// </summary>
    /// <returns>The search paths to append after the including file's directory.</returns>
    public static IReadOnlyList<string> LibraryPaths()
    {
        var paths = new List<string>();
        paths.AddRange(ParsePathList(
            Environment.GetEnvironmentVariable("OPENSCADPATH"),
            Path.PathSeparator,
            Directory.GetCurrentDirectory()));

        string userLibrary = UserLibraryPath();
        if (userLibrary.Length > 0)
        {
            paths.Add(userLibrary);
        }

        return paths;
    }

    // Splits an OPENSCADPATH-style list. Unlike a plain split, OpenSCAD keeps empty entries (an empty
    // entry resolves to the current directory, matching `fs::absolute("")`) and makes every entry
    // absolute relative to the current directory.
    internal static List<string> ParsePathList(string? value, char separator, string currentDirectory)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(value))
        {
            return result;
        }

        foreach (string raw in value.Split(separator))
        {
            string entry = raw.Trim();
            result.Add(entry.Length == 0 ? currentDirectory : SafeFullPath(entry, currentDirectory));
        }

        return result;
    }

    // The per-user library folder OpenSCAD adds in `parser_init` via `PlatformUtils::userLibraryPath`:
    // <My Documents>/OpenSCAD/libraries on Windows (CSIDL_PERSONAL) and
    // $HOME/.local/share/OpenSCAD/libraries on POSIX (PlatformUtils-posix `documentsPath`). macOS may
    // differ; verify against PlatformUtils-mac if a macOS build is targeted.
    internal static string UserLibraryPath()
    {
        if (OperatingSystem.IsWindows())
        {
            string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return documents.Length == 0
                ? string.Empty
                : Path.Combine(documents, "OpenSCAD", "libraries");
        }

        string? home = Environment.GetEnvironmentVariable("HOME");
        return string.IsNullOrEmpty(home)
            ? string.Empty
            : Path.Combine(home, ".local", "share", "OpenSCAD", "libraries");
    }

    private static string SafeFullPath(string path, string basePath)
    {
        try
        {
            return Path.GetFullPath(path, basePath);
        }
        catch (ArgumentException)
        {
            return path; // an unrepresentable entry is left as-is; resolution will simply miss it
        }
    }
}
