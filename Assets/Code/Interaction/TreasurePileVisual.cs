using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Heightfield-driven gold pile: displaced terrain + GPU-instanced coins/gems + real artifact props.
/// </summary>
public class TreasurePileVisual : MonoBehaviour, ITreasureOwner
{
	const int DefaultResolution = 64;
	const float DefaultWorldSize = 6f;
	const float DefaultMaxHeight = 1.75f;
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

	[Header( "Artifact Latent Bake" )]
	[Tooltip( "Authorable bake policy (near-surface seating, etc.). Shared across piles. Rebake after changing." )]
	[SerializeField]
	TreasurePileLatentBakeSettings latentBakeSettings;

	[Tooltip( "Per-pile baked latent poses for this heightmap + layout seed. Pure data — never share this asset across piles." )]
	[SerializeField]
	TreasurePileLatentBake latentBake;

	[Header( "Coin Seat Bake" )]
	[Tooltip( "Per-pile baked GPU coin seats for this heightmap + layout seed. Pure data — never share this asset across piles." )]
	[SerializeField]
	TreasurePileCoinSeatBake coinSeatBake;

	public TreasurePileLatentBakeSettings LatentBakeSettings => latentBakeSettings;

	public TreasurePileLatentBake LatentBake => latentBake;

	public TreasurePileCoinSeatBake CoinSeatBake => coinSeatBake;

	public const string AuthoredLootRootName = "_AuthoredLoot";
	public const string LatentBakePreviewRootName = "_LatentBakePreview";

	public void SetLatentBake( TreasurePileLatentBake bake )
	{
		latentBake = bake;
	}

	public void SetCoinSeatBake( TreasurePileCoinSeatBake bake )
	{
		coinSeatBake = bake;
	}

	/// <summary>Persisted child that holds curated scene props (artifacts, chests, keys).</summary>
	public Transform EnsureAuthoredLootRoot()
	{
		Transform existing = transform.Find( AuthoredLootRootName );
		if ( existing != null )
			return existing;

		GameObject go = new GameObject( AuthoredLootRootName );
		go.transform.SetParent( transform, false );
		go.transform.localPosition = Vector3.zero;
		go.transform.localRotation = Quaternion.identity;
		go.transform.localScale = Vector3.one;
		return go.transform;
	}

	public Transform FindAuthoredLootRoot()
	{
		return transform.Find( AuthoredLootRootName );
	}

	public Transform FindLatentBakePreviewRoot()
	{
		return transform.Find( LatentBakePreviewRootName );
	}

	public void CollectAuthoredItems( List<TreasurePileAuthoredItem> results )
	{
		if ( results == null )
			return;

		results.Clear();
		Transform root = FindAuthoredLootRoot();
		if ( root == null )
			return;

		TreasurePileAuthoredItem[] items = root.GetComponentsInChildren<TreasurePileAuthoredItem>( true );
		for ( int i = 0; i < items.Length; i++ )
		{
			TreasurePileAuthoredItem authored = items[ i ];
			if ( authored == null || !authored.IsValidCurated() )
				continue;
			results.Add( authored );
		}
	}

	public int CountAuthoredItems()
	{
		Transform root = FindAuthoredLootRoot();
		if ( root == null )
			return 0;

		int count = 0;
		TreasurePileAuthoredItem[] items = root.GetComponentsInChildren<TreasurePileAuthoredItem>( true );
		for ( int i = 0; i < items.Length; i++ )
		{
			TreasurePileAuthoredItem authored = items[ i ];
			if ( authored == null || !authored.IsValidCurated() )
				continue;
			count++;
		}

		return count;
	}

	/// <summary>Stable hash of curated prop definitions + local poses for bake fingerprinting.</summary>
	public int ComputeAuthoredFingerprint()
	{
		Transform root = FindAuthoredLootRoot();
		if ( root == null )
			return 0;

		TreasurePileAuthoredItem[] items = root.GetComponentsInChildren<TreasurePileAuthoredItem>( true );
		int curated = 0;
		for ( int i = 0; i < items.Length; i++ )
		{
			TreasurePileAuthoredItem authored = items[ i ];
			if ( authored == null || !authored.IsValidCurated() )
				continue;
			curated++;
		}

		if ( curated == 0 )
			return 0;

		unchecked
		{
			uint h = 2166136261u;
			for ( int i = 0; i < items.Length; i++ )
			{
				TreasurePileAuthoredItem authored = items[ i ];
				if ( authored == null || !authored.IsValidCurated() )
					continue;

				TreasureDefinition def = authored.Definition;
				Transform t = authored.transform;
				Vector3 localPos = transform.InverseTransformPoint( t.position );
				Quaternion localRot = Quaternion.Inverse( transform.rotation ) * t.rotation;
				Vector3 scale = t.lossyScale;

				h = MixAuthoredString( h, def.id );
				h = MixAuthoredString( h, def.name );
				h = ( h ^ ( uint )( int )def.category ) * 16777619u;
				h = MixAuthoredFloat( h, localPos.x );
				h = MixAuthoredFloat( h, localPos.y );
				h = MixAuthoredFloat( h, localPos.z );
				h = MixAuthoredFloat( h, localRot.x );
				h = MixAuthoredFloat( h, localRot.y );
				h = MixAuthoredFloat( h, localRot.z );
				h = MixAuthoredFloat( h, localRot.w );
				h = MixAuthoredFloat( h, scale.x );
				h = MixAuthoredFloat( h, scale.y );
				h = MixAuthoredFloat( h, scale.z );
			}

			return ( int )h;
		}
	}

	public bool IsLatentBakeStale()
	{
		return GetLatentBakeStaleReason() != null;
	}

	public bool IsCoinSeatBakeStale()
	{
		return GetCoinSeatBakeStaleReason() != null;
	}

	/// <summary>Null when the bake matches. Otherwise a short reason for the inspector warning.</summary>
	public string GetLatentBakeStaleReason()
	{
		if ( latentBake == null )
			return CountAuthoredItems() > 0 ? "no bake asset" : null;

		if ( !TryGetLatentBakeFingerprint(
			out int layoutSeed,
			out int heightFp,
			out int contentsFp,
			out int volumeAttempts,
			out bool spatialHash,
			out bool avoidCoins,
			out int authoredFp,
			out bool nearSurface ) )
		{
			return null;
		}

		return latentBake.DescribeFingerprintMismatch(
			layoutSeed,
			heightFp,
			contentsFp,
			volumeAttempts,
			spatialHash,
			avoidCoins,
			authoredFp,
			nearSurface );
	}

	/// <summary>Null when the coin-seat bake matches. Otherwise a short reason for the inspector warning.</summary>
	public string GetCoinSeatBakeStaleReason()
	{
		TreasurePileDefinition def = ResolveDefinitionForEditor();
		if ( def == null || def.coinContents == null || def.coinContents.Length == 0 )
			return null;

		if ( coinSeatBake == null )
			return "no bake asset";

		if ( !TryGetCoinSeatBakeFingerprint(
			out int layoutSeed,
			out int heightFp,
			out int contentsFp,
			out int placementFp,
			out int maxVisible,
			out int steadyBudget ) )
		{
			return null;
		}

		return coinSeatBake.DescribeFingerprintMismatch(
			layoutSeed,
			heightFp,
			contentsFp,
			placementFp,
			maxVisible,
			steadyBudget );
	}

	public bool TryGetLatentBakeFingerprint(
		out int layoutSeed,
		out int heightFp,
		out int contentsFp,
		out int volumeAttempts,
		out bool spatialHash,
		out bool avoidCoins,
		out int authoredFp,
		out bool nearSurface )
	{
		layoutSeed = 0;
		heightFp = 0;
		contentsFp = 0;
		volumeAttempts = 0;
		spatialHash = false;
		avoidCoins = false;
		authoredFp = 0;
		nearSurface = false;

		TreasurePileDefinition def = ResolveDefinitionForEditor();
		if ( def == null )
			return false;

		layoutSeed = lootLayoutSeed;
		heightFp = ComputeAuthoredHeightFingerprint( def );
		contentsFp = def.HashLargePropContents();
		volumeAttempts = Mathf.Max( 1, def.latentVolumeMaxAttempts );
		spatialHash = def.latentUseSpatialHash;
		avoidCoins = def.latentAvoidCoinSeats;
		authoredFp = ComputeAuthoredFingerprint();
		if ( latentBakeSettings != null )
			nearSurface = latentBakeSettings.spawnTreasureNearSurface;
		return true;
	}

	public bool TryGetCoinSeatBakeFingerprint(
		out int layoutSeed,
		out int heightFp,
		out int contentsFp,
		out int placementFp,
		out int maxVisible,
		out int steadyBudget )
	{
		layoutSeed = 0;
		heightFp = 0;
		contentsFp = 0;
		placementFp = 0;
		maxVisible = 0;
		steadyBudget = 0;

		TreasurePileDefinition def = ResolveDefinitionForEditor();
		if ( def == null )
			return false;

		layoutSeed = lootLayoutSeed;
		heightFp = ComputeAuthoredHeightFingerprint( def );
		contentsFp = def.HashCoinContents();
		maxVisible = Mathf.Max( 1, def.maxVisibleTotal );
		steadyBudget = def.SteadyCoinVisibleBudget();

		unchecked
		{
			uint h = ( uint )def.HashCoinPlacementSettings();
			GoldPileLootStreamSettings stream = lootInstances != null ? lootInstances.StreamSettings : null;
			if ( stream != null )
				h = ( h ^ ( uint )stream.ComputeCoinSeatPlacementFingerprint() ) * 16777619u;
			placementFp = ( int )h;
		}

		return true;
	}

	/// <summary>
	/// Stable mound fingerprint from serialized authored U16 heights (not live float samples).
	/// </summary>
	int ComputeAuthoredHeightFingerprint( TreasurePileDefinition def )
	{
		unchecked
		{
			uint h = 2166136261u;
			h = ( h ^ ( uint )authoredRes ) * 16777619u;
			h = ( h ^ ( uint )AuthoredFloatToBits( authoredWorldSize ) ) * 16777619u;
			h = ( h ^ ( uint )AuthoredFloatToBits( authoredMaxHeight ) ) * 16777619u;
			if ( def != null )
			{
				h = ( h ^ ( uint )AuthoredFloatToBits( def.groundLevelHeight ) ) * 16777619u;
				h = ( h ^ ( uint )AuthoredFloatToBits( def.lootGroundLevelHeight ) ) * 16777619u;
			}

			if ( !HasAuthoredHeight )
			{
				if ( _heightfield != null && _heightfield.IsInitialized )
					return _heightfield.ComputeLayoutFingerprint();
				return ( int )h;
			}

			h = ( h ^ ( uint )authoredHeights.Length ) * 16777619u;
			int step = Mathf.Max( 1, authoredHeights.Length / 4096 );
			for ( int i = 0; i < authoredHeights.Length; i += step )
				h = ( h ^ authoredHeights[ i ] ) * 16777619u;
			return ( int )h;
		}
	}

	static int AuthoredFloatToBits( float value )
	{
		return System.BitConverter.SingleToInt32Bits( value );
	}

	static uint MixAuthoredString( uint h, string value )
	{
		unchecked
		{
			if ( value == null )
				return h * 16777619u;

			for ( int i = 0; i < value.Length; i++ )
				h = ( h ^ value[ i ] ) * 16777619u;
			return h;
		}
	}

	static uint MixAuthoredFloat( uint h, float value )
	{
		unchecked
		{
			int bits = Mathf.RoundToInt( value * 1000f );
			return ( h ^ ( uint )bits ) * 16777619u;
		}
	}

	[Header( "Authored Height (level)" )]
	[SerializeField]
	[HideInInspector]
	ushort[] authoredHeights;

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
	GoldPileCarveSettings _carveSettings = GoldPileCarveSettings.Default;
	int _lastCarveUnits = 1;

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
	public TreasurePileDefinition Definition => ResolveDefinition();
	public int AuthoredResolution => authoredRes;
	public float AuthoredWorldSize => authoredWorldSize;
	public float AuthoredMaxHeight => authoredMaxHeight;
	public float PickRadius
	{
		get
		{
			TreasurePileDefinition def = ResolveDefinition();
			if ( def != null )
				return def.pickRadius;
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
	/// True when this pile has any height (at or above ground level) under world XZ.
	/// Uses the live heightfield when initialized; otherwise authored bake data so editor tools work.
	/// </summary>
	public bool HasAnyHeightAtWorld( Vector3 worldPos )
	{
		if ( _heightfield != null && _heightfield.IsInitialized )
			return _heightfield.ExistsAtWorld( worldPos, transform );

		if ( !HasAuthoredHeight || authoredWorldSize < 0.01f )
			return false;

		Vector3 local = transform.InverseTransformPoint( worldPos );
		float half = authoredWorldSize * 0.5f;
		if ( Mathf.Abs( local.x ) > half || Mathf.Abs( local.z ) > half )
			return false;

		float u = Mathf.Clamp01( ( local.x / authoredWorldSize ) + 0.5f );
		float v = Mathf.Clamp01( ( local.z / authoredWorldSize ) + 0.5f );
		float fx = u * ( authoredRes - 1 );
		float fz = v * ( authoredRes - 1 );
		int x0 = Mathf.Clamp( Mathf.FloorToInt( fx ), 0, authoredRes - 1 );
		int z0 = Mathf.Clamp( Mathf.FloorToInt( fz ), 0, authoredRes - 1 );
		int x1 = Mathf.Min( x0 + 1, authoredRes - 1 );
		int z1 = Mathf.Min( z0 + 1, authoredRes - 1 );
		float tx = fx - x0;
		float tz = fz - z0;
		float n00 = authoredHeights[ z0 * authoredRes + x0 ] / 65535f;
		float n10 = authoredHeights[ z0 * authoredRes + x1 ] / 65535f;
		float n01 = authoredHeights[ z1 * authoredRes + x0 ] / 65535f;
		float n11 = authoredHeights[ z1 * authoredRes + x1 ] / 65535f;
		float n = Mathf.Lerp( Mathf.Lerp( n00, n10, tx ), Mathf.Lerp( n01, n11, tx ), tz );

		float maxHeight = authoredMaxHeight > 0.01f ? authoredMaxHeight : 1f;
		float ground = 0.01f;
		TreasurePileDefinition def = ResolveDefinition();
		if ( def != null )
			ground = def.groundLevelHeight;
		return n * maxHeight >= ground;
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

	/// <summary>
	/// World loose-cap reclaim: absorb a live gem/artifact back into the pile with an updated pose.
	/// </summary>
	public bool AbsorbReclaimItem( TreasureItem item, Vector3 worldPos )
	{
		if ( item == null || item.Definition == null || !GoldPileArtifactProps.IsLargeProp( item.Definition ) )
			return false;
		if ( artifactProps == null )
			return false;

		WorldTreasurePersistence.NotifyOwned( item );
		if ( !artifactProps.AbsorbReclaim( item, worldPos ) )
			return false;

		DepositForUnitReturned( worldPos );
		if ( _pile != null )
		{
			_pile.SyncRemainingFromVisual();
			if ( !gameObject.activeSelf )
				gameObject.SetActive( true );
		}

		return true;
	}

	/// <summary>
	/// World loose-cap reclaim for a parked (despawned) gem/artifact record.
	/// </summary>
	public bool AbsorbParkedTreasure( TreasureDefinition definition, Vector3 worldPos )
	{
		if ( definition == null || !GoldPileArtifactProps.IsLargeProp( definition ) )
			return false;
		if ( artifactProps == null )
			return false;

		if ( !artifactProps.AbsorbParkedLatent( definition, worldPos ) )
			return false;

		DepositForUnitReturned( worldPos );
		if ( _pile != null )
		{
			_pile.SyncRemainingFromVisual();
			if ( !gameObject.activeSelf )
				gameObject.SetActive( true );
		}

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
	public float CarveRadius => _carveSettings.radius;
	public GoldPileCarveSettings CarveSettings => _carveSettings;

	public bool TryDebugCarveAmount( Vector3 worldPos, int amount )
	{
		return TryDebugCarveAmount( worldPos, amount, ResolveGlobalCarveSettings() );
	}

	public bool TryDebugCarveAmount( Vector3 worldPos, int amount, GoldPileCarveSettings settings )
	{
		if ( amount <= 0 || _emptied || _heightfield == null || !_heightfield.IsInitialized )
			return false;

		SetLastInteractPoint( worldPos );
		CarveForUnitsTaken( worldPos, amount, settings, inventoryAlreadyConsumed: false );
		return true;
	}

	public bool TryDebugDepositAmount( Vector3 worldPos, int amount )
	{
		return TryDebugDepositAmount( worldPos, amount, ResolveGlobalCarveSettings() );
	}

	/// <summary>
	/// Debug: seat catalog gems/artifacts inside the mound and force-spawn live props.
	/// </summary>
	public void DebugSpawnArtifactsAndGemsInside(
		IReadOnlyList<TreasureDefinition> definitions,
		System.Action<int, int> onComplete = null )
	{
		if ( artifactProps == null )
		{
			if ( onComplete != null )
				onComplete( 0, 0 );
			return;
		}

		artifactProps.DebugSpawnDefinitionsInside( definitions, onComplete );
	}

	/// <summary>
	/// Debug: force-spawn authored latent gems/artifacts already seated in this pile.
	/// </summary>
	public void DebugForceSpawnExistingArtifactsAndGems( System.Action<int, int> onComplete = null )
	{
		if ( artifactProps == null )
		{
			if ( onComplete != null )
				onComplete( 0, 0 );
			return;
		}

		artifactProps.DebugForceSpawnExistingInside( onComplete );
	}

	public bool TryDebugDepositAmount( Vector3 worldPos, int amount, GoldPileCarveSettings settings )
	{
		if ( amount <= 0 || _heightfield == null || !_heightfield.IsInitialized )
			return false;

		SetLastInteractPoint( worldPos );
		for ( int i = 0; i < amount; i++ )
			DepositForUnitReturned( worldPos, refresh: false, settings );
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
		{
			if ( _pile != null )
				_pile.OnEmptiedFromVisual();
			else
				OnPileEmptied();
		}

		return taken;
	}

	static readonly List<TreasureDefinition> DebugConsumeBuffer = new List<TreasureDefinition>( 128 );

	public void Bind( TreasurePileInteractable pile )
	{
		_pile = pile;
		ResolveDefinition();
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

		TreasurePileDefinition def = ResolveDefinition();
		if ( pileMaterial == null && def != null )
			pileMaterial = def.pileMaterial;

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
			ConfigureTerrainMesh( terrainMesh, pileMaterial, _heightfield.Resolution, def );
			// Runtime: defer PhysX cooks across frames (SyncColliderImmediate stalls LoadScene Integrate / first frames).
			terrainMesh.Bind( _heightfield, syncCollider: false );
			if ( Application.isPlaying )
				terrainMesh.BeginDeferredColliderCook();
		}

		if ( DebugDefinition.TreasureSpawningDisabled )
		{
			Transform authored = FindAuthoredLootRoot();
			if ( authored != null )
				authored.gameObject.SetActive( false );
		}
		else if ( lootInstances != null && def != null && _heightfield != null )
		{
			await lootInstances.BindAsync( this, def, _heightfield, transform, lootLayoutSeed );
		}

		// Bind may destroy/recreate during Addressables await (domain reload / scene unload).
		if ( this == null )
			return;

		if ( !DebugDefinition.TreasureSpawningDisabled && artifactProps != null && def != null && _heightfield != null )
		{
			GoldPileLootStreamSettings stream = lootInstances != null ? lootInstances.StreamSettings : null;
			await artifactProps.BindAsync( this, def, _heightfield, transform, lootInstances, stream, lootLayoutSeed );
		}

		if ( this == null )
			return;

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

	/// <summary>
	/// Called when a gem/artifact/coin auto-releases from the pile as loose world loot.
	/// </summary>
	public void NotifyPropReleasedToWorld( Vector3 carvePos )
	{
		NotifyPropReleasedToWorld( carvePos, coinInventoryAlreadyConsumed: false );
	}

	public void NotifyPropReleasedToWorld( Vector3 carvePos, bool coinInventoryAlreadyConsumed )
	{
		CarveForUnitTaken(
			carvePos,
			refresh: true,
			units: 1,
			settings: ResolveGlobalCarveSettings(),
			inventoryAlreadyConsumed: coinInventoryAlreadyConsumed );
		if ( _pile != null )
			_pile.OnEmptiedFromVisual();
	}

	/// <summary>
	/// Inventory was consumed by column spill / dig-physical spawn without an extra height carve.
	/// </summary>
	public void NotifyInventoryChangedFromSpill()
	{
		if ( _pile != null )
			_pile.OnEmptiedFromVisual();
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

	/// <summary>Carves <paramref name="amount"/> coin-units in a single brush stroke.</summary>
	public void CarveForUnitsTaken( Vector3 worldPos, int amount )
	{
		CarveForUnitsTaken( worldPos, amount, ResolveGlobalCarveSettings(), inventoryAlreadyConsumed: true );
	}

	public void CarveForUnitsTaken( Vector3 worldPos, int amount, GoldPileCarveSettings settings )
	{
		CarveForUnitsTaken( worldPos, amount, settings, inventoryAlreadyConsumed: true );
	}

	public void CarveForUnitsTaken(
		Vector3 worldPos,
		int amount,
		GoldPileCarveSettings settings,
		bool inventoryAlreadyConsumed )
	{
		CarveForUnitTaken( worldPos, refresh: true, units: amount, settings: settings, inventoryAlreadyConsumed );
	}

	public void CarveForUnitTaken( Vector3 worldPos )
	{
		CarveForUnitTaken( worldPos, refresh: true, units: 1, settings: ResolveGlobalCarveSettings(), inventoryAlreadyConsumed: true );
	}

	public void CarveForUnitTaken( Vector3 worldPos, GoldPileCarveSettings settings )
	{
		CarveForUnitTaken( worldPos, refresh: true, units: 1, settings: settings, inventoryAlreadyConsumed: true );
	}

	void CarveForUnitTaken(
		Vector3 worldPos,
		bool refresh,
		int units,
		GoldPileCarveSettings settings,
		bool inventoryAlreadyConsumed )
	{
		if ( units <= 0 || _emptied || _heightfield == null || !_heightfield.IsInitialized )
			return;

		GoldPileCarveSettings resolved = ResolveCarveSettings( settings );
		_carveSettings = resolved;
		_lastCarveUnits = Mathf.Max( 1, units );

		int coinCount = inventoryAlreadyConsumed
			? ResolveCoinCountForCarveVolume( units )
			: Mathf.Max( 1, ResolveRemainingCoinCount() );
		float volumePerUnit = _heightfield.VolumePerCoin( coinCount );

		GoldPileEditTiming.BeginCarve(
			units,
			this,
			$"r={resolved.radius:0.##} volPerCoin={volumePerUnit:0.####} coins={coinCount} blurPad={resolved.blurPadCells} passes={resolved.blurPasses} str={resolved.blurStrength:0.##} fall={resolved.falloffSharpness:0.##}" );

		// Live mound volume / remaining coins — each dig removes that share × units.
		float volume = volumePerUnit * units;
		_heightfield.CarveAtWorld( worldPos, transform, resolved.radius, volume, resolved );

		System.Diagnostics.Stopwatch phaseSw = GoldPileEditTiming.StartWatchIfEnabled();
		NotifySurfaceHeightChanged( worldPos, ResolveStampRadius( resolved ) );
		if ( phaseSw != null )
		{
			phaseSw.Stop();
			GoldPileEditTiming.Record( "notify.surface", phaseSw.Elapsed.TotalMilliseconds );
		}

		if ( refresh )
			RefreshVisuals( worldPos );

		GoldPileEditTiming.EndCarveImmediate();
	}

	/// <summary>Grows the mound when treasure is returned (inverse of carve).</summary>
	public void DepositForUnitReturned( Vector3 worldPos )
	{
		DepositForUnitReturned( worldPos, refresh: true, ResolveGlobalCarveSettings() );
	}

	public void DepositForUnitReturned( Vector3 worldPos, GoldPileCarveSettings settings )
	{
		DepositForUnitReturned( worldPos, refresh: true, settings );
	}

	void DepositForUnitReturned( Vector3 worldPos, bool refresh )
	{
		DepositForUnitReturned( worldPos, refresh, ResolveGlobalCarveSettings() );
	}

	void DepositForUnitReturned( Vector3 worldPos, bool refresh, GoldPileCarveSettings settings )
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

		GoldPileCarveSettings resolved = ResolveCarveSettings( settings );
		_carveSettings = resolved;

		int coinCount = Mathf.Max( 1, ResolveRemainingCoinCount() );
		float volumePerUnit = _heightfield.VolumePerCoin( coinCount );
		_heightfield.DepositAtWorld( worldPos, transform, resolved.radius, volumePerUnit, resolved );
		NotifySurfaceHeightChanged( worldPos, ResolveStampRadius( resolved ) );
		if ( refresh )
			RefreshVisuals( worldPos );
	}

	/// <summary>
	/// Surface stamp radius = brush radius + blur pad in world space (not 2R).
	/// </summary>
	float ResolveStampRadius( GoldPileCarveSettings settings )
	{
		float radius = Mathf.Max( 0.05f, settings.radius );
		if ( _heightfield == null || !_heightfield.IsInitialized )
			return radius;

		float cell = _heightfield.WorldSize / Mathf.Max( 1, _heightfield.Resolution - 1 );
		int pad = settings.blurPadCells <= 0
			? 0
			: Mathf.Min( settings.blurPadCells, Mathf.Max( 1, Mathf.CeilToInt( radius / cell ) ) );
		return radius + pad * cell;
	}

	GoldPileCarveSettings ResolveCarveSettings( GoldPileCarveSettings settings )
	{
		float worldSize = DefaultWorldSize;
		if ( _heightfield != null && _heightfield.IsInitialized )
			worldSize = _heightfield.WorldSize;
		else
		{
			TreasurePileDefinition def = ResolveDefinition();
			if ( def != null )
				worldSize = def.worldSize;
		}

		return settings.ResolvedForPile( worldSize );
	}

	GoldPileCarveSettings ResolveGlobalCarveSettings()
	{
		return GoldPileCarveSettings.FromGlobalDefinition();
	}

	/// <summary>
	/// Remaining coin inventory. Dig consume happens before carve, so callers that already
	/// removed <paramref name="unitsBeingCarved"/> should pass that count to restore the pre-dig divisor.
	/// </summary>
	int ResolveRemainingCoinCount()
	{
		if ( lootInstances != null )
			return lootInstances.TotalRemainingCoins;
		TreasurePileDefinition def = ResolveDefinition();
		if ( def != null )
			return Mathf.Max( 0, def.TotalCoinUnits() );
		return 0;
	}

	int ResolveCoinCountForCarveVolume( int unitsBeingCarved )
	{
		// Inventory already lost these units; include them so volume = SumHeights / preDigCoins.
		return Mathf.Max( 1, ResolveRemainingCoinCount() + Mathf.Max( 0, unitsBeingCarved ) );
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
		if ( PlanarDistanceSq( preferredWorldPos, player.transform.position ) > reach * reach )
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
		if ( PlanarDistanceSq( preferredWorldPos, player.transform.position ) > reach * reach )
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

	static float PlanarDistanceSq( Vector3 a, Vector3 b )
	{
		float dx = a.x - b.x;
		float dz = a.z - b.z;
		return dx * dx + dz * dz;
	}

	public bool TryConsumeDefinition( TreasureDefinition definition )
	{
		return lootInstances != null && lootInstances.ConsumeFallbackDefinition( definition );
	}

	public TreasureDefinition GetAnyRemainingDefinition()
	{
		TreasurePileDefinition def = ResolveDefinition();
		if ( lootInstances == null || def == null )
			return def != null ? def.GetPrimaryTreasure() : null;

		// Prefer coins for blank-mound dig / interact probes; gems require aiming an instance.
		TreasureDefinition coin = FirstRemaining( def.coinContents, coinsOnly: true );
		if ( coin != null )
			return coin;

		TreasureDefinition any = FirstRemaining( def.coinContents, coinsOnly: false );
		if ( any != null )
			return any;
		any = FirstRemaining( def.treasureContents, coinsOnly: false );
		if ( any != null )
			return any;

		return def.GetPrimaryTreasure();
	}

	TreasureDefinition FirstRemaining( TreasurePileEntry[] entries, bool coinsOnly )
	{
		if ( entries == null || lootInstances == null )
			return null;

		for ( int i = 0; i < entries.Length; i++ )
		{
			TreasureDefinition def = entries[ i ].treasure;
			if ( def == null )
				continue;
			if ( coinsOnly && def.category != TreasureCategory.Coin )
				continue;
			if ( lootInstances.GetRemaining( def ) > 0 )
				return def;
		}

		return null;
	}

	public void SetLastInteractPoint( Vector3 worldPoint )
	{
		_lastInteractPoint = worldPoint;
		_hasInteractPoint = true;
	}

	void ApplyDefinitionTuning()
	{
		_carveSettings = ResolveGlobalCarveSettings();
		_totalUnits = 1;

		TreasurePileDefinition def = ResolveDefinition();
		if ( def == null )
			return;

		_totalUnits = Mathf.Max( 1, def.TotalCoinUnits() );
		if ( def.pileMaterial != null )
			pileMaterial = def.pileMaterial;
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
	}

	void InitializeHeightfield()
	{
		ResolveLayout( out int res, out float size, out float height );

		if ( _heightfield != null )
			_heightfield.Release();

		_heightfield = new GoldPileHeightfield();
		TreasurePileDefinition def = ResolveDefinition();
		_heightfield.Initialize(
			res,
			size,
			height,
			def != null ? def.groundLevelHeight : 0.01f,
			def != null ? def.lootGroundLevelHeight : 0.6f );

		if ( HasAuthoredHeight && authoredRes == res )
			_heightfield.CopyFromNormalizedU16( authoredHeights );
		else
			_heightfield.FillMound( 1f );

		_heightfield.UploadIfDirty();
		_totalUnits = def != null
			? Mathf.Max( 1, def.TotalCoinUnits() )
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
		if ( _pile == null )
			_pile = GetComponent<TreasurePileInteractable>();

		if ( _pile != null )
			_definition = _pile.PileDefinition;

		return _definition;
	}

	/// <summary>Editor bake / tools: resolve definition without requiring runtime Bind.</summary>
	public TreasurePileDefinition ResolveDefinitionForEditor()
	{
		return ResolveDefinition();
	}

	static int ResolveMeshResolution( TreasurePileDefinition definition, int heightRes )
	{
		if ( definition != null )
			return definition.ResolveMeshResolution();
		return Mathf.Max( 8, heightRes );
	}

	static void ConfigureTerrainMesh(
		GoldPileTerrainMesh mesh,
		Material material,
		int heightRes,
		TreasurePileDefinition definition )
	{
		if ( mesh == null )
			return;

		int meshRes = ResolveMeshResolution( definition, heightRes );
		float soften = definition != null ? definition.meshDeformNormalSoften : 0f;
		float blur = definition != null ? definition.meshDeformSampleBlur : 4f;
		mesh.Configure( material, heightRes, meshRes, soften, blur );
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

	/// <summary>
	/// Builds or refreshes the displaced mound preview in edit mode (no loot, no surface bridge).
	/// </summary>
	public void EnsureEditorPreview()
	{
		if ( Application.isPlaying )
			return;

		EnsureChildComponents();
		TreasurePileDefinition def = ResolveDefinition();
		if ( pileMaterial == null && def != null )
			pileMaterial = def.pileMaterial;

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
			_heightfield.Initialize(
				res,
				size,
				height,
				def != null ? def.groundLevelHeight : 0.01f,
				def != null ? def.lootGroundLevelHeight : 0.6f );
		}
		else if ( _heightfield != null )
		{
			_heightfield.SetGroundLevel( def != null ? def.groundLevelHeight : 0.01f );
			_heightfield.SetLootGroundLevel( def != null ? def.lootGroundLevelHeight : 0.6f );
		}

		if ( HasAuthoredHeight && authoredRes == res )
			_heightfield.CopyFromNormalizedU16( authoredHeights );
		else
		{
			_heightfield.FillMound( 1f );
			WriteAuthoredFromHeightfield();
			_heightfield.CopyFromNormalizedU16( authoredHeights );
		}

		_heightfield.UploadIfDirty();

		if ( terrainMesh != null )
		{
			ConfigureTerrainMesh( terrainMesh, pileMaterial, res, def );
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

			if ( IsProtectedAuthoringMesh( renderer.transform ) )
			{
				renderer.enabled = true;
				continue;
			}

			if ( renderer.transform.name == "GoldPileTerrain" )
				continue;

			renderer.enabled = false;
		}

		StripLegacyMeshColliders();
	}

	bool IsProtectedAuthoringMesh( Transform t )
	{
		if ( t == null )
			return false;

		Transform cursor = t;
		while ( cursor != null && cursor != transform )
		{
			string name = cursor.name;
			if ( name == AuthoredLootRootName || name == LatentBakePreviewRootName )
				return true;
			if ( cursor.GetComponent<TreasurePileAuthoredItem>() != null )
				return true;
			if ( cursor.GetComponent<TreasureItem>() != null )
				return true;
			cursor = cursor.parent;
		}

		return false;
	}

	/// <summary>
	/// Removes old static MeshColliders on the pile root / art meshes.
	/// Dig collision comes from <see cref="GoldPileColliderTiles"/> only.
	/// </summary>
	void StripLegacyMeshColliders()
	{
		MeshCollider[] colliders = GetComponentsInChildren<MeshCollider>( true );
		for ( int i = 0; i < colliders.Length; i++ )
		{
			MeshCollider col = colliders[ i ];
			if ( col == null )
				continue;

			Transform t = col.transform;
			if ( t.name.StartsWith( "ColliderTile_" )
				|| t.name == "GoldPileColliders"
				|| t.name == "~GoldPileColliders" )
				continue;
			if ( IsProtectedAuthoringMesh( t ) )
				continue;

			if ( Application.isPlaying )
				Destroy( col );
			else
				DestroyImmediate( col );
		}
	}

	void RefreshVisuals( Vector3 worldCenter )
	{
		GoldPileEditTiming.Begin( "GoldPile.RefreshVisuals" );
		System.Diagnostics.Stopwatch phaseSw = GoldPileEditTiming.StartWatchIfEnabled();

		if ( terrainMesh != null )
		{
			terrainMesh.RefreshFromHeightfield();
			if ( phaseSw != null )
			{
				phaseSw.Stop();
				GoldPileEditTiming.Record( "refresh.queueTerrain", phaseSw.Elapsed.TotalMilliseconds );
				phaseSw.Restart();
			}
		}

		float lootRadius = _carveSettings.radius * 2.5f;
		if ( lootInstances != null )
			lootInstances.RefreshAfterCarve( worldCenter, lootRadius, _lastCarveUnits );

		if ( phaseSw != null )
		{
			phaseSw.Stop();
			GoldPileEditTiming.Record( "refresh.queueLoot", phaseSw.Elapsed.TotalMilliseconds );
			phaseSw.Restart();
		}

		bool artifacts = false;
		if ( artifactProps != null
			&& artifactProps.NeedsCarveUpdateNear( worldCenter, lootRadius ) )
		{
			artifactProps.QueueRevealAfterCarve( worldCenter, lootRadius );
			artifacts = true;
		}

		if ( phaseSw != null )
		{
			phaseSw.Stop();
			GoldPileEditTiming.Record(
				"refresh.artifacts",
				phaseSw.Elapsed.TotalMilliseconds,
				artifacts ? "queued" : "skip" );
		}

		GoldPileEditTiming.End();
	}

	public bool TryGetLastInteractPoint( out Vector3 point )
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

		TreasurePileDefinition def = ResolveDefinition();
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

	void OnEnable()
	{
		MapSystem.RegisterPile( this );
	}

	void OnDisable()
	{
		MapSystem.UnregisterPile( this );
	}

	void OnDestroy()
	{
		MapSystem.UnregisterPile( this );
		if ( _heightfield != null )
		{
			_heightfield.Release();
			_heightfield = null;
		}
	}
}
