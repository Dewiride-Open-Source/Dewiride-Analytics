using Dewiride.Analytics.Application.Sites;
using Dewiride.Analytics.Infrastructure.Persistence;
using Dewiride.Analytics.Integration.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Dewiride.Analytics.Integration.Tests.ControlPlane;

/// <summary>
/// Proves the list of sites the background work goes through.
/// </summary>
/// <remarks>
/// The engine judges every site on this list, from the moment the site was added and against the
/// address it is known by. A site missing from it is never judged at all, and a site listed under
/// the wrong address has every page its readers move between counted as an arrival from somewhere
/// else.
/// </remarks>
/// <param name="stack">The running stack.</param>
[Collection(SharedStackDefinition.Name)]
public sealed class SiteRosterTests(AnalyticsStackFixture stack)
{
    [Fact]
    public async Task A_Site_Is_Listed_With_The_Address_It_Is_Known_By_And_When_It_Was_Added()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());

        var listed = (await ListAsync()).Should().ContainSingle(entry => entry.Id == site.Id).Subject;

        listed.Domain.Should().Be(site.Domain);
        listed.AddedAt.Should().BeCloseTo(site.CreatedAt, TimeSpan.FromMicroseconds(1));
    }

    /// <summary>
    /// Oldest first, so a site that has waited longest is never the one a run runs out of time
    /// before reaching.
    /// </summary>
    [Fact]
    public async Task Sites_Are_Listed_Oldest_First()
    {
        var older = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var newer = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());

        var order = (await ListAsync()).Select(entry => entry.Id).ToList();

        order.IndexOf(older.Id).Should().BeLessThan(order.IndexOf(newer.Id));
    }

    /// <summary>
    /// Nothing is measured for the sites of a closed account, and a verdict written into an account
    /// about to be deleted would be work nobody could read.
    /// </summary>
    [Fact]
    public async Task A_Site_Whose_Account_Is_Closed_Is_Not_Listed()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var (_, owner) = await ControlPlaneSeed.AddAccountAsync(stack, $"{Guid.NewGuid():n}@example.com", Passwords.Acceptable);

        await using (var work = stack.Services.CreateAsyncScope())
        {
            var database = work.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();
            var organization = await database.Organizations
                .SingleAsync(candidate => candidate.Id == site.OrganizationId, Cancellation.Token);

            organization.Close(owner.Id, site.CreatedAt);
            await database.SaveChangesAsync(Cancellation.Token);
        }

        (await ListAsync()).Should().NotContain(entry => entry.Id == site.Id);
    }

    /// <summary>
    /// The background work reads every website's calendar in the website's own zone, so the roster
    /// carries it rather than leaving each piece of work to look it up.
    /// </summary>
    [Fact]
    public async Task The_Roster_Carries_Each_Websites_Time_Zone()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain(), timeZoneId: "Asia/Kathmandu");

        (await ListAsync()).Should().ContainSingle(entry => entry.Id == site.Id)
            .Which.TimeZoneId.Should().Be("Asia/Kathmandu");
    }

    /// <summary>
    /// A website of a closed account still exists until the closure is carried out, so what is kept
    /// of it is still written, and deleted with it then. A website that no longer exists has nothing
    /// written about it at all: the work is never run.
    /// </summary>
    [Fact]
    public async Task Work_Is_Done_For_A_Website_Of_A_Closed_Account_And_Never_For_A_Removed_One()
    {
        var closed = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var removed = Guid.NewGuid();
        var (_, owner) = await ControlPlaneSeed.AddAccountAsync(stack, $"{Guid.NewGuid():n}@example.com", Passwords.Acceptable);

        await using (var work = stack.Services.CreateAsyncScope())
        {
            var database = work.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();
            var organization = await database.Organizations
                .SingleAsync(candidate => candidate.Id == closed.OrganizationId, Cancellation.Token);

            organization.Close(owner.Id, closed.CreatedAt);
            await database.SaveChangesAsync(Cancellation.Token);
        }

        var done = new List<Guid>();

        await using var scope = stack.Services.CreateAsyncScope();
        var roster = scope.ServiceProvider.GetRequiredService<ISiteRoster>();

        var forClosed = await roster.WhileItExistsAsync(closed.Id, DoneFor(done, closed.Id), Cancellation.Token);
        var forRemoved = await roster.WhileItExistsAsync(removed, DoneFor(done, removed), Cancellation.Token);

        forClosed.Should().BeTrue();
        forRemoved.Should().BeFalse();
        done.Should().Equal(closed.Id);
    }

    /// <summary>Work that only records which website it was done for.</summary>
    private static Func<CancellationToken, Task> DoneFor(List<Guid> done, Guid siteId) =>
        _ =>
        {
            done.Add(siteId);

            return Task.CompletedTask;
        };

    private static string Domain() => $"{Guid.NewGuid():n}.example.com";

    private async Task<IReadOnlyList<SiteRegistration>> ListAsync()
    {
        await using var work = stack.Services.CreateAsyncScope();

        return await work.ServiceProvider.GetRequiredService<ISiteRoster>().ListAsync(Cancellation.Token);
    }
}
