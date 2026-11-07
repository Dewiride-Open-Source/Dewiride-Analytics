using System.Net;
using System.Net.Sockets;
using Dewiride.Analytics.Infrastructure.Posts;
using Dewiride.Analytics.Integration.Tests.Fixtures;

namespace Dewiride.Analytics.Integration.Tests.Posts;

/// <summary>
/// Covers where a connection to a website may go, against a name server that says whatever a test
/// tells it to.
/// </summary>
public sealed class PublicInternetEndpointsTests
{
    /// <summary>
    /// Nobody's website answers with both. One public address beside a private one is how somebody
    /// finds out whether only the first answer gets checked, so the whole name goes.
    /// </summary>
    /// <param name="answers">What the name server answers with.</param>
    [Theory]
    [InlineData("10.0.0.1")]
    [InlineData("8.8.8.8", "10.0.0.1")]
    [InlineData("10.0.0.1", "8.8.8.8")]
    [InlineData("2606:4700:4700::1111", "::1")]
    [InlineData("8.8.8.8", "1.1.1.1", "169.254.169.254")]
    public async Task A_Name_Answering_With_Any_Private_Address_Is_Refused_Whole(params string[] answers)
    {
        var endpoints = NameServerAnswering(answers);

        var refusal = await Record.ExceptionAsync(() =>
            endpoints.ResolveAsync(new DnsEndPoint("blog.example.com", 443), Cancellation.Token).AsTask());

        refusal.Should().BeOfType<AddressRefusedException>();
    }

    [Fact]
    public async Task A_Name_That_Resolves_To_Nothing_Is_Unreachable()
    {
        var endpoints = NameServerAnswering();

        var failure = await Record.ExceptionAsync(() =>
            endpoints.ResolveAsync(new DnsEndPoint("blog.example.com", 443), Cancellation.Token).AsTask());

        failure.Should().BeOfType<SocketException>()
            .Which.SocketErrorCode.Should().Be(SocketError.HostNotFound);
    }

    /// <summary>
    /// A website's own server has other things listening on it. Nobody is even asked about the name,
    /// because no answer could make this port one to connect to.
    /// </summary>
    /// <param name="port">The port.</param>
    [Theory]
    [InlineData(22)]
    [InlineData(25)]
    [InlineData(81)]
    [InlineData(5432)]
    [InlineData(8080)]
    [InlineData(8123)]
    [InlineData(8443)]
    public async Task Only_The_Default_Ports_Are_Reached(int port)
    {
        var asked = false;
        var endpoints = new PublicInternetEndpoints((_, _) =>
        {
            asked = true;

            return Task.FromResult<IPAddress[]>([IPAddress.Parse("8.8.8.8")]);
        });

        var refusal = await Record.ExceptionAsync(() =>
            endpoints.ResolveAsync(new DnsEndPoint("blog.example.com", port), Cancellation.Token).AsTask());

        refusal.Should().BeOfType<AddressRefusedException>();
        asked.Should().BeFalse();
    }

    /// <summary>
    /// Connecting by the very addresses that were judged is what leaves a name no second chance to
    /// answer differently.
    /// </summary>
    /// <param name="port">The port.</param>
    [Theory]
    [InlineData(80)]
    [InlineData(443)]
    public async Task A_Name_On_The_Public_Internet_Is_Connected_To_By_What_It_Answered(int port)
    {
        var endpoints = NameServerAnswering("2606:4700:4700::1111", "8.8.8.8");

        var vetted = await endpoints.ResolveAsync(new DnsEndPoint("blog.example.com", port), Cancellation.Token);

        vetted.Should().Equal(
            new IPEndPoint(IPAddress.Parse("2606:4700:4700::1111"), port),
            new IPEndPoint(IPAddress.Parse("8.8.8.8"), port));
    }

    /// <summary>
    /// An address written as a number is judged as it stands, as the second lock behind what decides
    /// whether a website may be read at all. No name server has anything to say about it.
    /// </summary>
    /// <param name="host">The host, as a request would name it.</param>
    /// <param name="permitted">Whether it is one that may be connected to.</param>
    [Theory]
    [InlineData("127.0.0.1", false)]
    [InlineData("::1", false)]
    [InlineData("[::1]", false)]
    [InlineData("169.254.169.254", false)]
    [InlineData("8.8.8.8", true)]
    [InlineData("2606:4700:4700::1111", true)]
    public async Task An_Address_Written_As_A_Number_Is_Judged_As_It_Stands(string host, bool permitted)
    {
        var endpoints = new PublicInternetEndpoints((_, _) =>
            throw new InvalidOperationException("Nobody should have asked a name server."));

        var failure = await Record.ExceptionAsync(() =>
            endpoints.ResolveAsync(new DnsEndPoint(host, 443), Cancellation.Token).AsTask());

        if (permitted)
        {
            failure.Should().BeNull();
        }
        else
        {
            failure.Should().BeOfType<AddressRefusedException>();
        }
    }

    private static PublicInternetEndpoints NameServerAnswering(params string[] answers) =>
        new((_, _) => Task.FromResult(Array.ConvertAll(answers, IPAddress.Parse)));
}
