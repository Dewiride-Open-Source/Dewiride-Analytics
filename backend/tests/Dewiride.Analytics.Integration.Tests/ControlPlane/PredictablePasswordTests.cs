using Dewiride.Analytics.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;

namespace Dewiride.Analytics.Integration.Tests.ControlPlane;

/// <summary>
/// Proves which long-but-guessable passwords are refused.
/// </summary>
/// <remarks>
/// <para>
/// The fifteen-character minimum is enforced separately and refuses almost everything on a
/// published leak list before this ever runs. What is left is the narrower set somebody reaches
/// for when told their password is too short: the same key held down, a walk across the keyboard,
/// a short password typed twice, or their own address padded out.
/// </para>
/// <para>
/// No container is needed and none is used. The validator answers from the proposed password and
/// the account's own details alone — it never consults the store it is handed, which is why the
/// tests pass nothing for it.
/// </para>
/// </remarks>
public sealed class PredictablePasswordTests
{
    private static readonly PredictablePasswordValidator Validator = new();

    [Theory]
    [InlineData("vermilion tractor almanac")]
    [InlineData("Copper Lantern Nine Hills")]
    [InlineData("h4rbour-brisket-window")]
    public async Task A_Few_Unrelated_Words_Are_Accepted(string password)
    {
        var result = await Validator.ValidateAsync(Store(), Account(), password);

        result.Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData("aaaaaaaaaaaaaaaaaa")]
    [InlineData("ababababababababab")]
    public async Task Too_Few_Different_Characters_Is_Refused(string password)
    {
        await Refuses(password);
    }

    [Theory]
    [InlineData("mangoAAAAAAAAtrellis")]
    [InlineData("mangoabcdefghtrellis")]
    [InlineData("mangohgfedcbatrellis")]
    public async Task A_Long_Run_Of_Repeated_Or_Neighbouring_Keys_Is_Refused(string password)
    {
        await Refuses(password);
    }

    [Theory]
    [InlineData("qwertyuiop mango tree")]
    [InlineData("mango poiuytrewq tree")]
    [InlineData("mango asdfghjkl trees")]
    public async Task A_Walk_Across_The_Keyboard_Is_Refused(string password)
    {
        await Refuses(password);
    }

    [Fact]
    public async Task One_Short_Password_Written_Twice_Is_Refused()
    {
        await Refuses("marmalade-marmalade");
    }

    [Theory]
    [InlineData("correct horse battery staple")]
    [InlineData("Correct-Horse-Battery-Staple!")]
    [InlineData("my temporary password 42")]
    public async Task A_Passphrase_From_A_Published_List_Is_Refused(string password)
    {
        await Refuses(password);
    }

    [Theory]
    [InlineData("dewiride is my product")]
    [InlineData("the analytics one 1234")]
    public async Task A_Password_Naming_This_Product_Is_Refused(string password)
    {
        await Refuses(password);
    }

    [Theory]
    [InlineData("jane went to the shops")]
    [InlineData("thornbury for the win!")]
    [InlineData("Marchetti goes walking")]
    public async Task A_Password_Containing_The_Account_Own_Details_Is_Refused(string password)
    {
        await Refuses(password);
    }

    /// <summary>
    /// An empty password is the length rule's business, and answering here as well would produce
    /// two complaints about one mistake.
    /// </summary>
    [Fact]
    public async Task An_Empty_Password_Is_Left_To_The_Length_Rule()
    {
        var result = await Validator.ValidateAsync(Store(), Account(), string.Empty);

        result.Succeeded.Should().BeTrue();
    }

    private static async Task Refuses(string password)
    {
        var result = await Validator.ValidateAsync(Store(), Account(), password);

        result.Succeeded.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.Code.Should().Be(PredictablePasswordValidator.ErrorCode);
    }

    private static ApplicationUser Account() => new()
    {
        Id = Guid.Empty,
        Email = "jane.thornbury@example.com",
        UserName = "jane.thornbury@example.com",
        DisplayName = "Jane Marchetti",
    };

    /// <summary>
    /// The store the validator is handed and never reads.
    /// </summary>
    private static UserManager<ApplicationUser> Store() => null!;
}
