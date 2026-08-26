using UnityEngine;

/// <summary>
/// Runtime treasure-surface fence: paints its <see cref="BoxCollider"/> XZ footprint
/// non-traversable while blocked, and restores authored paint when unblocked.
/// Legacy unlockQuestId keeps previously quest-gated fences open.
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
			FindObjectsInactive.Exclude,
			FindObjectsSortMode.None );
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
			FindObjectsInactive.Exclude,
			FindObjectsSortMode.None );
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

	void OnDrawGizmosSelected()
	{
		Bounds bounds = GetWorldBounds();
		Gizmos.color = new Color( 1f, 0.25f, 0.2f, 0.35f );
		Gizmos.DrawCube( bounds.center, bounds.size );
		Gizmos.color = new Color( 1f, 0.25f, 0.2f, 0.9f );
		Gizmos.DrawWireCube( bounds.center, bounds.size );
	}
}
