using Dewiride.Analytics.Infrastructure.Accounts;

namespace Dewiride.Analytics.MailTests;

/// <summary>
/// The messages the free product sends, approved as a reader receives them.
/// </summary>
/// <remarks>
/// Both of these go to somebody who may not have asked for them: a password reset can be triggered
/// by anybody who knows an address, and an invitation goes to a stranger. What they say about that
/// is the part worth pinning, and it is the part that erodes first when somebody is editing copy.
/// </remarks>
public sealed class AccountMessageTests
{
    /// <summary>
    /// Somebody asking for a way back into their account.
    /// </summary>
    /// <returns>The approval.</returns>
    [Fact]
    public Task Password_reset() =>
        Verify(MailReport.Render(PasswordResetMessage.For(
            "reader@example.com",
            "Jagdish",
            "https://analytics.example.com/app/reset-password?address=reader%40example.com&token=abc",
            "a-secret-token")));

    /// <summary>
    /// Somebody asked to join an account they have nothing to do with yet.
    /// </summary>
    /// <returns>The approval.</returns>
    [Fact]
    public Task Invitation() =>
        Verify(MailReport.Render(InvitationMessage.For(
            "stranger@example.com",
            new Guid("01a01b63-5adc-7407-b349-47ffb0922fde"),
            "Acme & Sons",
            "Jagdish Kumawat",
            "https://analytics.example.com/app/join?secret=abc")));

    /// <summary>
    /// The same reset, asked for twice, is the same message.
    /// </summary>
    /// <remarks>
    /// And a different token is a different message. Getting this the wrong way round either sends
    /// two links for one request or suppresses the second request entirely, and neither is visible
    /// from the outside until somebody cannot get into their account.
    /// </remarks>
    [Fact]
    public void One_token_is_one_message()
    {
        var first = PasswordResetMessage.For("a@example.com", null, "https://x.example/a", "token-one");
        var again = PasswordResetMessage.For("a@example.com", null, "https://x.example/a", "token-one");
        var second = PasswordResetMessage.For("a@example.com", null, "https://x.example/a", "token-two");

        first.IdempotencyKey.Should().Be(again.IdempotencyKey);
        first.IdempotencyKey.Should().NotBe(second.IdempotencyKey);
    }

    /// <summary>
    /// The token never leaves in the key that identifies the message.
    /// </summary>
    /// <remarks>
    /// Whatever delivers the message keeps that key, often for longer than the token is valid. A
    /// reset token sitting in somebody else's log because it was convenient to identify a message
    /// with is a credential leak nobody would go looking for.
    /// </remarks>
    [Fact]
    public void The_key_does_not_carry_the_token()
    {
        var message = PasswordResetMessage.For(
            "a@example.com",
            null,
            "https://x.example/a",
            "the-actual-secret-token");

        message.IdempotencyKey.Should().NotContain("the-actual-secret-token");
        message.IdempotencyKey.Should().StartWith("password-reset:");
    }

    /// <summary>
    /// One invitation is one message however many times the pass runs.
    /// </summary>
    [Fact]
    public void One_invitation_is_one_message()
    {
        var id = new Guid("01a01b63-5adc-7407-b349-47ffb0922fde");

        var first = InvitationMessage.For("a@example.com", id, "Acme", "Jagdish", "https://x.example/a");
        var again = InvitationMessage.For("a@example.com", id, "Acme", "Jagdish", "https://x.example/a");

        first.IdempotencyKey.Should().Be(again.IdempotencyKey);
    }
}
