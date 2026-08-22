using System.Reflection;

namespace Dewiride.Analytics.Architecture.Tests;

/// <summary>
/// Locates the compiled product assemblies and the repository they were built from.
/// </summary>
/// <remarks>
/// The assemblies are read from this project's output rather than named one by one, so a project
/// added later is examined by these rules without anybody remembering to add it.
/// </remarks>
internal static class Product
{
    /// <summary>Prefix every assembly belonging to this product carries.</summary>
    public const string AssemblyPrefix = "Dewiride.Analytics.";

    /// <summary>Namespace root of the commercially licensed projects.</summary>
    public const string CommercialNamespace = "Dewiride.Analytics.Ee";

    /// <summary>The domain model.</summary>
    public const string Domain = "Dewiride.Analytics.Domain";

    /// <summary>The seams the two editions meet on.</summary>
    public const string Extensibility = "Dewiride.Analytics.Extensibility";

    /// <summary>Use cases, ports and the analytics vocabulary.</summary>
    public const string Application = "Dewiride.Analytics.Application";

    /// <summary>The detection engine.</summary>
    public const string Classification = "Dewiride.Analytics.Classification";

    /// <summary>Control-plane persistence and the services built on it.</summary>
    public const string Infrastructure = "Dewiride.Analytics.Infrastructure";

    /// <summary>The telemetry store.</summary>
    public const string TelemetryInfrastructure = "Dewiride.Analytics.Infrastructure.ClickHouse";

    /// <summary>The web host.</summary>
    public const string Api = "Dewiride.Analytics.Api";

    private static readonly string[] FrameworkPrefixes =
    [
        "System",
        "Microsoft",
        "netstandard",
        "mscorlib",
        "WindowsBase",
    ];

    /// <summary>Every compiled assembly belonging to this product.</summary>
    public static IReadOnlyList<Assembly> Assemblies { get; } = LoadAssemblies();

    /// <summary>The repository root, found by walking up from this assembly's location.</summary>
    public static DirectoryInfo Repository { get; } = LocateRepository();

    /// <summary>
    /// Loads one product assembly by its simple name.
    /// </summary>
    /// <param name="name">The assembly's simple name.</param>
    /// <returns>The assembly.</returns>
    public static Assembly Assembly(string name) =>
        Assemblies.SingleOrDefault(assembly => Named(assembly, name))
        ?? throw new InvalidOperationException(
            $"'{name}' was not found alongside the tests. Add a project reference that brings it in.");

    /// <summary>
    /// Names the non-framework assemblies an assembly was compiled against.
    /// </summary>
    /// <param name="assembly">The assembly to inspect.</param>
    /// <returns>The referenced assembly names, framework ones excluded.</returns>
    public static IEnumerable<string> NonFrameworkReferences(Assembly assembly) =>
        assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .Where(name => !IsFramework(name));

    /// <summary>
    /// Determines whether an assembly carries a given simple name.
    /// </summary>
    /// <param name="assembly">The assembly.</param>
    /// <param name="name">The simple name to match.</param>
    /// <returns><see langword="true"/> when the names match.</returns>
    public static bool Named(Assembly assembly, string name) =>
        string.Equals(assembly.GetName().Name, name, StringComparison.Ordinal);

    /// <summary>
    /// Every project file in the repository, product and tests alike.
    /// </summary>
    /// <returns>The project files.</returns>
    public static IEnumerable<FileInfo> ProjectFiles() =>
        Repository.EnumerateFiles("*.csproj", SearchOption.AllDirectories)
            .Where(file => !IsBuildOutput(file));

    /// <summary>
    /// Every C# and project file a person maintains, excluding build output.
    /// </summary>
    /// <returns>The source files.</returns>
    public static IEnumerable<FileInfo> SourceFiles() =>
        Repository.EnumerateFiles("*.cs", SearchOption.AllDirectories)
            .Where(file => !IsBuildOutput(file));

    private static bool IsBuildOutput(FileInfo file) =>
        file.FullName.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || file.FullName.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    private static bool IsFramework(string name) =>
        Array.Exists(FrameworkPrefixes, prefix => name.StartsWith(prefix, StringComparison.Ordinal));

    private static IReadOnlyList<Assembly> LoadAssemblies() =>
    [
        .. Directory
            .EnumerateFiles(AppContext.BaseDirectory, $"{AssemblyPrefix}*.dll", SearchOption.TopDirectoryOnly)
            .Select(System.Reflection.Assembly.LoadFrom)
            .Where(assembly => !assembly.GetName().Name!.EndsWith(".Tests", StringComparison.Ordinal))
            .OrderBy(assembly => assembly.GetName().Name, StringComparer.Ordinal),
    ];

    private static DirectoryInfo LocateRepository()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Packages.props")))
        {
            directory = directory.Parent;
        }

        return directory
            ?? throw new InvalidOperationException(
                "The repository root was not found above " + AppContext.BaseDirectory + ".");
    }
}
