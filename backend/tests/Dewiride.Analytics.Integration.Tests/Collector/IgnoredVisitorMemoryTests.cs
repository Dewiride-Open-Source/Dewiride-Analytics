using Dewiride.Analytics.Infrastructure.Ingest;
using Microsoft.Extensions.Time.Testing;

namespace Dewiride.Analytics.Integration.Tests.Collector;

/// <summary>
/// Covers how long the collector goes on turning away a visitor whose visit it turned away at the
/// door: for as long as the visit lasts, which is until the visitor has been quiet for as long as
/// the engine waits before it calls a visit over.
/// </summary>
/// <remarks>
/// Here rather than with the unit suites because the memory is the infrastructure's own. Its clock
/// is a controlled one, so "half an hour later" is a statement rather than a wait.
/// </remarks>
public sealed class IgnoredVisitorMemoryTests
{
    private static readonly TimeSpan Visit = TimeSpan.FromMinutes(30);

    private static readonly Guid Site = new("0197c0de-0000-7000-8000-000000000001");

    private const string Visitor = "9f2a1c4e8b6d0a3f";

    [Fact]
    public void Recalls_A_Visitor_It_Was_Asked_To_Remember()
    {
        var (memory, _) = Memory();

        memory.Remember(Site, Visitor);

        memory.Recalls(Site, Visitor).Should().BeTrue();
    }

    [Fact]
    public void Recalls_Nobody_It_Was_Not_Asked_To_Remember()
    {
        var (memory, _) = Memory();

        memory.Remember(Site, Visitor);

        memory.Recalls(Site, "0000000000000000").Should().BeFalse();
        memory.Recalls(Guid.NewGuid(), Visitor).Should().BeFalse();
    }

    /// <summary>
    /// The engine starts a new visit for somebody who has been quiet this long, so this is when the
    /// visit that was turned away is over.
    /// </summary>
    [Fact]
    public void Forgets_A_Visitor_Who_Has_Been_Quiet_For_As_Long_As_A_Visit_Lasts()
    {
        var (memory, clock) = Memory();
        memory.Remember(Site, Visitor);

        clock.Advance(Visit - TimeSpan.FromMilliseconds(1));
        var stillThere = memory.Recalls(Site, Visitor);

        clock.Advance(Visit);
        var gone = memory.Recalls(Site, Visitor);

        stillThere.Should().BeTrue();
        gone.Should().BeFalse();
    }

    /// <summary>
    /// A page left open reports every minute for as long as it is open, and each report keeps its
    /// visit going — so it goes on being turned away however long that is.
    /// </summary>
    [Fact]
    public void Holds_A_Visitor_For_As_Long_As_They_Keep_Being_Heard_From()
    {
        var (memory, clock) = Memory();
        memory.Remember(Site, Visitor);

        for (var minute = 0; minute < 120; minute++)
        {
            clock.Advance(TimeSpan.FromMinutes(1));
            memory.Recalls(Site, Visitor).Should().BeTrue();
        }
    }

    [Fact]
    public void Holds_A_Visitor_Remembered_Again_From_The_Moment_They_Were()
    {
        var (memory, clock) = Memory();
        memory.Remember(Site, Visitor);

        clock.Advance(TimeSpan.FromMinutes(20));
        memory.Remember(Site, Visitor);
        clock.Advance(TimeSpan.FromMinutes(20));

        memory.Recalls(Site, Visitor).Should().BeTrue();
    }

    /// <summary>
    /// Whoever sends the visits decides how many visitors there are, so the memory stops growing
    /// rather than this process's memory growing with it.
    /// </summary>
    [Fact]
    public void Remembers_No_New_Visitor_Once_Full()
    {
        var (memory, _) = Memory(capacity: 2);

        memory.Remember(Site, "first");
        memory.Remember(Site, "second");
        memory.Remember(Site, "third");

        memory.Recalls(Site, "first").Should().BeTrue();
        memory.Recalls(Site, "second").Should().BeTrue();
        memory.Recalls(Site, "third").Should().BeFalse();
    }

    [Fact]
    public void Makes_Room_By_Forgetting_Visitors_Whose_Visit_Is_Over()
    {
        var (memory, clock) = Memory(capacity: 2);
        memory.Remember(Site, "first");
        memory.Remember(Site, "second");

        clock.Advance(Visit);
        memory.Remember(Site, "third");

        memory.Recalls(Site, "third").Should().BeTrue();
    }

    private static (MemoryIgnoredVisitors Memory, FakeTimeProvider Clock) Memory(
        int capacity = MemoryIgnoredVisitors.MostRemembered)
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 29, 16, 0, 0, TimeSpan.Zero));

        return (new MemoryIgnoredVisitors(Visit, capacity, clock), clock);
    }
}
