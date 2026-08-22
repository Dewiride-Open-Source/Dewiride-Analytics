using NetArchTest.Rules;

namespace Dewiride.Analytics.Architecture.Tests;

/// <summary>
/// Asserts that dependencies point inwards.
/// </summary>
/// <remarks>
/// The layering is what keeps the detection engine testable without a database and lets the
/// telemetry store be replaced without touching a use case. Stated in a document it lasts until
/// somebody is in a hurry; stated here it fails the build.
/// </remarks>
[Trait("Category", "Layering")]
public sealed class LayeringTests
{
    /// <summary>
    /// The seams are a leaf. A commercial project implements them without taking a reference to
    /// the open-source composition root, and the assembly the host discovers them through belongs
    /// to neither edition.
    /// </summary>
    [Fact]
    public void The_Edition_Seams_Depend_On_Nothing_In_The_Product()
    {
        AssertNoDependency(
            Product.Extensibility,
            Product.Domain,
            Product.Application,
            Product.Classification,
            Product.Infrastructure,
            Product.Api);
    }

    [Fact]
    public void The_Domain_Depends_On_Nothing_In_The_Product()
    {
        AssertNoDependency(
            Product.Domain,
            Product.Application,
            Product.Classification,
            Product.Infrastructure,
            Product.Api);
    }

    [Fact]
    public void The_Application_Layer_Does_Not_Reach_Into_Infrastructure()
    {
        AssertNoDependency(Product.Application, Product.Infrastructure, Product.Api);
    }

    /// <summary>
    /// The detection engine is pure: everything it needs arrives as an argument. That is what
    /// makes a verdict reproducible, and reproducibility is what makes the golden-fixture suite a
    /// regression gate rather than noise.
    /// </summary>
    [Fact]
    public void The_Detection_Engine_Reaches_Nothing_That_Performs_Input_Or_Output()
    {
        AssertNoDependency(
            Product.Classification,
            Product.Application,
            Product.Infrastructure,
            Product.Api);
    }

    [Fact]
    public void Control_Plane_Infrastructure_Does_Not_Depend_On_The_Telemetry_Store()
    {
        AssertNoDependency(Product.Infrastructure, Product.TelemetryInfrastructure, Product.Api);
    }

    [Fact]
    public void Telemetry_Infrastructure_Does_Not_Depend_On_The_Control_Plane()
    {
        var telemetry = Product.Assembly(Product.TelemetryInfrastructure);

        Product.NonFrameworkReferences(telemetry)
            .Should().NotContain(Product.Infrastructure);
    }

    [Fact]
    public void Nothing_In_The_Product_Depends_On_The_Host()
    {
        var dependents = Product.Assemblies
            .Where(assembly => !Product.Named(assembly, Product.Api))
            .Where(assembly => Product.NonFrameworkReferences(assembly).Contains(Product.Api))
            .Select(assembly => assembly.GetName().Name);

        dependents.Should().BeEmpty();
    }

    private static void AssertNoDependency(string assemblyName, params string[] forbidden)
    {
        var result = Types.InAssembly(Product.Assembly(assemblyName))
            .ShouldNot()
            .HaveDependencyOnAny(forbidden)
            .GetResult();

        var offenders = result.FailingTypes?.Select(type => type.FullName) ?? [];

        result.IsSuccessful.Should().BeTrue(
            "{0} must not depend on {1}, but these types do: {2}",
            assemblyName,
            string.Join(", ", forbidden),
            string.Join(", ", offenders));
    }
}
