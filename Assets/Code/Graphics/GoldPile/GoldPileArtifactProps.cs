using System.Collections.Generic;
using System.Threading.Tasks;

using UnityEngine;

/// <summary>
/// Real MeshRenderer treasure objects for large pile props (artifacts, crowns, etc.).
/// Frustum-culled; picked via colliders instead of GPU instance probes.
/// </summary>
[DisallowMultipleComponent]
public class GoldPileArtifactProps : MonoBehaviour
{
	static readonly Plane[] FrustumPlanes = new Plane[ 6 ];

	struct PropEntry
	{
		public TreasureItem Item;
		public TreasureDefinition Definition;
		public Vector3 LocalPos;
		public int ChunkX;
		public int ChunkZ;
		public bool RenderVisible;
	}

	readonly List<PropEntry> _props = new List<PropEntry>( 64 );
	readonly Dictionary<TreasureDefinition, int> _visibleByDef = new Dictionary<TreasureDefinition, int>();

	TreasurePileVisual _owner;
	TreasurePileDefinition _definition;
	GoldPileHeightfield _heightfield;
	Transform _pileRoot;
	GoldPileLootInstances _loot;
	Camera _cachedCamera;
	int _bindSerial;
	float _embedDepth = 0.06f;
	float _placementRadiusFraction = 0.88f;

	public int PropCount => _props.Count;

	public void Bind(
		TreasurePileVisual owner,
		TreasurePileDefinition definition,
		GoldPileHeightfield heightfield,
		Transform pileRoot,
		GoldPileLootInstances loot,
		GoldPileLootStreamSettings streamSettings )
	{
		int bindId = ++_bindSerial;
		ClearAll();

		_owner = owner;
		_definition = definition;
		_heightfield = heightfield;
		_pileRoot = pileRoot != null ? pileRoot : transform;
		_loot = loot;
		_cachedCamera = null;
		_ = streamSettings;

		if ( definition != null )
		{
			_embedDepth = Mathf.Max( 0.01f, definition.initialRevealDepth * 0.5f );
			_placementRadiusFraction = Mathf.Clamp( definition.placementRadiusFraction, 0.4f, 1f );
		}

		_ = SpawnInitialAsync( bindId );
	}

	public bool Contains( TreasureItem item )
	{
		return FindIndex( item ) >= 0;
	}

	public bool TryBeginSteal( TreasureItem item )
	{
		return FindIndex( item ) >= 0;
	}

	public void CancelSteal( TreasureItem item )
	{
		if ( item == null || FindIndex( item ) >= 0 )
			return;

		if ( item.Definition == null || !IsLargeProp( item.Definition ) )
			return;

		SeatExisting( item, item.transform.position );
	}

	public bool CompleteSteal( TreasureItem item )
	{
		int index = FindIndex( item );
		if ( index < 0 )
			return false;

		TreasureDefinition def = _props[ index ].Definition;
		_props.RemoveAt( index );
		if ( def != null )
		{
			int visible = GetVisibleCount( def );
			if ( visible > 0 )
				_visibleByDef[ def ] = visible - 1;
		}

		if ( _loot != null && def != null )
			_loot.ConsumeFallbackDefinition( def );

		if ( def != null )
			_ = RefillVisibleAsync( def );

		return true;
	}

	public bool TrySeatDeposit( TreasureDefinition definition, Vector3 preferredWorldPos, out Vector3 worldPos, out Quaternion worldRot, out bool becameVisible )
	{
		worldPos = preferredWorldPos;
		worldRot = Quaternion.identity;
		becameVisible = false;
		if ( definition == null || !IsLargeProp( definition ) || _pileRoot == null || _heightfield == null )
			return false;

		if ( _loot != null )
			_loot.AddRemaining( definition );

		ResolvePose( preferredWorldPos, out worldPos, out worldRot, out _ );

		if ( CountVisible( definition ) >= GetMaxVisible( definition ) )
			return true;

		becameVisible = true;
		_ = SeatNewAsync( definition, preferredWorldPos, null );
		return true;
	}

	public bool TryAbsorb( TreasureItem item, Vector3 worldPos )
	{
		if ( item == null || item.Definition == null || !IsLargeProp( item.Definition ) )
			return false;

		if ( _loot != null )
			_loot.AddRemaining( item.Definition );

		if ( CountVisible( item.Definition ) >= GetMaxVisible( item.Definition ) )
		{
			TreasureItemFactory.Despawn( item );
			return true;
		}

		SeatExisting( item, worldPos );
		return true;
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

			ApplyPose( entry.Item, entry.LocalPos );
			AssignChunk( ref entry );
			_props[ i ] = entry;
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
		_visibleByDef.Clear();
	}

	void LateUpdate()
	{
		if ( _props.Count == 0 )
			return;

		RefreshCulling();
	}

	void OnDisable()
	{
		_cachedCamera = null;
	}

	void OnDestroy()
	{
		ClearAll();
	}

	async Task SpawnInitialAsync( int bindId )
	{
		if ( _definition == null || _definition.contents == null || _heightfield == null )
			return;

		for ( int e = 0; e < _definition.contents.Length; e++ )
		{
			if ( bindId != _bindSerial || this == null )
				return;

			TreasurePileEntry entry = _definition.contents[ e ];
			TreasureDefinition def = entry.treasure;
			if ( def == null || !IsLargeProp( def ) )
				continue;

			int remaining = _loot != null ? _loot.GetRemaining( def ) : entry.count;
			int want = Mathf.Min( GetMaxVisible( def ), Mathf.Max( 0, remaining ) );
			for ( int i = 0; i < want; i++ )
			{
				if ( bindId != _bindSerial || this == null )
					return;

				Vector3 preferred = PickSpawnWorldPos();
				await SeatNewAsync( def, preferred, null );
			}
		}
	}

	async Task SeatNewAsync( TreasureDefinition definition, Vector3 preferredWorld, TreasureItem reuse )
	{
		if ( definition == null || _pileRoot == null )
			return;

		ResolvePose( preferredWorld, out Vector3 worldPos, out Quaternion worldRot, out Vector3 local );

		TreasureItem item = reuse;
		if ( item == null )
			item = await TreasureItemFactory.SpawnAsync( definition, worldPos, worldRot, transform );

		if ( item == null || this == null || _pileRoot == null )
		{
			if ( item != null && reuse == null )
				TreasureItemFactory.Despawn( item );
			return;
		}

		if ( CountVisible( definition ) >= GetMaxVisible( definition ) && reuse == null )
		{
			TreasureItemFactory.Despawn( item );
			return;
		}

		RegisterProp( item, definition, local );
	}

	async Task RefillVisibleAsync( TreasureDefinition definition )
	{
		if ( definition == null || _loot == null || _pileRoot == null )
			return;

		int remaining = _loot.GetRemaining( definition );
		int maxVisible = GetMaxVisible( definition );
		while ( CountVisible( definition ) < maxVisible && CountVisible( definition ) < remaining )
		{
			if ( this == null || _pileRoot == null )
				return;

			await SeatNewAsync( definition, PickSpawnWorldPos(), null );
			remaining = _loot.GetRemaining( definition );
		}
	}

	void SeatExisting( TreasureItem item, Vector3 preferredWorld )
	{
		if ( item == null || item.Definition == null || _pileRoot == null )
			return;

		ResolvePose( preferredWorld, out _, out _, out Vector3 local );
		RegisterProp( item, item.Definition, local );
	}

	void RegisterProp( TreasureItem item, TreasureDefinition definition, Vector3 local )
	{
		if ( item == null || definition == null )
			return;

		int existing = FindIndex( item );
		if ( existing >= 0 )
		{
			PropEntry updated = _props[ existing ];
			updated.LocalPos = local;
			AssignChunk( ref updated );
			_props[ existing ] = updated;
			ApplyPose( item, local );
			return;
		}

		item.SetOriginPile( _owner );
		item.EnterPile( _owner );
		item.transform.SetParent( transform, true );
		ApplyPose( item, local );

		PropEntry entry = new PropEntry
		{
			Item = item,
			Definition = definition,
			LocalPos = local,
			RenderVisible = true
		};
		AssignChunk( ref entry );
		_props.Add( entry );
		_visibleByDef[ definition ] = GetVisibleCount( definition ) + 1;
	}

	void ApplyPose( TreasureItem item, Vector3 local )
	{
		if ( item == null || _pileRoot == null || _heightfield == null )
			return;

		float surface = _heightfield.SampleNormalized( local.x, local.z ) * _heightfield.MaxHeight;
		Vector3 normal = SampleNormal( local.x, local.z );
		float lift = 0.08f;
		Collider col = item.GetComponent<Collider>();
		if ( col != null )
			lift = Mathf.Max( 0.06f, col.bounds.extents.y * 0.35f );

		Vector3 worldPos = _pileRoot.TransformPoint( new Vector3( local.x, surface, local.z ) )
			+ normal * ( lift - _embedDepth );
		float yaw = ( local.x * 37.1f + local.z * 91.7f ) * 25f;
		Quaternion worldRot = Quaternion.FromToRotation( Vector3.up, normal ) * Quaternion.Euler( 8f, yaw, 0f );
		item.transform.SetPositionAndRotation( worldPos, worldRot );
		item.ApplyWorldScale();
		item.SetMeshVisible( true );
	}

	void ResolvePose( Vector3 preferredWorld, out Vector3 worldPos, out Quaternion worldRot, out Vector3 local )
	{
		local = _pileRoot.InverseTransformPoint( preferredWorld );
		ClampLocal( ref local );
		float surface = _heightfield.SampleNormalized( local.x, local.z ) * _heightfield.MaxHeight;
		local.y = surface;
		Vector3 normal = SampleNormal( local.x, local.z );
		worldPos = _pileRoot.TransformPoint( local ) + normal * 0.08f;
		worldRot = Quaternion.FromToRotation( Vector3.up, normal );
	}

	Vector3 PickSpawnWorldPos()
	{
		float half = _heightfield.WorldSize * 0.5f * _placementRadiusFraction;
		float minSurface = 0.05f * _heightfield.MaxHeight;
		for ( int attempt = 0; attempt < 32; attempt++ )
		{
			float lx = Random.Range( -half, half );
			float lz = Random.Range( -half, half );
			float surface = _heightfield.SampleNormalized( lx, lz ) * _heightfield.MaxHeight;
			if ( surface < minSurface )
				continue;

			return _pileRoot.TransformPoint( new Vector3( lx, surface, lz ) );
		}

		return _pileRoot.position + Vector3.up * ( _heightfield.MaxHeight * 0.5f );
	}

	void ClampLocal( ref Vector3 local )
	{
		float half = _heightfield.WorldSize * 0.5f * _placementRadiusFraction;
		local.x = Mathf.Clamp( local.x, -half, half );
		local.z = Mathf.Clamp( local.z, -half, half );
	}

	Vector3 SampleNormal( float localX, float localZ )
	{
		float step = _heightfield.WorldSize / Mathf.Max( 1, _heightfield.Resolution - 1 );
		float hL = _heightfield.SampleNormalized( localX - step, localZ ) * _heightfield.MaxHeight;
		float hR = _heightfield.SampleNormalized( localX + step, localZ ) * _heightfield.MaxHeight;
		float hD = _heightfield.SampleNormalized( localX, localZ - step ) * _heightfield.MaxHeight;
		float hU = _heightfield.SampleNormalized( localX, localZ + step ) * _heightfield.MaxHeight;
		Vector3 normal = new Vector3( hL - hR, step * 2f, hD - hU ).normalized;
		return normal.sqrMagnitude < 0.0001f ? Vector3.up : normal;
	}

	void AssignChunk( ref PropEntry entry )
	{
		if ( _loot == null || _loot.ChunkGrid == null || _loot.ChunkGrid.ChunkCount == 0 )
		{
			entry.ChunkX = 0;
			entry.ChunkZ = 0;
			return;
		}

		_loot.ChunkGrid.LocalToChunk( entry.LocalPos.x, entry.LocalPos.z, out entry.ChunkX, out entry.ChunkZ );
	}

	void RefreshCulling()
	{
		Camera camera = ResolveCamera();
		bool hasFrustum = camera != null;
		if ( hasFrustum )
			GeometryUtility.CalculateFrustumPlanes( camera, FrustumPlanes );

		for ( int i = 0; i < _props.Count; i++ )
		{
			PropEntry entry = _props[ i ];
			TreasureItem item = entry.Item;
			if ( item == null )
				continue;

			bool visible = true;
			if ( hasFrustum )
			{
				Bounds bounds = GetItemBounds( item );
				visible = GeometryUtility.TestPlanesAABB( FrustumPlanes, bounds );
			}

			if ( visible == entry.RenderVisible )
				continue;

			entry.RenderVisible = visible;
			_props[ i ] = entry;
			item.SetMeshVisible( visible );

			Collider col = item.GetComponent<Collider>();
			if ( col != null )
				col.enabled = visible;
		}
	}

	static Bounds GetItemBounds( TreasureItem item )
	{
		Collider col = item.GetComponent<Collider>();
		if ( col != null )
			return col.bounds;

		Renderer renderer = item.GetComponentInChildren<Renderer>();
		if ( renderer != null )
			return renderer.bounds;

		return new Bounds( item.transform.position, Vector3.one * 0.5f );
	}

	Camera ResolveCamera()
	{
		if ( _cachedCamera != null )
			return _cachedCamera;

		Camera main = Camera.main;
		if ( main != null )
		{
			_cachedCamera = main;
			return _cachedCamera;
		}

		if ( GameMode.Instance != null && GameMode.Instance.cameraController != null )
		{
			CameraController controller = GameMode.Instance.cameraController;
			if ( controller.FirstPerson != null )
			{
				Camera cam = controller.FirstPerson.GetComponent<Camera>();
				if ( cam == null )
					cam = controller.FirstPerson.GetComponentInChildren<Camera>();
				if ( cam != null )
				{
					_cachedCamera = cam;
					return _cachedCamera;
				}
			}
		}

		return null;
	}

	int GetMaxVisible( TreasureDefinition definition )
	{
		if ( _definition == null || definition == null || _definition.contents == null )
			return 4;

		for ( int i = 0; i < _definition.contents.Length; i++ )
		{
			TreasurePileEntry entry = _definition.contents[ i ];
			if ( entry.treasure != definition )
				continue;
			return _definition.GetMaxVisibleFor( entry );
		}

		return Mathf.Max( 1, _definition.defaultPerTypeVisible );
	}

	int CountVisible( TreasureDefinition definition )
	{
		return GetVisibleCount( definition );
	}

	int GetVisibleCount( TreasureDefinition definition )
	{
		if ( definition == null )
			return 0;
		return _visibleByDef.TryGetValue( definition, out int n ) ? n : 0;
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

	public static bool IsLargeProp( TreasureDefinition definition )
	{
		if ( definition == null )
			return false;

		switch ( definition.category )
		{
			case TreasureCategory.Coin:
			case TreasureCategory.Gem:
				return false;
			default:
				return true;
		}
	}
}
