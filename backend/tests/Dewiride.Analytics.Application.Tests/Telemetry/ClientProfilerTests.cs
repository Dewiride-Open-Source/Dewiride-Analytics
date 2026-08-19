using Dewiride.Analytics.Application.Telemetry;
using Dewiride.Analytics.Domain.Telemetry;

namespace Dewiride.Analytics.Application.Tests.Telemetry;

/// <summary>
/// What the profiler makes of what clients say about themselves.
/// </summary>
/// <remarks>
/// Every string below is a real user agent, copied rather than composed. The whole of this
/// component is an ordered set of substring tests, so the only thing that can be wrong with it is
/// the order — and the only way to find that out is to run it against strings that actually occur.
/// </remarks>
public sealed class ClientProfilerTests
{
    private const string WindowsChrome =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) "
        + "Chrome/131.0.0.0 Safari/537.36";

    private const string MacSafari =
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) "
        + "Version/17.1 Safari/605.1.15";

    private const string IPhoneSafari =
        "Mozilla/5.0 (iPhone; CPU iPhone OS 17_1 like Mac OS X) AppleWebKit/605.1.15 "
        + "(KHTML, like Gecko) Version/17.1 Mobile/15E148 Safari/604.1";

    private const string IPadSafari =
        "Mozilla/5.0 (iPad; CPU OS 17_1 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) "
        + "Version/17.1 Mobile/15E148 Safari/604.1";

    private const string AndroidPhoneChrome =
        "Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 (KHTML, like Gecko) "
        + "Chrome/131.0.0.0 Mobile Safari/537.36";

    private const string AndroidTabletChrome =
        "Mozilla/5.0 (Linux; Android 13; SM-X700) AppleWebKit/537.36 (KHTML, like Gecko) "
        + "Chrome/131.0.0.0 Safari/537.36";

    private const string WindowsFirefox =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:133.0) Gecko/20100101 Firefox/133.0";

    private const string WindowsEdge =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) "
        + "Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0";

    private const string SamsungPhone =
        "Mozilla/5.0 (Linux; Android 13; SAMSUNG SM-S918B) AppleWebKit/537.36 (KHTML, like Gecko) "
        + "SamsungBrowser/23.0 Chrome/115.0.0.0 Mobile Safari/537.36";

    private const string ChromeBook =
        "Mozilla/5.0 (X11; CrOS x86_64 14541.0.0) AppleWebKit/537.36 (KHTML, like Gecko) "
        + "Chrome/131.0.0.0 Safari/537.36";

    [Theory]
    [InlineData(WindowsChrome, DeviceClass.Desktop, "Chrome", "Windows")]
    [InlineData(MacSafari, DeviceClass.Desktop, "Safari", "macOS")]
    [InlineData(IPhoneSafari, DeviceClass.Phone, "Safari", "iOS")]
    [InlineData(IPadSafari, DeviceClass.Tablet, "Safari", "iPadOS")]
    [InlineData(AndroidPhoneChrome, DeviceClass.Phone, "Chrome", "Android")]
    [InlineData(AndroidTabletChrome, DeviceClass.Tablet, "Chrome", "Android")]
    [InlineData(WindowsFirefox, DeviceClass.Desktop, "Firefox", "Windows")]
    [InlineData(WindowsEdge, DeviceClass.Desktop, "Edge", "Windows")]
    [InlineData(SamsungPhone, DeviceClass.Phone, "Samsung Internet", "Android")]
    [InlineData(ChromeBook, DeviceClass.Desktop, "Chrome", "ChromeOS")]
    public void A_Real_Browser_Is_Recognised(
        string userAgent,
        DeviceClass device,
        string browser,
        string system)
    {
        var profile = ClientProfiler.Profile(userAgent, ClientHints.None);

        profile.Device.Should().Be(device);
        profile.BrowserFamily.Should().Be(browser);
        profile.OperatingSystem.Should().Be(system);
    }

    /// <summary>
    /// Nearly every browser claims to be several others further along its string. Reading the
    /// last claim rather than the most specific one would file most of the web as Safari.
    /// </summary>
    [Theory]
    [InlineData(WindowsEdge, "Edge")]
    [InlineData(WindowsChrome, "Chrome")]
    [InlineData(SamsungPhone, "Samsung Internet")]
    public void The_Most_Specific_Claim_In_A_String_Wins(string userAgent, string expected)
    {
        ClientProfiler.Profile(userAgent, ClientHints.None).BrowserFamily.Should().Be(expected);
    }

    /// <summary>
    /// Android says <c>Mobile</c> on the device you can hold in a hand and says nothing on the
    /// one you cannot — an inversion of the obvious reading, and the reason a tablet cannot be
    /// found by looking for a word meaning tablet.
    /// </summary>
    [Fact]
    public void An_Android_Tablet_Is_Told_From_An_Android_Phone_By_What_It_Does_Not_Say()
    {
        ClientProfiler.Profile(AndroidTabletChrome, ClientHints.None).Device
            .Should().Be(DeviceClass.Tablet);
        ClientProfiler.Profile(AndroidPhoneChrome, ClientHints.None).Device
            .Should().Be(DeviceClass.Phone);
    }

    /// <summary>
    /// A browser answering the question directly is a better source than a string kept for the
    /// benefit of websites written twenty years ago.
    /// </summary>
    [Fact]
    public void What_A_Browser_States_Outright_Is_Preferred_To_What_It_Implies()
    {
        var hints = new ClientHints
        {
            Mobile = true,
            Platform = "\"Android\"",
            Brands = "\"Chromium\";v=\"131\", \"Google Chrome\";v=\"131\", \"Not?A_Brand\";v=\"24\"",
        };

        var profile = ClientProfiler.Profile(WindowsChrome, hints);

        profile.Device.Should().Be(DeviceClass.Phone);
        profile.OperatingSystem.Should().Be("Android");
        profile.BrowserFamily.Should().Be("Chrome");
    }

    /// <summary>
    /// The brand list holds the browser's real name, the engine it is built on, and an entry that
    /// exists only to stop anyone assuming the shape is fixed. The real name is the answer.
    /// </summary>
    [Theory]
    [InlineData("\"Chromium\";v=\"131\", \"Microsoft Edge\";v=\"131\", \"Not_A Brand\";v=\"24\"", "Edge")]
    [InlineData("\"Chromium\";v=\"131\", \"Brave\";v=\"131\", \"Not.A/Brand\";v=\"24\"", "Brave")]
    [InlineData("\"Chromium\";v=\"131\", \"Not;A=Brand\";v=\"24\"", "Chrome")]
    public void A_Brand_List_Is_Read_For_Its_Most_Specific_Name(string brands, string expected)
    {
        var hints = new ClientHints { Brands = brands };

        ClientProfiler.Profile(null, hints).BrowserFamily.Should().Be(expected);
    }

    /// <summary>
    /// Brave's user agent is byte-identical to Chrome's by design, so the brand list is the only
    /// place it can be recognised at all.
    /// </summary>
    [Fact]
    public void A_Browser_That_Hides_In_Its_String_Is_Still_Found_In_Its_Brands()
    {
        var hints = new ClientHints { Brands = "\"Brave\";v=\"131\", \"Chromium\";v=\"131\"" };

        ClientProfiler.Profile(WindowsChrome, hints).BrowserFamily.Should().Be("Brave");
    }

    /// <summary>
    /// A client saying it is not handheld while naming a tablet is a tablet. That is the one
    /// direction the stated answer is overridden in, because saying "not a phone" is not the same
    /// as saying "a computer".
    /// </summary>
    [Fact]
    public void A_Client_That_Denies_Being_Handheld_And_Names_A_Tablet_Is_A_Tablet()
    {
        var hints = new ClientHints { Mobile = false, Platform = "\"Android\"" };

        ClientProfiler.Profile(AndroidTabletChrome, hints).Device.Should().Be(DeviceClass.Tablet);
    }

    /// <summary>
    /// Only one family of browsers sends these, and only over a secure connection. Their absence
    /// is the ordinary case on the web and says nothing whatever about the visitor.
    /// </summary>
    [Fact]
    public void A_Browser_That_Volunteers_Nothing_Is_Read_From_Its_String_Alone()
    {
        var profile = ClientProfiler.Profile(MacSafari, ClientHints.None);

        profile.Device.Should().Be(DeviceClass.Desktop);
        profile.BrowserFamily.Should().Be("Safari");
        profile.OperatingSystem.Should().Be("macOS");
    }

    /// <summary>
    /// Much of what reaches a website is not a device. Naming one anyway would be inventing the
    /// most-reported fact on the screen.
    /// </summary>
    [Theory]
    [InlineData("curl/8.4.0")]
    [InlineData("python-requests/2.32.3")]
    [InlineData("Mozilla/5.0 (compatible; Googlebot/2.1; +http://www.google.com/bot.html)")]
    [InlineData("")]
    [InlineData(null)]
    public void Something_That_Is_Not_A_Device_Is_Left_Unknown(string? userAgent)
    {
        var profile = ClientProfiler.Profile(userAgent, ClientHints.None);

        profile.Device.Should().Be(DeviceClass.Unknown);
        profile.BrowserFamily.Should().BeEmpty();
    }

    [Theory]
    [InlineData("Mozilla/5.0 (SMART-TV; Linux; Tizen 6.0) AppleWebKit/537.36")]
    [InlineData("Mozilla/5.0 (PlayStation 5/6.00) AppleWebKit/605.1.15")]
    [InlineData("Mozilla/5.0 (Linux; Android 12) AppleWebKit/537.36 CrKey/1.56.500000")]
    public void Something_That_Is_Neither_Carried_Nor_Sat_At_Is_Its_Own_Answer(string userAgent)
    {
        ClientProfiler.Profile(userAgent, ClientHints.None).Device.Should().Be(DeviceClass.Other);
    }

    /// <summary>
    /// The four letters of Chrome OS sit inside the word Microsoft, which appears in far more
    /// user agents than Chrome OS does.
    /// </summary>
    [Fact]
    public void A_System_Name_Hiding_Inside_Another_Word_Is_Not_Matched()
    {
        const string outlook =
            "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 "
            + "Microsoft Outlook 16.0";

        ClientProfiler.Profile(outlook, ClientHints.None).OperatingSystem.Should().Be("macOS");
    }

    /// <summary>
    /// A platform nobody has heard of is a platform the visitor made up. Passing it through would
    /// let one request put a string of its choosing into a column held as a small repeated set.
    /// </summary>
    [Fact]
    public void An_Invented_Platform_Is_Discarded_Rather_Than_Stored()
    {
        var hints = new ClientHints { Platform = "\"'; DROP TABLE events; --\"" };

        var profile = ClientProfiler.Profile(WindowsChrome, hints);

        profile.OperatingSystem.Should().Be("Windows");
    }

    /// <summary>
    /// The searches are linear, so a client could otherwise decide how long each one takes by
    /// sending a header of arbitrary length.
    /// </summary>
    [Fact]
    public void A_Claim_Longer_Than_Any_Real_One_Is_Only_Read_So_Far()
    {
        var padded = new string('a', 4000) + "Firefox/133.0";

        ClientProfiler.Profile(padded, ClientHints.None).BrowserFamily.Should().BeEmpty();
    }

    /// <summary>
    /// A header a browser sends holds a token it chose from a fixed list, and it arrives quoted
    /// as the structured-header format requires.
    /// </summary>
    [Theory]
    [InlineData("\"Windows\"", "Windows")]
    [InlineData("\"macOS\"", "macOS")]
    [InlineData("\"Chrome OS\"", "ChromeOS")]
    [InlineData("\"iOS\"", "iOS")]
    [InlineData("\"Unknown\"", "")]
    public void A_Stated_Platform_Is_Read_Through_The_Catalogue(string platform, string expected)
    {
        var hints = new ClientHints { Platform = platform };

        ClientProfiler.Profile(null, hints).OperatingSystem.Should().Be(expected);
    }
}
