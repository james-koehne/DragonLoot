using System.Collections.Generic;
using System.Threading.Tasks;

using UnityEngine;

/// <summary>
/// Real MeshRenderer treasure objects for large pile props (artifacts, crowns, gems, etc.).
/// Latent seeded volume poses spawn when bounds touch outside the mound;
/// pickable once any of the probe is outside (visible from the pile).
/// </summary>
[DisallowMultipleComponent]
public class GoldPileArtifactProps : MonoBehaviour
{
	static readonly Plane[] FrustumPlanes = new Plane[ 6 ];

	struct LatentEntry
	{
		public TreasureDefinition Definition;
		public Vector3 LocalPos;
		public Quaternion LocalRot;
		public float Scale;
		public Bounds LocalBounds;
		public bool Taken;
		public bool Spawned;
		public int PropIndex;
	}

	struct PropEntry
	{
		public TreasureItem Item;
		public TreasureDefinition Definition;
		public Vector3 LocalPos;
		public Quaternion LocalRot;
		public int LatentIndex;
		public int ChunkX;
		public int ChunkZ;
		public bool RenderVisible;
		public bool Pickable;
	}

	readonly List<LatentEntry> _latent = new List<LatentEntry>( 64 );
	readonly List<PropEntry> _props = new List<PropEntry>( 64 );
	readonly Dictionary<TreasureDefinition, int> _visibleByDef = new Dictionary<TreasureDefinition, int>();

	TreasurePileVisual _owner;
	TreasurePileDefinition _definition;
	GoldPileHeightfield _heightfield;
	Transform _pileRoot;
	GoldPileLootInstances _loot;
	Camera _cachedCamera;
	int _bindSerial;
	float _placementRadiusFraction = 0.88f;
	float _placementMinSpacing = 0.35f;
	float _treasureRadialPower = 1.25f;
	float _treasureHeightBias = 0.75f;
	int _pileLootSeed = 1;

	public int PropCount => _props.Count;

	public void Bind(
		TreasurePileVisual owner,
		TreasurePileDefinition definition,
		GoldPileHeightfield heightfield,
		Transform pileRoot,
		GoldPileLootInstances loot,
		GoldPileLootStreamSettings streamSettings,
		int authoredLayoutSeed = 0 )
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
			_placementRadiusFraction = Mathf.Clamp( definition.placementRadiusFraction, 0.4f, 1f );
			_placementMinSpacing = Mathf.Max( 0.05f, definition.placementMinSpacing );
			_treasureRadialPower = Mathf.Clamp( definition.treasureRadialPower, 0.25f, 3f );
			_treasureHeightBias = Mathf.Clamp( definition.treasureHeightBias, 0f, 3f );
		}

		_pileLootSeed = WorldLootSeed.GetPileEffectiveSeed( _pileRoot, authoredLayoutSeed );
		BuildLatentEntries();
		_ = RefreshRevealAsync( bindId );
	}

	public bool Contains( TreasureItem item )
	{
		return FindPropIndex( item ) >= 0;
	}

	public bool TryBeginSteal( TreasureItem item )
	{
		int index = FindPropIndex( item );
		if ( index < 0 )
			return false;
		return _props[ index ].Pickable;
	}

	public void CancelSteal( TreasureItem item )
	{
		if ( item == null || FindPropIndex( item ) >= 0 )
			return;

		if ( item.Definition == null || !IsLargeProp( item.Definition ) )
			return;

		SeatExisting( item, item.transform.position );
	}

	public bool CompleteSteal( TreasureItem item )
	{
		int index = FindPropIndex( item );
		if ( index < 0 )
			return false;

		PropEntry prop = _props[ index ];
		TreasureDefinition def = prop.Definition;
		int latentIndex = prop.LatentIndex;
		_props.RemoveAt( index );
		if ( def != null )
		{
			int visible = GetVisibleCount( def );
			if ( visible > 0 )
				_visibleByDef[ def ] = visible - 1;
		}

		if ( latentIndex >= 0 && latentIndex < _latent.Count )
		{
			LatentEntry latent = _latent[ latentIndex ];
			latent.Taken = true;
			latent.Spawned = false;
			latent.PropIndex = -1;
			_latent[ latentIndex ] = latent;
		}

		if ( _loot != null && def != null )
			_loot.ConsumeFallbackDefinition( def );

		_ = RefreshRevealAsync( _bindSerial );
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

		int latentIndex = FindUnusedLatent( definition );
		if ( latentIndex < 0 )
		{
			latentIndex = AppendLatentFromWorld( definition, preferredWorldPos );
			if ( latentIndex < 0 )
				return true;
		}

		LatentEntry latent = _latent[ latentIndex ];
		latent.Taken = false;
		_latent[ latentIndex ] = latent;

		worldPos = _pileRoot.TransformPoint( latent.LocalPos );
		worldRot = _pileRoot.rotation * latent.LocalRot;

		if ( CountVisible( definition ) >= GetMaxVisible( definition ) )
			return true;

		float outside = GoldPileTreasurePlacement.OutsideFractionAabb( _heightfield, latent.LocalBounds );
		if ( outside <= 0f )
			return true;

		becameVisible = true;
		_ = SeatLatentAsync( latentIndex, null );
		return true;
	}

	public bool TryAbsorb( TreasureItem item, Vector3 worldPos )
	{
		if ( item == null || item.Definition == null || !IsLargeProp( item.Definition ) )
			return false;

		if ( _loot != null )
			_loot.AddRemaining( item.Definition );

		int latentIndex = FindUnusedLatent( item.Definition );
		if ( latentIndex < 0 )
			latentIndex = AppendLatentFromWorld( item.Definition, worldPos );

		if ( latentIndex < 0 )
		{
			TreasureItemFactory.Despawn( item );
			return true;
		}

		LatentEntry latent = _latent[ latentIndex ];
		latent.Taken = false;
		_latent[ latentIndex ] = latent;

		if ( CountVisible( item.Definition ) >= GetMaxVisible( item.Definition ) )
		{
			TreasureItemFactory.Despawn( item );
			return true;
		}

		float outside = GoldPileTreasurePlacement.OutsideFractionAabb( _heightfield, latent.LocalBounds );
		if ( outside <= 0f )
		{
			TreasureItemFactory.Despawn( item );
			return true;
		}

		_ = SeatLatentAsync( latentIndex, item );
		return true;
	}

	/// <summary>
	/// Evaluate latent poses after the mound carves — spawn when bounds touch outside;
	/// update pickable state. Does not move spawned props.
	/// </summary>
	public void RefreshAfterCarve()
	{
		_ = RefreshRevealAsync( _bindSerial );
	}

	/// <summary>
	/// True when any unseeded latent prop AABB intersects the carve influence sphere.
	/// </summary>
	public bool MightRevealNear( Vector3 worldCenter, float radius )
	{
		if ( _latent.Count == 0 || _pileRoot == null || _heightfield == null )
			return false;

		float radiusSq = radius * radius;
		for ( int i = 0; i < _latent.Count; i++ )
		{
			LatentEntry latent = _latent[ i ];
			if ( latent.Taken || latent.Spawned )
				continue;

			Vector3 worldPos = _pileRoot.TransformPoint( latent.LocalBounds.center );
			float dx = worldPos.x - worldCenter.x;
			float dz = worldPos.z - worldCenter.z;
			float extent = latent.LocalBounds.extents.magnitude;
			float reach = radius + extent;
			if ( dx * dx + dz * dz <= reach * reach )
				return true;
		}

		return false;
	}

	public void ClearAll()
	{
		for ( int i = 0; i < _props.Count; i++ )
		{
			if ( _props[ i ].Item != null )
				TreasureItemFactory.Despawn( _props[ i ].Item );
		}

		_props.Clear();
		_latent.Clear();
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

	void BuildLatentEntries()
	{
		_latent.Clear();
		if ( _definition == null || _definition.contents == null || _heightfield == null )
			return;

		List<Vector3> occupied = new List<Vector3>( 128 );
		if ( _loot != null )
			_loot.CollectVolumePoseOccupancy( occupied );

		for ( int e = 0; e < _definition.contents.Length; e++ )
		{
			TreasurePileEntry entry = _definition.contents[ e ];
			TreasureDefinition def = entry.treasure;
			if ( def == null || !IsLargeProp( def ) || entry.count <= 0 )
				continue;

			// Always author entry.count latent poses from the seed so layout is identical every run.
			// Inventory remaining only gates how many can spawn/reveal later.
			int count = entry.count;
			int remaining = _loot != null ? _loot.GetRemaining( def ) : count;
			for ( int i = 0; i < count; i++ )
			{
				int unitIndex = WorldLootSeed.StableUnitIndex( e, i );
				float scale = def.worldScale.x;
				if ( scale < 0.01f )
					scale = 1f;

				float probe = Mathf.Max( 0.08f, scale * 0.35f );
				float spacing = Mathf.Max( _placementMinSpacing, probe * 1.5f );
				if ( !GoldPileTreasurePlacement.TrySampleVolumePose(
					_heightfield,
					_pileLootSeed,
					unitIndex,
					_placementRadiusFraction,
					scale,
					probe,
					_treasureRadialPower,
					_treasureHeightBias,
					occupied,
					spacing,
					out GoldPileTreasurePlacement.VolumePose pose ) )
				{
					continue;
				}

				Bounds localBounds = GoldPileTreasurePlacement.LocalAabbFromPose(
					pose.LocalPos,
					pose.LocalRot,
					pose.Scale,
					null );
				// Inflate slightly so large authored meshes still reveal reasonably.
				localBounds.Expand( probe );

				GoldPileTreasurePlacement.LiftBoundsIntoPileColumn(
					_heightfield,
					ref pose.LocalPos,
					ref localBounds,
					probe );
				occupied[ occupied.Count - 1 ] = pose.LocalPos;

				_latent.Add( new LatentEntry
				{
					Definition = def,
					LocalPos = pose.LocalPos,
					LocalRot = pose.LocalRot,
					Scale = pose.Scale,
					LocalBounds = localBounds,
					// Extra seats beyond current inventory stay taken until deposited.
					Taken = i >= remaining,
					Spawned = false,
					PropIndex = -1
				} );
			}
		}
	}

	async Task RefreshRevealAsync( int bindId )
	{
		if ( _definition == null || _heightfield == null || _pileRoot == null )
			return;

		for ( int i = 0; i < _latent.Count; i++ )
		{
			if ( bindId != _bindSerial || this == null )
				return;

			LatentEntry latent = _latent[ i ];
			if ( latent.Taken || latent.Definition == null )
				continue;

			float outside = GoldPileTreasurePlacement.OutsideFractionAabb( _heightfield, latent.LocalBounds );
			bool touchesOutside = outside > 0f;
			bool pickable = touchesOutside;

			if ( latent.Spawned )
			{
				int propIndex = FindPropByLatent( i );
				if ( propIndex >= 0 )
				{
					PropEntry prop = _props[ propIndex ];
					if ( prop.Pickable != pickable )
					{
						prop.Pickable = pickable;
						_props[ propIndex ] = prop;
						ApplyInteractableState( prop );
					}
				}
				continue;
			}

			if ( !touchesOutside )
				continue;

			if ( CountVisible( latent.Definition ) >= GetMaxVisible( latent.Definition ) )
				continue;

			await SeatLatentAsync( i, null );
		}
	}

	async Task SeatLatentAsync( int latentIndex, TreasureItem reuse )
	{
		if ( latentIndex < 0 || latentIndex >= _latent.Count || _pileRoot == null )
			return;

		LatentEntry latent = _latent[ latentIndex ];
		if ( latent.Taken || latent.Definition == null )
			return;

		if ( latent.Spawned && reuse == null )
			return;

		Vector3 worldPos = _pileRoot.TransformPoint( latent.LocalPos );
		Quaternion worldRot = _pileRoot.rotation * latent.LocalRot;

		TreasureItem item = reuse;
		if ( item == null )
			item = await TreasureItemFactory.SpawnAsync( latent.Definition, worldPos, worldRot, transform );

		if ( item == null || this == null || _pileRoot == null )
		{
			if ( item != null && reuse == null )
				TreasureItemFactory.Despawn( item );
			return;
		}

		if ( CountVisible( latent.Definition ) >= GetMaxVisible( latent.Definition ) && reuse == null && !latent.Spawned )
		{
			TreasureItemFactory.Despawn( item );
			return;
		}

		RegisterProp( item, latent.Definition, latentIndex );
	}

	void SeatExisting( TreasureItem item, Vector3 preferredWorld )
	{
		if ( item == null || item.Definition == null || _pileRoot == null )
			return;

		int latentIndex = FindUnusedLatent( item.Definition );
		if ( latentIndex < 0 )
			latentIndex = AppendLatentFromWorld( item.Definition, preferredWorld );
		if ( latentIndex < 0 )
			return;

		LatentEntry latent = _latent[ latentIndex ];
		latent.Taken = false;
		_latent[ latentIndex ] = latent;
		_ = SeatLatentAsync( latentIndex, item );
	}

	void RegisterProp( TreasureItem item, TreasureDefinition definition, int latentIndex )
	{
		if ( item == null || definition == null )
			return;

		LatentEntry latent = _latent[ latentIndex ];
		float outside = GoldPileTreasurePlacement.OutsideFractionAabb( _heightfield, latent.LocalBounds );
		bool pickable = outside > 0f;

		int existing = FindPropIndex( item );
		if ( existing >= 0 )
		{
			PropEntry updated = _props[ existing ];
			updated.LocalPos = latent.LocalPos;
			updated.LocalRot = latent.LocalRot;
			updated.LatentIndex = latentIndex;
			updated.Pickable = pickable;
			AssignChunk( ref updated );
			_props[ existing ] = updated;
			ApplyFixedPose( item, latent );
			ApplyInteractableState( updated );
			latent.Spawned = true;
			latent.PropIndex = existing;
			_latent[ latentIndex ] = latent;
			return;
		}

		item.SetOriginPile( _owner );
		item.EnterPile( _owner );
		item.transform.SetParent( transform, true );
		if ( definition.category == TreasureCategory.Gem )
			item.EnsureGemSphereCollider();
		ApplyFixedPose( item, latent );

		// Refine latent AABB from the real prop for carve reveal / pick thresholds.
		Bounds worldBounds = GetItemBounds( item );
		latent.LocalBounds = WorldBoundsToLocal( worldBounds );
		if ( _heightfield != null && latent.LocalBounds.min.y < _heightfield.GroundLevel )
		{
			float probe = Mathf.Max( 0.08f, latent.Scale * 0.35f );
			Vector3 seatedPos = latent.LocalPos;
			Bounds seatedBounds = latent.LocalBounds;
			GoldPileTreasurePlacement.LiftBoundsIntoPileColumn(
				_heightfield,
				ref seatedPos,
				ref seatedBounds,
				probe );
			latent.LocalPos = seatedPos;
			latent.LocalBounds = seatedBounds;
			ApplyFixedPose( item, latent );
		}

		PropEntry entry = new PropEntry
		{
			Item = item,
			Definition = definition,
			LocalPos = latent.LocalPos,
			LocalRot = latent.LocalRot,
			LatentIndex = latentIndex,
			RenderVisible = true,
			Pickable = GoldPileTreasurePlacement.OutsideFractionAabb( _heightfield, latent.LocalBounds ) > 0f
		};
		AssignChunk( ref entry );
		_props.Add( entry );
		_visibleByDef[ definition ] = GetVisibleCount( definition ) + 1;

		latent.Spawned = true;
		latent.PropIndex = _props.Count - 1;
		_latent[ latentIndex ] = latent;
		ApplyInteractableState( entry );
	}

	void ApplyFixedPose( TreasureItem item, LatentEntry latent )
	{
		if ( item == null || _pileRoot == null )
			return;

		Vector3 localPos = latent.LocalPos;
		if ( _heightfield != null )
		{
			float probe = Mathf.Max( 0.08f, latent.Scale * 0.35f );
			localPos = GoldPileTreasurePlacement.ClampAboveFloor( _heightfield, localPos, probe );
		}

		Vector3 worldPos = _pileRoot.TransformPoint( localPos );
		Quaternion worldRot = _pileRoot.rotation * latent.LocalRot;
		item.transform.SetPositionAndRotation( worldPos, worldRot );
		item.ApplyWorldScale();
		item.SetMeshVisible( true );
	}

	void ApplyInteractableState( PropEntry prop )
	{
		if ( prop.Item == null )
			return;

		Collider col = prop.Item.GetComponent<Collider>();
		if ( col != null )
			col.enabled = prop.RenderVisible && prop.Pickable;
	}

	int FindUnusedLatent( TreasureDefinition definition )
	{
		for ( int i = 0; i < _latent.Count; i++ )
		{
			LatentEntry latent = _latent[ i ];
			if ( latent.Definition != definition )
				continue;
			if ( !latent.Taken && !latent.Spawned )
				return i;
			if ( latent.Taken )
				return i;
		}

		return -1;
	}

	int AppendLatentFromWorld( TreasureDefinition definition, Vector3 preferredWorld )
	{
		if ( definition == null || _pileRoot == null || _heightfield == null )
			return -1;

		Vector3 local = _pileRoot.InverseTransformPoint( preferredWorld );
		float half = _heightfield.WorldSize * 0.5f * _placementRadiusFraction;
		local.x = Mathf.Clamp( local.x, -half, half );
		local.z = Mathf.Clamp( local.z, -half, half );
		float surface = _heightfield.SampleNormalized( local.x, local.z ) * _heightfield.MaxHeight;
		float scale = definition.worldScale.x;
		if ( scale < 0.01f )
			scale = 1f;
		float probe = Mathf.Max( 0.08f, scale * 0.35f );
		float floorY = GoldPileTreasurePlacement.FloorClearanceY( _heightfield.GroundLevel, probe );
		float yMax = Mathf.Max( floorY, surface );
		local.y = Mathf.Clamp( local.y, floorY, yMax );
		local = GoldPileTreasurePlacement.ClampAboveFloor( _heightfield, local, probe );

		// Prefer a solid mound column; if preferred XZ is below-ground, snap toward center on that ray.
		if ( !_heightfield.ExistsAtLocal( local.x, local.z ) )
		{
			float angle = Mathf.Atan2( local.z, local.x );
			float radius = new Vector2( local.x, local.z ).magnitude;
			for ( int step = 0; step < 8; step++ )
			{
				radius *= 0.65f;
				local.x = Mathf.Cos( angle ) * radius;
				local.z = Mathf.Sin( angle ) * radius;
				if ( _heightfield.ExistsAtLocal( local.x, local.z ) )
				{
					surface = _heightfield.SampleNormalized( local.x, local.z ) * _heightfield.MaxHeight;
					local.y = Mathf.Clamp( local.y, floorY, Mathf.Max( floorY, surface ) );
					break;
				}
			}
		}

		Quaternion rot = GoldPileTreasurePlacement.HashRotation( _pileLootSeed, _latent.Count + 17 );
		Bounds bounds = GoldPileTreasurePlacement.LocalAabbFromPose( local, rot, scale, null );
		bounds.Expand( probe );
		GoldPileTreasurePlacement.LiftBoundsIntoPileColumn(
			_heightfield,
			ref local,
			ref bounds,
			probe );

		_latent.Add( new LatentEntry
		{
			Definition = definition,
			LocalPos = local,
			LocalRot = rot,
			Scale = scale,
			LocalBounds = bounds,
			Taken = false,
			Spawned = false,
			PropIndex = -1
		} );
		return _latent.Count - 1;
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
			{
				ApplyInteractableState( entry );
				continue;
			}

			entry.RenderVisible = visible;
			_props[ i ] = entry;
			item.SetMeshVisible( visible );
			ApplyInteractableState( entry );
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

	Bounds WorldBoundsToLocal( Bounds worldBounds )
	{
		if ( _pileRoot == null )
			return worldBounds;

		Vector3 c = _pileRoot.InverseTransformPoint( worldBounds.center );
		Vector3 e = worldBounds.extents;
		Vector3 x = _pileRoot.InverseTransformVector( new Vector3( e.x, 0f, 0f ) );
		Vector3 y = _pileRoot.InverseTransformVector( new Vector3( 0f, e.y, 0f ) );
		Vector3 z = _pileRoot.InverseTransformVector( new Vector3( 0f, 0f, e.z ) );
		Vector3 localExtents = new Vector3(
			Mathf.Abs( x.x ) + Mathf.Abs( y.x ) + Mathf.Abs( z.x ),
			Mathf.Abs( x.y ) + Mathf.Abs( y.y ) + Mathf.Abs( z.y ),
			Mathf.Abs( x.z ) + Mathf.Abs( y.z ) + Mathf.Abs( z.z ) );
		return new Bounds( c, localExtents * 2f );
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

	int FindPropIndex( TreasureItem item )
	{
		for ( int i = 0; i < _props.Count; i++ )
		{
			if ( _props[ i ].Item == item )
				return i;
		}

		return -1;
	}

	int FindPropByLatent( int latentIndex )
	{
		for ( int i = 0; i < _props.Count; i++ )
		{
			if ( _props[ i ].LatentIndex == latentIndex )
				return i;
		}

		return -1;
	}

	public static bool IsLargeProp( TreasureDefinition definition )
	{
		if ( definition == null )
			return false;

		return definition.category != TreasureCategory.Coin;
	}

	public bool IsPickable( TreasureItem item )
	{
		int index = FindPropIndex( item );
		if ( index < 0 )
			return false;
		return _props[ index ].Pickable;
	}
}
