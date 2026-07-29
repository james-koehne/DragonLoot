using System.Collections.Generic;

using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Heightfield-driven gold pile: displaced terrain + GPU-instanced coins/gems + real artifact props.
/// </summary>
public class TreasurePileVisual : MonoBehaviour, ITreasureOwner, ISerializationCallbackReceiver
{
	const int DefaultResolution = 64;
	const float DefaultWorldSize = 6f;
	const float DefaultMaxHeight = 1.75f;
	const float DefaultCarveRadius = 0.55f;
	const float DefaultCarveVolumeScale = 0.02f;
	const float DefaultPickRadius = 0.45f;

	[SerializeField]
	GoldPileTerrainMesh terrainMesh;

	[SerializeField]
	GoldPileLootInstances lootInstances;

	[SerializeField]
	GoldPileArtifactProps artifactProps;

	[SerializeField]
	Material pileMaterial;

	[Header( "Loot Layout" )]
	[Tooltip( "Authored per-pile layout seed mixed with CoreDefinition.lootWorldSeed. 0 = derive from hierarchy path." )]
	[SerializeField]
	int lootLayoutSeed;

	public int LootLayoutSeed => lootLayoutSeed;

	[Header( "Authored Height (level)" )]
	[SerializeField]
	[HideInInspector]
	ushort[] authoredHeights;

	/// <summary>Legacy 8-bit authored heights; migrated to authoredHeights on deserialize.</summary>
	[SerializeField]
	[HideInInspector]
	[FormerlySerializedAs( "authoredHeights" )]
	byte[] authoredHeights8;

	[SerializeField]
	[HideInInspector]
	int authoredRes;

	[SerializeField]
	[HideInInspector]
	float authoredWorldSize;

	[SerializeField]
	[HideInInspector]
	float authoredMaxHeight;

	[SerializeField]
	[HideInInspector]
	int authoredRevision;

	GoldPileHeightfield _heightfield;
	TreasurePileInteractable _pile;
	TreasurePileDefinition _definition;
	bool _bound;
	bool _emptied;
	Vector3 _lastInteractPoint;
	bool _hasInteractPoint;
	int _totalUnits = 1;
	float _carveRadius = DefaultCarveRadius;
	float _carveVolumeScale = DefaultCarveVolumeScale;

	public TreasureOwnerKind OwnerKind => TreasureOwnerKind.Pile;
	public GoldPileHeightfield Heightfield => _heightfield;
	public GoldPileLootInstances LootInstances => lootInstances;
	public GoldPileArtifactProps ArtifactProps => artifactProps;
	public GoldPileTerrainMesh TerrainMesh => terrainMesh;
	public Material PileMaterial => pileMaterial;
	public bool HasAuthoredHeight =>
		authoredHeights != null
		&& authoredRes >= 8
		&& authoredHeights.Length == authoredRes * authoredRes;
	public int AuthoredRevision => authoredRevision;
	public int AuthoredResolution => authoredRes;
	public float AuthoredWorldSize => authoredWorldSize;
	public float AuthoredMaxHeight => authoredMaxHeight;
	public float PickRadius
	{
		get
		{
			if ( _definition != null )
				return _definition.pickRadius;
			return DefaultPickRadius;
		}
	}

	/// <summary>
	/// True when a world point sits under the heightfield surface (buried / inside the mound).
	/// </summary>
	public bool IsPointBuried( Vector3 worldPos, float surfaceClearance = 0.03f )
	{
		if ( _heightfield == null || !_heightfield.IsInitialized )
			return false;

		if ( !_heightfield.ExistsAtWorld( worldPos, transform ) )
			return false;

		float surface = _heightfield.SampleWorldHeight( worldPos, transform );
		Vector3 local = transform.InverseTransformPoint( worldPos );
		float half = _heightfield.WorldSize * 0.5f;
		if ( Mathf.Abs( local.x ) > half || Mathf.Abs( local.z ) > half )
			return false;

		return local.y < surface - surfaceClearance;
	}

	public bool ContainsWorldPointXZ( Vector3 worldPos )
	{
		if ( _heightfield == null || !_heightfield.IsInitialized )
			return false;

		return _heightfield.ExistsAtWorld( worldPos, transform );
	}

	/// <summary>True when the pile surface exists at this world point (above ground level).</summary>
	public bool HasPileSurfaceAt( Vector3 worldPos )
	{
		if ( _heightfield == null || !_heightfield.IsInitialized )
			return false;
		return _heightfield.ExistsAtWorld( worldPos, transform );
	}

	/// <summary>
	/// Despawns a loose gem/artifact and seats it as a visible pile instance at <paramref name="worldPos"/>.
	/// </summary>
	public bool AbsorbLooseItemAt( TreasureItem item, Vector3 worldPos )
	{
		if ( item == null || item.Definition == null )
			return false;

		if ( GoldPileArtifactProps.IsLargeProp( item.Definition ) )
		{
			if ( artifactProps == null )
				return false;
			if ( !artifactProps.TryAbsorb( item, worldPos ) )
				return false;

			DepositForUnitReturned( worldPos );
			if ( _pile != null )
			{
				_pile.SyncRemainingFromVisual();
				if ( !gameObject.activeSelf )
					gameObject.SetActive( true );
			}

			LooseTreasureManager.Unregister( item );
			return true;
		}

		if ( lootInstances == null )
			return false;

		if ( !TryDepositTreasure(
			item.Definition,
			worldPos,
			out Vector3 depositPos,
			out _,
			out bool becameVisible ) )
			return false;

		if ( item.Definition.category != TreasureCategory.Coin && !becameVisible )
			return false;

		DepositForUnitReturned( depositPos );
		if ( _pile != null )
		{
			_pile.SyncRemainingFromVisual();
			if ( !gameObject.activeSelf )
				gameObject.SetActive( true );
		}

		LooseTreasureManager.Unregister( item );
		TreasureItemFactory.Despawn( item );
		return true;
	}

	public bool IsTreasureBuried( TreasureItem item, float surfaceClearance = 0.03f )
	{
		if ( item == null )
			return false;

		// Real pile props use outside-fraction pickability — not center-point burial.
		if ( artifactProps != null && artifactProps.Contains( item ) )
			return !artifactProps.IsPickable( item );

		return IsPointBuried( item.transform.position, surfaceClearance );
	}

	public int TotalRemainingLoot => lootInstances != null ? lootInstances.TotalRemaining : 0;
	public float CarveRadius => _carveRadius;
	public float CarveVolumeScale => _carveVolumeScale;

	public bool TryDebugCarveAmount( Vector3 worldPos, int amount )
	{
		if ( amount <= 0 || _emptied || _heightfield == null || !_heightfield.IsInitialized )
			return false;

		SetLastInteractPoint( worldPos );
		CarveForUnitsTaken( worldPos, amount );
		return true;
	}

	public bool TryDebugDepositAmount( Vector3 worldPos, int amount )
	{
		if ( amount <= 0 || _heightfield == null || !_heightfield.IsInitialized )
			return false;

		SetLastInteractPoint( worldPos );
		for ( int i = 0; i < amount; i++ )
			DepositForUnitReturned( worldPos, refresh: false );
		RefreshVisuals( worldPos );
		return true;
	}

	/// <summary>
	/// Debug: consume up to <paramref name="count"/> coin units, then carve once for the total taken.
	/// When <paramref name="addToCarry"/> is true, grants each unit to the player's carry.
	/// </summary>
	public int DebugTakeUnits( int count, Vector3 preferredWorldPos, PlayerController player, bool addToCarry )
	{
		if ( count <= 0 || lootInstances == null || _emptied )
			return 0;

		PlayerCarry carry = null;
		if ( addToCarry )
		{
			if ( player == null )
				return 0;
			carry = player.Carry;
			if ( carry == null )
				return 0;
		}

		int want = count;
		if ( addToCarry )
		{
			TreasureDefinition probe = GetAnyRemainingDefinition();
			if ( probe == null )
				return 0;
			want = Mathf.Min( want, carry.CountAffordableUnits( probe, count ) );
		}

		if ( want <= 0 )
			return 0;

		DebugConsumeBuffer.Clear();
		int taken = lootInstances.TryConsumeManyFromInventory(
			def =>
			{
				if ( def == null || def.category != TreasureCategory.Coin )
					return false;
				return !addToCarry || carry.CanAdd( def );
			},
			want,
			preferredWorldPos,
			DebugConsumeBuffer,
			out Vector3 carvePos,
			out _ );

		if ( taken <= 0 )
			return 0;

		if ( addToCarry )
		{
			int granted = carry.TryAddMany( DebugConsumeBuffer );
			taken = granted;
		}

		if ( taken > 0 )
			CarveForUnitsTaken( carvePos, taken );

		if ( _pile != null )
			_pile.SyncRemainingFromVisual();

		if ( TotalRemainingLoot <= 0 )
			OnPileEmptied();

		return taken;
	}

	static readonly List<TreasureDefinition> DebugConsumeBuffer = new List<TreasureDefinition>( 128 );

	public void Configure(
		TreasurePileInteractable pile,
		Transform baseTransform,
		Transform spawnLayerTransform,
		int initialSurface,
		int coinsPerSpawn,
		int maxSurface,
		float embed,
		float scaleFloor )
	{
		_pile = pile;
	}

	public void Bind( TreasurePileInteractable pile )
	{
		_pile = pile;
		_definition = pile != null ? pile.PileDefinition : null;
		_emptied = false;
		_bound = true;

		EnsureChildComponents();
		ApplyDefinitionTuning();
		InitializeHeightfield();
		WireSystemsAsync();
		BindSurfaceBridge();
	}

	void BindSurfaceBridge()
	{
		TreasureSurfaceWorld.EnsureExists();
		TreasurePileSurfaceBridge bridge = GetComponent<TreasurePileSurfaceBridge>();
		if ( bridge == null )
			bridge = gameObject.AddComponent<TreasurePileSurfaceBridge>();
		bridge.Bind( this );
	}

	void NotifySurfaceHeightChanged( Vector3 worldPos, float radius )
	{
		TreasurePileSurfaceBridge bridge = GetComponent<TreasurePileSurfaceBridge>();
		if ( bridge != null )
			bridge.NotifyHeightChanged( worldPos, radius );
	}

	async void WireSystemsAsync()
	{
		HideLegacyStaticMeshes();

		if ( pileMaterial == null && _definition != null )
			pileMaterial = _definition.pileMaterial;

		if ( pileMaterial == null )
		{
			MeshRenderer existing = GetComponentInChildren<MeshRenderer>();
			if ( existing != null && existing.sharedMaterial != null
				&& GoldPileQuality.IsGoldPileMaterial( existing.sharedMaterial ) )
			{
				pileMaterial = existing.sharedMaterial;
			}
		}

		if ( terrainMesh != null && _heightfield != null )
		{
			int meshRes = ResolveMeshResolution( _definition, _heightfield.Resolution );
			terrainMesh.Configure( pileMaterial, _heightfield.Resolution, meshRes );
			terrainMesh.Bind( _heightfield );
		}

		if ( lootInstances != null && _definition != null && _heightfield != null )
			await lootInstances.BindAsync( _definition, _heightfield, transform, lootLayoutSeed );

		// Bind may destroy/recreate during Addressables await (domain reload / scene unload).
		if ( this == null )
			return;

		if ( artifactProps != null && _definition != null && _heightfield != null )
		{
			GoldPileLootStreamSettings stream = lootInstances != null ? lootInstances.StreamSettings : null;
			artifactProps.Bind( this, _definition, _heightfield, transform, lootInstances, stream, lootLayoutSeed );
		}

		TreasurePileSurfaceBridge bridge = GetComponent<TreasurePileSurfaceBridge>();
		if ( bridge != null )
			bridge.RestampFull();
	}

	public void ReleaseTreasure( TreasureItem item )
	{
	}

	public bool TryBeginSteal( TreasureItem item )
	{
		if ( item == null )
			return false;
		if ( artifactProps != null && artifactProps.Contains( item ) )
			return artifactProps.TryBeginSteal( item );
		return true;
	}

	public void CancelSteal( TreasureItem item )
	{
		if ( artifactProps != null )
			artifactProps.CancelSteal( item );
	}

	public void CompleteSteal( TreasureItem item )
	{
		Vector3 carvePos = item != null ? item.transform.position : transform.position;
		if ( artifactProps != null && artifactProps.CompleteSteal( item ) )
		{
			CarveForUnitTaken( carvePos );
			if ( _pile != null )
				_pile.OnEmptiedFromVisual();
			return;
		}

		if ( _pile != null )
			_pile.NotifyUnitStolen();
	}

	public void OnCoinsTaken( int amount )
	{
		if ( amount <= 0 || _emptied || _heightfield == null || !_heightfield.IsInitialized )
			return;

		Vector3 carvePos = transform.position + Vector3.up * 0.5f;
		if ( TryGetLastInteractPoint( out Vector3 hit ) )
			carvePos = hit;

		CarveForUnitsTaken( carvePos, amount );
	}

	/// <summary>Carves <paramref name="amount"/> units in a single brush stroke.</summary>
	public void CarveForUnitsTaken( Vector3 worldPos, int amount )
	{
		CarveForUnitTaken( worldPos, refresh: true, units: amount );
	}

	public void CarveForUnitTaken( Vector3 worldPos )
	{
		CarveForUnitTaken( worldPos, refresh: true, units: 1 );
	}

	void CarveForUnitTaken( Vector3 worldPos, bool refresh, int units = 1 )
	{
		if ( units <= 0 || _emptied || _heightfield == null || !_heightfield.IsInitialized )
			return;

		// HeightPerCoin is total mound volume / units. Spread a tiny fraction of that
		// across a soft brush so each pick is a barely-visible blended dent.
		float volumePerUnit = _heightfield.HeightPerCoin( Mathf.Max( 1, _totalUnits ) );
		float volume = volumePerUnit * _carveVolumeScale * units;
		_heightfield.CarveAtWorld( worldPos, transform, _carveRadius, volume );
		NotifySurfaceHeightChanged( worldPos, _carveRadius * 2f );
		if ( refresh )
			RefreshVisuals( worldPos );
	}

	/// <summary>Grows the mound when treasure is returned (inverse of carve).</summary>
	public void DepositForUnitReturned( Vector3 worldPos )
	{
		DepositForUnitReturned( worldPos, refresh: true );
	}

	void DepositForUnitReturned( Vector3 worldPos, bool refresh )
	{
		if ( _heightfield == null || !_heightfield.IsInitialized )
			return;

		_emptied = false;
		if ( terrainMesh != null )
			terrainMesh.SetVisible( true );
		if ( lootInstances != null )
			lootInstances.enabled = true;
		if ( artifactProps != null )
			artifactProps.enabled = true;

		float volumePerUnit = _heightfield.HeightPerCoin( Mathf.Max( 1, _totalUnits ) );
		float volume = volumePerUnit * _carveVolumeScale;
		_heightfield.DepositAtWorld( worldPos, transform, _carveRadius, volume );
		NotifySurfaceHeightChanged( worldPos, _carveRadius * 2f );
		if ( refresh )
			RefreshVisuals( worldPos );
	}

	public bool TryDepositTreasure(
		TreasureDefinition definition,
		Vector3 preferredWorldPos,
		out Vector3 worldPos,
		out Quaternion worldRot,
		out bool becameVisible )
	{
		worldPos = preferredWorldPos;
		worldRot = Quaternion.identity;
		becameVisible = false;
		if ( definition == null )
			return false;

		if ( GoldPileArtifactProps.IsLargeProp( definition ) )
		{
			if ( artifactProps == null )
				return false;
			return artifactProps.TrySeatDeposit(
				definition,
				preferredWorldPos,
				out worldPos,
				out worldRot,
				out becameVisible );
		}

		if ( lootInstances == null )
			return false;

		bool requireVisible = definition.category != TreasureCategory.Coin;
		return lootInstances.TryDeposit(
			definition,
			preferredWorldPos,
			requireVisible,
			out worldPos,
			out worldRot,
			out becameVisible );
	}

	public void OnPileEmptied()
	{
		_emptied = true;
		if ( terrainMesh != null )
			terrainMesh.SetVisible( false );
		if ( lootInstances != null )
			lootInstances.enabled = false;
		if ( artifactProps != null )
		{
			artifactProps.ClearAll();
			artifactProps.enabled = false;
		}
	}

	public bool TryPickLootInstance(
		Vector3 worldPoint,
		out int slotIndex,
		out TreasureDefinition definition,
		out Vector3 worldPos,
		out Quaternion worldRot )
	{
		slotIndex = -1;
		definition = null;
		worldPos = worldPoint;
		worldRot = Quaternion.identity;
		if ( lootInstances == null )
			return false;

		return lootInstances.TryPickNearest( worldPoint, PickRadius, out slotIndex, out definition, out worldPos, out worldRot );
	}

	public bool TryConsumeLootSlot( int slotIndex, out TreasureDefinition definition )
	{
		definition = null;
		if ( lootInstances == null )
			return false;
		return lootInstances.TryTakeSlot( slotIndex, out definition );
	}

	public bool TryPickFallbackLoot(
		out int slotIndex,
		out TreasureDefinition definition,
		out Vector3 worldPos,
		out Quaternion worldRot )
	{
		slotIndex = -1;
		definition = null;
		worldPos = transform.position;
		worldRot = Quaternion.identity;
		if ( lootInstances == null )
			return false;
		return lootInstances.TryPickFallback( out slotIndex, out definition, out worldPos, out worldRot );
	}

	/// <summary>
	/// Digs a remaining coin from inventory (blank mound click).
	/// Does not require a nearby visible instance, but dig point must be within interact reach.
	/// Gems and other types are only taken via direct instance selection.
	/// </summary>
	public bool TryConsumeFromInventory(
		PlayerController player,
		Vector3 preferredWorldPos,
		out TreasureDefinition definition,
		out Vector3 worldPos,
		out Quaternion worldRot )
	{
		definition = null;
		worldPos = preferredWorldPos;
		worldRot = Quaternion.identity;
		if ( lootInstances == null || player == null )
			return false;

		float reach = player.Interaction != null ? player.Interaction.InteractRange : 8f;
		if ( ( preferredWorldPos - player.transform.position ).sqrMagnitude > reach * reach )
			return false;

		PlayerCarry carry = player.Carry;
		return lootInstances.TryConsumeFromInventory(
			def => def != null
				&& def.category == TreasureCategory.Coin
				&& carry != null
				&& carry.CanAdd( def ),
			preferredWorldPos,
			out definition,
			out worldPos,
			out worldRot );
	}

	/// <summary>
	/// Digs up to <paramref name="count"/> coin units with a single loot visibility rebuild.
	/// </summary>
	public int TryConsumeManyFromInventory(
		PlayerController player,
		Vector3 preferredWorldPos,
		int count,
		List<TreasureDefinition> consumedDefs,
		out Vector3 worldPos )
	{
		worldPos = preferredWorldPos;
		if ( lootInstances == null || player == null || count <= 0 )
			return 0;

		float reach = player.Interaction != null ? player.Interaction.InteractRange : 8f;
		if ( ( preferredWorldPos - player.transform.position ).sqrMagnitude > reach * reach )
			return 0;

		PlayerCarry carry = player.Carry;
		return lootInstances.TryConsumeManyFromInventory(
			def => def != null
				&& def.category == TreasureCategory.Coin
				&& carry != null
				&& carry.CanAdd( def ),
			count,
			preferredWorldPos,
			consumedDefs,
			out worldPos,
			out _ );
	}

	public bool TryConsumeDefinition( TreasureDefinition definition )
	{
		return lootInstances != null && lootInstances.ConsumeFallbackDefinition( definition );
	}

	public TreasureDefinition GetAnyRemainingDefinition()
	{
		if ( lootInstances == null || _definition == null || _definition.contents == null )
			return _definition != null ? _definition.GetPrimaryTreasure() : null;

		// Prefer coins for blank-mound dig / interact probes; gems require aiming an instance.
		for ( int i = 0; i < _definition.contents.Length; i++ )
		{
			TreasureDefinition def = _definition.contents[ i ].treasure;
			if ( def != null
				&& def.category == TreasureCategory.Coin
				&& lootInstances.GetRemaining( def ) > 0 )
				return def;
		}

		for ( int i = 0; i < _definition.contents.Length; i++ )
		{
			TreasureDefinition def = _definition.contents[ i ].treasure;
			if ( def != null && lootInstances.GetRemaining( def ) > 0 )
				return def;
		}

		return _definition.GetPrimaryTreasure();
	}

	public void SetLastInteractPoint( Vector3 worldPoint )
	{
		_lastInteractPoint = worldPoint;
		_hasInteractPoint = true;
	}

	void ApplyDefinitionTuning()
	{
		_carveRadius = DefaultCarveRadius;
		_carveVolumeScale = DefaultCarveVolumeScale;
		_totalUnits = 1;

		if ( _definition == null )
			return;

		float worldSize = Mathf.Max( 0.5f, _definition.worldSize );
		// Keep the brush wide enough to blend; tiny radii dig pinholes on large piles.
		_carveRadius = Mathf.Max( _definition.carveRadius, worldSize * 0.06f );
		_carveVolumeScale = Mathf.Clamp( _definition.carveVolumeScale, 0.0001f, 1f );
		_totalUnits = Mathf.Max( 1, _definition.TotalUnits() );
		if ( _definition.pileMaterial != null )
			pileMaterial = _definition.pileMaterial;
	}

	void EnsureChildComponents()
	{
		if ( terrainMesh == null )
			terrainMesh = GetComponent<GoldPileTerrainMesh>();
		if ( terrainMesh == null )
			terrainMesh = gameObject.AddComponent<GoldPileTerrainMesh>();

		if ( lootInstances == null )
			lootInstances = GetComponent<GoldPileLootInstances>();
		if ( lootInstances == null )
			lootInstances = gameObject.AddComponent<GoldPileLootInstances>();

		if ( artifactProps == null )
			artifactProps = GetComponent<GoldPileArtifactProps>();
		if ( artifactProps == null )
			artifactProps = gameObject.AddComponent<GoldPileArtifactProps>();

		if ( GetComponent<GoldPileLootStreamDebug>() == null )
			gameObject.AddComponent<GoldPileLootStreamDebug>();

		GoldPilePhysicsPool physics = GetComponent<GoldPilePhysicsPool>();
		if ( physics != null )
			physics.enabled = false;

		GoldPileEdgeProps edges = GetComponent<GoldPileEdgeProps>();
		if ( edges != null )
		{
			edges.ClearAll();
			edges.enabled = false;
		}

		GoldPileInstanceScatter scatter = GetComponent<GoldPileInstanceScatter>();
		if ( scatter != null )
			scatter.enabled = false;
	}

	void InitializeHeightfield()
	{
		ResolveLayout( out int res, out float size, out float height );

		if ( _heightfield != null )
			_heightfield.Release();

		_heightfield = new GoldPileHeightfield();
		TreasurePileDefinition def = ResolveDefinition();
		float groundLevel = def != null ? def.groundLevelHeight : 0.01f;
		_heightfield.Initialize( res, size, height, groundLevel );

		if ( HasAuthoredHeight && authoredRes == res )
			_heightfield.CopyFromNormalizedU16( authoredHeights );
		else
			_heightfield.FillMound( 1f );

		_heightfield.UploadIfDirty();
		_totalUnits = _definition != null
			? Mathf.Max( 1, _definition.TotalUnits() )
			: Mathf.Max( 1, _pile != null ? _pile.TotalCount : 1 );
	}

	void ResolveLayout( out int res, out float size, out float height )
	{
		res = DefaultResolution;
		size = DefaultWorldSize;
		height = DefaultMaxHeight;

		TreasurePileDefinition def = ResolveDefinition();
		if ( def != null )
		{
			res = def.heightResolution;
			size = def.worldSize;
			height = def.maxHeight;
		}
		else if ( HasAuthoredHeight )
		{
			res = authoredRes;
			size = authoredWorldSize > 0.1f ? authoredWorldSize : size;
			height = authoredMaxHeight > 0.01f ? authoredMaxHeight : height;
		}
	}

	TreasurePileDefinition ResolveDefinition()
	{
		if ( _definition != null )
			return _definition;

		TreasurePileInteractable interactable = _pile != null ? _pile : GetComponent<TreasurePileInteractable>();
		if ( interactable != null )
			return interactable.PileDefinition;

		return null;
	}

	static int ResolveMeshResolution( TreasurePileDefinition definition, int heightRes )
	{
		if ( definition != null )
			return definition.ResolveMeshResolution();
		return Mathf.Max( 8, heightRes );
	}

	public void EnsureAuthoredBuffers()
	{
		ResolveLayout( out int res, out float size, out float height );
		int count = res * res;

		if ( authoredHeights != null
			&& authoredRes == res
			&& authoredHeights.Length == count
			&& Mathf.Abs( authoredWorldSize - size ) < 0.001f
			&& Mathf.Abs( authoredMaxHeight - height ) < 0.001f )
			return;

		ushort[] previous = authoredHeights;
		int prevRes = authoredRes;
		authoredHeights = new ushort[ count ];
		authoredRes = res;
		authoredWorldSize = size;
		authoredMaxHeight = height;

		if ( previous != null && prevRes >= 8 && previous.Length == prevRes * prevRes )
		{
			// Nearest-neighbor resample when definition resolution changes.
			for ( int z = 0; z < res; z++ )
			{
				int srcZ = prevRes <= 1 ? 0 : Mathf.Clamp( ( z * ( prevRes - 1 ) ) / Mathf.Max( 1, res - 1 ), 0, prevRes - 1 );
				for ( int x = 0; x < res; x++ )
				{
					int srcX = prevRes <= 1 ? 0 : Mathf.Clamp( ( x * ( prevRes - 1 ) ) / Mathf.Max( 1, res - 1 ), 0, prevRes - 1 );
					authoredHeights[ z * res + x ] = previous[ srcZ * prevRes + srcX ];
				}
			}
		}
		else
		{
			GoldPileHeightfield temp = new GoldPileHeightfield();
			temp.Initialize( res, size, height );
			temp.FillMound( 1f );
			temp.CopyToNormalizedU16( ref authoredHeights );
			temp.Release();
		}

		authoredRevision++;
	}

	public void WriteAuthoredFromHeightfield()
	{
		if ( _heightfield == null || !_heightfield.IsInitialized )
			return;

		authoredRes = _heightfield.Resolution;
		authoredWorldSize = _heightfield.WorldSize;
		authoredMaxHeight = _heightfield.MaxHeight;
		_heightfield.CopyToNormalizedU16( ref authoredHeights );
		authoredRevision++;
	}

	public void ClearAuthoredHeight()
	{
		authoredHeights = null;
		authoredHeights8 = null;
		authoredRes = 0;
		authoredWorldSize = 0f;
		authoredMaxHeight = 0f;
		authoredRevision++;
	}

	public void ResetAuthoredToMound()
	{
		EnsureAuthoredBuffers();
		ResolveLayout( out int res, out float size, out float height );
		GoldPileHeightfield temp = new GoldPileHeightfield();
		temp.Initialize( res, size, height );
		temp.FillMound( 1f );
		temp.CopyToNormalizedU16( ref authoredHeights );
		temp.Release();
		authoredRes = res;
		authoredWorldSize = size;
		authoredMaxHeight = height;
		authoredRevision++;
	}

	public void OnBeforeSerialize()
	{
	}

	public void OnAfterDeserialize()
	{
		MigrateAuthoredHeights8To16();
	}

	void MigrateAuthoredHeights8To16()
	{
		if ( authoredHeights8 == null || authoredHeights8.Length == 0 )
			return;

		if ( authoredHeights == null || authoredHeights.Length != authoredHeights8.Length )
		{
			authoredHeights = new ushort[ authoredHeights8.Length ];
			for ( int i = 0; i < authoredHeights8.Length; i++ )
				authoredHeights[ i ] = ( ushort )( authoredHeights8[ i ] * 257 );
		}

		authoredHeights8 = null;
	}

	/// <summary>
	/// Builds or refreshes the displaced mound preview in edit mode (no loot, no surface bridge).
	/// </summary>
	public void EnsureEditorPreview()
	{
		if ( Application.isPlaying )
			return;

		EnsureChildComponents();
		_definition = ResolveDefinition();
		if ( pileMaterial == null && _definition != null )
			pileMaterial = _definition.pileMaterial;

		if ( pileMaterial == null )
		{
			MeshRenderer existing = GetComponentInChildren<MeshRenderer>();
			if ( existing != null && existing.sharedMaterial != null
				&& GoldPileQuality.IsGoldPileMaterial( existing.sharedMaterial ) )
			{
				pileMaterial = existing.sharedMaterial;
			}
		}

		EnsureAuthoredBuffers();
		ResolveLayout( out int res, out float size, out float height );

		if ( _heightfield == null || !_heightfield.IsInitialized
			|| _heightfield.Resolution != res
			|| Mathf.Abs( _heightfield.WorldSize - size ) > 0.001f
			|| Mathf.Abs( _heightfield.MaxHeight - height ) > 0.001f )
		{
			if ( _heightfield != null )
				_heightfield.Release();
			_heightfield = new GoldPileHeightfield();
			TreasurePileDefinition def = ResolveDefinition();
			float groundLevel = def != null ? def.groundLevelHeight : 0.01f;
			_heightfield.Initialize( res, size, height, groundLevel );
		}
		else if ( _heightfield != null )
		{
			TreasurePileDefinition def = ResolveDefinition();
			_heightfield.SetGroundLevel( def != null ? def.groundLevelHeight : 0.01f );
		}

		if ( HasAuthoredHeight && authoredRes == res )
			_heightfield.CopyFromNormalizedU16( authoredHeights );
		else
		{
			_heightfield.FillMound( 1f );
			WriteAuthoredFromHeightfield();
		}

		_heightfield.UploadIfDirty();

		int meshRes = ResolveMeshResolution( _definition, res );
		if ( terrainMesh != null )
		{
			terrainMesh.Configure( pileMaterial, res, meshRes );
			terrainMesh.Bind( _heightfield, syncCollider: false );
			terrainMesh.SetVisible( true );
		}

		HideLegacyStaticMeshes();
	}

	public void RefreshEditorPreviewFromHeightfield()
	{
		if ( Application.isPlaying || terrainMesh == null || _heightfield == null )
			return;

		terrainMesh.RefreshFromHeightfield();
	}

	public void CommitEditorStroke()
	{
		if ( Application.isPlaying || _heightfield == null )
			return;

		WriteAuthoredFromHeightfield();
		RefreshEditorPreviewFromHeightfield();
	}

	public bool TryEditorBrushWorld( Vector3 worldPos, GoldPileEditorBrushMode mode, in GoldPileBrushParams p )
	{
		if ( Application.isPlaying || _heightfield == null || !_heightfield.IsInitialized )
			return false;

		Vector3 local = transform.InverseTransformPoint( worldPos );
		float strength = p.invert ? -Mathf.Abs( p.strength ) : p.strength;
		float absStrength = Mathf.Abs( p.strength );

		switch ( mode )
		{
			case GoldPileEditorBrushMode.Raise:
				if ( p.invert )
					_heightfield.LowerAtLocal( local.x, local.z, p.radius, absStrength, p.falloff );
				else
					_heightfield.RaiseAtLocal( local.x, local.z, p.radius, absStrength, p.falloff );
				break;
			case GoldPileEditorBrushMode.Lower:
				if ( p.invert )
					_heightfield.RaiseAtLocal( local.x, local.z, p.radius, absStrength, p.falloff );
				else
					_heightfield.LowerAtLocal( local.x, local.z, p.radius, absStrength, p.falloff );
				break;
			case GoldPileEditorBrushMode.Smooth:
				_heightfield.SmoothAtLocal( local.x, local.z, p.radius, absStrength, p.falloff );
				break;
			case GoldPileEditorBrushMode.Flatten:
				_heightfield.FlattenAtLocal( local.x, local.z, p.radius, p.flattenTarget, absStrength, p.falloff );
				break;
			case GoldPileEditorBrushMode.Stamp:
				if ( p.stampMask == null )
					return false;
				if ( !p.stampMask.isReadable )
				{
					Debug.LogWarning(
						"Stamp mask must have Read/Write enabled: " + p.stampMask.name,
						p.stampMask );
					return false;
				}
				_heightfield.StampMaskAtLocal(
					p.stampMask,
					local.x,
					local.z,
					p.radius,
					absStrength,
					p.stampInvert ^ p.invert,
					p.stampFullFootprint,
					p.falloff );
				break;
			case GoldPileEditorBrushMode.Settle:
				_heightfield.SettleAtLocal(
					local.x,
					local.z,
					p.radius,
					Mathf.Max( 1, p.iterations ),
					p.angleOfReposeDegrees );
				break;
			case GoldPileEditorBrushMode.Flow:
				_heightfield.FlowAtLocal( local.x, local.z, p.radius, absStrength, p.falloff );
				break;
			case GoldPileEditorBrushMode.Inflate:
				_heightfield.InflateAtLocal( local.x, local.z, p.radius, strength, p.falloff );
				break;
			case GoldPileEditorBrushMode.Scrape:
				_heightfield.ScrapeAtLocal(
					local.x,
					local.z,
					p.radius,
					absStrength,
					p.falloff,
					p.moveDeltaX,
					p.moveDeltaZ );
				break;
			case GoldPileEditorBrushMode.Pinch:
				_heightfield.PinchAtLocal( local.x, local.z, p.radius, absStrength );
				break;
			case GoldPileEditorBrushMode.Noise:
				_heightfield.NoiseAtLocal(
					local.x,
					local.z,
					p.radius,
					strength,
					p.falloff,
					p.noiseFrequency,
					p.noiseOctaves,
					p.noiseSeed );
				break;
			case GoldPileEditorBrushMode.Erode:
				_heightfield.ErodeAtLocal(
					local.x,
					local.z,
					p.radius,
					absStrength,
					Mathf.Max( 1, p.iterations ) );
				break;
			case GoldPileEditorBrushMode.Fill:
				_heightfield.FillDepressionsAtLocal( local.x, local.z, p.radius, absStrength, p.falloff );
				break;
			case GoldPileEditorBrushMode.Peak:
				_heightfield.PeakAtLocal(
					local.x,
					local.z,
					p.radius,
					p.invert ? -Mathf.Abs( p.peakHeight ) : p.peakHeight,
					p.profileFalloff,
					p.settleAfterPeak );
				break;
			case GoldPileEditorBrushMode.Ridge:
				_heightfield.RidgeStampSegmentAtLocal(
					local.x - p.moveDeltaX,
					local.z - p.moveDeltaZ,
					local.x,
					local.z,
					p.ridgeWidth > 0.05f ? p.ridgeWidth : p.radius,
					p.invert ? -Mathf.Abs( p.peakHeight ) : p.peakHeight,
					p.falloff,
					p.splineSmooth );
				break;
			default:
				return false;
		}

		return true;
	}

	void HideLegacyStaticMeshes()
	{
		MeshRenderer[] renderers = GetComponentsInChildren<MeshRenderer>( true );
		for ( int i = 0; i < renderers.Length; i++ )
		{
			MeshRenderer renderer = renderers[ i ];
			if ( renderer == null )
				continue;

			if ( renderer.transform.name == "GoldPileTerrain" )
				continue;

			renderer.enabled = false;
			MeshCollider col = renderer.GetComponent<MeshCollider>();
			if ( col != null )
				col.enabled = false;
		}

		Collider[] rootColliders = GetComponents<Collider>();
		for ( int i = 0; i < rootColliders.Length; i++ )
		{
			if ( rootColliders[ i ] != null )
				rootColliders[ i ].enabled = false;
		}
	}

	void RefreshVisuals( Vector3 worldCenter )
	{
		GoldPileEditTiming.Begin( "GoldPile.RefreshVisuals" );
		System.Diagnostics.Stopwatch sw = GoldPileEditTiming.StartWatchIfEnabled();
		long terrainTicks = 0;

		if ( terrainMesh != null )
		{
			terrainMesh.RefreshFromHeightfield();
			if ( sw != null )
				terrainTicks = sw.ElapsedTicks;
		}

		float lootRadius = _carveRadius * 4f;
		if ( lootInstances != null )
			lootInstances.RefreshAfterCarve( worldCenter, lootRadius );

		if ( artifactProps != null
			&& artifactProps.MightRevealNear( worldCenter, lootRadius ) )
		{
			artifactProps.RefreshAfterCarve();
		}

		if ( sw != null )
		{
			sw.Stop();
			double tickMs = 1000.0 / System.Diagnostics.Stopwatch.Frequency;
			double terrainMs = terrainTicks * tickMs;
			double lootMs = ( sw.ElapsedTicks - terrainTicks ) * tickMs;
			GoldPileEditTiming.LogIfEnabled(
				$"[GoldPileEdit] refresh queue={terrainMs:F2}ms loot+artifacts={lootMs:F2}ms total={sw.Elapsed.TotalMilliseconds:F2}ms (densify+stamp deferred)",
				this );
		}

		GoldPileEditTiming.End();
	}

	bool TryGetLastInteractPoint( out Vector3 point )
	{
		point = default;
		if ( !_hasInteractPoint )
			return false;
		point = _lastInteractPoint;
		return true;
	}

	void ResolveGizmoSize( out float size, out float maxH )
	{
		size = DefaultWorldSize;
		maxH = DefaultMaxHeight;

		TreasurePileDefinition def = _definition;
		if ( def == null )
		{
			TreasurePileInteractable interactable = _pile != null ? _pile : GetComponent<TreasurePileInteractable>();
			if ( interactable != null )
				def = interactable.PileDefinition;
		}

		if ( def != null )
		{
			size = def.worldSize;
			maxH = def.maxHeight;
			return;
		}

		if ( _heightfield != null && _heightfield.IsInitialized )
		{
			size = _heightfield.WorldSize;
			maxH = _heightfield.MaxHeight;
		}
	}

	void OnDrawGizmosSelected()
	{
		ResolveGizmoSize( out float size, out float maxH );

		Matrix4x4 matrix = transform.localToWorldMatrix;
		Gizmos.matrix = matrix;
		Gizmos.color = new Color( 1f, 0.85f, 0.2f, 0.35f );
		Gizmos.DrawWireCube( new Vector3( 0f, maxH * 0.5f, 0f ), new Vector3( size, maxH, size ) );
		Gizmos.color = new Color( 1f, 0.75f, 0.1f, 0.9f );

		const int rings = 8;
		const int segments = 24;
		for ( int r = 1; r <= rings; r++ )
		{
			float t = r / ( float )rings;
			float radius = size * 0.5f * t;
			float falloff = 1f - t / 0.85f;
			if ( falloff < 0f )
				falloff = 0f;
			falloff = falloff * falloff * ( 3f - 2f * falloff );
			float y = maxH * falloff;

			Vector3 prev = Vector3.zero;
			for ( int s = 0; s <= segments; s++ )
			{
				float ang = ( s / ( float )segments ) * Mathf.PI * 2f;
				Vector3 p = new Vector3( Mathf.Cos( ang ) * radius, y, Mathf.Sin( ang ) * radius );
				if ( s > 0 )
					Gizmos.DrawLine( prev, p );
				prev = p;
			}
		}

		Gizmos.DrawLine( Vector3.zero, new Vector3( 0f, maxH, 0f ) );
		Gizmos.matrix = Matrix4x4.identity;
	}

	void OnDestroy()
	{
		if ( _heightfield != null )
		{
			_heightfield.Release();
			_heightfield = null;
		}
	}
}
