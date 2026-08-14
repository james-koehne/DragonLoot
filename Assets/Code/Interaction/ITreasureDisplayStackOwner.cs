using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Display owners that keep vertical slot stacks (tables / constellation).
/// Coin slots match ground stacks: LMB takes one, hold-E absorbs the slot, hover outlines the pile.
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

	bool TryGetSlotIndex( TreasureItem selected, out int slotIndex );

	int GetSlotCount( int slotIndex );

	void AppendSlotOutlineRenderers( TreasureItem selected, List<Renderer> renderers );

	bool TryConsumeSlotDefinitions(
		int slotIndex,
		List<TreasureDefinition> into,
		out Vector3 contact,
		out Quaternion rotation );

	/// <summary>
	/// How many more stackable coins this slot can accept (0 if invalid / non-coin / full).
	/// </summary>
	int GetSlotCoinAppendCapacity( int slotIndex, TreasureDefinition probe );

	/// <summary>
	/// World contact pose for appending onto a slot stack (top of pile).
	/// </summary>
	bool TryGetSlotAppendPose( int slotIndex, out Vector3 contact, out Quaternion rotation );

	/// <summary>
	/// Append logical coin definitions into a display slot (spawns visuals). Returns how many were accepted.
	/// </summary>
	int TryAppendSlotDefinitions( int slotIndex, IReadOnlyList<TreasureDefinition> definitions );
}
