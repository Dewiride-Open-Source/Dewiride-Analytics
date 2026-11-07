using System.Reflection;
using System.Reflection.Emit;
using NetArchTest.Rules;

namespace Dewiride.Analytics.Architecture.Tests;

/// <summary>
/// Asserts that a website's page is only ever read by its tokens.
/// </summary>
/// <remarks>
/// Any account can name a sitemap on a server it runs, so a page's markup is written by somebody who may mean
/// the reader harm. Building a document from it is what lets markup a few kibibytes long hold a processor for
/// good or take the process down, so that is not something the reader is able to do, and the one thing left
/// that could end the process is held to what the reader knows how to deal with.
/// </remarks>
[Trait("Category", "PageReading")]
public sealed class PageReadingBoundaryTests
{
    private const string Posts = "Dewiride.Analytics.Infrastructure.Posts";
    private const string ParserAssembly = "AngleSharp";
    private const string Tokenizer = "AngleSharp.Html.Parser.HtmlTokenizer";

    private static readonly Dictionary<short, OpCode> OpCodesByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(field => (OpCode)field.GetValue(null)!)
        .ToDictionary(code => code.Value);

    [Fact]
    public void No_Document_Is_Ever_Built_From_A_Websites_Page()
    {
        var result = Types.InAssembly(Product.Assembly(Product.Infrastructure))
            .That()
            .ResideInNamespace(Posts)
            .ShouldNot()
            .HaveDependencyOnAny(
                "AngleSharp.BrowsingContext",
                "AngleSharp.IBrowsingContext",
                "AngleSharp.Configuration",
                "AngleSharp.IConfiguration",
                "AngleSharp.Html.Parser.HtmlParser",
                "AngleSharp.Html.Parser.IHtmlParser",
                "AngleSharp.Dom.IDocument")
            .GetResult();

        var offenders = result.FailingTypes?.Select(type => type.FullName) ?? [];

        result.IsSuccessful.Should().BeTrue("these go through a document: {0}", string.Join(", ", offenders));
    }

    /// <summary>
    /// A stack overflow can not be caught, and ends the engine. The tokenizer calls itself on two runs a
    /// page can write, which the reader breaks up before it starts, and going back over a line ending, which
    /// goes one level deep and no further. A new version that calls itself anywhere else has to be read
    /// before the reader can use it, because a page the length of the limit may be able to make it overflow.
    /// </summary>
    [Fact]
    public void The_Tokenizer_Calls_Itself_Only_Where_The_Reader_Knows_It_Does()
    {
        var tokenizer = Assembly.Load(ParserAssembly).GetType(Tokenizer, throwOnError: true)!;
        var declared = new[] { tokenizer, tokenizer.BaseType! }
            .SelectMany(type => type.GetRuntimeMethods().Where(method => method.DeclaringType == type))
            .Cast<MethodBase>()
            .ToHashSet();
        var calls = declared.ToDictionary(method => method, method => CallsFrom(method, declared));

        var callingThemselves = declared
            .Where(method => Reaches(method, method, calls))
            .Select(method => method.Name)
            .Order(StringComparer.Ordinal);

        callingThemselves.Should().Equal(
            "BackUnsafe",
            "CommentBangEnd",
            "CommentDashEnd",
            "CommentEnd",
            "Data",
            "NormalizeBackward",
            "TagEnd",
            "TagOpen");
    }

    private static bool Reaches(
        MethodBase from,
        MethodBase target,
        Dictionary<MethodBase, HashSet<MethodBase>> calls)
    {
        var seen = new HashSet<MethodBase>();
        var waiting = new Stack<MethodBase>(calls[from]);

        while (waiting.TryPop(out var next))
        {
            if (next == target)
            {
                return true;
            }

            if (seen.Add(next))
            {
                foreach (var called in calls[next])
                {
                    waiting.Push(called);
                }
            }
        }

        return false;
    }

    private static HashSet<MethodBase> CallsFrom(MethodBase method, HashSet<MethodBase> among) =>
        [.. MethodTokens(method).Select(method.Module.ResolveMethod).OfType<MethodBase>().Where(among.Contains)];

    /// <summary>The method each call in a method's body is to, by going through its instructions.</summary>
    private static List<int> MethodTokens(MethodBase method)
    {
        var body = method.GetMethodBody()?.GetILAsByteArray() ?? [];
        var tokens = new List<int>();
        var at = 0;

        while (at < body.Length)
        {
            var code = ReadCode(body, ref at);

            if (code.OperandType == OperandType.InlineMethod)
            {
                tokens.Add(BitConverter.ToInt32(body, at));
            }

            at += OperandLength(code.OperandType, body, at);
        }

        return tokens;
    }

    private static OpCode ReadCode(byte[] body, ref int at)
    {
        var first = body[at++];

        return first == OpCodes.Prefix1.Value
            ? OpCodesByValue[unchecked((short)((first << 8) | body[at++]))]
            : OpCodesByValue[first];
    }

    private static int OperandLength(OperandType type, byte[] body, int at) =>
        type switch
        {
            OperandType.InlineNone => 0,
            OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
            OperandType.InlineVar => 2,
            OperandType.InlineI8 or OperandType.InlineR => 8,
            OperandType.InlineSwitch => 4 + (4 * BitConverter.ToInt32(body, at)),
            _ => 4,
        };
}
