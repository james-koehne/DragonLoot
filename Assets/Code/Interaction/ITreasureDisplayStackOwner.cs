using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Display owners that keep vertical slot stacks (e.g. mixed table) so pickup can
/// take from the clicked item upward, capacity-limited from the top — same as ground piles.
/// </summary>
public interface ITreasureDisplayStackOwner : ITreasureOwner
{
	/// <summary>
	/// Fills <paramref name="results"/> bottom-to-top from the aimed coin (or <paramref name="selected"/>)
	/// through every item stacked above it in the same slot. Returns false if selected is not owned here.
	/// </summary>
	bool TryCollectPickupColumn(
		TreasureItem selected,
		List<TreasureItem> results,
		Ray aimRay,
		bool hasAimRay );
}
