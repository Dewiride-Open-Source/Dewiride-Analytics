namespace Dewiride.Analytics.Infrastructure.ClickHouse.Analytics;

/// <summary>
/// Names the network a visit arrived over.
/// </summary>
/// <remarks>
/// <para>
/// One company runs many networks and a registry publishes each of them under its own handle, so a
/// list keyed on what the registry says divides that company across a dozen rows and shows a
/// reader "ALIBABA-CN-NET Alibaba US Technology Co., Ltd." where "Alibaba Cloud" is what they were
/// looking for. A routing number the catalogue recognises is given the operator's name; one it does
/// not keeps the registry's description with the handle in front of it dropped, which is the
/// readable half of what a registry publishes.
/// </para>
/// <para>
/// Written once here because three statements need it: the one that ranks the networks a window's
/// visitors arrived over, the one that rebuilds what each visit was so a reader can narrow to one
/// of them, and the one that gives the account a single opened visit shows of itself. A network is
/// therefore named identically wherever it is shown, which is what makes picking one off a card,
/// picking it off a filter and reading it inside a visit all name the same company.
/// </para>
/// <para>
/// The catalogue is bound by the caller as two parallel arrays rather than written into the
/// statement, on the same terms as every other value these statements depend on.
/// </para>
/// </remarks>
internal static class NamedNetworks
{
    /// <summary>
    /// Writes the naming, over a selection carrying <c>autonomous_system</c> and
    /// <c>network_owner</c>.
    /// </summary>
    /// <param name="indent">
    /// The column the calling statement places the expression at, which its own continuation lines
    /// are laid out against. The approved statements beside these compilers are read by people
    /// deciding whether a change to them was intended, and a fragment that keeps one statement's
    /// indentation wherever it is dropped makes the rest of them ragged.
    /// </param>
    /// <returns>The expression, ready to place at that column.</returns>
    public static string From(int indent)
    {
        var pad = new string(' ', indent);

        return $$"""
            transform(
            {{pad}}    autonomous_system,
            {{pad}}    {hosting_numbers:Array(UInt32)},
            {{pad}}    {hosting_names:Array(String)},
            {{pad}}    if(
            {{pad}}        position(network_owner, ' ') > 0,
            {{pad}}        substring(network_owner, position(network_owner, ' ') + 1),
            {{pad}}        network_owner))
            """;
    }
}
