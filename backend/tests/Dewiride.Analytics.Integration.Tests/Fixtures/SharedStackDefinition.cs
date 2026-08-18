namespace Dewiride.Analytics.Integration.Tests.Fixtures;

/// <summary>
/// Groups every test that runs against the shared stack.
/// </summary>
/// <remarks>
/// One pair of containers serves the whole suite. Starting a pair per class would multiply a
/// twenty-second start-up by the number of classes and buy nothing: tests create their own
/// organisations, sites and accounts with fresh identifiers, so nothing they write is visible to
/// anything else.
/// </remarks>
[CollectionDefinition(Name)]
public sealed class SharedStackDefinition : ICollectionFixture<AnalyticsStackFixture>
{
    /// <summary>Name the collection is referenced by.</summary>
    public const string Name = "analytics-stack";
}
