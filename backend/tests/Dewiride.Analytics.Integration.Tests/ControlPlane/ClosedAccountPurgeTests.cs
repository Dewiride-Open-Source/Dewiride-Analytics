using System.Net;
using System.Net.Http.Json;
using ClickHouse.Driver;
using Dewiride.Analytics.Api.Contracts;
using Dewiride.Analytics.Application.Accounts;
using Dewiride.Analytics.Application.Sessions;
using Dewiride.Analytics.Application.Sites;
using Dewiride.Analytics.Application.Telemetry;
using Dewiride.Analytics.Classification;
using Dewiride.Analytics.Domain.Sites;
using Dewiride.Analytics.Domain.Telemetry;
using Dewiride.Analytics.Infrastructure.Accounts;
using Dewiride.Analytics.Infrastructure.Identity;
using Dewiride.Analytics.Infrastructure.Persistence;
using Dewiride.Analytics.Integration.Tests.Fixtures;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Dewiride.Analytics.Integration.Tests.ControlPlane;

/// <summary>
/// Proves what the sweep does to a closed account once its time has run, and what it says a week
/// before.
/// </summary>
/// <remarks>
/// <para>
/// The promise on the closed screen is thirty days and then gone. Everything here drives the sweep
/// by hand against accounts closed some time ago — the closure instant is written back-dated,
/// because the host keeps real time — and reads both stores straight back.
/// </para>
/// <para>
/// Two things carry the weight. The deletion has to take everything the account held and nothing
/// a neighbour holds: a predicate wrong in either direction, in either store, is invisible to
/// every other test in the suite. And the people go only when they belong nowhere else, because
/// a standing elsewhere is somebody else's account.
/// </para>
/// </remarks>
/// <param name="stack">The running stack.</param>
[Collection(SharedStackDefinition.Name)]
public sealed class ClosedAccountPurgeTests(AnalyticsStackFixture stack)
{
    private const string Setup = "/api/setup";
    private const string Preview = "/api/invitations/preview";
    private const string UtcZone = "Etc/UTC";

    /// <summary>A closure a day past the retention, so the sweep deletes it.</summary>
    private static readonly TimeSpan Overdue = Organization.ClosureRetention + TimeSpan.FromDays(1);

    /// <summary>A closure a day short of the retention, so the sweep leaves it.</summary>
    private static readonly TimeSpan NotYetDue = Organization.ClosureRetention - TimeSpan.FromDays(1);

    /// <summary>A closure a day into the final week, so the sweep reminds its owners.</summary>
    private static readonly TimeSpan WithinTheFinalWeek =
        Organization.ClosureRetention - Organization.DeletionReminderLead + TimeSpan.FromDays(1);

    /// <summary>A closure three days short of the final week, so nobody is reminded yet.</summary>
    private static readonly TimeSpan BeforeTheFinalWeek =
        Organization.ClosureRetention - Organization.DeletionReminderLead - TimeSpan.FromDays(3);

    /// <summary>
    /// A grant, a key, an invitation or a bookmark that outlived the account it belongs to would be
    /// a row nothing can evaluate. They go by cascade from the one row the sweep deletes, and this
    /// is what proves every cascade is declared.
    /// </summary>
    [Fact]
    public async Task A_Closed_Account_Whose_Time_Has_Run_Is_Deleted_With_Everything_The_Control_Plane_Held()
    {
        var account = await ClosedAccountAsync(Overdue);

        await ControlPlaneSeed.AddServerKeyAsync(stack, account.SiteId);
        await BookmarkAsync(account.SiteId);
        await InviteIntoAsync(account.OrganizationId, account.OwnerId);

        (await HeldAboutAsync(account)).Should().Be(new Held(1, 1, 1, 1, 1, 1));

        await SweepAsync(stack.Services);

        (await HeldAboutAsync(account)).Should().Be(new Held(0, 0, 0, 0, 0, 0));
    }

    /// <summary>
    /// The most important property in this file. Deleting an account empties the telemetry store
    /// of everything measured for its websites — the activity and the verdicts alike — and touches
    /// nothing measured for anybody else's. One store holds every website on the installation, so
    /// the predicate that empties one account of it is the only thing keeping the rest.
    /// </summary>
    [Fact]
    public async Task Everything_Measured_For_Its_Websites_Is_Deleted_And_A_Neighbour_Keeps_Its_Own()
    {
        var going = await AccountAsync();
        var staying = await AccountAsync();
        var at = Now.AddDays(-1);

        await WriteAsync(
            Page(going.SiteId, "reader", at, "/"),
            Page(going.SiteId, "reader", at.AddMinutes(2), "/posts/hello"),
            Page(staying.SiteId, "neighbour", at, "/"),
            Page(staying.SiteId, "neighbour", at.AddMinutes(2), "/posts/hello"));

        await JudgeAsync(going.SiteId);
        await JudgeAsync(staying.SiteId);

        (await ActivityCountAsync(going.SiteId)).Should().Be(2);
        (await VerdictCountAsync(going.SiteId)).Should().Be(1);

        await CloseAsync(stack.Services, going.OrganizationId, going.OwnerId, Overdue);
        await SweepAsync(stack.Services);

        // Read straight back, with nothing waiting in between: the deletion has to have happened
        // by the time the sweep returns, not merely been accepted for later.
        (await ActivityCountAsync(going.SiteId)).Should().Be(0);
        (await VerdictCountAsync(going.SiteId)).Should().Be(0);

        (await ActivityCountAsync(staying.SiteId)).Should().Be(2);
        (await VerdictCountAsync(staying.SiteId)).Should().Be(1);
        (await HeldAboutAsync(staying)).Organizations.Should().Be(1);
    }

    /// <summary>
    /// The collector resolves a website out of a cache on every report, so until the entry is
    /// thrown away it goes on accepting reports for a website nothing can read — writing rows that
    /// the deletion was supposed to be the end of.
    /// </summary>
    [Fact]
    public async Task The_Collector_Stops_Resolving_Its_Websites_As_Soon_As_It_Is_Deleted()
    {
        var account = await AccountAsync();

        // Resolved first, so the collector is holding the website when the deletion lands.
        (await CollectorFindsAsync(account.SiteId)).Should().NotBeNull();

        await CloseAsync(stack.Services, account.OrganizationId, account.OwnerId, Overdue);
        await SweepAsync(stack.Services);

        (await CollectorFindsAsync(account.SiteId)).Should().BeNull();
    }

    /// <summary>
    /// An account is deleted only when somebody belongs nowhere else. A standing in another open
    /// organisation and a role on one of its websites are each somewhere else to be, and a person
    /// deleted for the account they lost would take that other account's member with them.
    /// </summary>
    [Fact]
    public async Task People_Who_Belonged_Nowhere_Else_Are_Deleted_And_Everybody_Else_Is_Kept()
    {
        var going = await ClosedAccountAsync(Overdue);
        var staying = await AccountAsync();
        var onlyHere = await PersonInAsync(going.OrganizationId, OrganizationRole.Member);
        var alsoStanding = await PersonInAsync(going.OrganizationId, OrganizationRole.Admin);
        var alsoOnAWebsite = await PersonInAsync(going.OrganizationId, OrganizationRole.Member);

        await ControlPlaneSeed.GrantInOrganizationAsync(
            stack,
            staying.OrganizationId,
            alsoStanding.Id,
            OrganizationRole.Member);
        await ControlPlaneSeed.GrantAsync(stack, staying.SiteId, alsoOnAWebsite.Id, SiteRole.Viewer);

        await SweepAsync(stack.Services);

        (await PersonExistsAsync(going.OwnerId)).Should().BeFalse();
        (await PersonExistsAsync(onlyHere.Id)).Should().BeFalse();
        (await PersonExistsAsync(alsoStanding.Id)).Should().BeTrue();
        (await PersonExistsAsync(alsoOnAWebsite.Id)).Should().BeTrue();
        (await PersonExistsAsync(staying.OwnerId)).Should().BeTrue();
    }

    /// <summary>
    /// An invitation is the history of the account it was sent into, not of whoever sent it. One a
    /// deleted person sent into an open account stays, with the sender's name gone, and the link
    /// it carries still works.
    /// </summary>
    [Fact]
    public async Task An_Invitation_A_Deleted_Person_Sent_Into_An_Open_Account_Still_Works_Without_Their_Name()
    {
        var going = await ClosedAccountAsync(Overdue);
        var open = await AccountAsync();
        var sender = await PersonInAsync(going.OrganizationId, OrganizationRole.Member);
        await ControlPlaneSeed.GrantInOrganizationAsync(stack, open.OrganizationId, sender.Id, OrganizationRole.Owner);

        var secret = await InviteIntoAsync(open.OrganizationId, sender.Id);

        // The sender's standing in the open account is what would keep them; taken away first, so
        // that the deletion is the one that empties their name from the invitation.
        await RemoveStandingAsync(open.OrganizationId, sender.Id);

        await SweepAsync(stack.Services);

        (await PersonExistsAsync(sender.Id)).Should().BeFalse();

        var invitation = await InvitationForAsync(secret);

        invitation.Should().NotBeNull();
        invitation.OrganizationId.Should().Be(open.OrganizationId);
        invitation.InvitedByUserId.Should().BeNull();

        using var browser = await Browser.OpenAsync(stack);
        using var previewed = await browser.PostAsync(Preview, new InvitationTokenRequest { Token = secret });

        previewed.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// A day short is a day the owners still have. Nothing about the account moves until the whole
    /// of the retention has run.
    /// </summary>
    [Fact]
    public async Task A_Closed_Account_Whose_Time_Has_Not_Run_Is_Left_Whole()
    {
        var account = await ClosedAccountAsync(NotYetDue);
        await ControlPlaneSeed.AddServerKeyAsync(stack, account.SiteId);
        var before = await HeldAboutAsync(account);
        var closedAt = await ClosedAtAsync(stack.Services, account.OrganizationId);

        await SweepAsync(stack.Services);

        (await HeldAboutAsync(account)).Should().Be(before);
        (await PersonExistsAsync(account.OwnerId)).Should().BeTrue();
        (await ClosedAtAsync(stack.Services, account.OrganizationId)).Should().Be(closedAt);
    }

    /// <summary>
    /// A second pass is what every hour after the first looks like, and what a retry looks like
    /// after a pass that stopped part-way. It has to find nothing to do and do nothing.
    /// </summary>
    [Fact]
    public async Task Running_Again_Changes_Nothing()
    {
        var going = await ClosedAccountAsync(Overdue);
        var staying = await AccountAsync();

        await SweepAsync(stack.Services);

        (await HeldAboutAsync(going)).Organizations.Should().Be(0);
        var census = await CensusAsync();

        await SweepAsync(stack.Services);

        (await CensusAsync()).Should().Be(census);
        (await HeldAboutAsync(going)).Organizations.Should().Be(0);
        (await HeldAboutAsync(staying)).Should().Be(new Held(1, 1, 1, 0, 0, 0));
        (await PersonExistsAsync(staying.OwnerId)).Should().BeTrue();
    }

    /// <summary>
    /// A failure on one account reaches no other, and is tried again.
    /// </summary>
    /// <remarks>
    /// The edition is asked last, after the account's rows have been marked for deletion in a
    /// transaction not yet committed, so its refusal is the failure that leaves the most behind:
    /// a tracked organisation in a deleted state that a shared context would carry into the next
    /// account's transaction. Each account has a context of its own, so the next account is
    /// deleted as though the first had never been tried, and the first is still whole in the
    /// control plane for the next pass. What the first has lost is its telemetry, which was purged
    /// before the edition was asked and cannot be rolled back — the residual the decision records.
    /// </remarks>
    [Fact]
    public async Task A_Failure_On_One_Account_Leaves_The_Next_Deleted_And_The_First_For_Another_Pass()
    {
        using var install = RefusingInstall.Start(stack);
        var refused = await ClosedAccountAsync(Overdue + TimeSpan.FromDays(1));
        var allowed = await ClosedAccountAsync(Overdue);

        await WriteAsync(Page(refused.SiteId, "reader", Now.AddDays(-40), "/"));
        install.Refuse(refused.OrganizationId);

        await SweepAsync(install.Services);

        (await HeldAboutAsync(allowed)).Organizations.Should().Be(0);
        (await PersonExistsAsync(allowed.OwnerId)).Should().BeFalse();

        (await HeldAboutAsync(refused)).Should().Be(new Held(1, 1, 1, 0, 0, 0));
        (await PersonExistsAsync(refused.OwnerId)).Should().BeTrue();
        (await ClosedAtAsync(stack.Services, refused.OrganizationId)).Should().NotBeNull();
        (await ActivityCountAsync(refused.SiteId)).Should().Be(0);

        install.Allow(refused.OrganizationId);
        await SweepAsync(install.Services);

        (await HeldAboutAsync(refused)).Organizations.Should().Be(0);
        (await PersonExistsAsync(refused.OwnerId)).Should().BeFalse();
    }

    /// <summary>
    /// Every owner is told once, a week out, and nobody else is told at all. Once is recorded on
    /// the account rather than remembered by a mail server, so a second pass has nothing to send.
    /// </summary>
    [Fact]
    public async Task The_Owners_Are_Reminded_Once_A_Week_Before_Deletion()
    {
        await using var install = MailboxInstall.Start(stack);
        var account = await ClosedAccountAsync(WithinTheFinalWeek);
        var secondOwner = await PersonInAsync(account.OrganizationId, OrganizationRole.Owner);
        var member = await PersonInAsync(account.OrganizationId, OrganizationRole.Member);

        await SweepAsync(install.Services);

        (await ReminderSentAtAsync(account.OrganizationId)).Should().NotBeNull();
        install.Mailbox.CountTo(account.OwnerAddress).Should().Be(1);
        install.Mailbox.CountTo(secondOwner.Address).Should().Be(1);
        install.Mailbox.CountTo(member.Address).Should().Be(0);

        var message = install.Mailbox.LastTo(account.OwnerAddress);

        message.Should().NotBeNull();
        message.Subject.Should().Contain("will be deleted on");

        await SweepAsync(install.Services);

        install.Mailbox.CountTo(account.OwnerAddress).Should().Be(1);
        install.Mailbox.CountTo(secondOwner.Address).Should().Be(1);
        (await HeldAboutAsync(account)).Organizations.Should().Be(1);
    }

    [Fact]
    public async Task Nobody_Is_Reminded_Before_The_Final_Week_Begins()
    {
        await using var install = MailboxInstall.Start(stack);
        var account = await ClosedAccountAsync(BeforeTheFinalWeek);

        await SweepAsync(install.Services);

        (await ReminderSentAtAsync(account.OrganizationId)).Should().BeNull();
        install.Mailbox.CountTo(account.OwnerAddress).Should().Be(0);
    }

    /// <summary>
    /// The reminder is claimed on the row before it is sent, and the claim matches only a closed
    /// account. One brought back before the claim lands is not reminded of a deletion that is no
    /// longer coming, and carries no record of a reminder it never got.
    /// </summary>
    [Fact]
    public async Task An_Account_Brought_Back_Before_The_Reminder_Is_Claimed_Gets_None()
    {
        await using var install = MailboxInstall.Start(stack);
        var account = await ClosedAccountAsync(WithinTheFinalWeek);

        await ReopenAsync(stack.Services, account.OrganizationId);
        await SweepAsync(install.Services);

        (await ReminderSentAtAsync(account.OrganizationId)).Should().BeNull();
        (await ClosedAtAsync(stack.Services, account.OrganizationId)).Should().BeNull();
        install.Mailbox.CountTo(account.OwnerAddress).Should().Be(0);
    }

    /// <summary>
    /// The claim takes the account's lock, so a restore already under way — its row read, not yet
    /// written — finishes before the claim looks, and the claim then finds an open account.
    /// </summary>
    /// <remarks>
    /// Without the lock the stamp could land between the restore's read and its write, be seen by
    /// neither, and leave a reopened account whose owners are told it is about to be deleted. The
    /// restore is held open at the point the edition is asked, which is inside its transaction and
    /// under its lock; the sweep is started and watched until it is waiting on that lock; only then
    /// is the restore let go.
    /// </remarks>
    [Fact]
    public async Task A_Reminder_Waits_For_A_Restore_Under_Way_And_Then_Has_Nothing_To_Send()
    {
        using var holding = RefusingInstall.Start(stack);
        await using var mailbox = MailboxInstall.Start(stack);
        var account = await ClosedAccountAsync(WithinTheFinalWeek);
        var gate = holding.Hold(account.OrganizationId);

        var restoring = RestoreAsync(holding.Services, account.OwnerId);

        // A restore that fails before it reaches the gate would leave this waiting for ever, so
        // whichever finishes first is awaited, and a failure surfaces as the failure it is.
        await await Task.WhenAny(gate.Arrived, restoring);

        var sweeping = SweepAsync(mailbox.Services);
        await WaitingOnALockAsync();
        gate.Release();
        await Task.WhenAll(restoring, sweeping);

        (await restoring).Should().Be(ClosureOutcome.Restored);
        (await ReminderSentAtAsync(account.OrganizationId)).Should().BeNull();
        (await ClosedAtAsync(stack.Services, account.OrganizationId)).Should().BeNull();
        mailbox.Mailbox.CountTo(account.OwnerAddress).Should().Be(0);
    }

    /// <summary>
    /// Two engines sweeping the same hour both choose the same account, and the claim is what
    /// makes only one of them send. Started on separate threads rather than one after the other,
    /// so both are genuinely inside the pass at the same moment.
    /// </summary>
    [Fact]
    public async Task Two_Sweeps_At_Once_Send_One_Reminder()
    {
        await using var install = MailboxInstall.Start(stack);
        var account = await ClosedAccountAsync(WithinTheFinalWeek);
        var purge = install.Services.GetRequiredService<ClosedAccountPurge>();

        await Task.WhenAll(
            Task.Run(() => purge.RunAsync(Cancellation.Token), Cancellation.Token),
            Task.Run(() => purge.RunAsync(Cancellation.Token), Cancellation.Token));

        install.Mailbox.CountTo(account.OwnerAddress).Should().Be(1);
        (await ReminderSentAtAsync(account.OrganizationId)).Should().NotBeNull();
    }

    /// <summary>
    /// The sweep chooses its accounts and then re-reads each one under its lock before touching it.
    /// One brought back in between is an open account, and an open account is never deleted.
    /// </summary>
    [Fact]
    public async Task An_Account_Brought_Back_Before_The_Sweep_Reaches_It_Survives()
    {
        var account = await ClosedAccountAsync(Overdue);
        var before = await HeldAboutAsync(account);

        await ReopenAsync(stack.Services, account.OrganizationId);
        await SweepAsync(stack.Services);

        (await HeldAboutAsync(account)).Should().Be(before);
        (await PersonExistsAsync(account.OwnerId)).Should().BeTrue();
        (await ClosedAtAsync(stack.Services, account.OrganizationId)).Should().BeNull();
    }

    /// <summary>
    /// The clock runs from the closure the account is under now. Brought back and closed again,
    /// it has its whole time over, however long ago the first closure was.
    /// </summary>
    [Fact]
    public async Task An_Account_Closed_Again_More_Recently_Has_Its_Whole_Time_Again()
    {
        var account = await ClosedAccountAsync(Overdue);
        var before = await HeldAboutAsync(account);

        await ReopenAsync(stack.Services, account.OrganizationId);
        await CloseAsync(stack.Services, account.OrganizationId, account.OwnerId, TimeSpan.Zero);
        var closedAt = await ClosedAtAsync(stack.Services, account.OrganizationId);

        await SweepAsync(stack.Services);

        (await HeldAboutAsync(account)).Should().Be(before);
        (await ClosedAtAsync(stack.Services, account.OrganizationId)).Should().Be(closedAt);
    }

    /// <summary>
    /// On a server somebody runs themselves there is one account, and deleting it empties the
    /// installation. What must not happen next is the welcome screen opening again: the one moment
    /// an anonymous caller may create an owner was spent when the installation was claimed, and an
    /// emptied installation is finished rather than new.
    /// </summary>
    [Fact]
    public async Task Deleting_The_Only_Account_On_A_Claimed_Installation_Leaves_It_Finished()
    {
        await using var install = await FreshInstall.StartAsync(stack);
        using var owner = await Browser.OpenAsync(install);

        using var claimed = await owner.PostAsync(Setup, Details("owner@example.com"));
        claimed.StatusCode.Should().Be(HttpStatusCode.OK);

        var created = await claimed.Content.ReadFromJsonAsync<SetupResponse>(Cancellation.Token);
        created.Should().NotBeNull();

        var organizationId = await ReadAsync(
            install.Services,
            database => database.Organizations.Select(organization => organization.Id).SingleAsync(Cancellation.Token));

        await CloseAsync(install.Services, organizationId, created.User.Id, Overdue);
        await SweepAsync(install.Services);

        (await PeopleCountAsync(install.Services)).Should().Be(0);
        (await ClaimCountAsync(install.Services)).Should().Be(1);

        // The owner's own open tab finds nobody signed in, and a stranger finding the installation
        // afterwards finds it claimed.
        (await owner.DescribeAsync()).User.Should().BeNull();

        using var stranger = await Browser.OpenAsync(install);
        var session = await stranger.DescribeAsync();

        session.SetupCompleted.Should().BeTrue();
        session.User.Should().BeNull();

        using var refused = await stranger.PostAsync(Setup, Details("someone.else@example.com"));

        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    /// <summary>
    /// On the hosted service accounts arrive by signing up and never through the welcome screen, so
    /// nothing has recorded a claim by the time the last one is deleted. The sweep records it
    /// itself as it empties the installation, so that the door stays shut there too.
    /// </summary>
    [Fact]
    public async Task Deleting_The_Only_Account_On_An_Installation_Nobody_Claimed_Records_The_Claim()
    {
        await using var install = await FreshInstall.StartAsync(stack);
        var (organizationId, ownerId) = await SignedUpAccountAsync(install.Services);

        (await ClaimCountAsync(install.Services)).Should().Be(0);

        await CloseAsync(install.Services, organizationId, ownerId, Overdue);
        await SweepAsync(install.Services);

        (await PeopleCountAsync(install.Services)).Should().Be(0);
        (await ClaimCountAsync(install.Services)).Should().Be(1);

        using var stranger = await Browser.OpenAsync(install);
        var session = await stranger.DescribeAsync();

        session.SetupCompleted.Should().BeTrue();
        session.User.Should().BeNull();

        using var refused = await stranger.PostAsync(Setup, Details("someone.else@example.com"));

        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    /// <summary>The present moment, taken from the host's own clock rather than the machine's.</summary>
    private DateTimeOffset Now => NowOn(stack.Services);

    private static DateTimeOffset NowOn(IServiceProvider services) =>
        services.GetRequiredService<TimeProvider>().GetUtcNow();

    private IClickHouseClient Telemetry => stack.Services.GetRequiredService<IClickHouseClient>();

    /// <summary>Runs one pass of the sweep on a host.</summary>
    private static Task SweepAsync(IServiceProvider services) =>
        services.GetRequiredService<ClosedAccountPurge>().RunAsync(Cancellation.Token);

    /// <summary>Brings an account back through the port, as the endpoint would.</summary>
    private static async Task<ClosureOutcome> RestoreAsync(IServiceProvider services, Guid userId)
    {
        await using var work = services.CreateAsyncScope();

        return await work.ServiceProvider
            .GetRequiredService<IAccountClosure>()
            .RestoreAsync(userId, Cancellation.Token);
    }

    /// <summary>
    /// Waits until some connection to the control plane is queued on an advisory lock.
    /// </summary>
    /// <remarks>
    /// The database is the one place that can say a caller is waiting rather than merely slow, so
    /// the test asks it rather than assuming that a moment's pause was enough.
    /// </remarks>
    private async Task WaitingOnALockAsync()
    {
        const int attempts = 200;
        var pause = TimeSpan.FromMilliseconds(50);

        for (var attempt = 0; attempt < attempts; attempt++)
        {
            var waiting = await ReadAsync(database => database.Database
                .SqlQueryRaw<int>(
                    """
                    SELECT count(*)::int AS "Value"
                    FROM pg_stat_activity
                    WHERE wait_event_type = 'Lock' AND query LIKE '%pg_advisory_xact_lock%'
                    """)
                .SingleAsync(Cancellation.Token));

            if (waiting > 0)
            {
                return;
            }

            await Task.Delay(pause, Cancellation.Token);
        }

        throw new TimeoutException("Nothing was waiting on an advisory lock.");
    }

    /// <summary>
    /// An open account with one website and one owner, who holds a role on the website as well as
    /// a standing in the account.
    /// </summary>
    /// <remarks>
    /// Built for each test rather than shared: the suite runs against one stack, and the sweep
    /// deletes whatever is due, so two tests sharing an account would be one test deleting the
    /// other's. The role on the website is there so that the people who go are only counted as
    /// belonging nowhere once the website's grants have gone with the website.
    /// </remarks>
    private async Task<Account> AccountAsync()
    {
        var site = await ControlPlaneSeed.AddSiteAsync(stack, domain: Domain());
        var owner = await PersonInAsync(site.OrganizationId, OrganizationRole.Owner);

        await ControlPlaneSeed.GrantAsync(stack, site.Id, owner.Id, SiteRole.Owner);

        return new Account(site.OrganizationId, site.Id, owner.Id, owner.Address);
    }

    /// <summary>An account closed by its owner some time ago.</summary>
    /// <param name="ago">How long ago it was closed.</param>
    private async Task<Account> ClosedAccountAsync(TimeSpan ago)
    {
        var account = await AccountAsync();

        await CloseAsync(stack.Services, account.OrganizationId, account.OwnerId, ago);

        return account;
    }

    /// <summary>Creates an account and gives it a standing in an organisation.</summary>
    private async Task<Person> PersonInAsync(Guid organizationId, OrganizationRole role)
    {
        var address = SignedIn.Address();
        var (created, user) = await ControlPlaneSeed.AddAccountAsync(stack, address, Passwords.Acceptable);

        created.Succeeded.Should().BeTrue();

        await ControlPlaneSeed.GrantInOrganizationAsync(stack, organizationId, user.Id, role);

        return new Person(user.Id, address);
    }

    /// <summary>
    /// An organisation, its website and its owner, written the way the hosted service writes
    /// them: straight into the store, with nothing having passed through the welcome screen.
    /// </summary>
    private static async Task<(Guid OrganizationId, Guid OwnerId)> SignedUpAccountAsync(IServiceProvider services)
    {
        await using var work = services.CreateAsyncScope();
        var database = work.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();
        var accounts = work.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var now = NowOn(services);
        var address = SignedIn.Address();

        var owner = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = address,
            Email = address,
            DisplayName = "Signed up",
            CreatedAt = now,
        };

        (await accounts.CreateAsync(owner, Passwords.Acceptable)).Succeeded.Should().BeTrue();

        var organization = new Organization(Guid.NewGuid(), "Signed up, never set up", now);

        database.Add(organization);
        database.Add(new Site(Guid.NewGuid(), organization.Id, Domain(), UtcZone, now));
        database.Add(new OrganizationMembership(Guid.NewGuid(), organization.Id, owner.Id, OrganizationRole.Owner, now));
        await database.SaveChangesAsync(Cancellation.Token);

        return (organization.Id, owner.Id);
    }

    /// <summary>
    /// Closes an organisation as though it had been closed some time ago.
    /// </summary>
    /// <remarks>
    /// Written through the store rather than through the endpoint, because the host keeps real
    /// time and the sweep is about what has been closed for weeks.
    /// </remarks>
    private static async Task CloseAsync(IServiceProvider services, Guid organizationId, Guid byUserId, TimeSpan ago)
    {
        await using var work = services.CreateAsyncScope();
        var database = work.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();

        var organization = await database.Organizations
            .SingleAsync(candidate => candidate.Id == organizationId, Cancellation.Token);

        organization.Close(byUserId, NowOn(services) - ago);
        await database.SaveChangesAsync(Cancellation.Token);
    }

    /// <summary>Brings a closed organisation back, exactly as an owner's press would.</summary>
    private static async Task ReopenAsync(IServiceProvider services, Guid organizationId)
    {
        await using var work = services.CreateAsyncScope();
        var database = work.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();

        var organization = await database.Organizations
            .SingleAsync(candidate => candidate.Id == organizationId, Cancellation.Token);

        organization.Reopen();
        await database.SaveChangesAsync(Cancellation.Token);
    }

    /// <summary>
    /// Sends an invitation into an organisation without a mail server, keeping the secret so the
    /// link can be followed afterwards.
    /// </summary>
    /// <returns>The secret the link would carry.</returns>
    private async Task<string> InviteIntoAsync(Guid organizationId, Guid invitedByUserId)
    {
        await using var work = stack.Services.CreateAsyncScope();
        var database = work.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();
        var now = Now;
        var (secret, hash) = InvitationSecret.Create();

        database.OrganizationInvitations.Add(
            new OrganizationInvitation(
                Guid.CreateVersion7(now),
                organizationId,
                SignedIn.Address(),
                OrganizationRole.Member,
                invitedByUserId,
                hash,
                now));

        await database.SaveChangesAsync(Cancellation.Token);

        return secret;
    }

    private Task<OrganizationInvitation?> InvitationForAsync(string secret)
    {
        var hash = InvitationSecret.Hash(secret);

        return ReadAsync(database => database.OrganizationInvitations
            .AsNoTracking()
            .SingleOrDefaultAsync(invitation => invitation.TokenHash == hash, Cancellation.Token));
    }

    private async Task RemoveStandingAsync(Guid organizationId, Guid userId)
    {
        await using var work = stack.Services.CreateAsyncScope();
        var database = work.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();

        await database.OrganizationMemberships
            .Where(membership => membership.OrganizationId == organizationId && membership.UserId == userId)
            .ExecuteDeleteAsync(Cancellation.Token);
    }

    /// <summary>Everything the control plane holds about one account.</summary>
    private async Task<Held> HeldAboutAsync(Account account)
    {
        await using var work = stack.Services.CreateAsyncScope();
        var database = work.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();

        return new Held(
            await database.Organizations
                .CountAsync(organization => organization.Id == account.OrganizationId, Cancellation.Token),
            await database.Sites
                .CountAsync(site => site.OrganizationId == account.OrganizationId, Cancellation.Token),
            await database.OrganizationMemberships
                .CountAsync(membership => membership.OrganizationId == account.OrganizationId, Cancellation.Token),
            await database.OrganizationInvitations
                .CountAsync(invitation => invitation.OrganizationId == account.OrganizationId, Cancellation.Token),
            await database.SiteIngestKeys
                .CountAsync(key => key.SiteId == account.SiteId, Cancellation.Token),
            await database.ClassificationProgress
                .CountAsync(progress => progress.SiteId == account.SiteId, Cancellation.Token));
    }

    /// <summary>How much of everything the control plane holds, across every account.</summary>
    private async Task<Census> CensusAsync()
    {
        await using var work = stack.Services.CreateAsyncScope();
        var database = work.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();

        return new Census(
            await database.Organizations.CountAsync(Cancellation.Token),
            await database.Sites.CountAsync(Cancellation.Token),
            await database.Users.CountAsync(Cancellation.Token),
            await database.OrganizationMemberships.CountAsync(Cancellation.Token),
            await database.SiteMemberships.CountAsync(Cancellation.Token),
            await database.OrganizationInvitations.CountAsync(Cancellation.Token),
            await database.SiteIngestKeys.CountAsync(Cancellation.Token));
    }

    private Task<bool> PersonExistsAsync(Guid userId) =>
        ReadAsync(database => database.Users.AnyAsync(user => user.Id == userId, Cancellation.Token));

    private static Task<int> PeopleCountAsync(IServiceProvider services) =>
        ReadAsync(services, database => database.Users.CountAsync(Cancellation.Token));

    private static Task<int> ClaimCountAsync(IServiceProvider services) =>
        ReadAsync(services, database => database.InstallationClaims.CountAsync(Cancellation.Token));

    private Task<DateTimeOffset?> ReminderSentAtAsync(Guid organizationId) =>
        ReadAsync(database => database.Organizations
            .Where(organization => organization.Id == organizationId)
            .Select(organization => organization.DeletionReminderSentAt)
            .SingleAsync(Cancellation.Token));

    private static Task<DateTimeOffset?> ClosedAtAsync(IServiceProvider services, Guid organizationId) =>
        ReadAsync(services, database => database.Organizations
            .Where(organization => organization.Id == organizationId)
            .Select(organization => organization.ClosedAt)
            .SingleAsync(Cancellation.Token));

    private Task<T> ReadAsync<T>(Func<ControlPlaneDbContext, Task<T>> read) => ReadAsync(stack.Services, read);

    private static async Task<T> ReadAsync<T>(IServiceProvider services, Func<ControlPlaneDbContext, Task<T>> read)
    {
        await using var work = services.CreateAsyncScope();

        return await read(work.ServiceProvider.GetRequiredService<ControlPlaneDbContext>());
    }

    /// <summary>Whether the collector would still resolve this website.</summary>
    private async Task<SiteSnapshot?> CollectorFindsAsync(Guid siteId)
    {
        await using var work = stack.Services.CreateAsyncScope();

        return await work.ServiceProvider
            .GetRequiredService<ISiteCatalog>()
            .FindAsync(siteId, Cancellation.Token);
    }

    private async Task<ulong> ActivityCountAsync(Guid siteId) =>
        await TelemetryStore.ScalarAsync<ulong>(
            Telemetry,
            "SELECT count() FROM events WHERE site_id = {site_id:UUID}",
            TelemetryStore.Bind("site_id", siteId));

    private async Task<ulong> VerdictCountAsync(Guid siteId) =>
        await TelemetryStore.ScalarAsync<ulong>(
            Telemetry,
            "SELECT count() FROM session_classifications WHERE site_id = {site_id:UUID}",
            TelemetryStore.Bind("site_id", siteId));

    private Task WriteAsync(params RawEvent[] events) =>
        stack.Services.GetRequiredService<IEventSink>().WriteBatchAsync(events, Cancellation.Token);

    /// <summary>
    /// Runs the engine over a website, so that there are verdicts to be deleted as well as
    /// activity.
    /// </summary>
    private async Task JudgeAsync(Guid siteId)
    {
        await using var work = stack.Services.CreateAsyncScope();

        await work.ServiceProvider
            .GetRequiredService<SessionClassifier>()
            .CatchUpAsync(siteId, Now.AddDays(-2), Cancellation.Token);
    }

    /// <summary>Starts the bookmark the engine keeps for a website.</summary>
    private async Task BookmarkAsync(Guid siteId)
    {
        await using var work = stack.Services.CreateAsyncScope();

        await work.ServiceProvider
            .GetRequiredService<IClassificationProgressStore>()
            .ResumeFromAsync(siteId, RulesetVersion.Current, Now, Cancellation.Token);
    }

    private static RawEvent Page(Guid siteId, string visitor, DateTimeOffset at, string path) => new()
    {
        EventId = Guid.CreateVersion7(at),
        SiteId = siteId,
        Kind = EventKind.PageView,
        Surface = IngestSurface.BrowserTracker,
        ServerTimestamp = at,
        VisitorKey = visitor,
        Host = "example.com",
        Path = path,
        UserAgent = Chrome,
    };

    private static SetupRequest Details(string emailAddress) => FreshInstall.Claim(emailAddress);

    private const string Chrome =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) "
        + "Chrome/141.0.0.0 Safari/537.36";

    private static string Domain() => $"purge-{Guid.NewGuid():n}.example";

    private sealed record Account(Guid OrganizationId, Guid SiteId, Guid OwnerId, string OwnerAddress);

    private sealed record Person(Guid Id, string Address);

    /// <summary>What the control plane holds about one account, counted.</summary>
    private sealed record Held(int Organizations, int Sites, int Standings, int Invitations, int Keys, int Bookmarks);

    /// <summary>What the control plane holds across every account, counted.</summary>
    private sealed record Census(
        int Organizations,
        int Sites,
        int People,
        int Standings,
        int Grants,
        int Invitations,
        int Keys);
}
