using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Merges loose gems into GPU pile instances, and large props into real pile MeshRenderer objects.
/// </summary>
public static class TreasurePileLooseDeposit
{
	public static bool TryAbsorbLooseItem( TreasureItem item, Vector3 worldPos )
	{
		if ( item == null || item.Definition == null )
			return false;

		TreasureCategory category = item.Definition.category;
		bool absorbable = category == TreasureCategory.Gem
			|| GoldPileArtifactProps.IsLargeProp( item.Definition );
		if ( !absorbable )
			return false;

		if ( !IsOnGoldSurface( worldPos ) )
			return false;

		TreasurePileVisual pile = ResolvePile( item, worldPos );
		if ( pile == null )
			return false;

		return pile.AbsorbLooseItemAt( item, worldPos );
	}

	static bool IsOnGoldSurface( Vector3 worldPos )
	{
		TreasureSurfaceWorld world = TreasureSurfaceWorld.Instance;
		if ( world == null || world.Sampler == null )
			return false;

		if ( !world.Sampler.TrySample( worldPos, out TreasureSurfaceSample sample ) )
			return false;

		return sample.Traversable && sample.Material == TreasureSurfaceMaterial.Gold;
	}

	static TreasurePileVisual ResolvePile( TreasureItem item, Vector3 worldPos )
	{
		TreasurePileVisual origin = item.OriginPile;
		if ( origin != null && origin.ContainsWorldPointXZ( worldPos ) )
			return origin;

		TreasurePileVisual best = null;
		float bestDistSq = float.MaxValue;
		IReadOnlyList<TreasurePileSurfaceBridge> bridges = TreasurePileSurfaceBridge.Active;
		for ( int i = 0; i < bridges.Count; i++ )
		{
			TreasurePileSurfaceBridge bridge = bridges[ i ];
			if ( bridge == null )
				continue;

			TreasurePileVisual visual = bridge.GetComponent<TreasurePileVisual>();
			if ( visual == null || !visual.ContainsWorldPointXZ( worldPos ) )
				continue;

			Vector3 delta = worldPos - visual.transform.position;
			float distSq = delta.x * delta.x + delta.z * delta.z;
			if ( distSq >= bestDistSq )
				continue;

			bestDistSq = distSq;
			best = visual;
		}

		return best;
	}
}
