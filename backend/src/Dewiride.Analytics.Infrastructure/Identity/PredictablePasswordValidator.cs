using System.Text;
using Microsoft.AspNetCore.Identity;

namespace Dewiride.Analytics.Infrastructure.Identity;

/// <summary>
/// Refuses passwords that are long enough to pass the length rule but still guessable.
/// </summary>
/// <remarks>
/// <para>
/// NIST SP 800-63B-4 §3.1.1.2 asks verifiers to compare a proposed password against a list of
/// commonly used, expected or compromised values, and to impose no composition rules alongside
/// it. The composition half is settled where the identity options are configured; this is the
/// other half.
/// </para>
/// <para>
/// The fifteen-character minimum already does most of the work, because nearly everything in a
/// published leak table is shorter than that and is refused before this runs. What survives the
/// length rule is a narrower and more predictable set: long keyboard runs, one character
/// repeated, a short password typed twice, well-known passphrases, and the account's own address
/// padded out to length. Those are what this refuses, along with a bundled list of long
/// passwords that are widely published.
/// </para>
/// <para>
/// It is deliberately not a breach-corpus check. Answering "has this exact password appeared in a
/// leak?" means either shipping a multi-gigabyte corpus with every install or sending part of a
/// password hash to somebody else's service on every sign-up, and neither belongs in a product
/// whose proposition is that it sends nothing anywhere.
/// </para>
/// </remarks>
public sealed class PredictablePasswordValidator : IPasswordValidator<ApplicationUser>
{
    /// <summary>Error code reported when a password is judged predictable.</summary>
    public const string ErrorCode = "PasswordIsPredictable";

    /// <summary>
    /// Fewest distinct characters a password may be built from.
    /// </summary>
    /// <remarks>
    /// Four is low on purpose. A passphrase of ordinary English words clears it comfortably,
    /// while a fifteen-character password drawn from three characters is searched in seconds
    /// however long it is.
    /// </remarks>
    private const int MinimumDistinctCharacters = 4;

    /// <summary>Longest run of repeated or neighbouring characters tolerated.</summary>
    private const int MaximumRunLength = 6;

    /// <summary>Shortest fragment of the account's own details worth matching against.</summary>
    private const int ShortestMeaningfulFragment = 4;

    /// <summary>
    /// Rows of an ordinary keyboard, forwards.
    /// </summary>
    /// <remarks>
    /// Runs typed backwards are found by searching each row reversed as well, so there is no
    /// separate entry for them. The alphabet is included because it is a keyboard path for the
    /// purposes of guessing software even though no keyboard is laid out that way.
    /// </remarks>
    private static readonly string[] KeyboardRows =
    [
        "qwertyuiop",
        "asdfghjkl",
        "zxcvbnm",
        "1234567890",
        "abcdefghijklmnopqrstuvwxyz",
    ];

    /// <summary>
    /// Words describing this product, which are the first thing anybody padding a password out
    /// to fifteen characters reaches for.
    /// </summary>
    private static readonly string[] ProductWords =
    [
        "dewiride",
        "analytics",
        "dashboard",
        "administrator",
    ];

    /// <summary>
    /// Long passwords and passphrases that are widely published and therefore worthless.
    /// </summary>
    /// <remarks>
    /// Short entries would be pointless: anything under fifteen characters is refused by the
    /// length rule before this runs, so the list holds only what is long enough to reach here.
    /// Matching ignores case, spacing and punctuation, so one entry covers the many ways the
    /// same phrase gets typed.
    /// </remarks>
    private static readonly string[] KnownPassphrases =
    [
        "correcthorsebatterystaple",
        "iloveyousomuchbaby",
        "letmeinrightnow",
        "passwordpassword",
        "passwordisapassword",
        "thisismypassword",
        "thereisnopassword",
        "notmyrealpassword",
        "temporarypassword",
        "defaultpassword",
        "administratorpassword",
        "superadministrator",
        "changemeimmediately",
        "welcometothejungle",
        "trustnoonebutme",
        "iamnotarobotatall",
        "opensesameplease",
        "keyboardcatforever",
        "startrekstarwars",
        "zaq12wsxcde34rfv",
        "1qaz2wsx3edc4rfv",
        "qazwsxedcrfvtgb",
    ];

    /// <inheritdoc />
    public Task<IdentityResult> ValidateAsync(
        UserManager<ApplicationUser> manager,
        ApplicationUser user,
        string? password)
    {
        ArgumentNullException.ThrowIfNull(user);

        if (string.IsNullOrEmpty(password))
        {
            return Task.FromResult(IdentityResult.Success);
        }

        var reason = FirstReasonToRefuse(user, password);

        return Task.FromResult(reason is null
            ? IdentityResult.Success
            : IdentityResult.Failed(new IdentityError { Code = ErrorCode, Description = reason }));
    }

    /// <summary>
    /// Names the first thing wrong with a password, or nothing when it is acceptable.
    /// </summary>
    /// <remarks>
    /// Only the first reason is reported. Listing every way a password is weak describes it back
    /// to whoever is watching the screen, and the person choosing it only needs to know to
    /// choose again.
    /// </remarks>
    private static string? FirstReasonToRefuse(ApplicationUser user, string password)
    {
        var folded = Fold(password);

        if (CountDistinct(folded) < MinimumDistinctCharacters)
        {
            return "This password is built from too few different characters. A few unrelated "
                + "words are much harder to guess.";
        }

        if (HasLongRun(password))
        {
            return "This password contains a long run of repeated or neighbouring keys. A few "
                + "unrelated words are much harder to guess.";
        }

        if (ContainsKeyboardSequence(folded))
        {
            return "This password follows a path across the keyboard, which is one of the first "
                + "things guessing software tries.";
        }

        if (IsTheSameFragmentTwice(folded))
        {
            return "This password is one short password written twice. Repeating it does not "
                + "make it harder to guess.";
        }

        if (Array.Exists(KnownPassphrases, known => folded.Contains(known, StringComparison.Ordinal)))
        {
            return "This password is one of a well-known set that appears in published lists. "
                + "Choose a few unrelated words instead.";
        }

        if (Array.Exists(ProductWords, word => folded.Contains(word, StringComparison.Ordinal)))
        {
            return "This password contains the name of this product, which is the first thing "
                + "anyone guessing would try.";
        }

        return ContainsOwnDetails(user, folded)
            ? "This password contains part of your own name or email address, which is public "
                + "enough to be guessed."
            : null;
    }

    /// <summary>
    /// Reduces a password to lower-case letters and digits.
    /// </summary>
    /// <remarks>
    /// Comparisons are made against the folded form so that "Correct-Horse Battery Staple!" and
    /// "correcthorsebatterystaple" are recognised as the same choice. The folded form is used
    /// only to decide whether to refuse; it is never stored and never leaves this method's
    /// caller.
    /// </remarks>
    private static string Fold(string value)
    {
        var builder = new StringBuilder(value.Length);

        foreach (var character in value.Where(char.IsLetterOrDigit))
        {
            builder.Append(char.ToLowerInvariant(character));
        }

        return builder.ToString();
    }

    private static int CountDistinct(string value)
    {
        var seen = new HashSet<char>(value.Length);

        foreach (var character in value)
        {
            seen.Add(character);
        }

        return seen.Count;
    }

    /// <summary>
    /// Detects a long run of one repeated character, or of consecutive ones in either direction.
    /// </summary>
    /// <remarks>
    /// Read over the password as typed rather than the folded form, so that a boundary between
    /// two separate words is not mistaken for the middle of a run.
    /// </remarks>
    private static bool HasLongRun(string password)
    {
        var repeated = 1;
        var ascending = 1;
        var descending = 1;

        for (var index = 1; index < password.Length; index++)
        {
            var previous = char.ToLowerInvariant(password[index - 1]);
            var current = char.ToLowerInvariant(password[index]);

            repeated = current == previous ? repeated + 1 : 1;
            ascending = current == previous + 1 ? ascending + 1 : 1;
            descending = current == previous - 1 ? descending + 1 : 1;

            if (Math.Max(repeated, Math.Max(ascending, descending)) > MaximumRunLength)
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsKeyboardSequence(string folded)
    {
        foreach (var row in KeyboardRows)
        {
            for (var start = 0; start + MaximumRunLength <= row.Length; start++)
            {
                var run = row.Substring(start, MaximumRunLength);

                if (folded.Contains(run, StringComparison.Ordinal)
                    || folded.Contains(Reverse(run), StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string Reverse(string value)
    {
        var characters = value.ToCharArray();
        Array.Reverse(characters);

        return new string(characters);
    }

    private static bool IsTheSameFragmentTwice(string folded)
    {
        if (folded.Length % 2 != 0 || folded.Length == 0)
        {
            return false;
        }

        var half = folded.Length / 2;

        return string.Equals(folded[..half], folded[half..], StringComparison.Ordinal);
    }

    /// <summary>
    /// Detects the account's own details inside the password.
    /// </summary>
    /// <remarks>
    /// The address is split on the characters that separate its parts, so "jane.doe@example.com"
    /// contributes "jane", "doe" and "example" rather than one string nobody would type whole.
    /// </remarks>
    private static bool ContainsOwnDetails(ApplicationUser user, string folded)
    {
        foreach (var fragment in Fragments(user))
        {
            var foldedFragment = Fold(fragment);

            if (foldedFragment.Length >= ShortestMeaningfulFragment
                && folded.Contains(foldedFragment, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> Fragments(ApplicationUser user)
    {
        var sources = new[] { user.Email, user.UserName, user.DisplayName };

        foreach (var source in sources)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                continue;
            }

            foreach (var fragment in source.Split(
                ['@', '.', '-', '_', '+', ' '],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                yield return fragment;
            }
        }
    }
}
