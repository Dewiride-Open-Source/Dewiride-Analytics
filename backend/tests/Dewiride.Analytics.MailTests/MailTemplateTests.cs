using Dewiride.Analytics.Infrastructure.Notifications;
using Dewiride.Analytics.Testing;

namespace Dewiride.Analytics.MailTests;

/// <summary>
/// What the shape every message takes actually produces.
/// </summary>
public sealed class MailTemplateTests
{
    private static readonly Recipient Somebody = new("reader@example.com", "Jagdish");

    /// <summary>
    /// The plainest message there is: a greeting, a paragraph, and one thing to do.
    /// </summary>
    [Fact]
    public void Plainest_message() =>
        Snapshot.Matches(MailReport.Render(MailTemplate.Compose(
            Somebody,
            "example:1",
            new MailContent
            {
                Subject = "Something happened to your account",
                Preheader = "And here is the short version of it.",
                Paragraphs = ["A sentence about the thing that happened."],
                Action = "Have a look",
                Link = "https://analytics.example.com/app",
            })));

    /// <summary>
    /// A message about figures, which is the shape the allowance notices take.
    /// </summary>
    [Fact]
    public void Message_with_figures_and_small_print() =>
        Snapshot.Matches(MailReport.Render(MailTemplate.Compose(
            Somebody,
            "example:2",
            new MailContent
            {
                Subject = "You have used most of this month's pages",
                Preheader = "417,000 of 500,000 pages. Nothing stops yet.",
                Paragraphs = ["Your websites have been busy. Here is where this month stands."],
                Facts =
                [
                    new MailFact("Pages this month", MailTemplate.Count(417_000)),
                    new MailFact("Included in Growth", MailTemplate.Count(500_000)),
                    new MailFact("Measuring pauses", MailTemplate.Day(new DateTimeOffset(2026, 9, 3, 0, 0, 0, TimeSpan.Zero))),
                ],
                Action = "See where you are",
                Link = "https://analytics.example.com/app/settings/plan",
                Footnotes = ["Nothing changes yet.", "We write once more if you pass it."],
            })));

    /// <summary>
    /// Somebody with no name is greeted by the thing they did give.
    /// </summary>
    [Fact]
    public void Nameless_reader_is_greeted_by_their_address() =>
        new Recipient("reader@example.com", null).Greeting.Should().Be("reader@example.com");

    /// <summary>
    /// A name that is only spaces is the same as no name at all.
    /// </summary>
    [Fact]
    public void Blank_name_is_no_name() =>
        new Recipient("reader@example.com", "   ").Greeting.Should().Be("reader@example.com");

    /// <summary>
    /// Everything a person supplied is escaped before it reaches the HTML.
    /// </summary>
    /// <remarks>
    /// The names in these messages come from whoever typed them — an organisation's name, an
    /// account holder's name — and land in a document a mail client renders. This is the one
    /// failure in the whole file that is a vulnerability rather than a blemish, so it is asserted
    /// directly rather than left to a reviewer noticing it in an approved file.
    /// </remarks>
    [Fact]
    public void Anything_a_person_typed_is_escaped()
    {
        var message = MailTemplate.Compose(
            new Recipient("reader@example.com", "<script>alert(1)</script>"),
            "example:3",
            new MailContent
            {
                Subject = "Join <b>Acme</b> & Co",
                Preheader = "<img src=x onerror=alert(1)>",
                Paragraphs = ["A paragraph with <i>markup</i> in it."],
                Facts = [new MailFact("<th>", "<td>")],
                Action = "Press <this>",
                Link = "https://analytics.example.com/app?a=1&b=2",
                Footnotes = ["<footer>"],
            });

        // What matters is that no tag survives as a tag. An attribute name can appear inside
        // escaped text — "&lt;img src=x onerror=alert(1)&gt;" contains the characters and is inert
        // — so the assertion is about the angle brackets, which are the whole of the difference.
        message.Html.Should().NotContain("<script>");
        message.Html.Should().NotContain("<img");
        message.Html.Should().NotContain("<b>Acme</b>");
        message.Html.Should().NotContain("<i>markup</i>");
        message.Html.Should().NotContain("<th>");
        message.Html.Should().NotContain("<footer>");
        message.Html.Should().Contain("&lt;script&gt;");
        message.Html.Should().Contain("a=1&amp;b=2");
    }

    /// <summary>
    /// The subject a reader sees is the subject the message was composed with.
    /// </summary>
    [Fact]
    public void Subject_travels_unescaped_on_the_message_itself()
    {
        var message = MailTemplate.Compose(
            Somebody,
            "example:4",
            new MailContent
            {
                Subject = "Join Acme & Co",
                Preheader = "Anything.",
                Paragraphs = ["Anything."],
                Action = "Go",
                Link = "https://analytics.example.com/app",
            });

        // Escaping belongs to the HTML body alone. A subject line is not markup, and a mail client
        // shown an escaped one displays the escape rather than the ampersand.
        message.Subject.Should().Be("Join Acme & Co");
        message.PlainText.Should().Contain("Join Acme & Co");
    }

    /// <summary>
    /// The footer names where the button actually goes.
    /// </summary>
    /// <remarks>
    /// Taken from the link rather than from a setting, so the two cannot disagree. A message whose
    /// footer names one installation and whose button leads to another is the shape of every
    /// phishing message ever written.
    /// </remarks>
    [Fact]
    public void Footer_names_the_host_the_button_leads_to()
    {
        var message = MailTemplate.Compose(
            Somebody,
            "example:5",
            new MailContent
            {
                Subject = "Anything",
                Preheader = "Anything.",
                Paragraphs = ["Anything."],
                Action = "Go",
                Link = "https://analytics.dewiride.org/app/sign-in",
            });

        message.Html.Should().Contain("analytics.dewiride.org");
        message.PlainText.Should().Contain("analytics.dewiride.org");
    }

    /// <summary>
    /// A count is grouped the way somebody reads one.
    /// </summary>
    [Fact]
    public void Counts_are_grouped() => MailTemplate.Count(1_234_567).Should().Be("1,234,567");

    /// <summary>
    /// A date is the day, without a time nobody needs.
    /// </summary>
    [Fact]
    public void Days_carry_no_time() =>
        MailTemplate.Day(new DateTimeOffset(2026, 9, 3, 14, 22, 0, TimeSpan.Zero))
            .Should()
            .Be("3 September");
}
