using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Hold E to absorb an entire sorted coin stack / gem pyramid into the left-hand collection.
/// Hold F to place the entire selected-category collection as a group.
/// </summary>
public class PlayerWholeStackInteraction : MonoBehaviour
{
	enum ChargeMode
	{
		None,
		Pickup,
		Place
	}

	static readonly List<TreasureDefinition> DefinitionScratch = new List<TreasureDefinition>( 128 );
	static readonly List<TreasureItem> ItemScratch = new List<TreasureItem>( 128 );
	static readonly List<TreasureItem> PlaceScratch = new List<TreasureItem>( 128 );
	static readonly List<TreasureDefinition> UniqueCoinTypesScratch = new List<TreasureDefinition>( 16 );
	static readonly List<TreasureDefinition> TypeBatchScratch = new List<TreasureDefinition>( 64 );
	static readonly List<TreasureDefinition> RemainderScratch = new List<TreasureDefinition>( 128 );

	PlayerController _player;
	PlayerInteraction _interaction;
	PlayerPlacement _placement;
	bool _inputEnabled = true;

	ChargeMode _mode;
	float _charge;
	float _holdSeconds = 5f;
	object _pickupTargetKey;
	GroundCoinStack _pickupCoinStack;
	GroundGoldBarStack _pickupGoldBarStack;
	GemPyramidCluster _pickupPyramid;
	ITreasureDisplayStackOwner _pickupDisplay;
	int _pickupDisplaySlot = -1;
	Vector3 _placePos;
	Quaternion _placeRot;
	bool _placeValid;
	GroundCoinStack _placeCoinTarget;
	GroundGoldBarStack _placeGoldBarTarget;
	CoinSortingHopper _placeHopper;
	ITreasureDisplayStackOwner _placeDisplay;
	int _placeDisplaySlot = -1;

	public float ChargeProgress01 =>
		_mode == ChargeMode.None || _holdSeconds <= 0.01f
			? 0f
			: Mathf.Clamp01( _charge / _holdSeconds );

	public bool IsChargingPickup => _mode == ChargeMode.Pickup;
	public bool IsChargingPlace => _mode == ChargeMode.Place;
	public bool IsPlaceChargeInvalid => _mode == ChargeMode.Place && !_placeValid;

	public bool CanOfferWholeStackPickup
	{
		get
		{
			ResolvePickupTarget(
				out GroundCoinStack stack,
				out GroundGoldBarStack barStack,
				out GemPyramidCluster pyramid,
				out ITreasureDisplayStackOwner display,
				out int displaySlot );
			return stack != null
				|| barStack != null
				|| pyramid != null
				|| ( display != null && displaySlot >= 0 );
		}
	}

	public bool CanOfferWholeStackPlace
	{
		get
		{
			PlayerCarry carry = _player != null ? _player.Carry : null;
			if ( carry == null || carry.Count <= 0 )
				return false;

			return TryResolveWholePlace( out _, out _, out _, out _, out _, out _, out _, out _ );
		}
	}

	public void Setup( PlayerController player, PlayerInteraction interaction, PlayerPlacement placement )
	{
		_player = player;
		_interaction = interaction;
		_placement = placement;
	}

	public void SetInputEnabled( bool enabled )
	{
		_inputEnabled = enabled;
		if ( !_inputEnabled )
			CancelCharge();
	}

	void Update()
	{
		if ( !_inputEnabled || _player == null )
		{
			CancelCharge();
			return;
		}

		PlayerSorterReposition sorter = _player.SorterReposition;
		if ( sorter != null && sorter.IsBusy )
		{
			CancelCharge();
			return;
		}

		GameInput input = GetGameInput();
		if ( input == null )
		{
			CancelCharge();
			return;
		}

		RefreshHoldSeconds();
		TryCategorySwitch( input );

		bool pickupHeld = input.WholeStackPickup != null && input.WholeStackPickup.IsPressed();
		bool placeHeld = input.WholeStackPlace != null && input.WholeStackPlace.IsPressed();

		if ( pickupHeld && !placeHeld )
		{
			TickPickupCharge();
			return;
		}

		if ( placeHeld && !pickupHeld )
		{
			TickPlaceCharge();
			return;
		}

		CancelCharge();
	}

	void RefreshHoldSeconds()
	{
		PlayerCarry carry = _player != null ? _player.Carry : null;
		if ( carry == null )
		{
			_holdSeconds = 5f;
			return;
		}

		int quantity = 1;
		if ( _mode == ChargeMode.Pickup )
		{
			if ( _pickupCoinStack != null )
				quantity = _pickupCoinStack.Count;
			else if ( _pickupGoldBarStack != null )
				quantity = _pickupGoldBarStack.Count;
			else if ( _pickupPyramid != null )
				quantity = _pickupPyramid.Count;
			else if ( _pickupDisplay != null && _pickupDisplaySlot >= 0 )
				quantity = _pickupDisplay.GetSlotCount( _pickupDisplaySlot );
		}
		else if ( _mode == ChargeMode.Place )
		{
			quantity = carry.GetBucketCount( carry.SelectedBucket );
		}
		else if ( ResolvePickupTarget(
			out GroundCoinStack stack,
			out GroundGoldBarStack barStack,
			out GemPyramidCluster pyramid,
			out ITreasureDisplayStackOwner display,
			out int displaySlot ) )
		{
			if ( stack != null )
				quantity = stack.Count;
			else if ( barStack != null )
				quantity = barStack.Count;
			else if ( pyramid != null )
				quantity = pyramid.Count;
			else
				quantity = display.GetSlotCount( displaySlot );
		}
		else if ( carry.Count > 0 )
		{
			quantity = carry.GetBucketCount( carry.SelectedBucket );
		}

		_holdSeconds = carry.GetWholeStackHoldSeconds( quantity );
	}

	void TryCategorySwitch( GameInput input )
	{
		if ( input.CategorySlots == null )
			return;

		PlayerCarry carry = _player != null ? _player.Carry : null;
		if ( carry == null )
			return;

		InputActionSlotPressed( input, 0, CarryBucketKind.Coin, carry );
		InputActionSlotPressed( input, 1, CarryBucketKind.Gem, carry );
		InputActionSlotPressed( input, 2, CarryBucketKind.Artifact, carry );
	}

	static void InputActionSlotPressed( GameInput input, int index, CarryBucketKind kind, PlayerCarry carry )
	{
		if ( index < 0 || index >= input.CategorySlots.Length )
			return;

		UnityEngine.InputSystem.InputAction action = input.CategorySlots[ index ];
		if ( action == null || !action.WasPressedThisFrame() )
			return;

		carry.TrySetSelectedBucket( kind );
	}

	void TickPickupCharge()
	{
		if ( !ResolvePickupTarget(
			out GroundCoinStack stack,
			out GroundGoldBarStack barStack,
			out GemPyramidCluster pyramid,
			out ITreasureDisplayStackOwner display,
			out int displaySlot ) )
		{
			CancelCharge();
			return;
		}

		object key = stack != null
			? (object)stack
			: barStack != null
				? (object)barStack
				: pyramid != null ? (object)pyramid : display;
		bool sameTarget = _mode == ChargeMode.Pickup
			&& ReferenceEquals( _pickupTargetKey, key )
			&& _pickupDisplaySlot == displaySlot;
		if ( !sameTarget )
		{
			_mode = ChargeMode.Pickup;
			_charge = 0f;
			_pickupTargetKey = key;
			_pickupCoinStack = stack;
			_pickupGoldBarStack = barStack;
			_pickupPyramid = pyramid;
			_pickupDisplay = display;
			_pickupDisplaySlot = displaySlot;
			_placeCoinTarget = null;
			_placeGoldBarTarget = null;
		}
		else
		{
			_pickupCoinStack = stack;
			_pickupGoldBarStack = barStack;
			_pickupPyramid = pyramid;
			_pickupDisplay = display;
			_pickupDisplaySlot = displaySlot;
		}

		_charge += Time.deltaTime;
		if ( _charge < _holdSeconds )
			return;

		CompletePickup();
		CancelCharge();
	}

	void TickPlaceCharge()
	{
		if ( !TryResolveWholePlace(
			out Vector3 pos,
			out Quaternion rot,
			out bool valid,
			out GroundCoinStack coinTarget,
			out GroundGoldBarStack barTarget,
			out CoinSortingHopper hopper,
			out ITreasureDisplayStackOwner display,
			out int displaySlot ) )
		{
			CancelCharge();
			return;
		}

		if ( _mode != ChargeMode.Place )
		{
			_mode = ChargeMode.Place;
			_charge = 0f;
			_pickupTargetKey = null;
			_pickupCoinStack = null;
			_pickupGoldBarStack = null;
			_pickupPyramid = null;
			_pickupDisplay = null;
			_pickupDisplaySlot = -1;
		}

		_placePos = pos;
		_placeRot = rot;
		_placeValid = valid;
		_placeCoinTarget = coinTarget;
		_placeGoldBarTarget = barTarget;
		_placeHopper = hopper;
		_placeDisplay = display;
		_placeDisplaySlot = displaySlot;

		if ( !valid )
		{
			// Keep charging only while a valid destination exists.
			_charge = 0f;
			return;
		}

		_charge += Time.deltaTime;
		if ( _charge < _holdSeconds )
			return;

		CompletePlace();
		CancelCharge();
	}

	void CompletePickup()
	{
		PlayerCarry carry = _player != null ? _player.Carry : null;
		if ( carry == null )
			return;

		if ( _pickupCoinStack != null )
		{
			CompleteCoinStackPickup( carry, _pickupCoinStack );
			return;
		}

		if ( _pickupGoldBarStack != null )
		{
			CompleteGoldBarStackPickup( carry, _pickupGoldBarStack );
			return;
		}

		if ( _pickupDisplay != null && _pickupDisplaySlot >= 0 )
		{
			CompleteDisplaySlotPickup( carry, _pickupDisplay, _pickupDisplaySlot );
			return;
		}

		if ( _pickupPyramid != null )
			CompleteGemPyramidPickup( carry, _pickupPyramid );
	}

	void CompleteCoinStackPickup( PlayerCarry carry, GroundCoinStack stack )
	{
		if ( stack == null || carry == null )
			return;

		DefinitionScratch.Clear();
		Vector3 startPos = stack.ContactPosition;
		Quaternion startRot = stack.transform.rotation;
		float seed = stack.VariationSeed;

		if ( !stack.TryConsumeAllDefinitions( DefinitionScratch ) || DefinitionScratch.Count == 0 )
		{
			DefinitionScratch.Clear();
			return;
		}

		List<TreasureDefinition> taken = new List<TreasureDefinition>( DefinitionScratch.Count );
		for ( int i = 0; i < DefinitionScratch.Count; i++ )
			taken.Add( DefinitionScratch[ i ] );
		DefinitionScratch.Clear();

		FlyCoinStackToHand( carry, taken, startPos, startRot, seed );
	}

	void CompleteGoldBarStackPickup( PlayerCarry carry, GroundGoldBarStack stack )
	{
		if ( stack == null || carry == null )
			return;

		ItemScratch.Clear();
		if ( !stack.TryConsumeAllItems( ItemScratch ) || ItemScratch.Count == 0 )
		{
			ItemScratch.Clear();
			return;
		}

		carry.TrySetSelectedBucket( CarryBucketKind.Artifact );
		if ( !carry.TryAbsorbAtHeldBottom( ItemScratch ) )
		{
			for ( int i = 0; i < ItemScratch.Count; i++ )
			{
				TreasureItem member = ItemScratch[ i ];
				if ( member == null )
					continue;
				member.EnterPhysics( member.transform.position, member.transform.rotation );
			}
		}
		ItemScratch.Clear();
	}

	void CompleteDisplaySlotPickup( PlayerCarry carry, ITreasureDisplayStackOwner display, int slotIndex )
	{
		if ( carry == null || display == null )
			return;

		DefinitionScratch.Clear();
		if ( !display.TryConsumeSlotDefinitions(
			slotIndex,
			DefinitionScratch,
			out Vector3 startPos,
			out Quaternion startRot ) || DefinitionScratch.Count == 0 )
		{
			DefinitionScratch.Clear();
			return;
		}

		List<TreasureDefinition> taken = new List<TreasureDefinition>( DefinitionScratch.Count );
		for ( int i = 0; i < DefinitionScratch.Count; i++ )
			taken.Add( DefinitionScratch[ i ] );
		DefinitionScratch.Clear();

		if ( display is GoldBarDisplayTableInteractable || ( taken[ 0 ] != null && GoldBarStack.IsStackable( taken[ 0 ] ) ) )
		{
			carry.TrySetSelectedBucket( CarryBucketKind.Artifact );
			carry.TryAbsorbDefinitionsAtHeldBottom( taken, CarryBucketKind.Artifact );
			return;
		}

		FlyCoinStackToHand( carry, taken, startPos, startRot, 1f );
	}

	void FlyCoinStackToHand(
		PlayerCarry carry,
		List<TreasureDefinition> taken,
		Vector3 startPos,
		Quaternion startRot,
		float variationSeed )
	{
		if ( carry == null || taken == null || taken.Count == 0 )
			return;

		TreasureDefinition first = taken[ 0 ];
		int count = taken.Count;
		CoinStackInteractSfx.PlayStackPickup( startPos );

		carry.TrySetSelectedBucket( CarryBucketKind.Coin );
		Transform holdRoot = carry.GetHoldRoot( CarryBucketKind.Coin );
		CarryDefinition carryDef = null;
		carryDef = RuntimeDefinition.Resolve( ref carryDef );
		float duration = carryDef != null ? carryDef.wholeStackAbsorbTweenDuration : 0.28f;
		Vector3 endLocal = carryDef != null ? carryDef.heldStackOffset : Vector3.zero;

		if ( holdRoot == null )
		{
			carry.TryAbsorbDefinitionsAtHeldBottom( taken, CarryBucketKind.Coin );
			CoinStackInteractSfx.PlayStackHandLand( first, count, startPos );
			return;
		}

		CoinStackFlight.FlyToHand(
			taken,
			startPos,
			startRot,
			variationSeed,
			holdRoot,
			endLocal,
			duration,
			() =>
			{
				if ( carry == null )
					return;

				carry.TryAbsorbDefinitionsAtHeldBottom( taken, CarryBucketKind.Coin );
				Vector3 handPos = holdRoot.TransformPoint( endLocal );
				CoinStackInteractSfx.PlayStackHandLand( first, count, handPos );
			} );
	}

	void CompleteGemPyramidPickup( PlayerCarry carry, GemPyramidCluster cluster )
	{
		if ( carry == null || cluster == null || cluster.Count <= 0 )
			return;

		ItemScratch.Clear();
		IReadOnlyList<TreasureItem> members = cluster.Members;
		for ( int i = 0; i < members.Count; i++ )
		{
			TreasureItem gem = members[ i ];
			if ( gem != null )
				ItemScratch.Add( gem );
		}

		// Remove from pyramid first so absorb owns them cleanly.
		for ( int i = 0; i < ItemScratch.Count; i++ )
			GemPyramidRegistry.NotifyRemoved( ItemScratch[ i ] );

		carry.TrySetSelectedBucket( CarryBucketKind.Gem );
		carry.TryAbsorbAtHeldBottom( ItemScratch );
		ItemScratch.Clear();
	}

	void CompletePlace()
	{
		PlayerCarry carry = _player != null ? _player.Carry : null;
		if ( carry == null || !_placeValid )
			return;

		CarryBucketKind bucket = carry.SelectedBucket;
		if ( bucket == CarryBucketKind.Coin )
		{
			if ( _placeHopper != null )
			{
				CoinSortingStation station = _placeHopper.Station;
				if ( station != null )
					station.TryDumpCarryIntoHopperAnimated( carry );
				return;
			}

			if ( _placeDisplay != null && _placeDisplaySlot >= 0 )
			{
				PlaceCoinDefinitionsOnDisplay(
					carry,
					_placeDisplay,
					_placeDisplaySlot );
				return;
			}

			if ( !carry.TryExtractAllCoinDefinitions(
				out List<TreasureDefinition> defs,
				out Vector3 startPos,
				out Quaternion startRot ) )
				return;

			PlaceCoinDefinitions( defs, startPos, startRot, carry.CoinHandVariationSeed, _placePos, _placeRot, _placeCoinTarget );
			return;
		}

		if ( !carry.TryRemoveAllFromSelected( out List<TreasureItem> items ) || items == null || items.Count == 0 )
			return;

		if ( bucket == CarryBucketKind.Gem )
		{
			PlaceGemCollection( items, _placePos );
			return;
		}

		PlaceScratch.Clear();
		ItemScratch.Clear();
		for ( int i = 0; i < items.Count; i++ )
		{
			TreasureItem member = items[ i ];
			if ( member == null )
				continue;
			if ( GoldBarStack.IsStackable( member ) )
				PlaceScratch.Add( member );
			else
				ItemScratch.Add( member );
		}

		GoldBarDisplayTableInteractable goldTable = _placeDisplay as GoldBarDisplayTableInteractable;
		if ( goldTable != null && _placeDisplaySlot >= 0 )
		{
			if ( PlaceScratch.Count > 0 )
				PlaceGoldBarCollectionOnDisplay( goldTable, _placeDisplaySlot, PlaceScratch );
			if ( ItemScratch.Count > 0 )
				carry.TryAbsorbAtHeldBottom( ItemScratch );
			PlaceScratch.Clear();
			ItemScratch.Clear();
			return;
		}

		if ( PlaceScratch.Count > 0 )
			PlaceGoldBarCollection( PlaceScratch, _placePos, _placeRot, _placeGoldBarTarget );
		if ( ItemScratch.Count > 0 )
			PlaceArtifactCollection( ItemScratch, _placePos, _placeRot );
		PlaceScratch.Clear();
		ItemScratch.Clear();
	}

	void PlaceCoinDefinitionsOnDisplay(
		PlayerCarry carry,
		ITreasureDisplayStackOwner display,
		int slotIndex )
	{
		if ( carry == null || display == null || slotIndex < 0 )
			return;

		if ( !carry.TryExtractAllCoinDefinitions(
			out List<TreasureDefinition> defs,
			out Vector3 startPos,
			out Quaternion startRot ) )
			return;

		if ( defs.Count == 0 )
			return;

		if ( display is CoinDisplayTableInteractable coinTable )
		{
			if ( !coinTable.CanAcceptWholeCarriedCoinStack( defs ) )
			{
				carry.TryAbsorbDefinitionsAtHeldBottom( defs, CarryBucketKind.Coin, promoteIfEmpty: true );
				return;
			}

			PlaceCoinDefinitionsOnDisplaySlot(
				carry,
				display,
				slotIndex,
				defs,
				startPos,
				startRot );
			return;
		}

		if ( display is MixedDisplayTableInteractable mixedTable )
		{
			if ( !TryBuildPlacementQuery( out PlacementQuery query ) )
			{
				carry.TryAbsorbDefinitionsAtHeldBottom( defs, CarryBucketKind.Coin, promoteIfEmpty: true );
				return;
			}

			if ( PlayerCarry.AreCoinDefinitionsUniform( defs, out _ ) )
			{
				PlaceCoinDefinitionsOnDisplaySlot(
					carry,
					display,
					slotIndex,
					defs,
					startPos,
					startRot );
				return;
			}

			PlaceMixedCoinDefinitionsOnMixedDisplay(
				carry,
				mixedTable,
				defs,
				startPos,
				startRot,
				in query );
			return;
		}

		PlaceCoinDefinitionsOnDisplaySlot(
			carry,
			display,
			slotIndex,
			defs,
			startPos,
			startRot );
	}

	void PlaceCoinDefinitionsOnDisplaySlot(
		PlayerCarry carry,
		ITreasureDisplayStackOwner display,
		int slotIndex,
		List<TreasureDefinition> defs,
		Vector3 startPos,
		Quaternion startRot )
	{
		if ( carry == null || display == null || slotIndex < 0 || defs == null || defs.Count == 0 )
			return;

		int room = display.GetSlotCoinAppendCapacity( slotIndex, defs[ 0 ] );
		if ( room <= 0 )
		{
			carry.TryAbsorbDefinitionsAtHeldBottom( defs, CarryBucketKind.Coin, promoteIfEmpty: true );
			return;
		}

		List<TreasureDefinition> flying = defs;
		if ( room < defs.Count )
		{
			flying = new List<TreasureDefinition>( room );
			List<TreasureDefinition> remainder = new List<TreasureDefinition>( defs.Count - room );
			for ( int i = 0; i < defs.Count; i++ )
			{
				if ( i < room )
					flying.Add( defs[ i ] );
				else
					remainder.Add( defs[ i ] );
			}

			if ( remainder.Count > 0 )
				carry.TryAbsorbDefinitionsAtHeldBottom( remainder, CarryBucketKind.Coin, promoteIfEmpty: true );
		}

		if ( !display.TryGetSlotAppendPose( slotIndex, out Vector3 endPos, out Quaternion endRot ) )
		{
			endPos = _placePos;
			endRot = _placeRot;
		}

		float seed = carry.CoinHandVariationSeed;
		CarryDefinition carryDef = null;
		carryDef = RuntimeDefinition.Resolve( ref carryDef );
		float duration = carryDef != null ? carryDef.wholeStackAbsorbTweenDuration : 0.28f;

		CoinStackFlight.FlyToWorld(
			flying,
			startPos,
			startRot,
			seed,
			endPos,
			endRot,
			duration,
			() =>
			{
				if ( display == null )
					return;

				if ( display is CoinDisplayTableInteractable coinTable )
					coinTable.TryAppendWholeStackWithAutoLevel( slotIndex, flying );
				else
					display.TryAppendSlotDefinitions( slotIndex, flying );

				CoinStackInteractSfx.PlayStackPlace( endPos );
			} );
	}

	void PlaceMixedCoinDefinitionsOnMixedDisplay(
		PlayerCarry carry,
		MixedDisplayTableInteractable mixedTable,
		List<TreasureDefinition> defs,
		Vector3 startPos,
		Quaternion startRot,
		in PlacementQuery query )
	{
		if ( carry == null || mixedTable == null || defs == null || defs.Count == 0 )
			return;

		CollectUniqueCoinTypes( defs, UniqueCoinTypesScratch );
		RemainderScratch.Clear();

		CarryDefinition carryDef = null;
		carryDef = RuntimeDefinition.Resolve( ref carryDef );
		float duration = carryDef != null ? carryDef.wholeStackAbsorbTweenDuration : 0.28f;
		float seed = carry.CoinHandVariationSeed;
		int flightIndex = 0;

		for ( int t = 0; t < UniqueCoinTypesScratch.Count; t++ )
		{
			TreasureDefinition coinType = UniqueCoinTypesScratch[ t ];
			if ( coinType == null )
				continue;

			TypeBatchScratch.Clear();
			for ( int i = 0; i < defs.Count; i++ )
			{
				if ( defs[ i ] == coinType )
					TypeBatchScratch.Add( defs[ i ] );
			}

			if ( TypeBatchScratch.Count == 0 )
				continue;

			if ( !mixedTable.TryFindWholeCoinPlaceSlot(
				coinType,
				in query,
				out int slotIndex,
				out Vector3 endPos,
				out Quaternion endRot ) )
			{
				RemainderScratch.AddRange( TypeBatchScratch );
				continue;
			}

			int room = mixedTable.GetSlotCoinAppendCapacity( slotIndex, coinType );
			if ( room <= 0 )
			{
				RemainderScratch.AddRange( TypeBatchScratch );
				continue;
			}

			List<TreasureDefinition> flying;
			if ( room >= TypeBatchScratch.Count )
			{
				flying = new List<TreasureDefinition>( TypeBatchScratch );
			}
			else
			{
				flying = new List<TreasureDefinition>( room );
				for ( int i = 0; i < TypeBatchScratch.Count; i++ )
				{
					if ( i < room )
						flying.Add( TypeBatchScratch[ i ] );
					else
						RemainderScratch.Add( TypeBatchScratch[ i ] );
				}
			}

			if ( flying.Count == 0 )
				continue;

			ITreasureDisplayStackOwner display = mixedTable;
			int capturedSlot = slotIndex;
			Vector3 capturedEndPos = endPos;
			float capturedSeed = seed + flightIndex * 0.137f;
			flightIndex++;

			CoinStackFlight.FlyToWorld(
				flying,
				startPos,
				startRot,
				capturedSeed,
				endPos,
				endRot,
				duration,
				() =>
				{
					if ( display != null )
						display.TryAppendSlotDefinitions( capturedSlot, flying );

					CoinStackInteractSfx.PlayStackPlace( capturedEndPos );
				} );
		}

		if ( RemainderScratch.Count > 0 )
			carry.TryAbsorbDefinitionsAtHeldBottom( RemainderScratch, CarryBucketKind.Coin, promoteIfEmpty: true );

		UniqueCoinTypesScratch.Clear();
		TypeBatchScratch.Clear();
		RemainderScratch.Clear();
	}

	static void CollectUniqueCoinTypes(
		IReadOnlyList<TreasureDefinition> defs,
		List<TreasureDefinition> unique )
	{
		unique.Clear();
		if ( defs == null )
			return;

		for ( int i = 0; i < defs.Count; i++ )
		{
			TreasureDefinition def = defs[ i ];
			if ( def == null )
				continue;

			bool seen = false;
			for ( int j = 0; j < unique.Count; j++ )
			{
				if ( unique[ j ] == def )
				{
					seen = true;
					break;
				}
			}

			if ( !seen )
				unique.Add( def );
		}
	}

	bool TryBuildPlacementQuery( out PlacementQuery query )
	{
		query = default;
		if ( _player == null || _interaction == null )
			return false;

		if ( !_interaction.TryGetLastHit( out RaycastHit hit ) )
			return false;

		query = new PlacementQuery
		{
			Player = _player,
			Hit = hit,
			HasHit = true,
			AutoFindValidSlot = true
		};
		return true;
	}

	void PlaceCoinDefinitions(
		List<TreasureDefinition> definitions,
		Vector3 startPos,
		Quaternion startRot,
		float variationSeed,
		Vector3 contact,
		Quaternion rotation,
		GroundCoinStack preferred )
	{
		if ( definitions == null || definitions.Count == 0 )
			return;

		GroundCoinStack stack = preferred;
		if ( stack == null || stack.IsFull )
			stack = GroundCoinStack.CreateAt( contact, rotation );

		Vector3 endPos = stack.ContactPosition + Vector3.up * stack.SettledHeight;
		Quaternion endRot = stack.transform.rotation;
		CarryDefinition carryDef = null;
		carryDef = RuntimeDefinition.Resolve( ref carryDef );
		float duration = carryDef != null ? carryDef.wholeStackAbsorbTweenDuration : 0.28f;

		CoinStackFlight.FlyToWorld(
			definitions,
			startPos,
			startRot,
			variationSeed,
			endPos,
			endRot,
			duration,
			() =>
			{
				if ( stack == null )
					return;

				stack.TryAppendDefinitions( definitions );
				if ( !stack.HasInFlight )
					stack.TryMergeNearby();

				CoinStackInteractSfx.PlayStackPlace( endPos );
			} );
	}

	void PlaceCoinCollection(
		List<TreasureItem> items,
		Vector3 contact,
		Quaternion rotation,
		GroundCoinStack preferred )
	{
		// Legacy individual path kept for non-definition callers.
		GroundCoinStack stack = preferred;
		if ( stack == null || stack.IsFull )
			stack = GroundCoinStack.CreateAt( contact, rotation );

		for ( int i = 0; i < items.Count; i++ )
		{
			TreasureItem member = items[ i ];
			if ( member == null )
				continue;

			if ( !stack.CanAccept( member.Definition ) )
			{
				member.EnterPhysics( member.transform.position, member.transform.rotation );
				continue;
			}

			stack.BeginAppendFlight( member, stack.transform.rotation );
		}

		stack.AbsorbNearbyLooseCoins();
		if ( !stack.HasInFlight )
			stack.TryMergeNearby();
	}

	void PlaceGemCollection( List<TreasureItem> items, Vector3 contact )
	{
		TreasureItem firstPlaced = null;
		for ( int i = 0; i < items.Count; i++ )
		{
			TreasureItem gem = items[ i ];
			if ( gem == null )
				continue;

			Vector3 pos = contact;
			Quaternion rot = gem.transform.rotation;
			if ( GemPyramidRegistry.TryGetJoinPose( gem, contact, out Vector3 joinPos, out Quaternion joinRot ) )
			{
				pos = joinPos;
				rot = joinRot;
			}
			else
			{
				// Slight scatter so mixed types don't stack on one point before join.
				pos += new Vector3( ( i % 3 ) * 0.04f, 0.02f, ( i / 3 ) * 0.04f );
			}

			gem.EnterSurface( pos, rot, Vector3.zero );
			GemPyramidRegistry.TryJoin( gem, animate: true );
			CoinGemInteractFeedback.PlayPlace( gem );
			if ( firstPlaced == null )
				firstPlaced = gem;
		}

		if ( _placement != null )
			_placement.PlayPlaceLandFeedback( firstPlaced );
	}

	void PlaceArtifactCollection( List<TreasureItem> items, Vector3 contact, Quaternion rotation )
	{
		TreasureItem firstPlaced = null;
		for ( int i = 0; i < items.Count; i++ )
		{
			TreasureItem item = items[ i ];
			if ( item == null )
				continue;

			Vector3 pos = contact + new Vector3( ( i % 3 ) * 0.12f, 0.05f, ( i / 3 ) * 0.12f );
			item.EnterPhysics( pos, rotation );
			if ( firstPlaced == null )
				firstPlaced = item;
		}

		if ( _placement != null )
			_placement.PlayPlaceLandFeedback( firstPlaced );
	}

	void PlaceGoldBarCollection(
		List<TreasureItem> items,
		Vector3 contact,
		Quaternion rotation,
		GroundGoldBarStack preferred )
	{
		if ( items == null || items.Count == 0 )
			return;

		TreasureItem first = null;
		for ( int i = 0; i < items.Count; i++ )
		{
			if ( items[ i ] == null )
				continue;
			first = items[ i ];
			break;
		}

		if ( first == null )
			return;

		GroundGoldBarStack stack = preferred;
		if ( stack != null && !stack.IsFull && stack.CanAccept( first.Definition ) )
		{
			AppendGoldBarsToStack( stack, items );
			return;
		}

		float joinRadius = GoldBarStack.ResolveJoinRadius( first.Definition );
		stack = GroundGoldBarStack.FindNearest( contact, joinRadius );
		if ( stack == null || stack.IsFull || !stack.CanAccept( first.Definition ) )
			stack = GroundGoldBarStack.CreateAt( contact, rotation );

		AppendGoldBarsToStack( stack, items );
	}

	static void AppendGoldBarsToStack( GroundGoldBarStack stack, List<TreasureItem> items )
	{
		if ( stack == null || items == null )
			return;

		for ( int i = 0; i < items.Count; i++ )
		{
			TreasureItem member = items[ i ];
			if ( member == null )
				continue;
			if ( !stack.CanAccept( member.Definition ) )
			{
				member.EnterPhysics( member.transform.position, member.transform.rotation );
				continue;
			}

			stack.BeginAppendFlight( member );
		}

		stack.AbsorbNearbyLooseBars();
	}

	void PlaceGoldBarCollectionOnDisplay(
		GoldBarDisplayTableInteractable table,
		int slotIndex,
		List<TreasureItem> items )
	{
		if ( table == null || items == null || items.Count == 0 || slotIndex < 0 )
			return;

		DefinitionScratch.Clear();
		for ( int i = 0; i < items.Count; i++ )
		{
			TreasureItem member = items[ i ];
			if ( member == null )
				continue;

			if ( table.GetSlotCoinAppendCapacity( slotIndex, member.Definition ) <= DefinitionScratch.Count )
			{
				member.EnterPhysics( member.transform.position, member.transform.rotation );
				continue;
			}

			DefinitionScratch.Add( member.Definition );
			TreasureItemFactory.Despawn( member );
		}

		if ( DefinitionScratch.Count > 0 )
			table.TryAppendSlotDefinitions( slotIndex, DefinitionScratch );
		DefinitionScratch.Clear();
	}

	bool ResolvePickupTarget(
		out GroundCoinStack stack,
		out GroundGoldBarStack barStack,
		out GemPyramidCluster pyramid,
		out ITreasureDisplayStackOwner display,
		out int displaySlot )
	{
		stack = null;
		barStack = null;
		pyramid = null;
		display = null;
		displaySlot = -1;
		if ( _interaction == null )
			return false;

		IInteractable focus = _interaction.Current;
		if ( focus == null )
			return false;

		GroundCoinStack coinStack = focus as GroundCoinStack;
		if ( coinStack != null && !coinStack.IsMachineBuffer && coinStack.Count > 0 )
		{
			stack = coinStack;
			return true;
		}

		GroundGoldBarStack goldBarStack = focus as GroundGoldBarStack;
		if ( goldBarStack != null && goldBarStack.Count > 0 )
		{
			barStack = goldBarStack;
			return true;
		}

		TreasureItemInteractable itemInteractable = focus as TreasureItemInteractable;
		if ( itemInteractable == null )
			return false;

		TreasureItem item = itemInteractable.Item;
		if ( item == null || item.Definition == null )
			return false;

		if ( item.Owner is ITreasureDisplayStackOwner displayOwner
			&& displayOwner.TryGetSlotIndex( item, out int slotIndex )
			&& displayOwner.GetSlotCount( slotIndex ) > 0
			&& ( item.Definition.category == TreasureCategory.Coin || GoldBarStack.IsStackable( item ) ) )
		{
			display = displayOwner;
			displaySlot = slotIndex;
			return true;
		}

		if ( item.Definition.category != TreasureCategory.Gem )
			return false;

		GemPyramidCluster cluster = GemPyramidRegistry.FindClusterContaining( item );
		if ( cluster == null || cluster.Count <= 1 )
			return false;

		pyramid = cluster;
		return true;
	}

	bool TryResolveWholePlace(
		out Vector3 position,
		out Quaternion rotation,
		out bool valid,
		out GroundCoinStack coinTarget,
		out GroundGoldBarStack barTarget,
		out CoinSortingHopper hopper,
		out ITreasureDisplayStackOwner display,
		out int displaySlot )
	{
		position = Vector3.zero;
		rotation = Quaternion.identity;
		valid = false;
		coinTarget = null;
		barTarget = null;
		hopper = null;
		display = null;
		displaySlot = -1;

		PlayerCarry carry = _player != null ? _player.Carry : null;
		if ( carry == null || carry.Count <= 0 || _interaction == null )
			return false;

		CarryBucketKind bucket = carry.SelectedBucket;

		if ( bucket == CarryBucketKind.Coin && carry.GetBucketCount( CarryBucketKind.Coin ) > 0 )
		{
			CoinSortingHopper focusHopper = ResolveHopperFromFocus();
			if ( focusHopper != null
				&& focusHopper.Station != null
				&& !focusHopper.Station.IsRepositioning
				&& focusHopper.Station.RemainingCapacity > 0 )
			{
				hopper = focusHopper;
				GroundCoinStack hopperStack = focusHopper.Station.HopperStack;
				if ( hopperStack != null )
				{
					position = hopperStack.ContactPosition + Vector3.up * hopperStack.SettledHeight;
					rotation = hopperStack.transform.rotation;
				}
				else
				{
					position = focusHopper.transform.position;
					rotation = TreasureOrientation.FlattenUpright( focusHopper.transform.rotation );
				}

				valid = true;
				return true;
			}
		}

		// Prefer looking directly at a ground coin stack when placing coins.
		if ( bucket == CarryBucketKind.Coin )
		{
			GroundCoinStack focusStack = _interaction.Current as GroundCoinStack;
			if ( focusStack != null && !focusStack.IsMachineBuffer && !focusStack.IsFull )
			{
				coinTarget = focusStack;
				position = focusStack.ContactPosition;
				rotation = focusStack.transform.rotation;
				valid = true;
				return true;
			}
		}

		if ( bucket == CarryBucketKind.Artifact )
		{
			GroundGoldBarStack focusBars = _interaction.Current as GroundGoldBarStack;
			if ( focusBars != null && !focusBars.IsFull && carry.GetBucketCount( CarryBucketKind.Artifact ) > 0 )
			{
				barTarget = focusBars;
				position = focusBars.ContactPosition;
				rotation = focusBars.transform.rotation;
				valid = HasCarriedGoldBars( carry );
				return true;
			}
		}

		if ( !_interaction.TryGetLastHit( out RaycastHit hit ) )
			return false;

		if ( bucket == CarryBucketKind.Coin && carry.GetBucketCount( CarryBucketKind.Coin ) > 0 )
		{
			CoinSortingHopper hitHopper =
				CoinSortingStation.ResolveHopperPlacementFromCollider( hit.collider );
			if ( hitHopper != null
				&& hitHopper.Station != null
				&& !hitHopper.Station.IsRepositioning
				&& hitHopper.Station.RemainingCapacity > 0 )
			{
				hopper = hitHopper;
				GroundCoinStack hopperStack = hitHopper.Station.HopperStack;
				if ( hopperStack != null )
				{
					position = hopperStack.ContactPosition + Vector3.up * hopperStack.SettledHeight;
					rotation = hopperStack.transform.rotation;
				}
				else
				{
					position = hit.point;
					rotation = TreasureOrientation.FlattenUpright( hitHopper.transform.rotation );
				}

				valid = true;
				return true;
			}
		}

		GroundCoinStack hitStack = hit.collider != null
			? hit.collider.GetComponentInParent<GroundCoinStack>()
			: null;
		if ( bucket == CarryBucketKind.Coin && hitStack != null && !hitStack.IsMachineBuffer && !hitStack.IsFull )
		{
			coinTarget = hitStack;
			position = hitStack.ContactPosition;
			rotation = hitStack.transform.rotation;
			valid = true;
			return true;
		}

		if ( bucket == CarryBucketKind.Coin
			&& TryResolveDisplayCoinPlace(
				hit,
				carry,
				out display,
				out displaySlot,
				out position,
				out rotation,
				out bool displayPlaceValid ) )
		{
			valid = displayPlaceValid;
			return true;
		}

		GroundGoldBarStack hitBarStack = hit.collider != null
			? hit.collider.GetComponentInParent<GroundGoldBarStack>()
			: null;
		if ( bucket == CarryBucketKind.Artifact && hitBarStack != null && !hitBarStack.IsFull )
		{
			barTarget = hitBarStack;
			position = hitBarStack.ContactPosition;
			rotation = hitBarStack.transform.rotation;
			valid = HasCarriedGoldBars( carry );
			return true;
		}

		if ( bucket == CarryBucketKind.Artifact
			&& TryResolveDisplayGoldBarPlace(
				hit,
				carry,
				out display,
				out displaySlot,
				out position,
				out rotation,
				out bool goldDisplayValid ) )
		{
			valid = goldDisplayValid;
			return true;
		}

		if ( !PlacementFloorSurface.IsWalkableFloorHit( in hit ) )
		{
			position = hit.point;
			rotation = TreasureOrientation.FlattenUpright( Quaternion.identity );
			valid = false;
			return true;
		}

		position = hit.point;
		rotation = TreasureOrientation.FlattenUpright( Quaternion.LookRotation(
			Vector3.ProjectOnPlane( _player.transform.forward, Vector3.up ).sqrMagnitude > 0.001f
				? Vector3.ProjectOnPlane( _player.transform.forward, Vector3.up ).normalized
				: Vector3.forward ) );

		if ( bucket == CarryBucketKind.Coin )
		{
			TreasureDefinition def = null;
			carry.TryPeekActive( out def );
			float radius = Mathf.Max( 0.42f, GroundCoinStack.ResolveJoinRadius( def ) );
			coinTarget = GroundCoinStack.FindNearest( position, radius );
			if ( coinTarget != null )
			{
				position = coinTarget.transform.position;
				rotation = coinTarget.transform.rotation;
			}

			valid = true;
			return true;
		}

		if ( bucket == CarryBucketKind.Artifact )
		{
			TreasureDefinition def = null;
			carry.TryPeekActive( out def );
			float radius = Mathf.Max( 0.42f, GoldBarStack.ResolveJoinRadius( def ) );
			barTarget = GroundGoldBarStack.FindNearest( position, radius );
			if ( barTarget != null )
			{
				position = barTarget.transform.position;
				rotation = barTarget.transform.rotation;
			}

			valid = true;
			return true;
		}

		valid = true;
		return true;
	}

	bool TryResolveDisplayCoinPlace(
		RaycastHit hit,
		PlayerCarry carry,
		out ITreasureDisplayStackOwner display,
		out int slotIndex,
		out Vector3 position,
		out Quaternion rotation,
		out bool placeValid )
	{
		display = null;
		slotIndex = -1;
		position = hit.point;
		rotation = Quaternion.identity;
		placeValid = false;

		if ( hit.collider == null || carry == null )
			return false;

		if ( !carry.TryPeekActive( out TreasureItem probeItem ) || probeItem == null )
			return false;

		if ( probeItem.Definition == null || probeItem.Definition.category != TreasureCategory.Coin )
			return false;

		if ( !carry.TryCollectCoinDefinitions( DefinitionScratch ) )
			return false;

		PlacementQuery query = new PlacementQuery
		{
			Player = _player,
			Hit = hit,
			HasHit = true,
			AutoFindValidSlot = true
		};

		CoinDisplayTableInteractable coinTable = hit.collider.GetComponentInParent<CoinDisplayTableInteractable>();
		if ( coinTable != null
			&& coinTable.TryResolveWholeCoinPlaceSlot( probeItem, in query, out slotIndex, out position, out rotation ) )
		{
			display = coinTable;
			placeValid = coinTable.CanAcceptWholeCarriedCoinStack( DefinitionScratch )
				&& coinTable.GetSlotCoinAppendCapacity( slotIndex, coinTable.AcceptedCoin ) > 0;
			DefinitionScratch.Clear();
			return true;
		}

		MixedDisplayTableInteractable mixed = hit.collider.GetComponentInParent<MixedDisplayTableInteractable>();
		if ( mixed != null
			&& mixed.TryResolveWholeCoinPlaceSlot( probeItem, in query, out slotIndex, out position, out rotation ) )
		{
			display = mixed;
			placeValid = mixed.CanAcceptWholeMixedCoinStack( DefinitionScratch, in query );
			DefinitionScratch.Clear();
			return true;
		}

		DefinitionScratch.Clear();
		return false;
	}

	bool TryResolveDisplayGoldBarPlace(
		RaycastHit hit,
		PlayerCarry carry,
		out ITreasureDisplayStackOwner display,
		out int slotIndex,
		out Vector3 position,
		out Quaternion rotation,
		out bool placeValid )
	{
		display = null;
		slotIndex = -1;
		position = hit.point;
		rotation = Quaternion.identity;
		placeValid = false;

		if ( hit.collider == null || carry == null )
			return false;

		GoldBarDisplayTableInteractable goldTable = hit.collider.GetComponentInParent<GoldBarDisplayTableInteractable>();
		if ( goldTable == null || goldTable.AcceptedBar == null )
			return false;

		if ( !HasCarriedGoldBars( carry, goldTable.AcceptedBar ) )
			return false;

		TreasureItem probeItem = null;
		carry.TryPeekActive( out probeItem );

		PlacementQuery query = new PlacementQuery
		{
			Player = _player,
			Hit = hit,
			HasHit = true,
			AutoFindValidSlot = true
		};

		if ( probeItem != null
			&& GoldBarStack.IsStackable( probeItem )
			&& goldTable.TryResolveWholeCoinPlaceSlot( probeItem, in query, out slotIndex, out position, out rotation ) )
		{
			display = goldTable;
			placeValid = goldTable.GetSlotCoinAppendCapacity( slotIndex, goldTable.AcceptedBar ) > 0;
			return true;
		}

		for ( int i = 0; i < goldTable.SlotCount; i++ )
		{
			if ( goldTable.GetSlotCoinAppendCapacity( i, goldTable.AcceptedBar ) <= 0 )
				continue;

			if ( !goldTable.TryGetSlotAppendPose( i, out position, out rotation ) )
				continue;

			display = goldTable;
			slotIndex = i;
			placeValid = true;
			return true;
		}

		return false;
	}

	static bool HasCarriedGoldBars( PlayerCarry carry )
	{
		return HasCarriedGoldBars( carry, null );
	}

	static bool HasCarriedGoldBars( PlayerCarry carry, TreasureDefinition accepted )
	{
		if ( carry == null )
			return false;

		return carry.BucketHasMatching( CarryBucketKind.Artifact, def =>
		{
			if ( !GoldBarStack.IsStackable( def ) )
				return false;
			if ( accepted != null && def != accepted )
				return false;
			return true;
		} );
	}

	CoinSortingHopper ResolveHopperFromFocus()
	{
		if ( _interaction == null )
			return null;

		IInteractable focus = _interaction.Current;
		CoinSortingHopper hopper = focus as CoinSortingHopper;
		if ( hopper != null )
			return hopper;

		GroundCoinStack stack = focus as GroundCoinStack;
		if ( stack != null && stack.IsMachineBuffer )
		{
			CoinSortingStation station = stack.MachineStation;
			if ( station != null )
				return station.Hopper;
		}

		MonoBehaviour focusBehaviour = focus as MonoBehaviour;
		if ( focusBehaviour != null )
			return CoinSortingStation.ResolveHopperPlacementFromCollider(
				focusBehaviour.GetComponentInChildren<Collider>() );

		return null;
	}

	void CancelCharge()
	{
		_mode = ChargeMode.None;
		_charge = 0f;
		_pickupTargetKey = null;
		_pickupCoinStack = null;
		_pickupGoldBarStack = null;
		_pickupPyramid = null;
		_pickupDisplay = null;
		_pickupDisplaySlot = -1;
		_placeCoinTarget = null;
		_placeGoldBarTarget = null;
		_placeHopper = null;
		_placeDisplay = null;
		_placeDisplaySlot = -1;
		_placeValid = false;
	}

	static GameInput GetGameInput()
	{
		return InputController.Instance != null ? InputController.Instance.GameInput : null;
	}
}
