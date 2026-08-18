namespace Dewiride.Analytics.Integration.Tests.Fixtures;

/// <summary>
/// Resolves files that ship with the repository rather than with the build output.
/// </summary>
internal static class RepositoryPaths
{
    /// <summary>File that marks the repository root, present nowhere else.</summary>
    private const string RootMarker = "Directory.Packages.props";

    private static readonly DirectoryInfo Root = Locate();

    /// <summary>
    /// Resolves a path relative to the repository root.
    /// </summary>
    /// <param name="relativePath">Path in forward-slash form, as it is written in the repository.</param>
    /// <returns>The file.</returns>
    /// <exception cref="FileNotFoundException">The file is not there.</exception>
    public static FileInfo File(string relativePath)
    {
        var file = new FileInfo(Path.Combine(Root.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar)));

        return file.Exists
            ? file
            : throw new FileNotFoundException($"'{relativePath}' was not found under {Root.FullName}.", file.FullName);
    }

    private static DirectoryInfo Locate()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !System.IO.File.Exists(Path.Combine(directory.FullName, RootMarker)))
        {
            directory = directory.Parent;
        }

        return directory
            ?? throw new DirectoryNotFoundException(
                $"No '{RootMarker}' was found above {AppContext.BaseDirectory}.");
    }
}
