using UnityEngine;

/// <summary>
/// Universal placement target. Player code supplies aim context; the target owns
/// accept rules, preview pose, capacity, and final place/remove behaviour.
/// </summary>
public interface ITreasurePlacementTarget
{
	bool CanPlace( TreasureItem item, in PlacementQuery query );

	bool TryGetPlacementPreview( TreasureItem item, in PlacementQuery query, out PlacementPreview preview );

	/// <summary>
	/// Removes from carry and places. Returns false without modifying carry on failure.
	/// </summary>
	bool TryPlace( TreasureItem item, in PlacementQuery query );

	void Remove( TreasureItem item );
}
