using System.Net;
using Dewiride.Analytics.Infrastructure.Posts;

namespace Dewiride.Analytics.Integration.Tests.Posts;

/// <summary>
/// Covers what counts as the public internet, which is everywhere a website's pages may be read from.
/// </summary>
/// <remarks>
/// Here rather than with the unit suites for the same reason the network tables are: the blocks are
/// somebody else's, and what is worth pinning is where each one begins and ends. The first and last
/// address of every block are refused below, and the addresses either side of each are permitted
/// further down, so a mask written one bit wrong fails in one list or the other.
/// </remarks>
public sealed class PublicAddressTests
{
    [Theory]
    // "This network".
    [InlineData("0.0.0.0")]
    [InlineData("0.255.255.255")]
    // Private use.
    [InlineData("10.0.0.0")]
    [InlineData("10.255.255.255")]
    // Shared between the customers of one carrier, where Alibaba's metadata answers.
    [InlineData("100.64.0.0")]
    [InlineData("100.100.100.200")]
    [InlineData("100.127.255.255")]
    // This machine.
    [InlineData("127.0.0.0")]
    [InlineData("127.0.0.1")]
    [InlineData("127.255.255.255")]
    // Link local, where most clouds' metadata answers.
    [InlineData("169.254.0.0")]
    [InlineData("169.254.169.254")]
    [InlineData("169.254.255.255")]
    // Private use.
    [InlineData("172.16.0.0")]
    [InlineData("172.31.255.255")]
    // Protocol assignments, where Oracle's metadata also answers.
    [InlineData("192.0.0.0")]
    [InlineData("192.0.0.192")]
    [InlineData("192.0.0.255")]
    // Documentation.
    [InlineData("192.0.2.0")]
    [InlineData("192.0.2.255")]
    // AS112.
    [InlineData("192.31.196.0")]
    [InlineData("192.31.196.255")]
    // Automatic multicast tunnelling.
    [InlineData("192.52.193.0")]
    [InlineData("192.52.193.255")]
    // The relay of a withdrawn tunnelling scheme.
    [InlineData("192.88.99.0")]
    [InlineData("192.88.99.255")]
    // Private use.
    [InlineData("192.168.0.0")]
    [InlineData("192.168.255.255")]
    // AS112.
    [InlineData("192.175.48.0")]
    [InlineData("192.175.48.255")]
    // Benchmarking.
    [InlineData("198.18.0.0")]
    [InlineData("198.19.255.255")]
    // Documentation.
    [InlineData("198.51.100.0")]
    [InlineData("198.51.100.255")]
    [InlineData("203.0.113.0")]
    [InlineData("203.0.113.255")]
    // Multicast.
    [InlineData("224.0.0.0")]
    [InlineData("239.255.255.255")]
    // Reserved, through to the limited broadcast address.
    [InlineData("240.0.0.0")]
    [InlineData("255.255.255.255")]
    // Looks public, and is how every Azure machine reaches the platform.
    [InlineData("168.63.129.16")]
    // An older address written in the newer family's form is the same address.
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("::ffff:10.0.0.1")]
    [InlineData("::ffff:169.254.169.254")]
    [InlineData("::ffff:168.63.129.16")]
    [InlineData("64:ff9b::7f00:1")]
    [InlineData("64:ff9b::a00:1")]
    [InlineData("64:ff9b::a9fe:a9fe")]
    [InlineData("64:ff9b::a83f:8110")]
    // The newer family's own unspecified and loopback addresses.
    [InlineData("::")]
    [InlineData("::1")]
    // The deprecated way of carrying an older address, which is no way to reach a public one.
    [InlineData("::8.8.8.8")]
    // Below the one block handed out.
    [InlineData("1fff:ffff:ffff:ffff:ffff:ffff:ffff:ffff")]
    // Translation for one network's own use.
    [InlineData("64:ff9b:1::1")]
    // Thrown away, and dummy.
    [InlineData("100::")]
    [InlineData("100:0:0:1::1")]
    // Protocol assignments, tunnelling through a third party among them.
    [InlineData("2001::")]
    [InlineData("2001:1ff:ffff:ffff:ffff:ffff:ffff:ffff")]
    // Documentation.
    [InlineData("2001:db8::")]
    [InlineData("2001:db8:ffff:ffff:ffff:ffff:ffff:ffff")]
    // Tunnelling to an address of the older family.
    [InlineData("2002::")]
    [InlineData("2002:7f00:1::")]
    [InlineData("2002:ffff:ffff:ffff:ffff:ffff:ffff:ffff")]
    // AS112.
    [InlineData("2620:4f:8000::")]
    [InlineData("2620:4f:8000:ffff:ffff:ffff:ffff:ffff")]
    // Documentation.
    [InlineData("3fff::")]
    [InlineData("3fff:fff:ffff:ffff:ffff:ffff:ffff:ffff")]
    // Above the one block handed out.
    [InlineData("4000::")]
    [InlineData("5f00::1")]
    // Private use, where AWS's metadata answers.
    [InlineData("fc00::")]
    [InlineData("fd00:ec2::254")]
    [InlineData("fdff:ffff:ffff:ffff:ffff:ffff:ffff:ffff")]
    // Link local, and what site local used to be.
    [InlineData("fe80::")]
    [InlineData("febf:ffff:ffff:ffff:ffff:ffff:ffff:ffff")]
    [InlineData("fec0::1")]
    // Multicast.
    [InlineData("ff00::")]
    [InlineData("ffff:ffff:ffff:ffff:ffff:ffff:ffff:ffff")]
    public void Every_Address_Set_Aside_From_The_Public_Internet_Is_Refused(string address)
    {
        PublicAddress.IsPublic(IPAddress.Parse(address)).Should().BeFalse();
    }

    [Theory]
    [InlineData("1.0.0.0")]
    [InlineData("8.8.8.8")]
    [InlineData("9.255.255.255")]
    [InlineData("11.0.0.0")]
    [InlineData("100.63.255.255")]
    [InlineData("100.128.0.0")]
    [InlineData("126.255.255.255")]
    [InlineData("128.0.0.0")]
    [InlineData("168.63.129.15")]
    [InlineData("168.63.129.17")]
    [InlineData("169.253.255.255")]
    [InlineData("169.255.0.0")]
    [InlineData("172.15.255.255")]
    [InlineData("172.32.0.0")]
    [InlineData("191.255.255.255")]
    [InlineData("192.0.1.255")]
    [InlineData("192.0.3.0")]
    [InlineData("192.31.195.255")]
    [InlineData("192.31.197.0")]
    [InlineData("192.52.192.255")]
    [InlineData("192.52.194.0")]
    [InlineData("192.88.98.255")]
    [InlineData("192.88.100.0")]
    [InlineData("192.167.255.255")]
    [InlineData("192.169.0.0")]
    [InlineData("192.175.47.255")]
    [InlineData("192.175.49.0")]
    [InlineData("198.17.255.255")]
    [InlineData("198.20.0.0")]
    [InlineData("198.51.99.255")]
    [InlineData("198.51.101.0")]
    [InlineData("203.0.112.255")]
    [InlineData("203.0.114.0")]
    [InlineData("223.255.255.255")]
    [InlineData("::ffff:8.8.8.8")]
    [InlineData("64:ff9b::808:808")]
    [InlineData("2000::")]
    [InlineData("2001:200::")]
    [InlineData("2001:db7:ffff:ffff:ffff:ffff:ffff:ffff")]
    [InlineData("2001:db9::")]
    [InlineData("2001:4860:4860::8888")]
    [InlineData("2003::")]
    [InlineData("2606:4700:4700::1111")]
    [InlineData("2620:4f:7fff:ffff:ffff:ffff:ffff:ffff")]
    [InlineData("2620:4f:8001::")]
    [InlineData("3ffe:ffff:ffff:ffff:ffff:ffff:ffff:ffff")]
    [InlineData("3fff:1000::")]
    public void Addresses_On_The_Public_Internet_Are_Permitted(string address)
    {
        PublicAddress.IsPublic(IPAddress.Parse(address)).Should().BeTrue();
    }
}
