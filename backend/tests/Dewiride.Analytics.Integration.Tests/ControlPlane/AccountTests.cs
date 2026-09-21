using System.Net;
using Dewiride.Analytics.Api.Contracts;
using Dewiride.Analytics.Infrastructure.Identity;
using Dewiride.Analytics.Infrastructure.Persistence;
using Dewiride.Analytics.Integration.Tests.Fixtures;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Dewiride.Analytics.Integration.Tests.ControlPlane;

/// <summary>
/// Proves the account policy the product actually enforces, and that a claim on an install
/// outlives the accounts it created.
/// </summary>
/// <remarks>
/// <para>
/// The policy is length plus a blocklist, and no composition rules, following the current
/// guidance from the standards body that used to require the opposite: rules about digits and
/// symbols are satisfied with predictable substitutions that cost an attacker nothing, and
/// fifteen characters is the stated minimum where a password is the only authenticator. That is
/// the case here until app-based two-step verification ships, so the number matters.
/// </para>
/// <para>
/// The blocklist half is covered in detail by <c>PredictablePasswordTests</c>. What is proved
/// here is that it is actually wired into the store, rather than being a class nothing calls.
/// </para>
/// <para>
/// The claim is the other thing proved here. An account's existence implies it, but accounts can
/// go — the purge of the last closed organisation on an install deletes every one of them — so
/// the claim is recorded in a row of its own. The tests about it run on installs created for
/// them, because on the shared stack the window closed before the first test ran.
/// </para>
/// </remarks>
/// <param name="stack">The running stack.</param>
[Collection(SharedStackDefinition.Name)]
public sealed class AccountTests(AnalyticsStackFixture stack)
{
    private const string Setup = "/api/setup";

    /// <summary>
    /// The statement the closure migration runs so that an install which already had accounts
    /// when the record was introduced is recorded as claimed, dated from its earliest account.
    /// </summary>
    private const string ClaimBackFillSql = """
        INSERT INTO installation_claims (id, claimed_at)
        SELECT 1, MIN(created_at)
        FROM users
        HAVING COUNT(*) > 0
        """;

    private const string ForgetClaimSql = "DELETE FROM installation_claims";

    [Fact]
    public async Task A_Passphrase_Of_Only_Letters_And_Spaces_Is_Accepted()
    {
        var (result, _) = await ControlPlaneSeed.AddAccountAsync(stack, Address(), Passwords.Acceptable);

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task A_Short_Password_Is_Refused_However_Complicated_It_Is()
    {
        var (result, _) = await ControlPlaneSeed.AddAccountAsync(stack, Address(), "Tr0ub4dor&3!x");

        result.Succeeded.Should().BeFalse();
        result.Errors.Select(error => error.Code).Should().Contain("PasswordTooShort");
    }

    /// <summary>
    /// Long enough, and still refused, because it is the single most published passphrase there
    /// is. Length on its own is not a policy.
    /// </summary>
    [Fact]
    public async Task A_Famous_Passphrase_Is_Refused_However_Long_It_Is()
    {
        var (result, _) = await ControlPlaneSeed.AddAccountAsync(
            stack,
            Address(),
            "correct horse battery staple");

        result.Succeeded.Should().BeFalse();
        result.Errors.Select(error => error.Code)
            .Should().Contain(PredictablePasswordValidator.ErrorCode);
    }

    [Fact]
    public async Task A_Password_Of_Exactly_The_Minimum_Length_Is_Accepted()
    {
        var (result, _) = await ControlPlaneSeed.AddAccountAsync(stack, Address(), "fifteenletters!");

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task Two_Accounts_Cannot_Share_An_Address()
    {
        var address = Address();

        var (first, _) = await ControlPlaneSeed.AddAccountAsync(stack, address, Passwords.Acceptable);
        var (second, _) = await ControlPlaneSeed.AddAccountAsync(stack, address, "another entirely different one");

        first.Succeeded.Should().BeTrue();
        second.Succeeded.Should().BeFalse();
        second.Errors.Select(error => error.Code).Should().Contain("DuplicateEmail");
    }

    [Fact]
    public async Task An_Account_Is_Found_By_Its_Address_Whatever_Case_It_Is_Typed_In()
    {
        var address = Address();
        await ControlPlaneSeed.AddAccountAsync(stack, address, Passwords.Acceptable);

        await using var scope = stack.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var found = await accounts.FindByEmailAsync(address.ToUpperInvariant());

        found.Should().NotBeNull();
        found.Email.Should().Be(address);
    }

    [Fact]
    public async Task A_Stored_Account_Never_Holds_The_Password_It_Was_Given()
    {
        const string password = Passwords.Acceptable;
        var address = Address();
        await ControlPlaneSeed.AddAccountAsync(stack, address, password);

        await using var scope = stack.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var stored = await accounts.FindByEmailAsync(address);

        stored.Should().NotBeNull();
        stored.PasswordHash.Should().NotBeNullOrEmpty();
        stored.PasswordHash.Should().NotContain(password);
        (await accounts.CheckPasswordAsync(stored, password)).Should().BeTrue();
    }

    [Fact]
    public void An_Account_Locks_After_Five_Wrong_Guesses_For_A_Quarter_Of_An_Hour()
    {
        var options = stack.Services.GetRequiredService<IOptions<IdentityOptions>>().Value;

        options.Lockout.AllowedForNewUsers.Should().BeTrue();
        options.Lockout.MaxFailedAccessAttempts.Should().Be(5);
        options.Lockout.DefaultLockoutTimeSpan.Should().Be(TimeSpan.FromMinutes(15));
    }

    [Fact]
    public void The_Password_Policy_Asks_For_Length_And_Nothing_Else()
    {
        var password = stack.Services.GetRequiredService<IOptions<IdentityOptions>>().Value.Password;

        password.RequiredLength.Should().Be(15);
        password.RequireDigit.Should().BeFalse();
        password.RequireLowercase.Should().BeFalse();
        password.RequireUppercase.Should().BeFalse();
        password.RequireNonAlphanumeric.Should().BeFalse();
    }

    /// <summary>
    /// The claim is written in the same transaction as the owner it creates, dated from the same
    /// instant, so the record and the account it stands for can never disagree about when.
    /// </summary>
    [Fact]
    public async Task Claiming_An_Install_Records_The_Claim_Once_Dated_From_Its_Owner()
    {
        await using var install = await FreshInstall.StartAsync(stack);
        using var browser = await Browser.OpenAsync(install);

        using var claimed = await browser.PostAsync(Setup, Claim());

        claimed.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var scope = install.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();

        var claims = await database.InstallationClaims.AsNoTracking().ToListAsync(Cancellation.Token);
        var owner = await database.Users.AsNoTracking().SingleAsync(Cancellation.Token);

        claims.Should().ContainSingle();
        claims[0].Id.Should().Be(InstallationClaim.TheOnlyRow);
        claims[0].ClaimedAt.Should().Be(owner.CreatedAt);
    }

    /// <summary>
    /// An install whose accounts have all gone is finished, not new. The welcome screen is the one
    /// moment an anonymous caller may create an owner, and it must not open again for whoever
    /// happens to find the install once its people are deleted.
    /// </summary>
    [Fact]
    public async Task An_Install_Whose_Accounts_Have_All_Gone_Is_Still_Claimed()
    {
        await using var install = await FreshInstall.StartAsync(stack);
        using var first = await Browser.OpenAsync(install);

        using var claimed = await first.PostAsync(Setup, Claim());

        claimed.StatusCode.Should().Be(HttpStatusCode.OK);

        await DeleteEveryAccountAsync(install);

        using var later = await Browser.OpenAsync(install);
        var session = await later.DescribeAsync();
        using var refused = await later.PostAsync(Setup, Claim("someone.else@example.com"));

        session.SetupCompleted.Should().BeTrue();
        session.User.Should().BeNull();
        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    /// <summary>
    /// An install that had accounts before there was a row to record the claim in was claimed all
    /// the same, and the migration says so after the fact. The instant it records is the earliest
    /// account's, not the first row it happens to read, so a second account dated earlier than the
    /// owner is what the statement is proved against.
    /// </summary>
    [Fact]
    public async Task The_Migration_Dates_An_Existing_Install_Claim_From_Its_Earliest_Account()
    {
        await using var install = await FreshInstall.StartAsync(stack);
        using var browser = await Browser.OpenAsync(install);

        using var claimed = await browser.PostAsync(Setup, Claim());

        claimed.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var scope = install.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();
        var owner = await database.Users.AsNoTracking().SingleAsync(Cancellation.Token);

        await AddAccountAsync(install, owner.CreatedAt.AddDays(-1));
        await database.Database.ExecuteSqlRawAsync(ForgetClaimSql, Cancellation.Token);

        await database.Database.ExecuteSqlRawAsync(ClaimBackFillSql, Cancellation.Token);

        var claims = await database.InstallationClaims.AsNoTracking().ToListAsync(Cancellation.Token);
        var earliest = await database.Users.AsNoTracking().MinAsync(user => user.CreatedAt, Cancellation.Token);

        claims.Should().ContainSingle();
        claims[0].Id.Should().Be(InstallationClaim.TheOnlyRow);
        claims[0].ClaimedAt.Should().Be(earliest);
        earliest.Should().BeBefore(owner.CreatedAt);
    }

    /// <summary>
    /// An install nobody has claimed must stay claimable through the migration, or a self-hoster
    /// who installed the product and upgraded before setting it up would find the door shut.
    /// </summary>
    [Fact]
    public async Task The_Migration_Records_No_Claim_For_An_Install_Nobody_Has_Claimed()
    {
        await using var install = await FreshInstall.StartAsync(stack);
        using var browser = await Browser.OpenAsync(install);

        await using var scope = install.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();

        await database.Database.ExecuteSqlRawAsync(ClaimBackFillSql, Cancellation.Token);

        (await database.InstallationClaims.AsNoTracking().AnyAsync(Cancellation.Token)).Should().BeFalse();
        (await browser.DescribeAsync()).SetupCompleted.Should().BeFalse();
    }

    private static string Address() => $"account-{Guid.NewGuid():n}@example.com";

    private static SetupRequest Claim(string emailAddress = "owner@example.com") =>
        FreshInstall.Claim(emailAddress);

    /// <summary>
    /// Adds an account to an install with a creation instant of the test's choosing, which is
    /// what the earliest-account rule has to be proved against.
    /// </summary>
    private static async Task AddAccountAsync(FreshInstall install, DateTimeOffset createdAt)
    {
        await using var scope = install.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var address = Address();

        var created = await accounts.CreateAsync(
            new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = address,
                Email = address,
                DisplayName = "Earlier Account",
                CreatedAt = createdAt,
            },
            Passwords.Acceptable);

        created.Succeeded.Should().BeTrue();
    }

    /// <summary>
    /// Deletes every account on an install, the way the purge of its last organisation does.
    /// </summary>
    private static async Task DeleteEveryAccountAsync(FreshInstall install)
    {
        await using var scope = install.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();

        await database.Users.ExecuteDeleteAsync(Cancellation.Token);

        (await database.Users.AnyAsync(Cancellation.Token)).Should().BeFalse();
    }
}
