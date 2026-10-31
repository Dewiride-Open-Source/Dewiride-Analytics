using Dewiride.Analytics.Classification;

namespace Dewiride.Analytics.Application.Analytics;

/// <summary>
/// How many visits one kind of verdict covered over a stretch, and the pages they read.
/// </summary>
/// <param name="Category">What the visits were concluded to be.</param>
/// <param name="Visits">How many visits.</param>
/// <param name="PageViews">How many pages those visits read.</param>
public readonly record struct VerdictTally(TrafficCategory Category, long Visits, long PageViews);
