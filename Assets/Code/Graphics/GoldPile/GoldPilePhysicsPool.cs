using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Ambient physics coins near the player for knock-around. Does not drive pile pickup selection.
/// </summary>
[DisallowMultipleComponent]
public class GoldPilePhysicsPool : MonoBehaviour
{
	[SerializeField]
	TreasureDefinition coinDefinition;

	[SerializeField]
	[Min( 1 )]
	int maxActive = 150;

	[SerializeField]
	[Min( 0.5f )]
	float activateRadius = 2.5f;

	[SerializeField]
	[Min( 0.5f )]
	float deactivateRadius = 3.5f;

	[SerializeField]
	[Min( 0.05f )]
	float refreshInterval = 0.35f;

	readonly List<TreasureItem> _active = new List<TreasureItem>();

	GoldPileHeightfield _heightfield;
	Transform _pileRoot;
	TreasurePileVisual _owner;
	float _timer;
	bool _spawning;

	public void Configure( TreasureDefinition coin, int max, float radius )
	{
		if ( coin != null )
			coinDefinition = coin;
		maxActive = Mathf.Clamp( max, 1, 200 );
		activateRadius = Mathf.Max( 0.5f, radius );
		deactivateRadius = Mathf.Max( activateRadius + 0.25f, deactivateRadius );
	}

	public void Bind( TreasurePileVisual owner, GoldPileHeightfield heightfield, Transform pileRoot )
	{
		_owner = owner;
		_heightfield = heightfield;
		_pileRoot = pileRoot != null ? pileRoot : transform;
	}

	public void ClearAll()
	{
		for ( int i = 0; i < _active.Count; i++ )
		{
			if ( _active[ i ] != null )
				TreasureItemFactory.Despawn( _active[ i ] );
		}

		_active.Clear();
	}

	void Update()
	{
		_timer -= Time.deltaTime;
		if ( _timer > 0f )
			return;

		_timer = refreshInterval;
		TickPool();
	}

	void TickPool()
	{
		if ( _heightfield == null || _pileRoot == null || coinDefinition == null )
			return;

		if ( !TreasureProximitySleep.TryGetPlayerPosition( out Vector3 playerPos ) )
			return;

		float activateSq = activateRadius * activateRadius;
		float deactivateSq = deactivateRadius * deactivateRadius;

		for ( int i = _active.Count - 1; i >= 0; i-- )
		{
			TreasureItem item = _active[ i ];
			if ( item == null )
			{
				_active.RemoveAt( i );
				continue;
			}

			if ( !item.IsWorldLoose || item.IsReclaiming )
			{
				_active.RemoveAt( i );
				continue;
			}

			float sq = ( item.transform.position - playerPos ).sqrMagnitude;
			if ( sq > deactivateSq )
			{
				TreasureItemFactory.Despawn( item );
				_active.RemoveAt( i );
			}
		}

		float pileSq = ( _pileRoot.position - playerPos ).sqrMagnitude;
		float pileReach = activateRadius + _heightfield.WorldSize;
		if ( pileSq > pileReach * pileReach )
			return;

		int budget = maxActive - _active.Count;
		if ( budget <= 0 || _spawning )
			return;

		SpawnBatchAsync( playerPos, activateSq, Mathf.Min( budget, 8 ) );
	}

	async void SpawnBatchAsync( Vector3 playerPos, float activateSq, int count )
	{
		_spawning = true;
		try
		{
			for ( int i = 0; i < count; i++ )
			{
				if ( _active.Count >= maxActive )
					break;

				if ( !TrySamplePointNearPlayer( playerPos, activateSq, out Vector3 worldPos, out Quaternion worldRot ) )
					continue;

				TreasureItem item = await TreasureItemFactory.SpawnAsync(
					coinDefinition,
					worldPos,
					worldRot,
					null );

				if ( item == null )
					continue;

				if ( _active.Count >= maxActive )
				{
					TreasureItemFactory.Despawn( item );
					break;
				}

				item.SetOriginPile( _owner );
				item.EnterPhysics( worldPos, worldRot, Vector3.zero );
				_active.Add( item );
			}
		}
		finally
		{
			_spawning = false;
		}
	}

	bool TrySamplePointNearPlayer( Vector3 playerPos, float activateSq, out Vector3 worldPos, out Quaternion worldRot )
	{
		worldPos = playerPos;
		worldRot = Quaternion.identity;
		float half = _heightfield.WorldSize * 0.5f;

		for ( int attempt = 0; attempt < 16; attempt++ )
		{
			Vector3 localPlayer = _pileRoot.InverseTransformPoint( playerPos );
			float lx = localPlayer.x + Random.Range( -activateRadius, activateRadius );
			float lz = localPlayer.z + Random.Range( -activateRadius, activateRadius );
			lx = Mathf.Clamp( lx, -half, half );
			lz = Mathf.Clamp( lz, -half, half );

			float h = _heightfield.SampleNormalized( lx, lz );
			if ( h * _heightfield.MaxHeight < _heightfield.GroundLevel )
				continue;

			Vector3 local = new Vector3( lx, h * _heightfield.MaxHeight + 0.05f, lz );
			worldPos = _pileRoot.TransformPoint( local );
			if ( ( worldPos - playerPos ).sqrMagnitude > activateSq )
				continue;

			Vector3 normal = _heightfield.SampleWorldNormal( worldPos, _pileRoot );
			worldRot = Quaternion.FromToRotation( Vector3.up, normal )
				* Quaternion.Euler( Random.Range( -20f, 20f ), Random.Range( 0f, 360f ), Random.Range( -20f, 20f ) );
			return true;
		}

		return false;
	}

	void OnDestroy()
	{
		ClearAll();
	}
}
