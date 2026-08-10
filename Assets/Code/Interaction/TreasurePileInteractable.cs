using System.Collections;
using System.Collections.Generic;

using UnityEngine;

public class TreasurePileInteractable : StackInteractable, ITreasurePlacementTarget
{
	[SerializeField]
	TreasurePileDefinition pileDefinition;

	[SerializeField]
	TreasurePileVisual pileVisual;

	bool _bound;

	static readonly List<TreasureDefinition> StealDefs = new List<TreasureDefinition>( 128 );

	public TreasurePileDefinition PileDefinition => pileDefinition;
	public TreasurePileVisual PileVisual => pileVisual;

	protected override void Reset()
	{
		EnsureFallbackName( "Treasure Pile" );
	}

	protected override void Awake()
	{
		EnsureFallbackName( "Treasure Pile" );
		ApplyDefinitionCounts();
		base.Awake();

		if ( pileVisual == null )
			pileVisual = GetComponent<TreasurePileVisual>();

		// Do not Bind here — Integrate/Awake must stay light. Bind runs from Start after
		// LoadSceneAsync finishes so PhysX tile cooks and heightfield setup are not in Integrate.
	}

	void Start()
	{
		TryBindVisual();
	}

	public void SetupPile( TreasurePileDefinition definition, TreasurePileVisual visual )
	{
		if ( definition != null )
			pileDefinition = definition;
		if ( visual != null )
			pileVisual = visual;

		ApplyDefinitionCounts();
		_bound = false;
		TryBindVisual();
	}

	/// <summary>Legacy greybox: single-type count when no definition asset is set.</summary>
	public void SetupPile( int coinCount, TreasurePileVisual visual )
	{
		if ( visual != null )
			pileVisual = visual;

		InitializeCount( coinCount );
		_bound = false;
		TryBindVisual();
	}

	void ApplyDefinitionCounts()
	{
		if ( pileDefinition == null )
			return;

		int total = pileDefinition.TotalUnits();
		InitializeCount( total );
		TreasureDefinition primary = pileDefinition.GetPrimaryTreasure();
		if ( primary != null )
			SetTreasureDefinition( primary );

		if ( !string.IsNullOrEmpty( pileDefinition.displayName ) )
			SetInteractionName( pileDefinition.displayName );
	}

	void TryBindVisual()
	{
		if ( _bound )
			return;

		if ( pileVisual == null )
			pileVisual = GetComponent<TreasurePileVisual>();

		if ( pileVisual == null )
			return;

		pileVisual.Bind( this );
		_bound = true;
	}

	public bool CanTakeUnit()
	{
		return RemainingCount > 0;
	}

	public void NotifyUnitStolen()
	{
		ConsumeOneUnit();
	}

	public override bool CanInteract( PlayerController player )
	{
		if ( player == null || RemainingCount <= 0 )
			return false;

		TreasureDefinition probe = Treasure;
		if ( pileVisual != null )
		{
			TreasureDefinition any = pileVisual.GetAnyRemainingDefinition();
			if ( any != null )
				probe = any;
		}

		if ( probe != null )
			return player.Carry != null && player.Carry.CanAdd( probe );

		return base.CanInteract( player );
	}

	public override void Interact( PlayerController player )
	{
		if ( player == null || RemainingCount <= 0 )
			return;

		StealFromPileAsync( player );
	}

	async void StealFromPileAsync( PlayerController player )
	{
		if ( player == null || RemainingCount <= 0 )
			return;

		if ( pileVisual == null )
			pileVisual = GetComponent<TreasurePileVisual>();

		if ( pileVisual == null )
			return;

		Vector3 digPoint = transform.position;
		PlayerInteraction interaction = player.Interaction;
		RaycastHit hit = default;
		bool hasHit = interaction != null && interaction.TryGetLastHit( out hit );
		if ( hasHit )
		{
			if ( pileVisual.HasPileSurfaceAt( hit.point ) )
			{
				pileVisual.SetLastInteractPoint( hit.point );
				digPoint = hit.point;
			}
			else
			{
				hasHit = false;
			}
		}

		PlayerCarry carry = player.Carry;
		if ( carry == null )
			return;

		int want = TreasurePilePull.ResolveUnitsPerInteract( player );
		if ( want <= 0 )
			return;

		// Single aimed instance: spawn the physical coin into hand (one Addressables hit).
		if ( want <= 1
			&& hasHit
			&& pileVisual.TryPickLootInstance(
				hit.point,
				out int slotIndex,
				out TreasureDefinition pickedDef,
				out Vector3 pickedPos,
				out Quaternion pickedRot )
			&& pickedDef != null
			&& carry.CanAdd( pickedDef ) )
		{
			await StealSingleInstanceAsync( player, slotIndex, pickedDef, pickedPos, pickedRot );
			return;
		}

		// Multi-take (and blank-mound dig): batch consume + TryAddMany (one visibility rebuild,
		// one held mesh spawn). Matches debug Fill cost profile.
		int batchCap = want;
		if ( pileVisual.TotalRemainingLoot > 0 )
			batchCap = Mathf.Min( batchCap, pileVisual.TotalRemainingLoot );
		batchCap = Mathf.Min( batchCap, RemainingCount );
		if ( batchCap <= 0 )
			return;

		TreasureDefinition probe = pileVisual.GetAnyRemainingDefinition();
		if ( probe == null )
			return;

		batchCap = Mathf.Min( batchCap, carry.CountAffordableUnits( probe, batchCap ) );
		if ( batchCap <= 0 )
			return;

		StealDefs.Clear();
		Vector3 carvePos = digPoint;

		// If aiming a visible instance, consume that slot first (no mesh spawn).
		if ( hasHit
			&& pileVisual.TryPickLootInstance(
				hit.point,
				out int multiSlot,
				out TreasureDefinition multiDef,
				out Vector3 multiPos,
				out _ )
			&& multiDef != null
			&& carry.CanAdd( multiDef )
			&& pileVisual.TryConsumeLootSlot( multiSlot, out TreasureDefinition multiTaken ) )
		{
			TreasureDefinition def = multiTaken != null ? multiTaken : multiDef;
			StealDefs.Add( def );
			carvePos = multiPos;
		}

		int stillWant = batchCap - StealDefs.Count;
		if ( stillWant > 0 )
		{
			int dug = pileVisual.TryConsumeManyFromInventory(
				player,
				digPoint,
				stillWant,
				StealDefs,
				out Vector3 dugPos );
			if ( dug > 0 )
				carvePos = dugPos;
		}

		int taken = StealDefs.Count;
		if ( taken <= 0 )
		{
			SyncRemainingFromVisual();
			return;
		}

		taken = carry.TryAddMany( StealDefs );
		if ( taken <= 0 )
		{
			SyncRemainingFromVisual();
			return;
		}

		pileVisual.CarveForUnitsTaken( carvePos, taken );
		NotifyCollected( StealDefs[ taken - 1 ], taken );
		SyncRemainingFromVisual();
		if ( RemainingCount <= 0 )
			OnEmptied();
	}

	async System.Threading.Tasks.Task StealSingleInstanceAsync(
		PlayerController player,
		int slotIndex,
		TreasureDefinition def,
		Vector3 worldPos,
		Quaternion worldRot )
	{
		if ( player == null || pileVisual == null || def == null )
			return;

		PlayerCarry carry = player.Carry;
		if ( carry == null || !carry.CanAdd( def ) )
			return;

		if ( !pileVisual.TryConsumeLootSlot( slotIndex, out TreasureDefinition takenDef ) )
			return;

		if ( takenDef != null )
			def = takenDef;

		TreasureItem spawned = await TreasureItemFactory.SpawnAsync( def, worldPos, worldRot, null );
		if ( spawned == null )
		{
			SyncRemainingFromVisual();
			return;
		}

		spawned.SetOriginPile( pileVisual );
		spawned.ApplyWorldScale();

		if ( !player.TryReceiveTreasureItem( spawned ) )
		{
			TreasureItemFactory.Despawn( spawned );
			SyncRemainingFromVisual();
			return;
		}

		pileVisual.CarveForUnitsTaken( worldPos, 1 );
		NotifyCollected( def, 1 );
		SyncRemainingFromVisual();
		if ( RemainingCount <= 0 )
			OnEmptied();
	}

	void NotifyCollected( TreasureDefinition def, int amount = 1 )
	{
		if ( amount <= 0 )
			return;

		TreasureCounterManager manager = TreasureCounterManager.Instance;
		if ( manager != null && def != null )
			manager.NotifyCollected( def, amount );
	}

	public void SyncRemainingFromVisual()
	{
		int remaining = pileVisual != null ? pileVisual.TotalRemainingLoot : 0;
		SetRemainingCount( remaining );
	}

	public void OnEmptiedFromVisual()
	{
		SyncRemainingFromVisual();
		if ( RemainingCount <= 0 )
			OnEmptied();
	}

	protected override void OnUnitTaken()
	{
		// Inventory path uses NotifyCollected directly; keep legacy carve if ConsumeOneUnit is used.
		if ( pileVisual != null )
			pileVisual.OnCoinsTaken( 1 );

		TreasureCounterManager manager = TreasureCounterManager.Instance;
		if ( manager != null && Treasure != null )
			manager.NotifyCollected( Treasure, 1 );
	}

	protected override void OnEmptied()
	{
		if ( pileVisual != null )
			pileVisual.OnPileEmptied();

		EventBus.Publish( new TreasurePileEmptiedEvent
		{
			Pile = this,
			Treasure = Treasure
		} );
	}

	public bool CanPlace( TreasureItem item, in PlacementQuery query )
	{
		// Placing/throwing back into a gold pile is disabled — items use surface physics
		// and flow down the stamped mound instead.
		return false;
	}

	public bool TryGetPlacementPreview( TreasureItem item, in PlacementQuery query, out PlacementPreview preview )
	{
		preview = default;
		if ( item == null )
			return false;

		Vector3 pos = query.HasHit ? query.Hit.point : transform.position;
		if ( pileVisual != null && pileVisual.Heightfield != null )
		{
			float h = pileVisual.Heightfield.SampleWorldHeight( pos, pileVisual.transform );
			Vector3 local = pileVisual.transform.InverseTransformPoint( pos );
			pos = pileVisual.transform.TransformPoint( new Vector3( local.x, h + 0.04f, local.z ) );
		}

		preview.Position = pos;
		preview.Rotation = Quaternion.identity;
		preview.Scale = item.GetWorldScale();
		preview.IsValid = CanPlace( item, in query );
		preview.GhostStyle = PlacementGhostStyle.Suppressed;
		return true;
	}

	public bool TryPlace( TreasureItem item, in PlacementQuery query )
	{
		if ( !CanPlace( item, in query ) )
			return false;

		PlayerController player = query.Player;
		if ( player == null )
			return false;

		PlayerCarry carry = player.Carry;
		if ( carry == null )
			return false;

		if ( !carry.TryConsumeActive( out TreasureItem removed ) || removed == null || removed != item )
		{
			if ( removed != null && removed != item )
				removed.EnterPhysics( removed.transform.position, removed.transform.rotation );
			return false;
		}

		if ( !TryGetPlacementPreview( item, in query, out PlacementPreview preview ) )
			preview.Position = query.HasHit ? query.Hit.point : transform.position;

		StartCoroutine( DepositIntoPileRoutine( removed, preview.Position, player ) );
		return true;
	}

	public void Remove( TreasureItem item )
	{
	}

	IEnumerator DepositIntoPileRoutine( TreasureItem item, Vector3 targetPos, PlayerController player )
	{
		if ( item == null || pileVisual == null )
			yield break;

		TreasureDefinition def = item.Definition;
		Transform t = item.transform;
		Vector3 startPos = t.position;
		Quaternion startRot = t.rotation;
		item.BeginFlight();
		item.ApplyWorldScale();

		bool flipCoin = CoinFlipMotion.IsCoin( item );
		float duration = flipCoin ? CoinFlipMotion.DefaultDuration : CoinFlipMotion.DefaultItemArcDuration;
		float arcHeight = flipCoin ? CoinFlipMotion.DefaultArcHeight * 0.45f : CoinFlipMotion.DefaultItemArcHeight * 2f;
		float spins = flipCoin ? CoinFlipMotion.DefaultSpins : 0f;

		PlayerPlacement placement = player != null ? player.Placement : null;
		if ( placement != null )
		{
			arcHeight = Mathf.Max( arcHeight, placement.PlacementArcHeight );
			if ( flipCoin )
				duration = Mathf.Max( 0.05f, duration / Mathf.Max( 0.1f, placement.CoinFlipSpeed ) );
		}

		Quaternion endRot = Quaternion.identity;
		float elapsed = 0f;
		while ( elapsed < duration )
		{
			elapsed += Time.deltaTime;
			float u = Mathf.Clamp01( elapsed / duration );
			if ( flipCoin )
			{
				t.position = CoinFlipMotion.EvaluateArcPosition( startPos, targetPos, u, arcHeight );
				t.rotation = CoinFlipMotion.EvaluateFlipRotation( startRot, endRot, startPos, targetPos, u, spins );
			}
			else
			{
				t.position = CoinFlipMotion.EvaluateArcPosition( startPos, targetPos, u, arcHeight );
				t.rotation = Quaternion.Slerp( startRot, endRot, CoinFlipMotion.SmoothStep( u ) );
			}

			yield return null;
		}

		t.position = targetPos;
		t.rotation = endRot;

		if ( GoldPileArtifactProps.IsLargeProp( def ) )
		{
			if ( pileVisual.AbsorbLooseItemAt( item, targetPos ) )
			{
				item.EndFlight();
				yield break;
			}

			item.EndFlight();
			if ( TreasureItem.UsesSurfaceSimulation( def ) )
				item.EnterSurface( targetPos, endRot, Vector3.zero );
			else
				item.EnterPhysics( targetPos, endRot, Vector3.zero );
			yield break;
		}

		if ( pileVisual.TryDepositTreasure( def, targetPos, out Vector3 depositPos, out _, out _ ) )
		{
			pileVisual.DepositForUnitReturned( depositPos );
			SyncRemainingFromVisual();
			if ( !gameObject.activeSelf )
				gameObject.SetActive( true );

			item.EndFlight();
			TreasureItemFactory.Despawn( item );
		}
		else
		{
			item.EndFlight();
			if ( TreasureItem.UsesSurfaceSimulation( def ) )
				item.EnterSurface( targetPos, endRot, Vector3.zero );
			else
				item.EnterPhysics( targetPos, endRot, Vector3.zero );
		}
	}
}
