using Dewiride.Analytics.Application.Telemetry;
using Dewiride.Analytics.Infrastructure.Telemetry;
using Dewiride.Analytics.Integration.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Dewiride.Analytics.Integration.Tests.ControlPlane;

/// <summary>
/// Proves the property the privacy envelope rests on.
/// </summary>
/// <remarks>
/// A visitor's activity groups within one day and cannot be followed into the next, because the
/// salt the key was derived from is deleted and the hash cannot be recomputed by anyone — the
/// operator included. That is a property of the system rather than a promise about intentions,
/// and a key that could still be derived for a day whose salt is gone would quietly remove it.
/// </remarks>
/// <param name="stack">The running stack.</param>
[Collection(SharedStackDefinition.Name)]
public sealed class VisitorKeyTests(AnalyticsStackFixture stack)
{
    private const string Address = "203.0.113.7";
    private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36";

    [Fact]
    public async Task The_Same_Visitor_Gets_The_Same_Key_Twice_On_The_Same_Day()
    {
        var (factory, today) = await ReadyAsync();
        var site = Guid.NewGuid();

        var first = factory.Derive(site, Address, UserAgent, today);
        var second = factory.Derive(site, Address, UserAgent, today);

        first.Should().NotBeNullOrEmpty();
        second.Should().Be(first);
    }

    [Fact]
    public async Task The_Same_Visitor_Gets_A_Different_Key_On_Each_Site()
    {
        var (factory, today) = await ReadyAsync();

        var first = factory.Derive(Guid.NewGuid(), Address, UserAgent, today);
        var second = factory.Derive(Guid.NewGuid(), Address, UserAgent, today);

        first.Should().NotBe(second);
    }

    [Fact]
    public async Task A_Different_Visitor_Gets_A_Different_Key()
    {
        var (factory, today) = await ReadyAsync();
        var site = Guid.NewGuid();

        var first = factory.Derive(site, Address, UserAgent, today);
        var second = factory.Derive(site, "198.51.100.24", UserAgent, today);

        first.Should().NotBe(second);
    }

    /// <summary>
    /// Once the salt for a day has gone there is no key for that day, for anybody. Returning an
    /// unsalted hash instead would be a stable, reversible identifier — which is the thing the
    /// design exists to prevent.
    /// </summary>
    [Fact]
    public async Task No_Key_Can_Be_Derived_For_A_Day_Whose_Salt_Has_Gone()
    {
        var (factory, today) = await ReadyAsync();

        var key = factory.Derive(Guid.NewGuid(), Address, UserAgent, today.AddDays(-30));

        key.Should().BeNull();
    }

    /// <summary>
    /// With neither an address nor a user agent there is nothing distinguishing to hash, and a key
    /// derived from the site alone would group every such visitor into one fictitious person.
    /// </summary>
    [Fact]
    public async Task No_Key_Is_Invented_When_There_Is_Nothing_To_Derive_One_From()
    {
        var (factory, today) = await ReadyAsync();

        factory.Derive(Guid.NewGuid(), null, null, today).Should().BeNull();
        factory.Derive(Guid.NewGuid(), string.Empty, string.Empty, today).Should().BeNull();
    }

    [Fact]
    public async Task A_Key_Reveals_Nothing_About_The_Address_It_Came_From()
    {
        var (factory, today) = await ReadyAsync();

        var key = factory.Derive(Guid.NewGuid(), Address, UserAgent, today);

        key.Should().NotBeNull().And.NotContain(Address);
        key.Should().HaveLength(32);
    }

    [Fact]
    public async Task Rotating_Twice_On_The_Same_Day_Keeps_The_Same_Salt()
    {
        var (factory, today) = await ReadyAsync();
        var site = Guid.NewGuid();
        var before = factory.Derive(site, Address, UserAgent, today);

        await stack.Services.GetRequiredService<VisitorKeySaltStore>().RotateAsync(Cancellation.Token);

        factory.Derive(site, Address, UserAgent, today).Should().Be(before);
    }

    /// <summary>
    /// A day's first reports arrive the instant it begins. A salt made only by whatever ran next
    /// after midnight would leave every one of them without a key — nobody, counted nowhere. A
    /// rotation the evening before has already made the next day's.
    /// </summary>
    [Fact]
    public async Task The_Next_Day_Has_A_Key_The_Moment_It_Begins()
    {
        var evening = new DateTimeOffset(2026, 3, 14, 23, 30, 0, TimeSpan.Zero);
        var factory = new RotatingSaltVisitorKeyFactory(await RotatedAtAsync(evening));

        var key = factory.Derive(
            Guid.NewGuid(), Address, UserAgent, evening.AddMinutes(30).AddMilliseconds(1));

        key.Should().NotBeNullOrEmpty();
    }

    /// <summary>
    /// The salt made ahead is the one its day is keyed by from its first instant to its last. The
    /// first rotation of the new day finds it rather than making another, so a visitor keyed a
    /// minute after midnight is the same visitor once that rotation has run.
    /// </summary>
    [Fact]
    public async Task The_Salt_Made_Ahead_Is_The_One_Its_Day_Uses()
    {
        var evening = new DateTimeOffset(2026, 4, 18, 23, 30, 0, TimeSpan.Zero);
        var site = Guid.NewGuid();
        var justAfterMidnight = evening.AddMinutes(31);

        var before = new RotatingSaltVisitorKeyFactory(await RotatedAtAsync(evening))
            .Derive(site, Address, UserAgent, justAfterMidnight);
        var after = new RotatingSaltVisitorKeyFactory(await RotatedAtAsync(evening.AddHours(1)))
            .Derive(site, Address, UserAgent, justAfterMidnight);

        after.Should().NotBeNull().And.Be(before);
    }

    /// <summary>
    /// A salt is made one day ahead and no further, so nothing that could key a visitor exists
    /// long before it is about to be needed.
    /// </summary>
    [Fact]
    public async Task No_Salt_Is_Made_For_The_Day_After_Tomorrow()
    {
        var morning = new DateTimeOffset(2026, 5, 22, 9, 0, 0, TimeSpan.Zero);
        var factory = new RotatingSaltVisitorKeyFactory(await RotatedAtAsync(morning));

        factory.Derive(Guid.NewGuid(), Address, UserAgent, morning.AddDays(2)).Should().BeNull();
    }

    /// <summary>
    /// A store of its own on a clock of its own, rotated once. Its days lie months in the past, so
    /// its clearing out of older salts reaches nothing the rest of the stack keys visitors by.
    /// </summary>
    private async Task<VisitorKeySaltStore> RotatedAtAsync(DateTimeOffset at)
    {
        var store = new VisitorKeySaltStore(
            stack.Services.GetRequiredService<IServiceScopeFactory>(),
            new FakeTimeProvider(at),
            NullLogger<VisitorKeySaltStore>.Instance);

        await store.RotateAsync(Cancellation.Token);

        return store;
    }

    private async Task<(IVisitorKeyFactory Factory, DateTimeOffset Today)> ReadyAsync()
    {
        // Rotation also runs in the background; calling it here means a test never races the
        // hosted service for the day's first salt.
        await stack.Services.GetRequiredService<VisitorKeySaltStore>().RotateAsync(Cancellation.Token);

        return (
            stack.Services.GetRequiredService<IVisitorKeyFactory>(),
            stack.Services.GetRequiredService<TimeProvider>().GetUtcNow());
    }
}
