namespace Dewiride.Analytics.Architecture.Tests;

/// <summary>
/// Asserts that nothing compiled into the product can take the reader of a website's pages anywhere
/// but the public internet.
/// </summary>
/// <remarks>
/// A website's owner chooses what its name resolves to, so the check of where a connection may go is
/// all that stands between naming a sitemap and reaching the engine's own network. That check has a
/// seam, because the tests have to read from a website on this machine. What keeps the seam from being
/// a way round it is that no product assembly holds anything else to put there, and nothing can be
/// configured to.
/// </remarks>
[Trait("Category", "NetworkBoundary")]
public sealed class NetworkBoundaryTests
{
    private const string Port = "Dewiride.Analytics.Infrastructure.Posts.IPublicEndpoints";
    private const string TheOneAnswer = "Dewiride.Analytics.Infrastructure.Posts.PublicInternetEndpoints";

    [Fact]
    public void Only_The_Public_Internet_Is_Reached_From_A_Websites_Pages()
    {
        var port = Product.Assembly(Product.Infrastructure).GetType(Port, throwOnError: true)!;

        var answers = Product.Assemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type is { IsClass: true, IsAbstract: false } && port.IsAssignableFrom(type))
            .Select(type => type.FullName);

        answers.Should().Equal(TheOneAnswer);
    }

    /// <summary>
    /// The one thing it takes is a name server. A setting able to widen where a connection may go would
    /// also be a setting able to point the hosted service at its own network.
    /// </summary>
    [Fact]
    public void The_Check_Takes_No_Settings()
    {
        var answer = Product.Assembly(Product.Infrastructure).GetType(TheOneAnswer, throwOnError: true)!;

        var parameters = answer.GetConstructors().Should().ContainSingle().Which.GetParameters();

        parameters.Should().ContainSingle()
            .Which.ParameterType.Should().Be<Func<string, CancellationToken, Task<System.Net.IPAddress[]>>>();
    }
}
