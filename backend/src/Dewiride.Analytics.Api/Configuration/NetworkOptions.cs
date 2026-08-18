namespace Dewiride.Analytics.Api.Configuration;

/// <summary>
/// Which upstream hops may be believed about a visitor's network address.
/// </summary>
/// <remarks>
/// <para>
/// The address a request appears to come from decides the visitor key, the country, the network
/// owner and whether the traffic looks like a datacenter — so believing a forwarded address that
/// anyone can write is not a misconfiguration, it is a way to make the product report fiction.
/// </para>
/// <para>
/// Nothing is trusted unless it is named here. With no entries the forwarded headers are not read
/// at all and the address is the one the connection actually came from, which is correct for a
/// stack with no proxy in front of it. Behind a load balancer or a CDN, name it: the deployment
/// documentation lists the address ranges for each.
/// </para>
/// </remarks>
public sealed class NetworkOptions
{
    /// <summary>Configuration section these options are bound from.</summary>
    public const string SectionName = "Dewiride:Network";

    /// <summary>Individual proxy addresses whose forwarded headers are believed.</summary>
    public IReadOnlyList<string> TrustedProxies { get; init; } = [];

    /// <summary>Proxy address ranges, in CIDR form, whose forwarded headers are believed.</summary>
    public IReadOnlyList<string> TrustedNetworks { get; init; } = [];

    /// <summary>
    /// How many forwarded entries to walk back through, counting from the connection inwards.
    /// </summary>
    /// <remarks>
    /// One per trusted hop and no more. Walking further reaches entries written by whoever sent
    /// the request, which is the point at which a forwarded address stops being evidence.
    /// </remarks>
    public int ForwardLimit { get; init; } = 1;

    /// <summary>Whether any upstream hop has been declared trustworthy.</summary>
    public bool HasTrustedHops => TrustedProxies.Count > 0 || TrustedNetworks.Count > 0;
}
