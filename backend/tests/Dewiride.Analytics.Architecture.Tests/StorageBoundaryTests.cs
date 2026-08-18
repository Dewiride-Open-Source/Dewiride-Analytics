using System.Xml.Linq;

namespace Dewiride.Analytics.Architecture.Tests;

/// <summary>
/// Asserts that each store stays behind the project that owns it.
/// </summary>
/// <remarks>
/// Two stores that are not interchangeable: PostgreSQL holds the control plane and is reached
/// through Entity Framework Core; ClickHouse holds all telemetry and is reached through
/// hand-written SQL compiled from a closed vocabulary. Change tracking is the wrong abstraction
/// for an append-mostly columnar store, and the provider that would offer it is immature — so
/// the rule is not "prefer not to", it is "cannot".
/// </remarks>
[Trait("Category", "StorageBoundary")]
public sealed class StorageBoundaryTests
{
    private const string TelemetryDriverPackage = "ClickHouse.Driver";
    private const string EntityFrameworkPackage = "Microsoft.EntityFrameworkCore";

    [Fact]
    public void Only_Telemetry_Infrastructure_Was_Compiled_Against_The_Telemetry_Driver()
    {
        var users = Product.Assemblies
            .Where(assembly => Product.NonFrameworkReferences(assembly).Contains(TelemetryDriverPackage))
            .Select(assembly => assembly.GetName().Name);

        users.Should().Equal(Product.TelemetryInfrastructure);
    }

    [Fact]
    public void Only_Telemetry_Infrastructure_Declares_The_Telemetry_Driver()
    {
        DeclaringProjects(TelemetryDriverPackage)
            .Should().Equal("Dewiride.Analytics.Infrastructure.ClickHouse.csproj");
    }

    /// <summary>
    /// Entity Framework Core is never pointed at the telemetry store. The project that owns that
    /// store does not reference it, so there is nothing to point.
    /// </summary>
    [Fact]
    public void The_Telemetry_Project_Does_Not_Declare_An_Object_Relational_Mapper()
    {
        DeclaringProjects(EntityFrameworkPackage)
            .Should().NotContain("Dewiride.Analytics.Infrastructure.ClickHouse.csproj");
    }

    /// <summary>
    /// The domain model declares no dependency of its own. Making one impossible to add without
    /// it appearing in a diff is cheaper than noticing it later.
    /// </summary>
    [Fact]
    public void The_Domain_Project_Declares_No_Dependencies()
    {
        var project = XDocument.Load(ProjectPath("Dewiride.Analytics.Domain.csproj").FullName);

        project.Descendants("PackageReference").Should().BeEmpty();
        project.Descendants("ProjectReference").Should().BeEmpty();
    }

    [Fact]
    public void The_Domain_Was_Compiled_Against_The_Framework_And_Nothing_Else()
    {
        Product.NonFrameworkReferences(Product.Assembly(Product.Domain)).Should().BeEmpty();
    }

    /// <summary>
    /// The detection engine may see the domain model and nothing else. A package that could open
    /// a socket or read a file is what makes a verdict irreproducible, and reproducibility is the
    /// whole basis of the golden-fixture suite.
    /// </summary>
    [Fact]
    public void The_Detection_Engine_Was_Compiled_Against_Nothing_Beyond_The_Domain()
    {
        Product.NonFrameworkReferences(Product.Assembly(Product.Classification))
            .Should().BeSubsetOf([Product.Domain]);
    }

    private static IEnumerable<string> DeclaringProjects(string packageName) =>
        Product.ProjectFiles()
            .Where(file => Declares(file, packageName))
            .Select(file => file.Name)
            .Order(StringComparer.Ordinal);

    private static bool Declares(FileInfo project, string packageName) =>
        XDocument.Load(project.FullName)
            .Descendants("PackageReference")
            .Any(reference => string.Equals(
                reference.Attribute("Include")?.Value,
                packageName,
                StringComparison.Ordinal));

    private static FileInfo ProjectPath(string fileName) =>
        Product.ProjectFiles().Single(file => string.Equals(file.Name, fileName, StringComparison.Ordinal));
}
