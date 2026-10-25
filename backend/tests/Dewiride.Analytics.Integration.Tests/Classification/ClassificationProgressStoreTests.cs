using Dewiride.Analytics.Application.Sessions;
using Dewiride.Analytics.Classification;
using Dewiride.Analytics.Infrastructure.Persistence;
using Dewiride.Analytics.Integration.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Dewiride.Analytics.Integration.Tests.Classification;

/// <summary>
/// Proves how a judging bookmark moves, against the database that keeps it.
/// </summary>
/// <remarks>
/// Forward only is the store's to enforce, because only the store sees every instance of the engine
/// at once — so it is proved here, with instances racing each other and with one holding a store
/// for a whole run as the background work does, rather than against a value held in memory.
/// </remarks>
/// <param name="stack">The running stack.</param>
[Collection(SharedStackDefinition.Name)]
public sealed class ClassificationProgressStoreTests(AnalyticsStackFixture stack)
{
    /// <summary>A whole second, so that every instant survives the store's microseconds exactly.</summary>
    private static readonly DateTimeOffset Start = new(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Rules other than the ones being run, for a site judged under two sets of them.</summary>
    private static readonly RulesetVersion Earlier = new(1, 0);

    [Fact]
    public async Task Each_Instant_Moves_Forward_On_Its_Own()
    {
        var siteId = await StartedAsync();

        (await AdvanceAsync(siteId, Start, Start.AddHours(6))).Should().BeTrue();
        (await AdvanceAsync(siteId, Start.AddHours(2), Start.AddHours(5))).Should().BeTrue();

        var bookmark = await ResumeAsync(siteId);

        bookmark.ClassifiedThrough.Should().Be(Start.AddHours(2));
        bookmark.ReadThrough.Should().Be(Start.AddHours(6));
    }

    /// <summary>
    /// An instance a moment behind another is told nothing moved, which is what tells it the other
    /// is the one to carry on.
    /// </summary>
    [Fact]
    public async Task Nothing_Moves_Where_The_Bookmark_Has_Already_Been_As_Far()
    {
        var siteId = await StartedAsync();
        await AdvanceAsync(siteId, Start.AddHours(2), Start.AddHours(6));

        var moved = await AdvanceAsync(siteId, Start.AddHours(1), Start.AddHours(6));

        moved.Should().BeFalse();
        (await ResumeAsync(siteId)).Should().Be(new ClassificationBookmark(Start.AddHours(2), Start.AddHours(6)));
    }

    /// <summary>
    /// The background work resolves one store for a whole run, so whatever it read as the run began
    /// is long out of date by the time it moves the bookmark. Another instance moving the bookmark
    /// further in between is not undone, and a move past it on one instant still lands.
    /// </summary>
    [Fact]
    public async Task A_Store_Held_For_A_Whole_Run_Does_Not_Undo_What_Another_Engine_Recorded()
    {
        var siteId = await StartedAsync();
        await using var run = stack.Services.CreateAsyncScope();
        var held = run.ServiceProvider.GetRequiredService<IClassificationProgressStore>();
        await held.ResumeFromAsync(siteId, RulesetVersion.Current, Start, Cancellation.Token);

        await AdvanceAsync(siteId, Start.AddHours(2), Start.AddHours(6));

        var behind = await held.AdvanceAsync(
            siteId,
            RulesetVersion.Current,
            new ClassificationBookmark(Start.AddHours(1), Start.AddHours(3)),
            Cancellation.Token);

        behind.Should().BeFalse();
        (await ResumeAsync(siteId)).Should().Be(new ClassificationBookmark(Start.AddHours(2), Start.AddHours(6)));

        var further = await held.AdvanceAsync(
            siteId,
            RulesetVersion.Current,
            new ClassificationBookmark(Start.AddHours(3), Start.AddHours(5)),
            Cancellation.Token);

        further.Should().BeTrue();
        (await ResumeAsync(siteId)).Should().Be(new ClassificationBookmark(Start.AddHours(3), Start.AddHours(6)));
    }

    /// <summary>
    /// Instances of the engine move one bookmark without coordinating. Whatever order their moves
    /// land in, each instant ends at the furthest any of them reached.
    /// </summary>
    [Fact]
    public async Task Engines_Moving_One_Bookmark_At_Once_Leave_It_At_The_Furthest_Each_Reached()
    {
        var siteId = await StartedAsync();
        var offers = Enumerable.Range(0, 8)
            .Select(engine => new ClassificationBookmark(
                Start.AddMinutes(10 * engine),
                Start.AddHours(16 - engine)))
            .ToArray();

        await Task.WhenAll(offers.Select(offer =>
            AdvanceAsync(siteId, offer.ClassifiedThrough, offer.ReadThrough)));

        (await ResumeAsync(siteId)).Should().Be(new ClassificationBookmark(Start.AddMinutes(70), Start.AddHours(16)));
    }

    /// <summary>A move records when it was made; a move refused records nothing.</summary>
    [Fact]
    public async Task A_Move_Is_Stamped_With_When_It_Was_Made_And_A_Refused_One_Is_Not()
    {
        var siteId = await StartedAsync();
        var now = stack.Services.GetRequiredService<TimeProvider>().GetUtcNow();

        await AdvanceAsync(siteId, Start.AddHours(2), Start.AddHours(6));
        var moved = await UpdatedAtAsync(siteId);
        await AdvanceAsync(siteId, Start.AddHours(1), Start.AddHours(5));

        moved.Should().BeCloseTo(now, TimeSpan.FromMinutes(1));
        (await UpdatedAtAsync(siteId)).Should().Be(moved);
    }

    /// <summary>A site removed while a run was reading it leaves nothing to move.</summary>
    [Fact]
    public async Task A_Bookmark_That_Is_Not_There_Does_Not_Move()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());

        var moved = await AdvanceAsync(site.Id, Start, Start.AddHours(6));

        moved.Should().BeFalse();
        (await CaughtUpToAsync(site.Id)).Should().BeNull();
    }

    /// <summary>Nothing can have been judged that was not read.</summary>
    [Fact]
    public async Task Judging_Further_Than_Was_Read_Is_Refused()
    {
        var siteId = await StartedAsync();

        var act = () => AdvanceAsync(siteId, Start.AddHours(6), Start.AddHours(5));

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
        (await ResumeAsync(siteId)).Should().Be(new ClassificationBookmark(Start, Start));
    }

    /// <summary>
    /// The rules being run judge whatever they found in progress once it finishes, so they count
    /// as far as they have read; other rules count only as far as they judged.
    /// </summary>
    [Fact]
    public async Task A_Site_Is_Caught_Up_As_Far_As_The_Rules_Being_Run_Have_Read()
    {
        var siteId = await StartedAsync();
        await ResumeAsync(siteId, Earlier);
        await AdvanceAsync(siteId, Start.AddHours(1), Start.AddHours(9), Earlier);
        await AdvanceAsync(siteId, Start.AddHours(3), Start.AddHours(4));

        var elsewhere = await StartedAsync();
        await AdvanceAsync(elsewhere, Start.AddHours(20), Start.AddHours(20));

        (await CaughtUpToAsync(siteId)).Should().Be(Start.AddHours(4));
    }

    /// <summary>
    /// A visit other rules found still in progress is judged by nothing until the rules being run
    /// reach it in their own walk, so what those rules read past where they judged is not counted —
    /// and where they judged further than the rules being run have read, that is.
    /// </summary>
    [Fact]
    public async Task Other_Rules_Count_Only_As_Far_As_They_Judged()
    {
        var siteId = await StartedAsync();
        await ResumeAsync(siteId, Earlier);
        await AdvanceAsync(siteId, Start.AddHours(5), Start.AddHours(9), Earlier);
        await AdvanceAsync(siteId, Start.AddHours(3), Start.AddHours(4));

        (await CaughtUpToAsync(siteId)).Should().Be(Start.AddHours(5));
    }

    [Fact]
    public async Task A_Site_Judging_Has_Not_Started_On_Is_Not_Caught_Up_At_All()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());

        (await CaughtUpToAsync(site.Id)).Should().BeNull();
    }

    /// <summary>
    /// A release that keeps only how far judging has got — one the installation was rolled back to
    /// — starts a bookmark without saying how far it has read, and moves the judging alone.
    /// Everything judged was read, so the reading is taken to have got as far as the judging, and
    /// the next run reads on from there rather than through what was judged already.
    /// </summary>
    [Fact]
    public async Task A_Bookmark_Kept_By_A_Release_That_Records_Only_Its_Judging_Reads_As_Read_That_Far()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());

        await using (var scope = stack.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>().Database;

            await database.ExecuteSqlAsync(
                $"""
                INSERT INTO classification_progress
                    (site_id, ruleset_major, ruleset_minor, classified_through, updated_at)
                VALUES
                    ({site.Id}, {RulesetVersion.Current.Major}, {RulesetVersion.Current.Minor}, {Start}, {Start})
                """,
                Cancellation.Token);

            await database.ExecuteSqlAsync(
                $"""
                UPDATE classification_progress
                SET classified_through = {Start.AddHours(5)}
                WHERE site_id = {site.Id}
                """,
                Cancellation.Token);
        }

        (await ResumeAsync(site.Id)).Should().Be(new ClassificationBookmark(Start.AddHours(5), Start.AddHours(5)));
        (await CaughtUpToAsync(site.Id)).Should().Be(Start.AddHours(5));
    }

    private static string Domain() => $"{Guid.NewGuid():n}.example.com";

    /// <summary>Adds a site and starts its bookmark under the rules being run.</summary>
    private async Task<Guid> StartedAsync()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());

        await ResumeAsync(site.Id);

        return site.Id;
    }

    private async Task<ClassificationBookmark> ResumeAsync(Guid siteId, RulesetVersion? ruleset = null)
    {
        await using var scope = stack.Services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<IClassificationProgressStore>()
            .ResumeFromAsync(siteId, ruleset ?? RulesetVersion.Current, Start, Cancellation.Token)
            .ConfigureAwait(false);
    }

    /// <summary>Moves a bookmark from a scope of its own, as a separate instance of the engine would.</summary>
    private async Task<bool> AdvanceAsync(
        Guid siteId,
        DateTimeOffset classifiedThrough,
        DateTimeOffset readThrough,
        RulesetVersion? ruleset = null)
    {
        await using var scope = stack.Services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<IClassificationProgressStore>()
            .AdvanceAsync(
                siteId,
                ruleset ?? RulesetVersion.Current,
                new ClassificationBookmark(classifiedThrough, readThrough),
                Cancellation.Token)
            .ConfigureAwait(false);
    }

    private async Task<DateTimeOffset?> CaughtUpToAsync(Guid siteId)
    {
        await using var scope = stack.Services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<IClassificationProgressStore>()
            .CaughtUpToAsync(siteId, RulesetVersion.Current, Cancellation.Token)
            .ConfigureAwait(false);
    }

    private async Task<DateTimeOffset> UpdatedAtAsync(Guid siteId)
    {
        await using var scope = stack.Services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>().ClassificationProgress
            .AsNoTracking()
            .Where(progress => progress.SiteId == siteId)
            .Select(progress => progress.UpdatedAt)
            .SingleAsync(Cancellation.Token)
            .ConfigureAwait(false);
    }
}
