using System;
using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Heightfield-driven gold pile: displaced terrain + GPU-instanced coins/gems + real artifact props.
/// </summary>
public class TreasurePileVisual : MonoBehaviour, ITreasureOwner
{
	const int AuthoredFormatLegacy = 0;
	const int AuthoredFormatWorldMeters = 1;
	const int DefaultMinResolution = 32;
	const float DefaultCellSize = 0.125f;
	const float DefaultPickRadius = 0.45f;
	const float DefaultLootFloorClearance = 0.05f;
	const float ExpandRimCells = 1.5f;

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
			h = ( h ^ ( uint )AuthoredResolutionX ) * 16777619u;
			h = ( h ^ ( uint )AuthoredResolutionZ ) * 16777619u;
			h = ( h ^ ( uint )AuthoredFloatToBits( AuthoredWorldSizeX ) ) * 16777619u;
			h = ( h ^ ( uint )AuthoredFloatToBits( AuthoredWorldSizeZ ) ) * 16777619u;
			h = ( h ^ ( uint )AuthoredFloatToBits( authoredMaxHeight ) ) * 16777619u;
			if ( def != null )
			{
				h = ( h ^ ( uint )AuthoredFloatToBits( def.groundLevelHeight ) ) * 16777619u;
				h = ( h ^ ( uint )AuthoredFloatToBits( def.lootFloorClearance ) ) * 16777619u;
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
	int authoredRes; // legacy square

	[SerializeField]
	[HideInInspector]
	int authoredResX;

	[SerializeField]
	[HideInInspector]
	int authoredResZ;

	[SerializeField]
	[HideInInspector]
	float authoredWorldSize; // legacy square

	[SerializeField]
	[HideInInspector]
	float authoredWorldSizeX;

	[SerializeField]
	[HideInInspector]
	float authoredWorldSizeZ;

	[SerializeField]
	[HideInInspector]
	float authoredMaxHeight; // pack scale

	[SerializeField]
	[HideInInspector]
	int authoredRevision;

	[SerializeField]
	[HideInInspector]
	int authoredHeightFormat;

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
		&& (
			( authoredHeightFormat == AuthoredFormatWorldMeters
				&& authoredResX >= 8
				&& authoredResZ >= 8
				&& authoredHeights.Length == authoredResX * authoredResZ )
			|| ( authoredRes >= 8 && authoredHeights.Length == authoredRes * authoredRes ) );
	public int AuthoredRevision => authoredRevision;
	public TreasurePileDefinition Definition => ResolveDefinition();
	public int AuthoredResolution => Mathf.Max( authoredResX, authoredResZ, authoredRes );
	public int AuthoredResolutionX => authoredResX > 0 ? authoredResX : authoredRes;
	public int AuthoredResolutionZ => authoredResZ > 0 ? authoredResZ : authoredRes;
	public float AuthoredWorldSize => Mathf.Max( authoredWorldSizeX, authoredWorldSizeZ, authoredWorldSize );
	public float AuthoredWorldSizeX => authoredWorldSizeX > 0.1f ? authoredWorldSizeX : authoredWorldSize;
	public float AuthoredWorldSizeZ => authoredWorldSizeZ > 0.1f ? authoredWorldSizeZ : authoredWorldSize;
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
		float halfX = _heightfield.WorldSizeX * 0.5f;
		float halfZ = _heightfield.WorldSizeZ * 0.5f;
		if ( Mathf.Abs( local.x ) > halfX || Mathf.Abs( local.z ) > halfZ )
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

		if ( !HasAuthoredHeight )
			return false;

		EnsureMigratedAuthoredHeights();
		float sizeX = AuthoredWorldSizeX;
		float sizeZ = AuthoredWorldSizeZ;
		int resX = AuthoredResolutionX;
		int resZ = AuthoredResolutionZ;
		if ( sizeX < 0.01f || sizeZ < 0.01f || resX < 8 || resZ < 8 )
			return false;

		Vector3 local = transform.InverseTransformPoint( worldPos );
		float halfX = sizeX * 0.5f;
		float halfZ = sizeZ * 0.5f;
		if ( Mathf.Abs( local.x ) > halfX || Mathf.Abs( local.z ) > halfZ )
			return false;

		float u = Mathf.Clamp01( ( local.x / sizeX ) + 0.5f );
		float v = Mathf.Clamp01( ( local.z / sizeZ ) + 0.5f );
		float fx = u * ( resX - 1 );
		float fz = v * ( resZ - 1 );
		int x0 = Mathf.Clamp( Mathf.FloorToInt( fx ), 0, resX - 1 );
		int z0 = Mathf.Clamp( Mathf.FloorToInt( fz ), 0, resZ - 1 );
		int x1 = Mathf.Min( x0 + 1, resX - 1 );
		int z1 = Mathf.Min( z0 + 1, resZ - 1 );
		float tx = fx - x0;
		float tz = fz - z0;
		float pack = authoredMaxHeight > 0.01f ? authoredMaxHeight : 1f;
		float h00 = authoredHeights[ z0 * resX + x0 ] / 65535f * pack;
		float h10 = authoredHeights[ z0 * resX + x1 ] / 65535f * pack;
		float h01 = authoredHeights[ z1 * resX + x0 ] / 65535f * pack;
		float h11 = authoredHeights[ z1 * resX + x1 ] / 65535f * pack;
		float h = Mathf.Lerp( Mathf.Lerp( h00, h10, tx ), Mathf.Lerp( h01, h11, tx ), tz );

		float ground = 0.01f;
		TreasurePileDefinition def = ResolveDefinition();
		if ( def != null )
			ground = def.groundLevelHeight;
		return h >= ground;
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
	public int TotalInitialLoot => lootInstances != null ? lootInstances.TotalInitial : 0;
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
			ConfigureTerrainMesh( terrainMesh, pileMaterial, _heightfield.ResolutionX, _heightfield.ResolutionZ, def );
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
		float worldSize = DefaultCellSize * ( DefaultMinResolution - 1 );
		if ( _heightfield != null && _heightfield.IsInitialized )
			worldSize = _heightfield.WorldSize;
		else if ( HasAuthoredHeight )
			worldSize = AuthoredWorldSize;

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
		EnsureMigratedAuthoredHeights();
		ResolveLayout( out int resX, out int resZ, out float sizeX, out float sizeZ );

		if ( _heightfield != null )
			_heightfield.Release();

		_heightfield = new GoldPileHeightfield();
		TreasurePileDefinition def = ResolveDefinition();
		float ground = def != null ? def.groundLevelHeight : 0.01f;
		_heightfield.Initialize( resX, resZ, sizeX, sizeZ, ground );
		_heightfield.SetLootFloorResolver( ResolveLootFloorLocal );
		_heightfield.SetMaxHeightLocked( Application.isPlaying );

		if ( HasAuthoredHeight
			&& authoredHeightFormat == AuthoredFormatWorldMeters
			&& authoredResX == resX
			&& authoredResZ == resZ )
		{
			_heightfield.CopyFromPackedU16( authoredHeights, authoredMaxHeight > 0.01f ? authoredMaxHeight : 1f );
		}
		else
			_heightfield.FillZeros();

		_heightfield.UploadIfDirty();
		_totalUnits = def != null
			? Mathf.Max( 1, def.TotalCoinUnits() )
			: Mathf.Max( 1, _pile != null ? _pile.TotalCount : 1 );
	}

	void ResolveLayout( out int resX, out int resZ, out float sizeX, out float sizeZ )
	{
		TreasurePileDefinition def = ResolveDefinition();
		float cell = def != null ? def.ResolveCellSize() : DefaultCellSize;
		int minRes = def != null ? def.ResolveMinResolution() : DefaultMinResolution;

		if ( HasAuthoredHeight && authoredHeightFormat == AuthoredFormatWorldMeters
			&& authoredResX >= 8 && authoredResZ >= 8 )
		{
			resX = authoredResX;
			resZ = authoredResZ;
			sizeX = authoredWorldSizeX > 0.1f ? authoredWorldSizeX : cell * ( resX - 1 );
			sizeZ = authoredWorldSizeZ > 0.1f ? authoredWorldSizeZ : cell * ( resZ - 1 );
			return;
		}

		resX = minRes;
		resZ = minRes;
		sizeX = cell * ( resX - 1 );
		sizeZ = cell * ( resZ - 1 );
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

	static void ConfigureTerrainMesh(
		GoldPileTerrainMesh mesh,
		Material material,
		int resX,
		int resZ,
		TreasurePileDefinition definition )
	{
		if ( mesh == null )
			return;

		float soften = definition != null ? definition.meshDeformNormalSoften : 0f;
		float blur = definition != null ? definition.meshDeformSampleBlur : 4f;
		mesh.Configure( material, resX, resZ, soften, blur );
	}

	float ResolveLootFloorClearance()
	{
		TreasurePileDefinition def = ResolveDefinition();
		return def != null ? Mathf.Max( 0f, def.lootFloorClearance ) : DefaultLootFloorClearance;
	}

	float ResolveLootFloorLocal( float localX, float localZ )
	{
		float clearance = ResolveLootFloorClearance();
		float ground = 0.01f;
		TreasurePileDefinition def = ResolveDefinition();
		if ( def != null )
			ground = def.groundLevelHeight;

		Vector3 world = transform.TransformPoint( new Vector3( localX, 0f, localZ ) );
		if ( TrySampleAuthoredSurfaceHeight( world, out float worldY ) )
			return Mathf.Max( ground, ( worldY - transform.position.y ) + clearance );

		return ground + clearance;
	}

	bool TrySampleAuthoredSurfaceHeight( Vector3 worldPos, out float worldY )
	{
		worldY = 0f;
		if ( Application.isPlaying )
		{
			TreasureSurfaceWorld surfaceWorld = TreasureSurfaceWorld.Instance;
			if ( surfaceWorld != null && surfaceWorld.IsInitialized && surfaceWorld.Sampler != null )
				return surfaceWorld.Sampler.TrySampleBaseHeight( worldPos, out worldY );
			return false;
		}

#if UNITY_EDITOR
		TreasureSurfaceAuthoring authoring = TreasureSurfaceAuthoring.Instance;
		if ( authoring != null && authoring.TryWorldToCell( worldPos, out int cellX, out int cellZ ) )
		{
			worldY = authoring.GetPaintHeight( cellX, cellZ );
			return true;
		}
#endif
		return false;
	}

	/// <summary>Convert legacy square 0–1 authored heights to world-meter rectangular format.</summary>
	public void EnsureMigratedAuthoredHeights()
	{
		if ( authoredHeights == null )
			return;

		if ( authoredHeightFormat == AuthoredFormatWorldMeters
			&& authoredResX >= 8
			&& authoredResZ >= 8
			&& authoredHeights.Length == authoredResX * authoredResZ )
			return;

		bool legacySquare = authoredRes >= 8 && authoredHeights.Length == authoredRes * authoredRes;
		if ( !legacySquare )
			return;

		TreasurePileDefinition def = ResolveDefinition();
		float cell = def != null ? def.ResolveCellSize() : DefaultCellSize;
		int minRes = def != null ? def.ResolveMinResolution() : DefaultMinResolution;

		int oldRes = authoredRes;
		float oldSize = authoredWorldSize > 0.1f ? authoredWorldSize : cell * ( oldRes - 1 );
		float oldMax = authoredMaxHeight > 0.01f ? authoredMaxHeight : 1f;

		float[] worldH = new float[ oldRes * oldRes ];
		for ( int i = 0; i < worldH.Length; i++ )
			worldH[ i ] = authoredHeights[ i ] * ( 1f / 65535f ) * oldMax;

		// Preserve authored footprint and resolution — do not clamp to definition maxResolution.
		int needed = Mathf.Max( minRes, Mathf.RoundToInt( oldSize / cell ) );
		needed = Mathf.Max( needed, oldRes );
		int newRes = Mathf.NextPowerOfTwo( Mathf.Max( 8, needed ) );
		float newSize = cell * ( newRes - 1 );

		ushort[] packed = new ushort[ newRes * newRes ];
		float oldHalf = oldSize * 0.5f;
		float newHalf = newSize * 0.5f;
		float newStep = newSize / Mathf.Max( 1, newRes - 1 );

		float[] meters = new float[ newRes * newRes ];
		float packScale = 0.01f;
		for ( int z = 0; z < newRes; z++ )
		{
			float lz = -newHalf + z * newStep;
			for ( int x = 0; x < newRes; x++ )
			{
				float lx = -newHalf + x * newStep;
				float h = 0f;
				if ( Mathf.Abs( lx ) <= oldHalf && Mathf.Abs( lz ) <= oldHalf )
				{
					float u = ( lx / oldSize ) + 0.5f;
					float v = ( lz / oldSize ) + 0.5f;
					float fx = Mathf.Clamp01( u ) * ( oldRes - 1 );
					float fz = Mathf.Clamp01( v ) * ( oldRes - 1 );
					int x0 = Mathf.FloorToInt( fx );
					int z0 = Mathf.FloorToInt( fz );
					int x1 = Mathf.Min( x0 + 1, oldRes - 1 );
					int z1 = Mathf.Min( z0 + 1, oldRes - 1 );
					float tx = fx - x0;
					float tz = fz - z0;
					float h00 = worldH[ z0 * oldRes + x0 ];
					float h10 = worldH[ z0 * oldRes + x1 ];
					float h01 = worldH[ z1 * oldRes + x0 ];
					float h11 = worldH[ z1 * oldRes + x1 ];
					h = Mathf.Lerp( Mathf.Lerp( h00, h10, tx ), Mathf.Lerp( h01, h11, tx ), tz );
				}

				meters[ z * newRes + x ] = h;
				if ( h > packScale )
					packScale = h;
			}
		}

		for ( int i = 0; i < meters.Length; i++ )
			packed[ i ] = ( ushort )Mathf.Clamp( Mathf.RoundToInt( ( meters[ i ] / packScale ) * 65535f ), 0, 65535 );

		authoredHeights = packed;
		authoredRes = newRes;
		authoredResX = newRes;
		authoredResZ = newRes;
		authoredWorldSize = newSize;
		authoredWorldSizeX = newSize;
		authoredWorldSizeZ = newSize;
		authoredMaxHeight = packScale;
		authoredHeightFormat = AuthoredFormatWorldMeters;
		authoredRevision++;

#if UNITY_EDITOR
		if ( !Application.isPlaying )
			UnityEditor.EditorUtility.SetDirty( this );
#endif
	}

	public void EnsureAuthoredBuffers()
	{
		EnsureMigratedAuthoredHeights();
		if ( HasAuthoredHeight && authoredHeightFormat == AuthoredFormatWorldMeters )
			return;

		TreasurePileDefinition def = ResolveDefinition();
		float cell = def != null ? def.ResolveCellSize() : DefaultCellSize;
		int minRes = def != null ? def.ResolveMinResolution() : DefaultMinResolution;
		int count = minRes * minRes;
		authoredHeights = new ushort[ count ];
		authoredRes = minRes;
		authoredResX = minRes;
		authoredResZ = minRes;
		authoredWorldSize = cell * ( minRes - 1 );
		authoredWorldSizeX = authoredWorldSize;
		authoredWorldSizeZ = authoredWorldSize;
		authoredMaxHeight = 0.01f;
		authoredHeightFormat = AuthoredFormatWorldMeters;
		authoredRevision++;
	}

	public void WriteAuthoredFromHeightfield()
	{
		if ( _heightfield == null || !_heightfield.IsInitialized )
			return;

		_heightfield.RecomputeMaxHeight();
		authoredResX = _heightfield.ResolutionX;
		authoredResZ = _heightfield.ResolutionZ;
		authoredRes = Mathf.Max( authoredResX, authoredResZ );
		authoredWorldSizeX = _heightfield.WorldSizeX;
		authoredWorldSizeZ = _heightfield.WorldSizeZ;
		authoredWorldSize = Mathf.Max( authoredWorldSizeX, authoredWorldSizeZ );
		authoredMaxHeight = _heightfield.MaxHeight;
		authoredHeightFormat = AuthoredFormatWorldMeters;
		_heightfield.CopyToNormalizedU16( ref authoredHeights );
		authoredRevision++;
	}

	public void ClearAuthoredHeight()
	{
		authoredHeights = null;
		authoredRes = 0;
		authoredResX = 0;
		authoredResZ = 0;
		authoredWorldSize = 0f;
		authoredWorldSizeX = 0f;
		authoredWorldSizeZ = 0f;
		authoredMaxHeight = 0f;
		authoredHeightFormat = AuthoredFormatLegacy;
		authoredRevision++;
	}

	public void ResetAuthoredToEmpty()
	{
		EnsureAuthoredBuffers();
		if ( authoredHeights == null )
			return;

		for ( int i = 0; i < authoredHeights.Length; i++ )
			authoredHeights[ i ] = 0;
		authoredMaxHeight = 0.01f;
		authoredHeightFormat = AuthoredFormatWorldMeters;
		authoredRevision++;
	}

	[System.Obsolete( "Use ResetAuthoredToEmpty" )]
	public void ResetAuthoredToMound()
	{
		ResetAuthoredToEmpty();
	}

	/// <summary>
	/// Builds or refreshes the displaced pile preview in edit mode (no loot, no surface bridge).
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
		EnsureMigratedAuthoredHeights();
		ResolveLayout( out int resX, out int resZ, out float sizeX, out float sizeZ );
		float ground = def != null ? def.groundLevelHeight : 0.01f;

		if ( _heightfield == null || !_heightfield.IsInitialized
			|| _heightfield.ResolutionX != resX
			|| _heightfield.ResolutionZ != resZ
			|| Mathf.Abs( _heightfield.WorldSizeX - sizeX ) > 0.001f
			|| Mathf.Abs( _heightfield.WorldSizeZ - sizeZ ) > 0.001f )
		{
			if ( _heightfield != null )
				_heightfield.Release();
			_heightfield = new GoldPileHeightfield();
			_heightfield.Initialize( resX, resZ, sizeX, sizeZ, ground );
		}
		else
			_heightfield.SetGroundLevel( ground );

		_heightfield.SetLootFloorResolver( ResolveLootFloorLocal );
		_heightfield.SetMaxHeightLocked( false );

		if ( HasAuthoredHeight && authoredHeightFormat == AuthoredFormatWorldMeters
			&& authoredResX == resX && authoredResZ == resZ )
		{
			_heightfield.CopyFromPackedU16( authoredHeights, authoredMaxHeight > 0.01f ? authoredMaxHeight : 1f );
		}
		else
		{
			_heightfield.FillZeros();
			WriteAuthoredFromHeightfield();
		}

		_heightfield.UploadIfDirty();

		if ( terrainMesh != null )
		{
			ConfigureTerrainMesh( terrainMesh, pileMaterial, resX, resZ, def );
			terrainMesh.Bind( _heightfield, syncCollider: false );
			terrainMesh.SetVisible( true );
		}

		HideLegacyStaticMeshes();
	}

	/// <summary>Expand power-of-two footprint when a brush approaches the rim (editor only).</summary>
	public bool TryExpandForBrush( Vector3 worldPos, float radius )
	{
		if ( Application.isPlaying || _heightfield == null || !_heightfield.IsInitialized )
			return false;

		TreasurePileDefinition def = ResolveDefinition();
		int maxRes = def != null ? def.ResolveMaxResolution() : 256;
		float cell = def != null ? def.ResolveCellSize() : DefaultCellSize;

		Vector3 local = transform.InverseTransformPoint( worldPos );
		float halfX = _heightfield.WorldSizeX * 0.5f;
		float halfZ = _heightfield.WorldSizeZ * 0.5f;
		float rim = Mathf.Max( radius, cell * ExpandRimCells );

		bool needX = ( local.x + rim > halfX || local.x - rim < -halfX )
			&& _heightfield.ResolutionX < maxRes;
		bool needZ = ( local.z + rim > halfZ || local.z - rim < -halfZ )
			&& _heightfield.ResolutionZ < maxRes;
		if ( !needX && !needZ )
			return false;

		int newResX = needX
			? Mathf.Min( maxRes, Mathf.NextPowerOfTwo( _heightfield.ResolutionX + 1 ) )
			: _heightfield.ResolutionX;
		int newResZ = needZ
			? Mathf.Min( maxRes, Mathf.NextPowerOfTwo( _heightfield.ResolutionZ + 1 ) )
			: _heightfield.ResolutionZ;
		if ( newResX == _heightfield.ResolutionX && newResZ == _heightfield.ResolutionZ )
			return false;

		ResampleHeightfieldTo( newResX, newResZ, shiftOrigin: false );
		return true;
	}

	/// <summary>Crop to occupied power-of-two rectangle and recenter transform (editor only).</summary>
	public void FitBounds()
	{
		if ( Application.isPlaying || _heightfield == null || !_heightfield.IsInitialized )
			return;

		TreasurePileDefinition def = ResolveDefinition();
		float cell = def != null ? def.ResolveCellSize() : DefaultCellSize;
		int minRes = def != null ? def.ResolveMinResolution() : DefaultMinResolution;
		int maxRes = def != null ? def.ResolveMaxResolution() : 256;
		float ground = _heightfield.GroundLevel;

		int resX = _heightfield.ResolutionX;
		int resZ = _heightfield.ResolutionZ;
		int minOX = resX;
		int maxOX = -1;
		int minOZ = resZ;
		int maxOZ = -1;
		for ( int z = 0; z < resZ; z++ )
		{
			for ( int x = 0; x < resX; x++ )
			{
				if ( _heightfield.GetCellHeight( x, z ) < ground )
					continue;
				if ( x < minOX ) minOX = x;
				if ( x > maxOX ) maxOX = x;
				if ( z < minOZ ) minOZ = z;
				if ( z > maxOZ ) maxOZ = z;
			}
		}

		if ( maxOX < 0 )
		{
			ResampleHeightfieldTo( minRes, minRes, shiftOrigin: false );
			_heightfield.FillZeros();
			WriteAuthoredFromHeightfield();
			EnsureEditorPreview();
			return;
		}

		const int pad = 1;
		minOX = Mathf.Max( 0, minOX - pad );
		minOZ = Mathf.Max( 0, minOZ - pad );
		maxOX = Mathf.Min( resX - 1, maxOX + pad );
		maxOZ = Mathf.Min( resZ - 1, maxOZ + pad );
		int spanX = maxOX - minOX + 1;
		int spanZ = maxOZ - minOZ + 1;
		int newResX = Mathf.Clamp( Mathf.NextPowerOfTwo( Mathf.Max( minRes, spanX ) ), minRes, maxRes );
		int newResZ = Mathf.Clamp( Mathf.NextPowerOfTwo( Mathf.Max( minRes, spanZ ) ), minRes, maxRes );

		float oldHalfX = _heightfield.WorldSizeX * 0.5f;
		float oldHalfZ = _heightfield.WorldSizeZ * 0.5f;
		float stepX = _heightfield.LocalCellSizeX;
		float stepZ = _heightfield.LocalCellSizeZ;
		float occMinX = -oldHalfX + minOX * stepX;
		float occMaxX = -oldHalfX + maxOX * stepX;
		float occMinZ = -oldHalfZ + minOZ * stepZ;
		float occMaxZ = -oldHalfZ + maxOZ * stepZ;
		float occCenterX = ( occMinX + occMaxX ) * 0.5f;
		float occCenterZ = ( occMinZ + occMaxZ ) * 0.5f;

		float[] src = new float[ resX * resZ ];
		for ( int z = 0; z < resZ; z++ )
			for ( int x = 0; x < resX; x++ )
				src[ z * resX + x ] = _heightfield.GetCellHeight( x, z );

		float newSizeX = cell * ( newResX - 1 );
		float newSizeZ = cell * ( newResZ - 1 );
		float newHalfX = newSizeX * 0.5f;
		float newHalfZ = newSizeZ * 0.5f;
		float newStepX = newSizeX / Mathf.Max( 1, newResX - 1 );
		float newStepZ = newSizeZ / Mathf.Max( 1, newResZ - 1 );

		float[] dst = new float[ newResX * newResZ ];
		for ( int z = 0; z < newResZ; z++ )
		{
			float lz = -newHalfZ + z * newStepZ + occCenterZ;
			for ( int x = 0; x < newResX; x++ )
			{
				float lx = -newHalfX + x * newStepX + occCenterX;
				float u = ( lx + oldHalfX ) / stepX;
				float v = ( lz + oldHalfZ ) / stepZ;
				int x0 = Mathf.FloorToInt( u );
				int z0 = Mathf.FloorToInt( v );
				if ( x0 < 0 || z0 < 0 || x0 >= resX || z0 >= resZ )
					continue;
				int x1 = Mathf.Min( x0 + 1, resX - 1 );
				int z1 = Mathf.Min( z0 + 1, resZ - 1 );
				float tx = u - x0;
				float tz = v - z0;
				float h00 = src[ z0 * resX + x0 ];
				float h10 = src[ z0 * resX + x1 ];
				float h01 = src[ z1 * resX + x0 ];
				float h11 = src[ z1 * resX + x1 ];
				dst[ z * newResX + x ] = Mathf.Lerp( Mathf.Lerp( h00, h10, tx ), Mathf.Lerp( h01, h11, tx ), tz );
			}
		}

		Vector3 shift = transform.TransformVector( new Vector3( occCenterX, 0f, occCenterZ ) );
		transform.position += shift;
		Transform authoredRoot = FindAuthoredLootRoot();
		if ( authoredRoot != null )
		{
			for ( int i = 0; i < authoredRoot.childCount; i++ )
			{
				Transform child = authoredRoot.GetChild( i );
				child.localPosition -= new Vector3( occCenterX, 0f, occCenterZ );
			}
		}

		float groundLevel = def != null ? def.groundLevelHeight : 0.01f;
		_heightfield.Release();
		_heightfield = new GoldPileHeightfield();
		_heightfield.Initialize( newResX, newResZ, newSizeX, newSizeZ, groundLevel );
		_heightfield.SetLootFloorResolver( ResolveLootFloorLocal );
		_heightfield.SetMaxHeightLocked( false );

		float pack = 0.01f;
		for ( int i = 0; i < dst.Length; i++ )
			if ( dst[ i ] > pack ) pack = dst[ i ];
		ushort[] packed = new ushort[ dst.Length ];
		for ( int i = 0; i < dst.Length; i++ )
			packed[ i ] = ( ushort )Mathf.Clamp( Mathf.RoundToInt( ( dst[ i ] / pack ) * 65535f ), 0, 65535 );
		_heightfield.CopyFromPackedU16( packed, pack );
		WriteAuthoredFromHeightfield();
		EnsureEditorPreview();
	}

	void ResampleHeightfieldTo( int newResX, int newResZ, bool shiftOrigin )
	{
		if ( _heightfield == null || !_heightfield.IsInitialized )
			return;

		TreasurePileDefinition def = ResolveDefinition();
		float cell = def != null ? def.ResolveCellSize() : DefaultCellSize;
		float ground = def != null ? def.groundLevelHeight : 0.01f;
		int oldResX = _heightfield.ResolutionX;
		int oldResZ = _heightfield.ResolutionZ;
		float oldSizeX = _heightfield.WorldSizeX;
		float oldSizeZ = _heightfield.WorldSizeZ;
		float oldHalfX = oldSizeX * 0.5f;
		float oldHalfZ = oldSizeZ * 0.5f;
		float[] src = new float[ oldResX * oldResZ ];
		for ( int z = 0; z < oldResZ; z++ )
			for ( int x = 0; x < oldResX; x++ )
				src[ z * oldResX + x ] = _heightfield.GetCellHeight( x, z );

		float newSizeX = cell * ( newResX - 1 );
		float newSizeZ = cell * ( newResZ - 1 );
		float newHalfX = newSizeX * 0.5f;
		float newHalfZ = newSizeZ * 0.5f;
		float newStepX = newSizeX / Mathf.Max( 1, newResX - 1 );
		float newStepZ = newSizeZ / Mathf.Max( 1, newResZ - 1 );
		float[] dst = new float[ newResX * newResZ ];

		for ( int z = 0; z < newResZ; z++ )
		{
			float lz = -newHalfZ + z * newStepZ;
			for ( int x = 0; x < newResX; x++ )
			{
				float lx = -newHalfX + x * newStepX;
				if ( Mathf.Abs( lx ) > oldHalfX || Mathf.Abs( lz ) > oldHalfZ )
					continue;
				float u = ( lx / oldSizeX ) + 0.5f;
				float v = ( lz / oldSizeZ ) + 0.5f;
				float fx = Mathf.Clamp01( u ) * ( oldResX - 1 );
				float fz = Mathf.Clamp01( v ) * ( oldResZ - 1 );
				int x0 = Mathf.FloorToInt( fx );
				int z0 = Mathf.FloorToInt( fz );
				int x1 = Mathf.Min( x0 + 1, oldResX - 1 );
				int z1 = Mathf.Min( z0 + 1, oldResZ - 1 );
				float tx = fx - x0;
				float tz = fz - z0;
				float h00 = src[ z0 * oldResX + x0 ];
				float h10 = src[ z0 * oldResX + x1 ];
				float h01 = src[ z1 * oldResX + x0 ];
				float h11 = src[ z1 * oldResX + x1 ];
				dst[ z * newResX + x ] = Mathf.Lerp( Mathf.Lerp( h00, h10, tx ), Mathf.Lerp( h01, h11, tx ), tz );
			}
		}

		_heightfield.Release();
		_heightfield = new GoldPileHeightfield();
		_heightfield.Initialize( newResX, newResZ, newSizeX, newSizeZ, ground );
		_heightfield.SetLootFloorResolver( ResolveLootFloorLocal );
		_heightfield.SetMaxHeightLocked( false );
		float pack = 0.01f;
		for ( int i = 0; i < dst.Length; i++ )
			if ( dst[ i ] > pack ) pack = dst[ i ];
		ushort[] packed = new ushort[ dst.Length ];
		for ( int i = 0; i < dst.Length; i++ )
			packed[ i ] = ( ushort )Mathf.Clamp( Mathf.RoundToInt( ( dst[ i ] / pack ) * 65535f ), 0, 65535 );
		_heightfield.CopyFromPackedU16( packed, pack );
		WriteAuthoredFromHeightfield();
		EnsureEditorPreview();
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

		_heightfield.RecomputeMaxHeight();
		WriteAuthoredFromHeightfield();
		RefreshEditorPreviewFromHeightfield();
	}

	public bool TryEditorBrushWorld( Vector3 worldPos, GoldPileEditorBrushMode mode, in GoldPileBrushParams p )
	{
		if ( Application.isPlaying || _heightfield == null || !_heightfield.IsInitialized )
			return false;

		if ( mode == GoldPileEditorBrushMode.ResetMound )
		{
			_heightfield.FillZeros();
			return true;
		}

		float brushRadius = mode == GoldPileEditorBrushMode.Ridge
			? ( p.ridgeWidth > 0.05f ? p.ridgeWidth : p.radius )
			: p.radius;
		TryExpandForBrush( worldPos, brushRadius );

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

	void ResolveGizmoSize( out float sizeX, out float sizeZ, out float maxH )
	{
		sizeX = DefaultCellSize * ( DefaultMinResolution - 1 );
		sizeZ = sizeX;
		maxH = 1f;

		if ( _heightfield != null && _heightfield.IsInitialized )
		{
			sizeX = _heightfield.WorldSizeX;
			sizeZ = _heightfield.WorldSizeZ;
			maxH = Mathf.Max( 0.01f, _heightfield.MaxHeight );
			return;
		}

		if ( HasAuthoredHeight )
		{
			sizeX = AuthoredWorldSizeX;
			sizeZ = AuthoredWorldSizeZ;
			maxH = Mathf.Max( 0.01f, authoredMaxHeight );
		}
	}

	void OnDrawGizmosSelected()
	{
		ResolveGizmoSize( out float sizeX, out float sizeZ, out float maxH );

		Matrix4x4 matrix = transform.localToWorldMatrix;
		Gizmos.matrix = matrix;
		Gizmos.color = new Color( 1f, 0.85f, 0.2f, 0.55f );
		Gizmos.DrawWireCube( new Vector3( 0f, maxH * 0.5f, 0f ), new Vector3( sizeX, maxH, sizeZ ) );
		Gizmos.color = new Color( 1f, 0.75f, 0.1f, 0.9f );
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
