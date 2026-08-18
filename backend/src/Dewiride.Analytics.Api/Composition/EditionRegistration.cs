using System.Reflection;
using Dewiride.Analytics.Application.Abstractions;

namespace Dewiride.Analytics.Api.Composition;

/// <summary>
/// Finds and runs the composition module for the edition this build was compiled as.
/// </summary>
/// <remarks>
/// <para>
/// The host never names either module. It looks for the one that was compiled alongside it, which
/// is how the open-source and commercial editions stay a fact about the project graph rather than
/// a branch in the source. Conditional compilation was rejected for this: Roslyn and SonarQube do
/// not analyse an inactive branch, so half the product would sit outside the quality gate.
/// </para>
/// <para>
/// Exactly one module must be present. Finding none means the host was built without an edition
/// project; finding two means both were referenced, which would leave it ambiguous which set of
/// services the process is running. Both are start-up failures with the cause named, rather than
/// a silent choice.
/// </para>
/// </remarks>
internal static class EditionRegistration
{
    /// <summary>
    /// Assemblies belonging to this product, which are the only ones searched.
    /// </summary>
    private const string AssemblyPrefix = "Dewiride.Analytics.";

    /// <summary>
    /// Locates the edition module and lets it register its services.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <returns>The module that was found, so the host can report which edition it is running.</returns>
    /// <exception cref="InvalidOperationException">There is not exactly one module to run.</exception>
    public static IEditionModule AddEdition(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var module = Locate();

        builder.Services.AddSingleton(module);
        module.Register(builder);

        return module;
    }

    private static IEditionModule Locate()
    {
        var candidates = ProductAssemblies()
            .SelectMany(assembly => assembly.GetExportedTypes())
            .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(IEditionModule).IsAssignableFrom(type))
            .ToArray();

        if (candidates.Length != 1)
        {
            throw new InvalidOperationException(
                $"Expected exactly one edition module alongside the host but found {candidates.Length}"
                + $" ({string.Join(", ", candidates.Select(type => type.FullName))}). Build with"
                + " -p:DewirideEdition=Community or -p:DewirideEdition=Cloud, never both.");
        }

        return (IEditionModule)Activator.CreateInstance(candidates[0])!;
    }

    private static IEnumerable<Assembly> ProductAssemblies() =>
        Directory
            .EnumerateFiles(AppContext.BaseDirectory, $"{AssemblyPrefix}*.dll", SearchOption.TopDirectoryOnly)
            .Select(Assembly.LoadFrom);
}
