using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Rare Addressable treasure props on pile edges / high-gradient cells.
/// </summary>
[DisallowMultipleComponent]
public class GoldPileEdgeProps : MonoBehaviour
{
	struct PropEntry
	{
		public TreasureItem Item;
		public Vector3 LocalPos;
	}

	[SerializeField]
	TreasureDefinition[] propLoot;

	[SerializeField]
	[Min( 1 )]
	int maxProps = 40;

	[SerializeField]
	[Range( 0.001f, 0.05f )]
	float densityFraction = 0.01f;

	[SerializeField]
	[Min( 0.05f )]
	float minGradient = 0.04f;

	[SerializeField]
	[Min( 0f )]
	float embedDepth = 0.06f;

	readonly List<PropEntry> _props = new List<PropEntry>();

	GoldPileHeightfield _heightfield;
	Transform _pileRoot;
	TreasurePileVisual _owner;
	bool _spawnStarted;

	public void Configure( TreasureDefinition[] loot, int max )
	{
		if ( loot != null && loot.Length > 0 )
			propLoot = loot;
		maxProps = Mathf.Max( 1, max );
	}

	public void Bind( TreasurePileVisual owner, GoldPileHeightfield heightfield, Transform pileRoot )
	{
		_owner = owner;
		_heightfield = heightfield;
		_pileRoot = pileRoot != null ? pileRoot : transform;
		TrySpawnInitial();
	}

	public bool TryBeginSteal( TreasureItem item )
	{
		int index = FindIndex( item );
		if ( index < 0 )
			return false;

		_props.RemoveAt( index );
		return true;
	}

	public void CancelSteal( TreasureItem item )
	{
		if ( item == null || FindIndex( item ) >= 0 )
			return;

		if ( _props.Count >= maxProps )
		{
			TreasureItemFactory.Despawn( item );
			return;
		}

		PlaceExisting( item, item.transform.position );
	}

	public void CompleteSteal( TreasureItem item )
	{
		if ( _owner != null )
			_owner.CompleteSteal( item );
	}

	public TreasureItem FindNearest( Vector3 worldPoint, float maxDistance )
	{
		TreasureItem best = null;
		float bestSq = maxDistance * maxDistance;
		for ( int i = 0; i < _props.Count; i++ )
		{
			TreasureItem item = _props[ i ].Item;
			if ( item == null )
				continue;

			float sq = ( item.transform.position - worldPoint ).sqrMagnitude;
			if ( sq > bestSq )
				continue;

			bestSq = sq;
			best = item;
		}

		return best;
	}

	public void RepositionAll()
	{
		if ( _heightfield == null || _pileRoot == null )
			return;

		for ( int i = _props.Count - 1; i >= 0; i-- )
		{
			PropEntry entry = _props[ i ];
			if ( entry.Item == null )
			{
				_props.RemoveAt( i );
				continue;
			}

			if ( !TryFindEdgePoint( out Vector3 local, out Vector3 normal ) )
				continue;

			entry.LocalPos = local;
			_props[ i ] = entry;
			ApplyPose( entry.Item, local, normal );
		}
	}

	public void ClearAll()
	{
		for ( int i = 0; i < _props.Count; i++ )
		{
			if ( _props[ i ].Item != null )
				TreasureItemFactory.Despawn( _props[ i ].Item );
		}

		_props.Clear();
	}

	async void TrySpawnInitial()
	{
		if ( _spawnStarted || _heightfield == null )
			return;

		_spawnStarted = true;
		int target = Mathf.Min( maxProps, Mathf.Max( 1, Mathf.RoundToInt( 3000f * densityFraction ) ) );
		for ( int i = 0; i < target; i++ )
			await SpawnOneAsync();
	}

	async System.Threading.Tasks.Task SpawnOneAsync()
	{
		if ( _props.Count >= maxProps || propLoot == null || propLoot.Length == 0 )
			return;

		TreasureDefinition def = PickDefinition();
		if ( def == null )
			return;

		if ( !TryFindEdgePoint( out Vector3 local, out Vector3 normal ) )
			return;

		Vector3 worldPos = _pileRoot.TransformPoint( local ) - normal * embedDepth;
		Quaternion worldRot = Quaternion.FromToRotation( Vector3.up, normal )
			* Quaternion.Euler( 0f, Random.Range( 0f, 360f ), 0f );

		TreasureItem item = await TreasureItemFactory.SpawnAsync( def, worldPos, worldRot, transform );
		if ( item == null )
			return;

		if ( _props.Count >= maxProps )
		{
			TreasureItemFactory.Despawn( item );
			return;
		}

		item.SetOriginPile( _owner );
		item.EnterPile( _owner );
		_props.Add( new PropEntry { Item = item, LocalPos = local } );
		ApplyPose( item, local, normal );
	}

	void PlaceExisting( TreasureItem item, Vector3 preferredWorld )
	{
		Vector3 local = _pileRoot.InverseTransformPoint( preferredWorld );
		Vector3 normal = _heightfield.SampleWorldNormal( preferredWorld, _pileRoot );
		item.SetOriginPile( _owner );
		item.EnterPile( _owner );
		item.transform.SetParent( transform, true );
		ApplyPose( item, local, normal );
		_props.Add( new PropEntry { Item = item, LocalPos = local } );
	}

	void ApplyPose( TreasureItem item, Vector3 local, Vector3 normal )
	{
		if ( item == null || _pileRoot == null )
			return;

		if ( normal.sqrMagnitude < 0.0001f )
			normal = Vector3.up;

		float lift = 0.04f;
		Collider col = item.GetComponent<Collider>();
		if ( col != null )
			lift = Mathf.Max( 0.02f, col.bounds.extents.y );

		Vector3 worldPos = _pileRoot.TransformPoint( local ) + normal * ( lift - embedDepth );
		Quaternion worldRot = Quaternion.FromToRotation( Vector3.up, normal )
			* Quaternion.Euler( 0f, Random.Range( 0f, 360f ), 0f );
		item.transform.SetPositionAndRotation( worldPos, worldRot );
		item.ApplyWorldScale();
	}

	bool TryFindEdgePoint( out Vector3 local, out Vector3 normal )
	{
		local = Vector3.zero;
		normal = Vector3.up;
		if ( _heightfield == null || _pileRoot == null )
			return false;

		float half = _heightfield.WorldSize * 0.5f;
		for ( int attempt = 0; attempt < 40; attempt++ )
		{
			float lx = Random.Range( -half, half );
			float lz = Random.Range( -half, half );
			float h = _heightfield.SampleNormalized( lx, lz );
			if ( h * _heightfield.MaxHeight < _heightfield.GroundLevel )
				continue;

			float grad = _heightfield.GradientMagnitude( lx, lz );
			if ( grad < minGradient && Random.value > 0.15f )
				continue;

			local = new Vector3( lx, h * _heightfield.MaxHeight, lz );
			Vector3 world = _pileRoot.TransformPoint( local );
			normal = _heightfield.SampleWorldNormal( world, _pileRoot );
			return true;
		}

		return false;
	}

	TreasureDefinition PickDefinition()
	{
		if ( propLoot == null || propLoot.Length == 0 )
			return null;

		int valid = 0;
		for ( int i = 0; i < propLoot.Length; i++ )
		{
			if ( propLoot[ i ] != null )
				valid++;
		}

		if ( valid == 0 )
			return null;

		int pick = Random.Range( 0, valid );
		for ( int i = 0; i < propLoot.Length; i++ )
		{
			if ( propLoot[ i ] == null )
				continue;
			if ( pick == 0 )
				return propLoot[ i ];
			pick--;
		}

		return null;
	}

	int FindIndex( TreasureItem item )
	{
		for ( int i = 0; i < _props.Count; i++ )
		{
			if ( _props[ i ].Item == item )
				return i;
		}

		return -1;
	}

	void OnDestroy()
	{
		ClearAll();
	}
}
