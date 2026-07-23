using UnityEngine;

/// <summary>
/// Spatial view over a sub-rect of a pile heightfield. Tracks stream/LOD state for loot draw.
/// </summary>
public sealed class GoldPileChunk
{
	public GoldPileChunkCoord Coord { get; }
	public int Seed { get; }
	public Bounds WorldBounds { get; private set; }
	public Bounds LocalBounds { get; private set; }

	public GoldPileChunkStreamState State = GoldPileChunkStreamState.Unloaded;
	public int Lod = 3;
	public int PendingLod = 3;
	public int LodStableFrames;
	public bool Dirty = true;
	public bool FrustumVisible;
	public int LastDrawnCount;

	public GoldPileChunk( GoldPileChunkCoord coord, int seed )
	{
		Coord = coord;
		Seed = seed;
	}

	public void SetLocalBounds( Bounds localBounds, Transform pileRoot )
	{
		LocalBounds = localBounds;
		if ( pileRoot == null )
		{
			WorldBounds = localBounds;
			return;
		}

		Vector3 center = pileRoot.TransformPoint( localBounds.center );
		Vector3 extents = localBounds.extents;
		Vector3 axisX = pileRoot.TransformVector( new Vector3( extents.x, 0f, 0f ) );
		Vector3 axisY = pileRoot.TransformVector( new Vector3( 0f, extents.y, 0f ) );
		Vector3 axisZ = pileRoot.TransformVector( new Vector3( 0f, 0f, extents.z ) );
		Vector3 worldExtents = new Vector3(
			Mathf.Abs( axisX.x ) + Mathf.Abs( axisY.x ) + Mathf.Abs( axisZ.x ),
			Mathf.Abs( axisX.y ) + Mathf.Abs( axisY.y ) + Mathf.Abs( axisZ.y ),
			Mathf.Abs( axisX.z ) + Mathf.Abs( axisY.z ) + Mathf.Abs( axisZ.z ) );
		WorldBounds = new Bounds( center, worldExtents * 2f );
	}
}
