using Dewiride.Analytics.Application.Telemetry;

namespace Dewiride.Analytics.Application.Tests.Telemetry;

/// <summary>
/// Covers the day a website's record begins on: the day of its first visit, where that can be
/// known.
/// </summary>
/// <remarks>
/// Asked at the middle of March 2026, in UTC.
/// </remarks>
public sealed class FirstDaysTests
{
    private static readonly DateTimeOffset AsOf = new(2026, 3, 15, 12, 0, 0, TimeSpan.Zero);

    /// <summary>When a website was added two years before <see cref="AsOf"/>.</summary>
    private static readonly DateTimeOffset AddedLongAgo = AsOf.AddYears(-2);

    /// <summary>When a website was added three months before <see cref="AsOf"/>.</summary>
    private static readonly DateTimeOffset AddedLately = AsOf.AddMonths(-3);

    private static readonly DateOnly OldestHeld = new(2026, 1, 19);

    [Fact]
    public void A_Kept_First_Day_Stands_Once_Its_Report_Has_Gone()
    {
        var kept = new DateOnly(2025, 2, 10);

        Of(kept, new DateOnly(2025, 3, 20), AddedLongAgo, keepingSince: null).Should().Be(kept);
    }

    /// <summary>
    /// While the report a kept first day was taken from is still held, the day is read again on the
    /// calendar the website keeps now, so a change of zone moves it to the day it falls on there.
    /// </summary>
    [Fact]
    public void A_Kept_First_Day_Is_Read_On_The_Calendar_Kept_Now_While_Its_Report_Is_Held()
    {
        Of(new DateOnly(2026, 1, 31), new DateOnly(2026, 2, 1), AddedLongAgo, keepingSince: null)
            .Should().Be(new DateOnly(2026, 2, 1));
    }

    /// <summary>
    /// A day read again on a new calendar is kept with the month it falls in there. Where that month
    /// can no longer be counted, the day could be kept nowhere, so it stays as it was kept rather than
    /// being lost with the next count of the month that holds it.
    /// </summary>
    [Fact]
    public void A_Kept_First_Day_Stays_As_It_Was_Kept_Where_The_Month_It_Would_Move_Into_Can_No_Longer_Be_Counted()
    {
        var kept = new DateOnly(2026, 3, 1);

        FirstDays.Of(kept, new DateOnly(2026, 2, 28), oldestHeldCanBeCounted: false, AddedLongAgo, null, AsOf)
            .Should().Be(kept);
    }

    /// <summary>
    /// Two zones differ by two days at most, so an oldest report further from the kept day than that
    /// is a later report, not the same one read on another calendar.
    /// </summary>
    [Fact]
    public void A_Kept_First_Day_Stands_Over_An_Oldest_Report_Further_Than_A_Change_Of_Zone_Could_Move_It()
    {
        var kept = new DateOnly(2026, 1, 10);

        Of(kept, new DateOnly(2026, 1, 13), AddedLongAgo, keepingSince: null).Should().Be(kept);
    }

    [Fact]
    public void With_Nothing_Kept_The_Oldest_Report_Is_The_First_Day_Of_A_Website_Added_Within_The_Year()
    {
        Of(null, OldestHeld, AddedLately, keepingSince: null).Should().Be(OldestHeld);
    }

    /// <summary>
    /// Months kept since before the website was added mean every month it sent anything in was
    /// kept, so its oldest report is its first however long ago it was added.
    /// </summary>
    [Fact]
    public void With_Nothing_Kept_The_Oldest_Report_Is_The_First_Day_Where_Months_Were_Kept_All_Along()
    {
        Of(null, OldestHeld, AddedLongAgo, keepingSince: AsOf.AddYears(-3)).Should().Be(OldestHeld);
    }

    /// <summary>
    /// Keeping that began after the month a website was added in could last be counted leaves room
    /// for reports that went before anything of them was kept.
    /// </summary>
    [Fact]
    public void With_Nothing_Kept_No_First_Day_Is_Named_Where_Keeping_Began_After_The_Websites_First_Month_Could_Be_Counted()
    {
        Of(null, OldestHeld, AddedLongAgo, keepingSince: AsOf.AddMonths(-2)).Should().BeNull();
    }

    [Fact]
    public void Keeping_Is_Taken_To_Begin_Now_Where_It_Is_Not_Known()
    {
        Of(null, OldestHeld, AddedLongAgo, keepingSince: null).Should().BeNull();
        FirstDays.NothingWentUncounted(AddedLately, keepingSince: null, AsOf).Should().BeTrue();
    }

    [Fact]
    public void Keeping_Said_To_Begin_After_The_Question_Is_Taken_To_Begin_Now()
    {
        FirstDays.NothingWentUncounted(AddedLongAgo, keepingSince: AsOf.AddDays(1), AsOf).Should().BeFalse();
        FirstDays.NothingWentUncounted(AddedLately, keepingSince: AsOf.AddDays(1), AsOf).Should().BeTrue();
    }

    /// <summary>
    /// A month is counted whole or not at all, so what matters is whether the month the website was
    /// added in could still be counted when keeping began — not whether a year had passed since it
    /// was added. Added on the tenth of November 2025, its month begins on the first of November on
    /// the clock furthest ahead, which can be counted until about ten o'clock on the twenty-ninth of
    /// October 2026.
    /// </summary>
    [Fact]
    public void A_Website_Is_Vouched_For_Only_Where_The_Month_It_Was_Added_In_Could_Still_Be_Counted_When_Keeping_Began()
    {
        var added = new DateTimeOffset(2025, 11, 10, 9, 0, 0, TimeSpan.Zero);
        var asOf = new DateTimeOffset(2027, 2, 1, 0, 0, 0, TimeSpan.Zero);

        FirstDays.NothingWentUncounted(added, new DateTimeOffset(2026, 10, 29, 9, 0, 0, TimeSpan.Zero), asOf).Should().BeTrue();
        FirstDays.NothingWentUncounted(added, new DateTimeOffset(2026, 10, 29, 11, 0, 0, TimeSpan.Zero), asOf).Should().BeFalse();
    }

    /// <summary>
    /// The months were counted on whatever calendar the website kept at the time, and its zone can
    /// change after, so the month it was added in is taken on the calendar that puts it earliest.
    /// Added at three in the morning of the first of August in UTC, it was added on the last day of
    /// July on the clock furthest behind — so keeping that began after July could be counted, though
    /// August still could on its own calendar, does not vouch for it, in any zone it is given.
    /// </summary>
    [Fact]
    public void The_Month_A_Website_Was_Added_In_Is_Taken_On_The_Calendar_That_Puts_It_Earliest()
    {
        var added = new DateTimeOffset(2026, 8, 1, 3, 0, 0, TimeSpan.Zero);
        var asOf = new DateTimeOffset(2027, 9, 1, 0, 0, 0, TimeSpan.Zero);

        FirstDays.NothingWentUncounted(added, new DateTimeOffset(2027, 6, 27, 0, 0, 0, TimeSpan.Zero), asOf).Should().BeTrue();
        FirstDays.NothingWentUncounted(added, new DateTimeOffset(2027, 7, 10, 0, 0, 0, TimeSpan.Zero), asOf).Should().BeFalse();
    }

    [Fact]
    public void A_Website_Never_Measured_Has_No_First_Day()
    {
        Of(null, null, AddedLately, keepingSince: null).Should().BeNull();
    }

    /// <summary>The day, where the month the oldest report falls in can still be counted.</summary>
    private static DateOnly? Of(DateOnly? kept, DateOnly? oldestHeld, DateTimeOffset addedAt, DateTimeOffset? keepingSince) =>
        FirstDays.Of(kept, oldestHeld, oldestHeldCanBeCounted: true, addedAt, keepingSince, AsOf);
}
