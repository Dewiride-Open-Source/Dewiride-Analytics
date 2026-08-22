using System.Net;
using System.Net.Http.Json;
using Dewiride.Analytics.Api.Contracts;
using Dewiride.Analytics.Domain.Sites;
using Dewiride.Analytics.Integration.Tests.Fixtures;

namespace Dewiride.Analytics.Integration.Tests.Dashboard;

/// <summary>
/// Covers the account somebody belongs to, and the people in it.
/// </summary>
/// <remarks>
/// <para>
/// Which account each answer is about is never in the request. It is the one the caller belongs
/// to, so the thing worth proving is not that a change worked but that it worked on the caller's
/// own account and on nobody else's.
/// </para>
/// <para>
/// The rule that carries the most weight is the last owner. An account with nobody who can manage
/// it cannot be repaired from inside the product, and both ways of reaching that state — removing
/// the last owner and moving them to something narrower — have to be refused.
/// </para>
/// </remarks>
/// <param name="stack">The running stack.</param>
[Collection(SharedStackDefinition.Name)]
public sealed class OrganizationTests(AnalyticsStackFixture stack)
{
    private const string Organization = "/api/organization";

    [Fact]
    public async Task An_Owner_Is_Told_What_The_Account_Is_Called_And_Who_Is_In_It()
    {
        using var account = await AccountWithOwnerAsync().ConfigureAwait(true);

        using var response = await account.Owner.GetAsync(Organization).ConfigureAwait(true);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var described = await ReadAsync(response).ConfigureAwait(true);

        described.Id.Should().Be(account.OrganizationId);
        described.Role.Should().Be("owner");
        described.People.Should().ContainSingle(person => person.Id == account.OwnerId);
        described.People.Single(person => person.Id == account.OwnerId).Role.Should().Be("owner");
    }

    [Fact]
    public async Task Somebody_Who_Belongs_To_No_Account_Is_Told_There_Is_Nothing_To_Show()
    {
        using var stranger = await SignedIn.AsAsync(stack, SiteRole.Viewer).ConfigureAwait(true);

        using var response = await stranger.GetAsync(Organization).ConfigureAwait(true);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_Owner_Can_Change_What_The_Account_Is_Called()
    {
        using var account = await AccountWithOwnerAsync().ConfigureAwait(true);

        using var renamed = await account.Owner
            .PatchAsync(Organization, new RenameOrganizationRequest { Name = "Renamed by its owner" })
            .ConfigureAwait(true);

        renamed.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var response = await account.Owner.GetAsync(Organization).ConfigureAwait(true);
        var described = await ReadAsync(response).ConfigureAwait(true);

        described.Name.Should().Be("Renamed by its owner");
    }

    [Fact]
    public async Task An_Empty_Name_Is_Refused_With_A_Reason_The_Screen_Can_Explain()
    {
        using var account = await AccountWithOwnerAsync().ConfigureAwait(true);

        using var response = await account.Owner
            .PatchAsync(Organization, new RenameOrganizationRequest { Name = "   " })
            .ConfigureAwait(true);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Refusal.ReasonsOfAsync(response).ConfigureAwait(true))
            .Should().Contain(OrganizationEndpointCodes.NameRejected);
    }

    /// <summary>
    /// Somebody who merely belongs to an account may read it and may change nothing about it.
    /// Adding and removing people decides who can see a whole account's traffic.
    /// </summary>
    [Fact]
    public async Task Somebody_Who_Is_Not_An_Owner_May_Read_The_Account_And_Change_Nothing()
    {
        using var account = await AccountWithOwnerAsync().ConfigureAwait(true);
        using var member = await JoinAsync(account.OrganizationId, OrganizationRole.Member).ConfigureAwait(true);

        using var read = await member.Browser.GetAsync(Organization).ConfigureAwait(true);
        read.StatusCode.Should().Be(HttpStatusCode.OK);

        var described = await ReadAsync(read).ConfigureAwait(true);
        described.Role.Should().Be("member");

        // Waiting invitations name people who have been asked but have not arrived, which is
        // somebody else's business until they do.
        described.Invitations.Should().BeEmpty();

        using var refused = await member.Browser
            .PatchAsync(Organization, new RenameOrganizationRequest { Name = "Mine now" })
            .ConfigureAwait(true);

        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_Owner_Can_Change_What_Somebody_Else_May_Do()
    {
        using var account = await AccountWithOwnerAsync().ConfigureAwait(true);
        using var member = await JoinAsync(account.OrganizationId, OrganizationRole.Member).ConfigureAwait(true);

        using var changed = await account.Owner
            .PatchAsync(
                $"{Organization}/people/{member.UserId}",
                new ChangeStandingRequest { Role = "admin" })
            .ConfigureAwait(true);

        changed.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var response = await account.Owner.GetAsync(Organization).ConfigureAwait(true);
        var described = await ReadAsync(response).ConfigureAwait(true);

        described.People.Single(person => person.Id == member.UserId).Role.Should().Be("admin");
    }

    [Fact]
    public async Task A_Standing_This_Product_Does_Not_Define_Is_Refused()
    {
        using var account = await AccountWithOwnerAsync().ConfigureAwait(true);
        using var member = await JoinAsync(account.OrganizationId, OrganizationRole.Member).ConfigureAwait(true);

        using var response = await account.Owner
            .PatchAsync(
                $"{Organization}/people/{member.UserId}",
                new ChangeStandingRequest { Role = "superuser" })
            .ConfigureAwait(true);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Refusal.ReasonsOfAsync(response).ConfigureAwait(true))
            .Should().Contain(OrganizationEndpointCodes.RoleUnknown);
    }

    [Fact]
    public async Task An_Owner_Can_Take_Somebody_Out_Of_The_Account()
    {
        using var account = await AccountWithOwnerAsync().ConfigureAwait(true);
        using var member = await JoinAsync(account.OrganizationId, OrganizationRole.Member).ConfigureAwait(true);

        using var removed = await account.Owner
            .DeleteAsync($"{Organization}/people/{member.UserId}")
            .ConfigureAwait(true);

        removed.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var response = await account.Owner.GetAsync(Organization).ConfigureAwait(true);
        var described = await ReadAsync(response).ConfigureAwait(true);

        described.People.Should().NotContain(person => person.Id == member.UserId);
    }

    /// <summary>
    /// Removing somebody has to reach the grants made on individual websites as well. One left
    /// behind would keep them reading a site after everybody had been told they no longer could.
    /// </summary>
    [Fact]
    public async Task Somebody_Removed_From_The_Account_Loses_The_Websites_They_Were_Named_On()
    {
        using var account = await AccountWithOwnerAsync().ConfigureAwait(true);
        using var member = await JoinAsync(account.OrganizationId, OrganizationRole.Member).ConfigureAwait(true);

        await ControlPlaneSeed
            .GrantAsync(stack, account.SiteId, member.UserId, SiteRole.Editor)
            .ConfigureAwait(true);

        using var before = await member.Browser.GetAsync("/api/sites").ConfigureAwait(true);
        (await SitesInAsync(before).ConfigureAwait(true)).Should().NotBeEmpty();

        using var removed = await account.Owner
            .DeleteAsync($"{Organization}/people/{member.UserId}")
            .ConfigureAwait(true);

        removed.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var after = await member.Browser.GetAsync("/api/sites").ConfigureAwait(true);
        (await SitesInAsync(after).ConfigureAwait(true)).Should().BeEmpty();
    }

    [Fact]
    public async Task The_Last_Owner_Cannot_Be_Taken_Out_Of_The_Account()
    {
        using var account = await AccountWithOwnerAsync().ConfigureAwait(true);

        using var response = await account.Owner
            .DeleteAsync($"{Organization}/people/{account.OwnerId}")
            .ConfigureAwait(true);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Refusal.ReasonsOfAsync(response).ConfigureAwait(true))
            .Should().Contain(OrganizationEndpointCodes.LastOwner);
    }

    /// <summary>
    /// The other way of reaching the same state, and the one somebody would try after being
    /// refused the first.
    /// </summary>
    [Fact]
    public async Task The_Last_Owner_Cannot_Be_Moved_To_Anything_Narrower()
    {
        using var account = await AccountWithOwnerAsync().ConfigureAwait(true);

        using var response = await account.Owner
            .PatchAsync(
                $"{Organization}/people/{account.OwnerId}",
                new ChangeStandingRequest { Role = "member" })
            .ConfigureAwait(true);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Refusal.ReasonsOfAsync(response).ConfigureAwait(true))
            .Should().Contain(OrganizationEndpointCodes.LastOwner);
    }

    [Fact]
    public async Task An_Owner_May_Step_Back_Once_Somebody_Else_Owns_The_Account_Too()
    {
        using var account = await AccountWithOwnerAsync().ConfigureAwait(true);
        using var second = await JoinAsync(account.OrganizationId, OrganizationRole.Owner).ConfigureAwait(true);

        using var response = await account.Owner
            .PatchAsync(
                $"{Organization}/people/{account.OwnerId}",
                new ChangeStandingRequest { Role = "member" })
            .ConfigureAwait(true);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    /// <summary>
    /// Somebody in one account naming somebody in another must not reach them. The identifier is
    /// the only thing a caller supplies here, and the account it is looked up in is the caller's.
    /// </summary>
    [Fact]
    public async Task Naming_Somebody_In_Another_Account_Reaches_Nobody()
    {
        using var mine = await AccountWithOwnerAsync().ConfigureAwait(true);
        using var theirs = await AccountWithOwnerAsync().ConfigureAwait(true);

        using var response = await mine.Owner
            .DeleteAsync($"{Organization}/people/{theirs.OwnerId}")
            .ConfigureAwait(true);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        using var unchanged = await theirs.Owner.GetAsync(Organization).ConfigureAwait(true);
        var described = await ReadAsync(unchanged).ConfigureAwait(true);

        described.People.Should().Contain(person => person.Id == theirs.OwnerId);
    }

    /// <summary>
    /// The reason a standing exists at all. Somebody added to an account sees the websites it owns,
    /// including the ones added after they arrived, without anybody naming them on each.
    /// </summary>
    [Fact]
    public async Task A_Standing_In_The_Account_Reaches_Every_Website_It_Owns()
    {
        using var account = await AccountWithOwnerAsync().ConfigureAwait(true);
        using var member = await JoinAsync(account.OrganizationId, OrganizationRole.Member).ConfigureAwait(true);

        var later = await ControlPlaneSeed
            .AddSiteToAsync(stack, account.OrganizationId, $"later-{Guid.NewGuid():n}.example")
            .ConfigureAwait(true);

        using var response = await member.Browser.GetAsync("/api/sites").ConfigureAwait(true);
        var sites = await SitesInAsync(response).ConfigureAwait(true);

        sites.Should().Contain(site => site.Id == account.SiteId);
        sites.Should().Contain(site => site.Id == later.Id);
        sites.Single(site => site.Id == later.Id).Role.Should().Be("viewer");
    }

    [Fact]
    public async Task A_Change_Without_Proof_Of_Where_It_Came_From_Is_Refused()
    {
        using var account = await AccountWithOwnerAsync().ConfigureAwait(true);

        using var response = await account.Owner
            .PatchWithoutProofAsync(Organization, new RenameOrganizationRequest { Name = "Nope" })
            .ConfigureAwait(true);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private static async Task<OrganizationResponse> ReadAsync(HttpResponseMessage response)
    {
        var described = await response.Content
            .ReadFromJsonAsync<OrganizationResponse>(Cancellation.Token)
            .ConfigureAwait(false);

        described.Should().NotBeNull();

        return described;
    }

    private static async Task<IReadOnlyList<SiteSummary>> SitesInAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var sites = await response.Content
            .ReadFromJsonAsync<IReadOnlyList<SiteSummary>>(Cancellation.Token)
            .ConfigureAwait(false);

        return sites ?? [];
    }

    /// <summary>
    /// An account with one website and one owner, signed in.
    /// </summary>
    /// <remarks>
    /// Built for each test rather than shared. The suite runs against one stack, so two tests that
    /// shared an account would be one test taking people out of the other's.
    /// </remarks>
    private async Task<Account> AccountWithOwnerAsync()
    {
        var site = await ControlPlaneSeed
            .AddSiteAsync(stack, domain: $"account-{Guid.NewGuid():n}.example")
            .ConfigureAwait(false);

        var address = SignedIn.Address();
        var (created, owner) = await ControlPlaneSeed
            .AddAccountAsync(stack, address, Passwords.Acceptable)
            .ConfigureAwait(false);

        created.Succeeded.Should().BeTrue();

        await ControlPlaneSeed
            .GrantInOrganizationAsync(stack, site.OrganizationId, owner.Id, OrganizationRole.Owner)
            .ConfigureAwait(false);

        var browser = await SignedIn.AsAccountAsync(stack, address).ConfigureAwait(false);

        return new Account(site.OrganizationId, site.Id, owner.Id, browser);
    }

    private async Task<Joined> JoinAsync(Guid organizationId, OrganizationRole role)
    {
        var address = SignedIn.Address();
        var (created, user) = await ControlPlaneSeed
            .AddAccountAsync(stack, address, Passwords.Acceptable)
            .ConfigureAwait(false);

        created.Succeeded.Should().BeTrue();

        await ControlPlaneSeed
            .GrantInOrganizationAsync(stack, organizationId, user.Id, role)
            .ConfigureAwait(false);

        var browser = await SignedIn.AsAccountAsync(stack, address).ConfigureAwait(false);

        return new Joined(user.Id, browser);
    }

    private sealed record Account(Guid OrganizationId, Guid SiteId, Guid OwnerId, Browser Owner) : IDisposable
    {
        public void Dispose() => Owner.Dispose();
    }

    private sealed record Joined(Guid UserId, Browser Browser) : IDisposable
    {
        public void Dispose() => Browser.Dispose();
    }
}

/// <summary>
/// The codes the account endpoints refuse with.
/// </summary>
/// <remarks>
/// Written out here rather than read from the endpoints, which are internal to the host. A test
/// that read them from the same constant it is checking would pass whatever they were changed to,
/// and these are what the dashboard looks its own sentences up by.
/// </remarks>
internal static class OrganizationEndpointCodes
{
    public const string NameRejected = "OrganizationNameRejected";
    public const string LastOwner = "LastOwnerRemains";
    public const string RoleUnknown = "StandingNotRecognised";
    public const string AddressUnusable = "InvitationAddressUnusable";
    public const string AlreadyHere = "InvitationAlreadyHere";
    public const string LinkNotUsable = "InvitationLinkNotUsable";
    public const string DetailsMissing = "JoinDetailsMissing";
    public const string CurrentPasswordWrong = "CurrentPasswordWrong";
    public const string AccountNameRejected = "AccountNameRejected";
}
