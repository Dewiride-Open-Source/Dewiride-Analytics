using System.Reflection;
using Dewiride.Analytics.Extensibility;

namespace Dewiride.Analytics.Api.Composition;

/// <summary>
/// Finds and runs the composition module for the edition this build was compiled as.
/// </summary>
/// <remarks>
/// <para>
/// The host never names either module. It looks for the one that was compiled alongside it, which
/// is how the open-source and commercial editions stay a fact about the project graph rather than
/// a branch in the source. Conditional compilation was rejected for this: Roslyn analyzers and
/// SonarQube do not analyse an inactive branch, so half the product would sit outside the quality
/// gate.
/// </para>
/// <para>
/// Exactly one module must be present. Finding none means the host was built without an edition
/// project; finding two means both were referenced, which would leave it ambiguous which set of
/// services the process is running. Both are start-up failures with the cause named, rather than
/// a silent choice.
/// </para>
/// <para>
/// Endpoint sources are found the same way and counted differently: an edition may supply any
/// number, including none. The open-source edition supplies none, so an empty result is the
/// ordinary outcome rather than a sign that something failed to compile.
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

        var compiled = ProductAssemblies();
        var module = Locate(compiled);

        builder.Services.AddSingleton(module);
        module.Register(builder);

        // Registered as a type rather than an instance, so a group of endpoints may take the same
        // dependencies as any other component instead of having to reach for them at map time.
        foreach (var source in Implementations<IEditionEndpoints>(compiled))
        {
            builder.Services.AddSingleton(typeof(IEditionEndpoints), source);
        }

        return module;
    }

    /// <summary>
    /// Adds the endpoints the compiled edition brought with it.
    /// </summary>
    /// <remarks>
    /// Called after the host has mapped its own, so an edition cannot quietly take an address the
    /// product already answers: the framework refuses a second endpoint for a route that is
    /// already claimed.
    /// </remarks>
    /// <param name="routes">The route builder.</param>
    public static void MapEdition(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        foreach (var source in routes.ServiceProvider.GetServices<IEditionEndpoints>())
        {
            source.Map(routes);
        }
    }

    private static IEditionModule Locate(IReadOnlyList<Assembly> compiled)
    {
        var candidates = Implementations<IEditionModule>(compiled);

        if (candidates.Count != 1)
        {
            throw new InvalidOperationException(
                $"Expected exactly one edition module alongside the host but found {candidates.Count}"
                + $" ({string.Join(", ", candidates.Select(type => type.FullName))}). Build with"
                + " -p:DewirideEdition=Community or -p:DewirideEdition=Cloud, never both.");
        }

        return (IEditionModule)Activator.CreateInstance(candidates[0])!;
    }

    private static IReadOnlyList<Type> Implementations<TContract>(IReadOnlyList<Assembly> compiled) =>
    [
        .. compiled
            .SelectMany(assembly => assembly.GetExportedTypes())
            .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(TContract).IsAssignableFrom(type)),
    ];

    private static IReadOnlyList<Assembly> ProductAssemblies() =>
    [
        .. Directory
            .EnumerateFiles(AppContext.BaseDirectory, $"{AssemblyPrefix}*.dll", SearchOption.TopDirectoryOnly)
            .Select(Assembly.LoadFrom),
    ];
}
