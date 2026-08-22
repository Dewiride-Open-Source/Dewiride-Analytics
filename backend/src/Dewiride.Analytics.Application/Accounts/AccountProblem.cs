namespace Dewiride.Analytics.Application.Accounts;

/// <summary>
/// One reason something somebody typed about their account could not be used.
/// </summary>
/// <remarks>
/// The code is stable and safe to switch on; the sentence is English and fit to show somebody.
/// The interface looks the code up in its own message catalogue and falls back to the sentence
/// when it has no wording of its own — so a reason this product has never had words for is still
/// explained rather than hidden behind something generic.
/// </remarks>
/// <param name="Code">Stable identifier for the reason.</param>
/// <param name="Description">A sentence describing it, in English.</param>
public sealed record AccountProblem(string Code, string Description);
