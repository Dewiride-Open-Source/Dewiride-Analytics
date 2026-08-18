using System.Reflection;
using Dewiride.Analytics.Application.Analytics;
using Dewiride.Analytics.Application.Tenancy;

namespace Dewiride.Analytics.Architecture.Tests;

/// <summary>
/// Asserts that tenant isolation and the query vocabulary are enforced by the type system.
/// </summary>
/// <remarks>
/// Both invariants are stated as accessibility rather than as guidance, and accessibility is
/// exactly the sort of thing a later refactor widens without meaning to. Reading it back from the
/// compiled assembly is what turns "the constructor is internal" into something that stays true.
/// </remarks>
[Trait("Category", "AuthorisationBoundary")]
public sealed class AuthorisationBoundaryTests
{
    /// <summary>
    /// A scope is proof that membership was checked. If any caller holding a site identifier could
    /// construct one, the type would document an intention rather than enforce a rule.
    /// </summary>
    [Fact]
    public void An_Authorisation_Scope_Cannot_Be_Constructed_By_Arbitrary_Code()
    {
        typeof(TenantScope).GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .Should().BeEmpty();
    }

    /// <summary>
    /// A window is the only thing a question is built around, and the constructor that takes one
    /// is reachable from this assembly alone. What a subtype introduced some other way would get
    /// is covered by the SQL suite: the compiler refuses a question it was not taught.
    /// </summary>
    [Fact]
    public void A_Question_Can_Only_Be_Given_A_Window_From_Within_The_Vocabulary()
    {
        var constructors = typeof(AnalyticsQuery)
            .GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(constructor => constructor.GetParameters().Any(parameter => parameter.ParameterType == typeof(TimeRange)));

        constructors.Should().NotBeEmpty();
        constructors.Should().OnlyContain(constructor => constructor.IsFamilyAndAssembly);
    }

    [Fact]
    public void Every_Question_In_The_Vocabulary_Lives_Beside_It()
    {
        var vocabulary = typeof(AnalyticsQuery).Assembly;

        Questions().Should().OnlyContain(question => question.Assembly == vocabulary);
    }

    [Fact]
    public void Every_Question_In_The_Vocabulary_Is_A_Leaf()
    {
        Questions().Should().OnlyContain(question => question.IsSealed);
    }

    [Fact]
    public void The_Vocabulary_Has_At_Least_The_Questions_The_Dashboard_Asks()
    {
        Questions().Select(question => question.Name)
            .Should().Contain(["OverviewQuery", "TimeSeriesQuery"]);
    }

    private static IReadOnlyList<Type> Questions() =>
    [
        .. typeof(AnalyticsQuery).Assembly
            .GetExportedTypes()
            .Where(type => type.IsSubclassOf(typeof(AnalyticsQuery))),
    ];
}
