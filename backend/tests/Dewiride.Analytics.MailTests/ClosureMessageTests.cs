using Dewiride.Analytics.Infrastructure.Accounts;
using Dewiride.Analytics.Infrastructure.Notifications;
using Dewiride.Analytics.Testing;

namespace Dewiride.Analytics.MailTests;

/// <summary>
/// The messages an account's owners get when it is closed, about to be deleted, and brought back.
/// </summary>
/// <remarks>
/// Every one of these goes to an owner who may not have been the one who acted, and the whole
/// point of sending them is that a closure nobody meant can be noticed in time. What each says
/// about what has happened, what has not yet happened, and by when to act is the part worth pinning.
/// </remarks>
public sealed class ClosureMessageTests
{
    private static readonly Guid Account = new("01a01b63-5adc-7407-b349-47ffb0922fde");
    private static readonly DateTimeOffset ClosedAt = new(2026, 9, 21, 9, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset DeletionDue = ClosedAt.AddDays(30);
    private const string SignIn = "https://analytics.example.com/app/sign-in";

    /// <summary>
    /// An owner told that somebody else closed the account.
    /// </summary>
    [Fact]
    public void Account_closed() =>
        Snapshot.Matches(MailReport.Render(AccountClosedMessage.For(
            new Recipient("owner@example.com", "Priya"),
            Account,
            "Acme & Sons",
            "Jagdish Kumawat",
            ClosedAt,
            DeletionDue,
            SignIn)));

    /// <summary>
    /// The week's notice before a closed account is deleted.
    /// </summary>
    [Fact]
    public void Deletion_reminder() =>
        Snapshot.Matches(MailReport.Render(AccountDeletionReminderMessage.For(
            new Recipient("owner@example.com", "Priya"),
            Account,
            "Acme & Sons",
            ClosedAt,
            DeletionDue,
            SignIn)));

    /// <summary>
    /// An owner told that the account is open again.
    /// </summary>
    [Fact]
    public void Account_restored() =>
        Snapshot.Matches(MailReport.Render(AccountRestoredMessage.For(
            new Recipient("owner@example.com", "Priya"),
            Account,
            "Acme & Sons",
            "Jagdish Kumawat",
            ClosedAt.AddDays(3),
            SignIn)));

    /// <summary>
    /// One closure announced twice is one message; the same account closed again is another.
    /// </summary>
    /// <remarks>
    /// The key carries the instant the closure was recorded, never the moment of sending, so a
    /// pass that fails after sending and runs again sends nothing new — and an account brought
    /// back and closed a second time is told about the second time.
    /// </remarks>
    [Fact]
    public void One_closure_is_one_message()
    {
        var to = new Recipient("owner@example.com", null);

        var first = AccountClosedMessage.For(to, Account, "Acme", "Jagdish", ClosedAt, DeletionDue, SignIn);
        var again = AccountClosedMessage.For(to, Account, "Acme", "Jagdish", ClosedAt, DeletionDue, SignIn);
        var later = AccountClosedMessage.For(
            to,
            Account,
            "Acme",
            "Jagdish",
            ClosedAt.AddDays(5),
            DeletionDue.AddDays(5),
            SignIn);

        first.IdempotencyKey.Should().Be(again.IdempotencyKey);
        first.IdempotencyKey.Should().NotBe(later.IdempotencyKey);
    }

    /// <summary>
    /// The same closure told to two owners is two messages.
    /// </summary>
    /// <remarks>
    /// Whatever delivers the hosted edition's mail treats one key as one message and will not send
    /// it to a second address, so a key that did not name the mailbox would leave every owner but
    /// the first untold. The mailbox is matched the way the account store matches addresses, so a
    /// difference of case is not a second owner.
    /// </remarks>
    [Fact]
    public void Two_owners_are_two_messages()
    {
        var one = AccountClosedMessage.For(
            new Recipient("one@example.com", null), Account, "Acme", "Jagdish", ClosedAt, DeletionDue, SignIn);
        var two = AccountClosedMessage.For(
            new Recipient("two@example.com", null), Account, "Acme", "Jagdish", ClosedAt, DeletionDue, SignIn);
        var oneAgain = AccountClosedMessage.For(
            new Recipient("One@Example.com", null), Account, "Acme", "Jagdish", ClosedAt, DeletionDue, SignIn);

        one.IdempotencyKey.Should().NotBe(two.IdempotencyKey);
        one.IdempotencyKey.Should().Be(oneAgain.IdempotencyKey);
    }

    /// <summary>
    /// The three messages about one closure never share a key.
    /// </summary>
    [Fact]
    public void The_three_messages_are_distinct()
    {
        var to = new Recipient("owner@example.com", null);

        var closed = AccountClosedMessage.For(to, Account, "Acme", "Jagdish", ClosedAt, DeletionDue, SignIn);
        var reminder = AccountDeletionReminderMessage.For(to, Account, "Acme", ClosedAt, DeletionDue, SignIn);
        var restored = AccountRestoredMessage.For(to, Account, "Acme", "Jagdish", ClosedAt, SignIn);

        new[] { closed.IdempotencyKey, reminder.IdempotencyKey, restored.IdempotencyKey }
            .Should().OnlyHaveUniqueItems();
    }
}
