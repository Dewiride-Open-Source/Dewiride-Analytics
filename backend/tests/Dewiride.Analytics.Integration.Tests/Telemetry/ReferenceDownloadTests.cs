using System.Net;
using Dewiride.Analytics.Infrastructure.Crawlers;
using Dewiride.Analytics.Infrastructure.Network;
using Dewiride.Analytics.Integration.Tests.Fixtures;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;

namespace Dewiride.Analytics.Integration.Tests.Telemetry;

/// <summary>
/// Covers the background downloads of the data that says where an address is and whose crawlers it
/// belongs to, over the two things that are not about the data at all: how long they are told to wait
/// between downloads, and the host stopping in the middle of one.
/// </summary>
/// <param name="stack">The running stack.</param>
[Collection(SharedStackDefinition.Name)]
public sealed class ReferenceDownloadTests(AnalyticsStackFixture stack) : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("dewiride-reference-").FullName;

    /// <summary>
    /// A timer waits a little over 49 days at most, and a service asked to wait longer fails as it
    /// starts and takes the engine with it. So the setting is refused with a sentence naming it.
    /// </summary>
    /// <param name="setting">The setting.</param>
    [Theory]
    [InlineData("Dewiride:ReferenceData:RefreshInterval")]
    [InlineData("Dewiride:CrawlerRanges:RefreshInterval")]
    public void An_Installation_Does_Not_Start_With_A_Wait_No_Timer_Can_Keep(string setting)
    {
        HostRefusal.Of(() => stack.WithWebHostBuilder(builder => builder.UseSetting(setting, "60.00:00:00")))
            .Should().BeOfType<OptionsValidationException>()
            .Which.Message.Should().Contain(setting);
    }

    /// <summary>
    /// A first start spends minutes on this download. Stopping meanwhile says nothing about the
    /// download, so nothing is said of it, the release before is not asked for, and the half of the
    /// file that arrived is not left behind under a name no later download would write over. That
    /// holds whether the download in hand ends as cancelled or as a connection that went with the
    /// machine.
    /// </summary>
    /// <param name="ending">How the download in hand ends once the host is stopping.</param>
    [Theory]
    [InlineData(HowItEnds.Cancelled)]
    [InlineData(HowItEnds.ResetWhileReading)]
    [InlineData(HowItEnds.ResetBeforeAnswering)]
    public async Task Stopping_During_A_Place_Download_Says_Nothing_Of_It_And_Leaves_No_Part_Behind(HowItEnds ending)
    {
        var source = new SlowWebsite(ending);
        var logger = new FakeLogger<ReferenceDataRefresher>();
        var settings = Options.Create(new ReferenceDataOptions { Directory = _directory });

        using var store = new ReferenceDataStore();
        using var service = new ReferenceDataRefresher(store, settings, source, TimeProvider.System, logger);

        await service.StartAsync(Cancellation.Token);
        await source.Reached.WaitAsync(TimeSpan.FromSeconds(30), Cancellation.Token);
        await service.StopAsync(Cancellation.Token);

        source.Asked.Should().ContainSingle();
        logger.Collector.GetSnapshot().Should().NotContain(record => record.Level >= LogLevel.Warning);
        Directory.GetFiles(_directory, "*.part", SearchOption.AllDirectories).Should().BeEmpty();
    }

    /// <summary>
    /// The same for the lists of crawler addresses: stopping is not one more company's list that
    /// could not be fetched, however the fetch in hand ends.
    /// </summary>
    /// <param name="ending">How the fetch in hand ends once the host is stopping.</param>
    [Theory]
    [InlineData(HowItEnds.Cancelled)]
    [InlineData(HowItEnds.ResetWhileReading)]
    [InlineData(HowItEnds.ResetBeforeAnswering)]
    public async Task Stopping_During_A_Crawler_List_Download_Says_Nothing_Of_It(HowItEnds ending)
    {
        var source = new SlowWebsite(ending);
        var logger = new FakeLogger<CrawlerRangeRefresher>();
        var settings = Options.Create(new CrawlerRangeOptions { Directory = _directory });

        using var service = new CrawlerRangeRefresher(new CrawlerRangeStore(), settings, source, TimeProvider.System, logger);

        await service.StartAsync(Cancellation.Token);
        await source.Reached.WaitAsync(TimeSpan.FromSeconds(30), Cancellation.Token);
        await service.StopAsync(Cancellation.Token);

        source.Asked.Should().ContainSingle();
        logger.Collector.GetSnapshot().Should().NotContain(record => record.Level >= LogLevel.Warning);
        Directory.GetFiles(_directory, "*.part", SearchOption.AllDirectories).Should().BeEmpty();
    }

    /// <inheritdoc />
    public void Dispose() => Directory.Delete(_directory, recursive: true);

    /// <summary>
    /// How a download in hand ends once whoever asked for it stops waiting.
    /// </summary>
    public enum HowItEnds
    {
        /// <summary>As a cancellation, which is what stopping is.</summary>
        Cancelled,

        /// <summary>
        /// As a connection reset while the answer was being read. Where the HTTP client reads the
        /// answer itself it reports this as a cancellation; where the caller reads it, it arrives as
        /// it is.
        /// </summary>
        ResetWhileReading,

        /// <summary>As a connection reset before any answer began, which arrives as it is.</summary>
        ResetBeforeAnswering,
    }

    /// <summary>
    /// A website that takes a request and then goes no further, for as long as anybody waits: either
    /// before answering at all, or after the first of its answer.
    /// </summary>
    /// <param name="ending">How it ends once whoever asked stops waiting.</param>
    private sealed class SlowWebsite(HowItEnds ending) : HttpMessageHandler, IHttpClientFactory
    {
        private readonly TaskCompletionSource _reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly List<Uri> _asked = [];

        /// <summary>Completes once the download is under way.</summary>
        public Task Reached => _reached.Task;

        /// <summary>Everything it was asked for.</summary>
        public IReadOnlyList<Uri> Asked => _asked;

        /// <inheritdoc />
        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);

        /// <inheritdoc />
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _asked.Add(request.RequestUri!);

            if (ending == HowItEnds.ResetBeforeAnswering)
            {
                _reached.TrySetResult();

                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw new IOException("The connection was reset.");
                }
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new Dripping(_reached, ending == HowItEnds.ResetWhileReading)),
            };
        }

        /// <summary>A body that hands over its first bytes and then waits to be cancelled.</summary>
        /// <param name="reached">Said once the first bytes have gone.</param>
        /// <param name="resets">Whether being cancelled is reported as a reset connection.</param>
        private sealed class Dripping(TaskCompletionSource reached, bool resets) : MemoryStream
        {
            private bool _began;

            /// <inheritdoc />
            public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                if (!_began)
                {
                    _began = true;
                    buffer.Span[0] = 0x1f;

                    return 1;
                }

                reached.TrySetResult();

                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (resets)
                {
                    throw new IOException("The connection was reset.");
                }

                return 0;
            }
        }
    }
}
