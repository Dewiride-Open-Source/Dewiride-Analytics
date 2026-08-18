using Dewiride.Analytics.Infrastructure.Identity;
using Dewiride.Analytics.Integration.Tests.Fixtures;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Dewiride.Analytics.Integration.Tests.ControlPlane;

/// <summary>
/// Proves the account policy the product actually enforces.
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
/// </remarks>
/// <param name="stack">The running stack.</param>
[Collection(SharedStackDefinition.Name)]
public sealed class AccountTests(AnalyticsStackFixture stack)
{
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

    private static string Address() => $"account-{Guid.NewGuid():n}@example.com";
}
