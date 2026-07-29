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
	/// <paramref name="hasHitWorldY"/> / <paramref name="hitWorldY"/> should be the interact raycast
	/// hit when available so mid-stack aim matches the crosshair (not closest-approach-to-axis).
	/// </summary>
	bool TryCollectPickupColumn(
		TreasureItem selected,
		List<TreasureItem> results,
		Ray aimRay,
		bool hasAimRay,
		bool hasHitWorldY = false,
		float hitWorldY = 0f );
}
