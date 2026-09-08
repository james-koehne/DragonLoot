using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Raycast pickup for a physical <see cref="TreasureItem"/> (pile, physics, or displayed).
/// Loose coin columns and display stacks take from the aimed coin upward (capacity-limited).
/// Capacity leaves lower coins in place. Aimed coin becomes Active; coins above insert bottom-up.
/// </summary>
[RequireComponent( typeof( TreasureItem ) )]
public class TreasureItemInteractable : InteractableBase
{
	static readonly List<TreasureItem> SupportBuffer = new List<TreasureItem>();
	static readonly List<TreasureItem> TakeBuffer = new List<TreasureItem>();
	static readonly List<TreasureItem> ColumnBuffer = new List<TreasureItem>();
	static readonly List<TreasureItem> CleanupBuffer = new List<TreasureItem>();

	TreasureItem _item;

	public TreasureItem Item => ResolveItemReference();

	public override bool UsesPickupInteract => true;

	/// <summary>
	/// Resolves pickup interactable from a raycast hit (mesh colliders often live on child transforms).
	/// </summary>
	public static TreasureItemInteractable ResolveFromCollider( Collider collider )
	{
		if ( collider == null )
			return null;

		TreasureItem item = collider.GetComponentInParent<TreasureItem>();
		if ( item == null )
		{
			MinecartInteractable cart = collider.GetComponentInParent<MinecartInteractable>();
			if ( cart != null )
				cart.TryResolveTreasureFromCollider( collider, out item );
		}

		if ( item != null )
		{
			TreasureItemInteractable onItem = item.GetComponent<TreasureItemInteractable>();
			if ( onItem == null )
				onItem = item.GetComponentInChildren<TreasureItemInteractable>( true );
			if ( onItem != null )
				return onItem;
		}

		InteractableBase found = collider.GetComponentInParent<InteractableBase>();
		return found as TreasureItemInteractable;
	}

	TreasureItem ResolveItemReference()
	{
		if ( _item != null )
			return _item;

		TreasureItem onSelf = GetComponent<TreasureItem>();
		if ( onSelf != null && GetComponent<Rigidbody>() != null )
		{
			_item = onSelf;
			return _item;
		}

		TreasureItem inParent = GetComponentInParent<TreasureItem>();
		if ( inParent != null )
		{
			_item = inParent;
			return _item;
		}

		if ( onSelf != null )
		{
			_item = onSelf;
			return _item;
		}

		_item = GetComponentInChildren<TreasureItem>( true );
		return _item;
	}

	void Awake()
	{
		ResolveItemReference();
		ApplyNameFromItem();
	}

	void OnEnable()
	{
		ResolveItemReference();
		ApplyNameFromItem();
	}

	public void SetDisplayName( string displayName )
	{
		SetInteractionName( displayName );
	}

	public override bool CanInteract( PlayerController player )
	{
		TreasureItem item = Item;
		if ( !base.CanInteract( player ) || player == null || item == null )
			return false;

		item.TryRepairPickupState();

		if ( item.State == TreasureItemState.Held
			|| item.State == TreasureItemState.Stacked
			|| item.IsReclaiming )
			return false;

		if ( IsBuriedInPile( item ) )
			return false;

		if ( item.State == TreasureItemState.Displayed
			&& item.Owner is IPermanentTreasureDisplayOwner )
			return false;

		if ( item.Owner is CleaningStationInteractable cleaningStation
			&& !cleaningStation.AllowsPickup )
			return false;

		if ( UsesDisplayedCoinStackPickup( item ) )
		{
			PlayerCarry carry = player.Carry;
			if ( carry == null )
				return false;

			return carry.CanAdd( item.Definition );
		}

		if ( UsesColumnPickup( item ) )
		{
			PlayerCarry carry = player.Carry;
			if ( carry == null )
				return false;

			if ( !TryCollectPickupColumn( item, SupportBuffer, player ) )
				return false;

			return carry.CountAffordableSuffix( SupportBuffer ) > 0;
		}

		return player.CanReceiveTreasureItem( item );
	}

	public override void Interact( PlayerController player )
	{
		TreasureItem item = Item;
		if ( player == null || item == null )
			return;

		if ( item.State == TreasureItemState.Held
			|| item.State == TreasureItemState.Stacked
			|| item.IsReclaiming )
			return;

		if ( item.Owner is CleaningStationInteractable cleaningStation
			&& !cleaningStation.AllowsPickup )
			return;

		if ( UsesDisplayedCoinStackPickup( item ) )
		{
			TryPickupDisplayedCoin( player, item );
			return;
		}

		if ( UsesColumnPickup( item ) )
		{
			TryPickupSupportStack( player );
			return;
		}

		TreasurePileVisual pile = item.PileOwner;

		if ( pile != null )
		{
			if ( !pile.TryBeginSteal( item ) )
				return;
		}

		// BeginHold releases the previous owner — avoid release-then-fail orphans.
		if ( !player.TryReceiveTreasureItem( item ) )
		{
			if ( pile != null )
				pile.CancelSteal( item );
			return;
		}

		if ( pile != null )
			pile.CompleteSteal( item );
	}

	static bool UsesDisplayedCoinStackPickup( TreasureItem item )
	{
		if ( item == null || item.Definition == null )
			return false;

		if ( item.State != TreasureItemState.Displayed )
			return false;

		if ( item.Definition.category != TreasureCategory.Coin )
			return false;

		return item.Owner is ITreasureDisplayStackOwner;
	}

	static bool UsesColumnPickup( TreasureItem item )
	{
		if ( item == null )
			return false;

		if ( item.IsWorldLoose )
			return true;

		return item.State == TreasureItemState.Displayed
			&& item.Owner is ITreasureDisplayStackOwner;
	}

	static bool TryCollectPickupColumn( TreasureItem selected, List<TreasureItem> results, PlayerController player )
	{
		results.Clear();
		if ( selected == null )
			return false;

		bool hasAimRay = false;
		Ray aimRay = default;
		bool hasHitWorldY = false;
		float hitWorldY = 0f;
		if ( player != null && player.Interaction != null )
		{
			if ( player.Interaction.TryGetAimRay( out aimRay ) )
				hasAimRay = true;

			if ( player.Interaction.TryGetLastHit( out RaycastHit hit ) )
			{
				hasHitWorldY = true;
				hitWorldY = hit.point.y;
			}
		}

		if ( selected.IsWorldLoose )
		{
			// Always resolve from the true column bottom so mid-stack hits still see the full tower.
			TreasureItem bottom = TreasureSupportStack.FindColumnBottom( selected );
			TreasureSupportStack.CollectColumn( bottom != null ? bottom : selected, ColumnBuffer );
			if ( ColumnBuffer.Count == 0 )
			{
				results.Add( selected );
				return true;
			}

			int startIndex = 0;
			if ( hasAimRay )
			{
				startIndex = CoinColumnPickup.ResolveIndexFromAimRay(
					ColumnBuffer,
					aimRay,
					hasHitWorldY,
					hitWorldY );
				startIndex = Mathf.Clamp( startIndex, 0, ColumnBuffer.Count - 1 );
			}
			else
			{
				for ( int i = 0; i < ColumnBuffer.Count; i++ )
				{
					if ( ColumnBuffer[ i ] == selected )
					{
						startIndex = i;
						break;
					}
				}
			}

			for ( int i = startIndex; i < ColumnBuffer.Count; i++ )
				results.Add( ColumnBuffer[ i ] );

			ColumnBuffer.Clear();
			return results.Count > 0;
		}

		ITreasureDisplayStackOwner stackOwner = selected.Owner as ITreasureDisplayStackOwner;
		if ( stackOwner != null )
		{
			return stackOwner.TryCollectPickupColumn(
				selected,
				results,
				aimRay,
				hasAimRay,
				hasHitWorldY,
				hitWorldY );
		}

		results.Add( selected );
		return true;
	}

	static void TryPickupDisplayedCoin( PlayerController player, TreasureItem item )
	{
		if ( player == null || item == null )
			return;

		PlayerCarry carry = player.Carry;
		if ( carry == null )
			return;

		carry.TrySetSelectedBucket( CarryBucketKind.Coin );
		carry.TryReceiveActiveCoinFromWorld( item );
	}

	void TryPickupSupportStack( PlayerController player )
	{
		TreasureItem item = Item;
		if ( item == null )
			return;

		if ( !TryCollectPickupColumn( item, SupportBuffer, player ) || SupportBuffer.Count == 0 )
			return;

		PlayerCarry carry = player.Carry;
		if ( carry == null )
			return;

		if ( carry.HoldRoot == null || carry.ActiveRoot == null )
			return;

		// Snapshot the full column before take so leftover bottom binders can be cleared/restacked.
		TreasureItem columnBottom = TreasureSupportStack.FindColumnBottom( item );
		CleanupBuffer.Clear();
		TreasureSupportStack.CollectColumn( columnBottom != null ? columnBottom : item, CleanupBuffer );

		// Take from the top of the column down so capacity leaves the bottom coins in place.
		int takeCount = carry.CountAffordableSuffix( SupportBuffer );
		if ( takeCount <= 0 )
		{
			CleanupBuffer.Clear();
			return;
		}

		int startIndex = SupportBuffer.Count - takeCount;
		TakeBuffer.Clear();
		for ( int i = startIndex; i < SupportBuffer.Count; i++ )
			TakeBuffer.Add( SupportBuffer[ i ] );

		// BeginHold releases the previous owner — do not release-then-add (orphans on mid-loop failure).
		if ( TakeBuffer.Count == 1 )
			carry.TryAddExisting( TakeBuffer[ 0 ] );
		else
			carry.TryAddSupportStack( TakeBuffer );

		RestackRemainingColumnAfterPickup();
	}

	static void RestackRemainingColumnAfterPickup()
	{
		TreasureItem remainingSeed = null;
		for ( int i = 0; i < CleanupBuffer.Count; i++ )
		{
			TreasureItem member = CleanupBuffer[ i ];
			if ( member == null || member.IsInFlight || !member.IsWorldLoose )
				continue;

			// Clear stale cylinder hosts left on coins that were not taken (especially the bottom).
			CoinColumnCylinderBinder.StripFromItem( member );

			if ( remainingSeed == null )
				remainingSeed = member;
		}

		CleanupBuffer.Clear();

		if ( remainingSeed != null )
			GroundTreasureStackTarget.RestackColumnByThickness( remainingSeed );
	}

	static bool IsBuriedInPile( TreasureItem item )
	{
		if ( item == null )
			return false;

		TreasurePileVisual origin = item.OriginPile;
		if ( origin != null && origin.IsTreasureBuried( item ) )
			return true;

		TreasurePileVisual ownerPile = item.PileOwner;
		if ( ownerPile != null && ownerPile.IsTreasureBuried( item ) )
			return true;

		return false;
	}

	void ApplyNameFromItem()
	{
		TreasureItem item = Item;
		if ( item == null || item.Definition == null )
			return;

		if ( !string.IsNullOrEmpty( item.Definition.displayName ) )
			SetInteractionName( item.Definition.displayName );
	}
}
