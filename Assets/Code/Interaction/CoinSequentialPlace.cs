using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Places coins one-at-a-time with the shared flip arc + place SFX into stacks and display slots.
/// </summary>
public static class CoinSequentialPlace
{
	public static void IntoStack(
		GroundCoinStack stack,
		IReadOnlyList<TreasureDefinition> definitions,
		Vector3 startWorldPos,
		Quaternion startWorldRot,
		bool playerDirected,
		Action onComplete = null )
	{
		if ( stack == null || definitions == null || definitions.Count == 0 )
		{
			if ( onComplete != null )
				onComplete();
			return;
		}

		TreasureMotionHost.Run( IntoStackRoutine( stack, definitions, startWorldPos, startWorldRot, playerDirected, onComplete ) );
	}

	public static void IntoDisplaySlot(
		ITreasureDisplayStackOwner display,
		int slotIndex,
		IReadOnlyList<TreasureDefinition> definitions,
		Vector3 startWorldPos,
		Quaternion startWorldRot,
		Action onComplete = null )
	{
		if ( display == null || definitions == null || definitions.Count == 0 || slotIndex < 0 )
		{
			if ( onComplete != null )
				onComplete();
			return;
		}

		TreasureMotionHost.Run( IntoDisplaySlotRoutine(
			display,
			slotIndex,
			definitions,
			startWorldPos,
			startWorldRot,
			onComplete ) );
	}

	static IEnumerator IntoStackRoutine(
		GroundCoinStack stack,
		IReadOnlyList<TreasureDefinition> definitions,
		Vector3 startWorldPos,
		Quaternion startWorldRot,
		bool playerDirected,
		Action onComplete )
	{
		for ( int i = 0; i < definitions.Count; i++ )
		{
			if ( stack == null || stack.IsFull )
				break;

			TreasureDefinition def = definitions[ i ];
			if ( def == null || !stack.CanAccept( def ) )
				continue;

			while ( stack != null && stack.HasInFlight )
				yield return null;

			if ( stack == null || stack.IsFull || !stack.CanAccept( def ) )
				break;

			TreasureItem coin = TreasureItemFactory.RentVisualCoin( def, startWorldPos, startWorldRot );
			if ( coin == null )
				coin = TreasureItemFactory.SpawnFallback( def, startWorldPos, startWorldRot, null );
			if ( coin == null )
				continue;

			coin.SetMeshVisible( true );
			coin.ApplyWorldScale();
			stack.BeginAppendFlight( coin, stack.transform.rotation, playerDirected );
		}

		while ( stack != null && stack.HasInFlight )
			yield return null;

		if ( stack != null && !stack.HasInFlight )
			stack.TryMergeNearby();

		if ( onComplete != null )
			onComplete();
	}

	static IEnumerator IntoDisplaySlotRoutine(
		ITreasureDisplayStackOwner display,
		int slotIndex,
		IReadOnlyList<TreasureDefinition> definitions,
		Vector3 startWorldPos,
		Quaternion startWorldRot,
		Action onComplete )
	{
		CoinDisplayTableInteractable coinTable = display as CoinDisplayTableInteractable;
		MixedDisplayTableInteractable mixedTable = display as MixedDisplayTableInteractable;
		TypedDisplayTableInteractable typedTable = display as TypedDisplayTableInteractable;
		int added = 0;

		if ( coinTable != null )
			coinTable.BeginSuppressAutoLevel();

		try
		{
			for ( int i = 0; i < definitions.Count; i++ )
			{
				if ( display == null )
					break;

				TreasureDefinition def = definitions[ i ];
				if ( def == null )
					continue;

				int room = display.GetSlotCoinAppendCapacity( slotIndex, def );
				if ( coinTable != null )
					room = coinTable.GetWholeStackAppendCapacity( def );
				if ( room <= 0 )
					break;

				TreasureItem coin = TreasureItemFactory.RentVisualCoin( def, startWorldPos, startWorldRot );
				if ( coin == null )
					coin = TreasureItemFactory.SpawnFallback( def, startWorldPos, startWorldRot, null );
				if ( coin == null )
					continue;

				coin.SetMeshVisible( true );
				coin.ApplyWorldScale();

				bool accepted = false;
				if ( mixedTable != null )
					accepted = mixedTable.TryAcceptWorldItemAtSlot( slotIndex, coin, animate: true );
				else if ( typedTable != null )
					accepted = typedTable.TryAcceptWorldItemAtSlot( slotIndex, coin, respectPerSlotMax: coinTable == null );
				else
				{
					TreasureItemFactory.Despawn( coin );
					List<TreasureDefinition> one = new List<TreasureDefinition>( 1 ) { def };
					accepted = display.TryAppendSlotDefinitions( slotIndex, one ) > 0;
				}

				if ( !accepted )
				{
					if ( coin != null && coin.Owner == null )
						TreasureItemFactory.Despawn( coin );
					break;
				}

				added++;
				while ( coin != null && coin.IsInFlight )
					yield return null;
			}
		}
		finally
		{
			if ( coinTable != null )
			{
				coinTable.EndSuppressAutoLevel();
				if ( added > 0 )
					coinTable.RequestAutoLevelAfterWholeStackPlace( slotIndex );
			}
		}

		if ( onComplete != null )
			onComplete();
	}
}
