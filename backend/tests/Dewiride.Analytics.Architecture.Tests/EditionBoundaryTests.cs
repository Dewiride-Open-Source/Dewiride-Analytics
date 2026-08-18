using System.Text.RegularExpressions;
using Dewiride.Analytics.Application.Abstractions;

namespace Dewiride.Analytics.Architecture.Tests;

/// <summary>
/// Asserts that the open-source build contains no commercially licensed code.
/// </summary>
/// <remarks>
/// The two editions differ by which projects were compiled, never by a preprocessor branch:
/// Roslyn and SonarQube do not analyse an inactive branch, so under conditional compilation half
/// the product would sit outside a quality gate this repository treats as non-negotiable, and
/// coverage across the two editions would stop meaning anything. This suite is what makes the
/// separation a fact about the build output rather than a promise.
/// </remarks>
[Trait("Category", "EditionBoundary")]
public sealed partial class EditionBoundaryTests
{
    [Fact]
    public void No_Assembly_In_This_Build_Was_Compiled_Against_A_Commercial_One()
    {
        var offenders = Product.Assemblies
            .Where(assembly => Product.NonFrameworkReferences(assembly)
                .Any(reference => reference.StartsWith(Product.CommercialNamespace, StringComparison.Ordinal)))
            .Select(assembly => assembly.GetName().Name);

        offenders.Should().BeEmpty();
    }

    [Fact]
    public void No_Commercial_Assembly_Was_Built_Into_This_Output()
    {
        var present = Product.Assemblies
            .Select(assembly => assembly.GetName().Name!)
            .Where(name => name.StartsWith(Product.CommercialNamespace, StringComparison.Ordinal));

        present.Should().BeEmpty();
    }

    /// <summary>
    /// The host finds its composition module rather than naming one, and refuses to start unless
    /// there is exactly one. Finding none means no edition was compiled; finding two means both
    /// were, and which set of services the process runs would be undefined.
    /// </summary>
    [Fact]
    public void Exactly_One_Composition_Module_Was_Compiled()
    {
        Modules().Should().ContainSingle();
    }

    [Fact]
    public void The_Composition_Module_Can_Be_Created_By_The_Host()
    {
        var module = Modules().Should().ContainSingle().Subject;

        module.IsPublic.Should().BeTrue();
        module.IsSealed.Should().BeTrue();
        module.GetConstructor(Type.EmptyTypes).Should().NotBeNull();
    }

    [Fact]
    public void The_Composition_Module_Names_The_Edition_It_Composes()
    {
        var module = Modules().Should().ContainSingle().Subject;
        var instance = (IEditionModule)Activator.CreateInstance(module)!;

        instance.EditionName.Should().Be("Community");
    }

    /// <summary>
    /// Conditional compilation is the thing this arrangement exists instead of. A branch that the
    /// compiler does not take is a branch no analyzer reads.
    /// </summary>
    [Fact]
    public void No_Source_File_Switches_On_The_Edition()
    {
        var offenders = Product.SourceFiles()
            .Where(file => File.ReadLines(file.FullName).Any(SwitchesOnTheEdition))
            .Select(file => file.FullName);

        offenders.Should().BeEmpty();
    }

    private static bool SwitchesOnTheEdition(string line) =>
        EditionDirective().IsMatch(line);

    [GeneratedRegex(@"^\s*#\s*(if|elif)\b.*\bEE\b", RegexOptions.CultureInvariant)]
    private static partial Regex EditionDirective();

    private static IEnumerable<Type> Modules() =>
        Product.Assemblies
            .SelectMany(assembly => assembly.GetExportedTypes())
            .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(IEditionModule).IsAssignableFrom(type));
}
