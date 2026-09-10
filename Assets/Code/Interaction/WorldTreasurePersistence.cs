using UnityEngine;

/// <summary>
/// Facade over <see cref="WorldTreasureStreamer"/> for existing gem/artifact park callers.
/// </summary>
public static class WorldTreasurePersistence
{
	public static int ParkedCount => WorldTreasureStreamer.ParkedPropCount;

	public static void EnsureExists()
	{
		WorldTreasureStreamer.EnsureExists();
	}

	public static void NotifyLoose( TreasureItem item )
	{
		WorldTreasureStreamer.NotifyLoose( item );
	}

	public static void NotifyOwned( TreasureItem item )
	{
		WorldTreasureStreamer.NotifyOwned( item );
	}

	public static void CancelParkForItem( TreasureItem item )
	{
		WorldTreasureStreamer.CancelParkForItem( item );
	}

	public static void CancelParkMatching( TreasureDefinition definition, Vector3 nearWorld, float radius )
	{
		WorldTreasureStreamer.CancelParkMatching( definition, nearWorld, radius );
	}

	public static int CountParkedCategory( TreasureCategory category )
	{
		return WorldTreasureStreamer.CountParkedCategory( category );
	}

	public static int CountParkedGems()
	{
		return WorldTreasureStreamer.CountParkedGems();
	}

	public static int CountParkedArtifacts()
	{
		return WorldTreasureStreamer.CountParkedArtifacts();
	}

	public static bool TryTakeParkedForReclaim(
		bool gems,
		out TreasureDefinition definition,
		out Vector3 position,
		out TreasurePileVisual origin )
	{
		return WorldTreasureStreamer.TryTakeParkedForReclaim( gems, out definition, out position, out origin );
	}
}
