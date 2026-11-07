using Dewiride.Analytics.Infrastructure.Posts;
using Dewiride.Analytics.Integration.Tests.Fixtures;
using Microsoft.Extensions.Time.Testing;

namespace Dewiride.Analytics.Integration.Tests.Posts;

/// <summary>
/// Covers how often a website is asked for anything.
/// </summary>
/// <remarks>
/// A waiter is woken inline by the clock being moved, or an instant afterwards, so these tests wait in
/// real time for it to have happened, and never block anything the clock runs.
/// </remarks>
public sealed class PacerTests
{
    private static readonly TimeSpan OneSecond = TimeSpan.FromSeconds(1);

    private readonly FakeTimeProvider _clock = new();

    [Fact]
    public async Task A_First_Request_Goes_At_Once()
    {
        var pacer = new Pacer(_clock, OneSecond);

        await pacer.WaitTurnAsync("blog.example.com", Cancellation.Token).WaitAsync(RealTime, Cancellation.Token);
    }

    [Fact]
    public async Task A_Second_Request_Waits_Until_A_Second_Has_Passed()
    {
        var pacer = new Pacer(_clock, OneSecond);

        await pacer.WaitTurnAsync("blog.example.com", Cancellation.Token);

        var second = pacer.WaitTurnAsync("blog.example.com", Cancellation.Token);

        second.IsCompleted.Should().BeFalse();

        _clock.Advance(TimeSpan.FromMilliseconds(999));
        second.IsCompleted.Should().BeFalse();

        _clock.Advance(TimeSpan.FromMilliseconds(1));
        await second.WaitAsync(RealTime, Cancellation.Token);
    }

    /// <summary>
    /// Each request booked has its own turn, however many readers have come to the same website, so
    /// three together are spread over two seconds and not sent together when the first second is up.
    /// </summary>
    [Fact]
    public async Task Requests_Waiting_Together_Go_One_At_A_Time()
    {
        var pacer = new Pacer(_clock, OneSecond);

        await pacer.WaitTurnAsync("blog.example.com", Cancellation.Token);

        var second = pacer.WaitTurnAsync("blog.example.com", Cancellation.Token);
        var third = pacer.WaitTurnAsync("blog.example.com", Cancellation.Token);

        _clock.Advance(OneSecond);
        await second.WaitAsync(RealTime, Cancellation.Token);
        third.IsCompleted.Should().BeFalse();

        _clock.Advance(OneSecond);
        await third.WaitAsync(RealTime, Cancellation.Token);
    }

    [Fact]
    public async Task A_Request_Long_After_The_Last_Goes_At_Once()
    {
        var pacer = new Pacer(_clock, OneSecond);

        await pacer.WaitTurnAsync("blog.example.com", Cancellation.Token);

        _clock.Advance(TimeSpan.FromSeconds(30));

        pacer.WaitTurnAsync("blog.example.com", Cancellation.Token).IsCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task Another_Website_Has_A_Turn_Of_Its_Own()
    {
        var pacer = new Pacer(_clock, OneSecond);

        await pacer.WaitTurnAsync("blog.example.com", Cancellation.Token);

        pacer.WaitTurnAsync("shop.example.com", Cancellation.Token).IsCompleted.Should().BeTrue();
    }

    /// <summary>
    /// A website at both names is one server. Two readings that named the two different names would
    /// otherwise ask it twice a second between them.
    /// </summary>
    /// <param name="first">The name the first request goes to.</param>
    /// <param name="second">The name the second goes to.</param>
    [Theory]
    [InlineData("example.com", "www.example.com")]
    [InlineData("www.example.com", "example.com")]
    [InlineData("example.com", "EXAMPLE.COM")]
    [InlineData("example.com", "example.com.")]
    public async Task A_Website_And_Its_Twin_Share_A_Turn(string first, string second)
    {
        var pacer = new Pacer(_clock, OneSecond);

        await pacer.WaitTurnAsync(first, Cancellation.Token);

        pacer.WaitTurnAsync(second, Cancellation.Token).IsCompleted.Should().BeFalse();

        _clock.Advance(OneSecond);
    }

    [Fact]
    public async Task Whoever_Stops_Waiting_Stops_Waiting()
    {
        var pacer = new Pacer(_clock, OneSecond);
        using var stopping = new CancellationTokenSource();

        await pacer.WaitTurnAsync("blog.example.com", Cancellation.Token);

        var second = pacer.WaitTurnAsync("blog.example.com", stopping.Token);

        await stopping.CancelAsync();

        await second.Invoking(waiting => waiting.WaitAsync(RealTime, Cancellation.Token))
            .Should().ThrowAsync<OperationCanceledException>();
    }

    /// <summary>
    /// The table is tidied of whatever nothing is waiting behind, so a long-running engine that has
    /// seen a great many websites does not hold on to them, and a website still being waited for is not
    /// forgotten by the tidying.
    /// </summary>
    [Fact]
    public async Task Websites_No_Longer_Being_Asked_Are_Forgotten_And_Those_Still_Being_Asked_Are_Not()
    {
        var pacer = new Pacer(_clock, OneSecond);

        await pacer.WaitTurnAsync("kept.example.com", Cancellation.Token);

        _clock.Advance(TimeSpan.FromMilliseconds(500));

        for (var website = 0; website < 5000; website++)
        {
            await pacer.WaitTurnAsync($"site-{website}.example.com", Cancellation.Token);

            _clock.Advance(TimeSpan.FromTicks(1));
        }

        pacer.WaitTurnAsync("kept.example.com", Cancellation.Token).IsCompleted.Should().BeFalse();

        _clock.Advance(OneSecond);
    }

    private static TimeSpan RealTime => TimeSpan.FromSeconds(10);
}
