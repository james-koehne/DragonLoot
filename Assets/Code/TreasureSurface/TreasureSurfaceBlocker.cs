using UnityEngine;

/// <summary>
/// Runtime treasure-surface fence: paints its <see cref="BoxCollider"/> XZ footprint
/// non-traversable while blocked, and restores authored paint when unblocked.
/// Legacy unlockQuestId keeps previously quest-gated fences open.
/// Enabled blockers also exclude pile latent / loot spawns from their XZ footprint.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent( typeof( BoxCollider ) )]
public class TreasureSurfaceBlocker : MonoBehaviour
{
	[SerializeField]
	bool startsBlocked = true;

	[SerializeField]
	string unlockQuestId = "quest_starting";

	public bool IsBlocked => _blocked;
	public BoxCollider Box => _box != null ? _box : ( _box = GetComponent<BoxCollider>() );

	BoxCollider _box;
	bool _blocked;
	bool _painted;

	static TreasureSurfaceBlocker[] _spawnBlockCache;
	static int _spawnBlockCacheFrame = -1;

	void Reset()
	{
		BoxCollider box = Box;
		box.isTrigger = true;
		box.center = Vector3.zero;
		box.size = new Vector3( 2f, 0.5f, 1f );
	}

	void OnEnable()
	{
		EvaluateBlockedState();
	}

	void Start()
	{
		EvaluateBlockedState();
	}

	void OnDisable()
	{
		if ( _painted )
			RestorePaint();
		_painted = false;
		_blocked = false;
	}

	public void SetBlocked( bool blocked )
	{
		ApplyBlocked( blocked, force: true );
	}

	public void Refresh()
	{
		EvaluateBlockedState();
	}

	public static void RefreshAll()
	{
		TreasureSurfaceBlocker[] blockers = UnityEngine.Object.FindObjectsByType<TreasureSurfaceBlocker>(
			FindObjectsInactive.Exclude );
		if ( blockers == null )
			return;

		for ( int i = 0; i < blockers.Length; i++ )
		{
			TreasureSurfaceBlocker blocker = blockers[ i ];
			if ( blocker == null )
				continue;
			blocker.EvaluateBlockedState();
		}
	}

	void EvaluateBlockedState()
	{
		bool wantBlocked = startsBlocked && !IsLegacyUnlocked();
		ApplyBlocked( wantBlocked, force: true );
	}

	void ApplyBlocked( bool blocked, bool force )
	{
		if ( !force && _blocked == blocked && ( !blocked || _painted ) )
			return;

		_blocked = blocked;
		if ( blocked )
			PaintBlocked();
		else if ( _painted )
			RestorePaint();
	}

	void PaintBlocked()
	{
		TreasureSurfaceWorld world = TreasureSurfaceWorld.Instance;
		if ( world == null || !world.IsInitialized )
			return;

		TreasureSurfaceOps.PaintTraversableInBounds( world, GetWorldBounds(), false );
		_painted = true;
		WakeSurfaceItemsInBounds();
	}

	void RestorePaint()
	{
		TreasureSurfaceWorld world = TreasureSurfaceWorld.Instance;
		if ( world == null || !world.IsInitialized )
			return;

		TreasureSurfaceOps.RestoreTraversableInBounds( world, GetWorldBounds() );
		_painted = false;
		WakeSurfaceItemsInBounds();
	}

	void WakeSurfaceItemsInBounds()
	{
		TreasureSurfaceWorld world = TreasureSurfaceWorld.Instance;
		if ( world == null || world.Simulator == null )
			return;

		Bounds bounds = GetWorldBounds();
		TreasureItem[] items = UnityEngine.Object.FindObjectsByType<TreasureItem>(
			FindObjectsInactive.Exclude );
		if ( items == null )
			return;

		for ( int i = 0; i < items.Length; i++ )
		{
			TreasureItem item = items[ i ];
			if ( item == null || !item.IsWorldLoose || !TreasureItem.UsesSurfaceSimulation( item.Definition ) )
				continue;
			if ( !bounds.Contains( item.transform.position ) )
				continue;
			world.Simulator.Wake( item, item.Body != null ? item.Body.linearVelocity : Vector3.zero );
		}
	}

	bool IsLegacyUnlocked()
	{
		// Quest gates removed; previously quest-gated fences stay open.
		return !string.IsNullOrEmpty( unlockQuestId );
	}

	public Bounds GetWorldBounds()
	{
		BoxCollider box = Box;
		if ( box == null )
			return new Bounds( transform.position, Vector3.one );

		return box.bounds;
	}

	/// <summary>
	/// True when world XZ sits inside any enabled blocker's box footprint.
	/// Ignores paint / IsBlocked — the collider always excludes pile spawns.
	/// </summary>
	public static bool BlocksWorldXZ( float worldX, float worldZ )
	{
		TreasureSurfaceBlocker[] blockers = GetSpawnBlockCache();
		if ( blockers == null || blockers.Length == 0 )
			return false;

		for ( int i = 0; i < blockers.Length; i++ )
		{
			TreasureSurfaceBlocker blocker = blockers[ i ];
			if ( blocker == null || !blocker.isActiveAndEnabled )
				continue;

			Bounds bounds = blocker.GetWorldBounds();
			if ( worldX < bounds.min.x || worldX > bounds.max.x )
				continue;
			if ( worldZ < bounds.min.z || worldZ > bounds.max.z )
				continue;
			return true;
		}

		return false;
	}

	public static bool BlocksWorldPoint( Vector3 worldPos )
	{
		return BlocksWorldXZ( worldPos.x, worldPos.z );
	}

	public static bool BlocksLocalXZ( Transform pileRoot, float localX, float localZ )
	{
		if ( pileRoot == null )
			return false;

		Vector3 world = pileRoot.TransformPoint( new Vector3( localX, 0f, localZ ) );
		return BlocksWorldXZ( world.x, world.z );
	}

	public static bool BlocksLocalPoint( Transform pileRoot, Vector3 localPos )
	{
		return BlocksLocalXZ( pileRoot, localPos.x, localPos.z );
	}

	/// <summary>
	/// True when the pile-local AABB's XZ footprint overlaps any enabled blocker.
	/// </summary>
	public static bool OverlapsLocalBoundsXZ( Transform pileRoot, Bounds localBounds )
	{
		if ( pileRoot == null )
			return false;

		Vector3 min = localBounds.min;
		Vector3 max = localBounds.max;
		Vector3 c0 = pileRoot.TransformPoint( new Vector3( min.x, 0f, min.z ) );
		Vector3 c1 = pileRoot.TransformPoint( new Vector3( max.x, 0f, min.z ) );
		Vector3 c2 = pileRoot.TransformPoint( new Vector3( min.x, 0f, max.z ) );
		Vector3 c3 = pileRoot.TransformPoint( new Vector3( max.x, 0f, max.z ) );

		float minX = Mathf.Min( Mathf.Min( c0.x, c1.x ), Mathf.Min( c2.x, c3.x ) );
		float maxX = Mathf.Max( Mathf.Max( c0.x, c1.x ), Mathf.Max( c2.x, c3.x ) );
		float minZ = Mathf.Min( Mathf.Min( c0.z, c1.z ), Mathf.Min( c2.z, c3.z ) );
		float maxZ = Mathf.Max( Mathf.Max( c0.z, c1.z ), Mathf.Max( c2.z, c3.z ) );

		TreasureSurfaceBlocker[] blockers = GetSpawnBlockCache();
		if ( blockers == null || blockers.Length == 0 )
			return false;

		for ( int i = 0; i < blockers.Length; i++ )
		{
			TreasureSurfaceBlocker blocker = blockers[ i ];
			if ( blocker == null || !blocker.isActiveAndEnabled )
				continue;

			Bounds bounds = blocker.GetWorldBounds();
			if ( maxX < bounds.min.x || minX > bounds.max.x )
				continue;
			if ( maxZ < bounds.min.z || minZ > bounds.max.z )
				continue;
			return true;
		}

		return false;
	}

	static TreasureSurfaceBlocker[] GetSpawnBlockCache()
	{
		int frame = Time.frameCount;
		if ( _spawnBlockCache != null && _spawnBlockCacheFrame == frame )
			return _spawnBlockCache;

		_spawnBlockCacheFrame = frame;
		_spawnBlockCache = UnityEngine.Object.FindObjectsByType<TreasureSurfaceBlocker>(
			FindObjectsInactive.Exclude );
		return _spawnBlockCache;
	}

	void OnDrawGizmosSelected()
	{
		Bounds bounds = GetWorldBounds();
		Gizmos.color = new Color( 1f, 0.25f, 0.2f, 0.35f );
		Gizmos.DrawCube( bounds.center, bounds.size );
		Gizmos.color = new Color( 1f, 0.25f, 0.2f, 0.9f );
		Gizmos.DrawWireCube( bounds.center, bounds.size );
	}
}
