namespace Dewiride.Analytics.Infrastructure.ClickHouse.Analytics;

/// <summary>
/// A statement ready to send to the telemetry store, with every value bound.
/// </summary>
/// <param name="Sql">The statement text. Contains only compiler-authored text and parameter placeholders.</param>
/// <param name="Parameters">The values the placeholders bind to, in the order they were declared.</param>
/// <remarks>
/// Compilation is separated from execution so that what gets sent to the store can be asserted
/// directly, without a database. That is the point at which a change to the generated SQL becomes
/// visible in review: a golden-file test fails and the new statement has to be read and approved.
/// </remarks>
public sealed record CompiledStatement(string Sql, IReadOnlyList<QueryParameter> Parameters);

/// <summary>
/// One bound value.
/// </summary>
/// <param name="Name">Placeholder name, without braces.</param>
/// <param name="Value">The value to bind.</param>
public readonly record struct QueryParameter(string Name, object Value);
