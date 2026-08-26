using System.Reflection;
using System.Text.RegularExpressions;
using Dewiride.Analytics.Infrastructure.Notifications;

namespace Dewiride.Analytics.MailTests;

/// <summary>
/// What a message tells a mail client about colour, and what it is allowed to say.
/// </summary>
/// <remarks>
/// None of this is visible in an approved file, because an approved file is correct whatever the
/// colours in it are. Each of these fails silently in an inbox and nowhere else: a message that
/// does not declare a scheme is repainted by the client, a dark rule the client's own inversion
/// cannot reach is a rule that never applies, and one colour written by hand is one colour that
/// stops matching the rest the next time the palette moves.
/// </remarks>
public sealed partial class MailColourTests
{
    /// <summary>
    /// A message carrying every part that has a colour of its own.
    /// </summary>
    private static readonly string Html = MailTemplate.Compose(
        new Recipient("reader@example.com", "Jagdish"),
        "colour:1",
        new MailContent
        {
            Subject = "Something happened to your account",
            Preheader = "And here is the short version of it.",
            Paragraphs = ["A sentence about the thing that happened."],
            Facts = [new MailFact("Pages this month", MailTemplate.Count(417_000))],
            Action = "Have a look",
            Link = "https://analytics.example.com/app",
            Footnotes = ["A sentence nobody has to read."],
        }).Html;

    /// <summary>
    /// A message says it handles both schemes itself, twice.
    /// </summary>
    /// <remarks>
    /// A client that finds neither declaration assumes the message knows nothing about dark mode
    /// and inverts it channel by channel, which is what turns a considered palette into a set of
    /// similar greys. The element and the property are honoured by different clients, so both are
    /// stated.
    /// </remarks>
    [Fact]
    public void A_message_declares_that_it_handles_both_schemes()
    {
        Html.Should().Contain("""<meta name="color-scheme" content="light dark">""");
        Html.Should().Contain("""<meta name="supported-color-schemes" content="light dark">""");
        Html.Should().Contain("color-scheme:light dark;");
    }

    /// <summary>
    /// Every rule for a dark client is also reachable when a client darkens the message itself.
    /// </summary>
    /// <remarks>
    /// Outlook.com inverts whatever the message says and marks what it touched, so the same
    /// declarations have to be reachable through those marks as well as through the reader's stated
    /// preference. A rule added to one and not the other is a part of the message that keeps the
    /// colour Outlook chose for it.
    /// </remarks>
    [Fact]
    public void Every_dark_rule_survives_a_client_that_darkens_the_message_itself()
    {
        var darkened = DarkClasses();

        darkened.Should().NotBeEmpty();

        foreach (var name in darkened)
        {
            MailStyles.Sheet.Should().Contain($"[data-ogsc] .{name}");
            MailStyles.Sheet.Should().Contain($"[data-ogsb] .{name}");
            MailStyles.Sheet.Should().Contain($".{name}[data-ogsc]");
            MailStyles.Sheet.Should().Contain($".{name}[data-ogsb]");
        }
    }

    /// <summary>
    /// Every name the template writes onto an element is a name some rule acts on.
    /// </summary>
    /// <remarks>
    /// The other direction of the rule above. A name is either coloured for a dark client or
    /// rearranged for a narrow one; a name in neither block does nothing at all, and the way that
    /// happens is a coloured element gaining a class the dark set was never told about — which
    /// leaves it wearing its light colour on a dark card, the one arrangement where text disappears
    /// entirely.
    /// </remarks>
    [Fact]
    public void Every_name_the_template_writes_is_a_name_some_rule_acts_on()
    {
        var written = ClassAttribute()
            .Matches(Html)
            .SelectMany(match => match.Groups[1].Value.Split(' '))
            .Where(name => name.Length > 0)
            .Distinct();

        written.Should().BeSubsetOf([.. DarkClasses(), .. NarrowClasses()]);
    }

    /// <summary>
    /// Every colour in a message is one of the product's own.
    /// </summary>
    /// <remarks>
    /// The failure this catches is a colour written straight onto an element, which looks right on
    /// the day and is then missed by every later change to the palette — including the dark set,
    /// where the consequence is lettering that keeps its light-theme colour on a dark card.
    /// </remarks>
    [Fact]
    public void No_colour_is_written_outside_the_palette()
    {
        var used = HexColour()
            .Matches(Html)
            .Select(match => match.Value.ToLowerInvariant())
            .Distinct();

        used.Should().BeSubsetOf(Palette());
    }

    /// <summary>What a reader in a dark client is shown instead.</summary>
    private static string[] DarkClasses() => ClassesIn("@media (prefers-color-scheme: dark) {");

    /// <summary>What is rearranged for a reader on a phone.</summary>
    private static string[] NarrowClasses() =>
        ClassesIn("@media only screen and (max-width: 620px) {");

    /// <summary>The names one block of the stylesheet acts on.</summary>
    private static string[] ClassesIn(string opening)
    {
        var sheet = MailStyles.Sheet;
        var start = sheet.IndexOf(opening, StringComparison.Ordinal);

        start.Should().BePositive("the stylesheet is expected to carry {0}", opening);

        var end = sheet.IndexOf("\n}", start, StringComparison.Ordinal);

        return [.. ClassSelector()
            .Matches(sheet[start..end])
            .Select(match => match.Groups[1].Value)
            .Distinct()];
    }

    private static string[] Palette() =>
        [.. typeof(MailPalette)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .Where(value => value.StartsWith('#'))
            .Select(value => value.ToLowerInvariant())
            .Distinct()];

    [GeneratedRegex("#[0-9a-fA-F]{6}")]
    private static partial Regex HexColour();

    [GeneratedRegex(@"^\s*\.(dw-[a-z-]+) \{", RegexOptions.Multiline)]
    private static partial Regex ClassSelector();

    [GeneratedRegex(@"class=""(dw-[a-z0-9 -]*)""")]
    private static partial Regex ClassAttribute();
}
