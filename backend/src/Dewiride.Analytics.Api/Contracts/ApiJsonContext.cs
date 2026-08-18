using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dewiride.Analytics.Api.Contracts;

/// <summary>
/// Serialisation plan for the API's payloads, generated at compile time.
/// </summary>
/// <remarks>
/// Source-generated rather than reflection-based: the collector deserialises on the busiest path
/// in the product, and this removes the per-request reflection and the start-up cost of building
/// a contract for a type whose shape is already known.
/// </remarks>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = false,
    NumberHandling = JsonNumberHandling.Strict,
    ReadCommentHandling = JsonCommentHandling.Disallow,
    AllowTrailingCommas = false)]
[JsonSerializable(typeof(CollectRequest))]
[JsonSerializable(typeof(ServerCollectRequest))]
[JsonSerializable(typeof(ServerCollectResponse))]
[JsonSerializable(typeof(CreateServerKeyRequest))]
[JsonSerializable(typeof(IssuedServerKey))]
[JsonSerializable(typeof(IReadOnlyList<ServerKeySummary>))]
[JsonSerializable(typeof(SignInRequest))]
[JsonSerializable(typeof(SetupRequest))]
[JsonSerializable(typeof(SessionResponse))]
[JsonSerializable(typeof(SetupResponse))]
[JsonSerializable(typeof(IReadOnlyList<SiteSummary>))]
[JsonSerializable(typeof(OverviewResponse))]
[JsonSerializable(typeof(SeriesResponse))]
[JsonSerializable(typeof(TrafficResponse))]
[JsonSerializable(typeof(VisitsResponse))]
public sealed partial class ApiJsonContext : JsonSerializerContext;
