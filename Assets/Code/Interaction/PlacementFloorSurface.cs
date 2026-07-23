using UnityEngine;

/// <summary>
/// Shared rules for which colliders count as walkable floor for placement.
/// Coin piles, treasure, and other interactables are never floor.
/// </summary>
public static class PlacementFloorSurface
{
	public static bool IsFloorCollider( Collider collider )
	{
		if ( collider == null )
			return false;

		// Single parent walk — cheaper than multiple GetComponentInParent scans.
		Transform t = collider.transform;
		while ( t != null )
		{
			if ( t.TryGetComponent( out InteractableBase _ ) )
				return false;

			if ( t.TryGetComponent( out TreasurePileVisual _ ) )
				return false;

			if ( t.TryGetComponent( out GoldPileTerrainMesh _ ) )
				return false;

			if ( t.TryGetComponent( out ITreasurePlacementTarget _ ) )
				return false;

			t = t.parent;
		}

		return true;
	}

	/// <summary>
	/// Heightfield gold piles (mound mesh + buried instanced loot), not vertical loose coin columns.
	/// </summary>
	public static bool IsHeightfieldPileCollider( Collider collider )
	{
		if ( collider == null )
			return false;

		Transform t = collider.transform;
		while ( t != null )
		{
			if ( t.TryGetComponent( out TreasurePileVisual _ ) )
				return true;

			if ( t.TryGetComponent( out GoldPileTerrainMesh _ ) )
				return true;

			if ( t.TryGetComponent( out TreasurePileInteractable _ ) )
				return true;

			t = t.parent;
		}

		return false;
	}

	public static bool IsHeightfieldPilePlacementTarget( ITreasurePlacementTarget target )
	{
		return target is TreasurePileInteractable;
	}
}
