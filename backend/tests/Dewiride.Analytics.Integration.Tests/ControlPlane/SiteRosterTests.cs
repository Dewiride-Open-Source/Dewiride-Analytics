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

    private static string Domain() => $"{Guid.NewGuid():n}.example.com";

    private async Task<IReadOnlyList<SiteRegistration>> ListAsync()
    {
        await using var work = stack.Services.CreateAsyncScope();

        return await work.ServiceProvider.GetRequiredService<ISiteRoster>().ListAsync(Cancellation.Token);
    }
}
