using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Live current/required counts for countable objective subs (dig, display, constellation).
/// </summary>
public static class ObjectiveProgress
{
	public static bool HasCountProgress( ObjectiveSubCompleteType type )
	{
		return type == ObjectiveSubCompleteType.DisplayComplete
			|| type == ObjectiveSubCompleteType.PileEmptied
			|| type == ObjectiveSubCompleteType.ConstellationComplete;
	}

	public static string FormatLabel( ObjectiveDefinition objective, ObjectiveSubDefinition sub, bool complete )
	{
		string label = sub != null && !string.IsNullOrEmpty( sub.label ) ? sub.label : ( sub != null ? sub.id : string.Empty );
		if ( sub == null || !HasCountProgress( sub.completeType ) )
			return label;

		int current;
		int required;
		if ( !TryGetCounts( objective, sub, out current, out required ) || required <= 0 )
			return label;

		if ( complete && current < required )
			current = required;
		else if ( current > required )
			current = required;

		return label + ": " + current + "/" + required;
	}

	public static bool IsCountMet( ObjectiveDefinition objective, ObjectiveSubDefinition sub )
	{
		int current;
		int required;
		if ( !TryGetCounts( objective, sub, out current, out required ) )
			return false;
		return required > 0 && current >= required;
	}

	public static bool TryGetCounts( ObjectiveDefinition objective, ObjectiveSubDefinition sub, out int current, out int required )
	{
		current = 0;
		required = 0;
		if ( sub == null )
			return false;

		if ( sub.completeType == ObjectiveSubCompleteType.PileEmptied )
			return TryGetPileCounts( objective, sub, out current, out required );

		if ( sub.completeType == ObjectiveSubCompleteType.DisplayComplete )
			return TryGetDisplayCounts( objective, sub, out current, out required );

		if ( sub.completeType == ObjectiveSubCompleteType.ConstellationComplete )
			return TryGetConstellationCounts( sub, out current, out required );

		return false;
	}

	static bool TryGetPileCounts( ObjectiveDefinition objective, ObjectiveSubDefinition sub, out int current, out int required )
	{
		current = 0;
		required = 0;

		TreasurePileInteractable pile = ResolveOnTarget<TreasurePileInteractable>( sub.targetId );
		if ( pile != null )
			return FillPileCounts( pile, sub.requiredCount, out current, out required );

		if ( objective == null || string.IsNullOrEmpty( objective.showVolumeId ) )
			return false;
		if ( !EventTargetRegistry.TryGetVolume( objective.showVolumeId, out QuestVolume volume ) || volume == null )
			return false;

		int dug = 0;
		int expected = 0;
		IReadOnlyList<TreasurePileInteractable> piles = TreasurePileInteractable.ActivePiles;
		for ( int i = 0; i < piles.Count; i++ )
		{
			TreasurePileInteractable candidate = piles[ i ];
			if ( candidate == null || !volume.ContainsWorldPoint( candidate.transform.position ) )
				continue;

			int pileDug;
			int pileExpected;
			if ( !FillPileCounts( candidate, 0, out pileDug, out pileExpected ) )
				continue;
			dug += pileDug;
			expected += pileExpected;
		}

		if ( expected <= 0 )
			return false;

		required = sub.requiredCount > 0 ? sub.requiredCount : expected;
		current = Mathf.Clamp( dug, 0, required );
		return true;
	}

	static bool FillPileCounts( TreasurePileInteractable pile, int requiredOverride, out int current, out int required )
	{
		current = 0;
		required = 0;
		if ( pile == null )
			return false;

		int expected = pile.ExpectedCoinCount;
		if ( expected <= 0 && pile.Treasure != null && pile.Treasure.category == TreasureCategory.Coin )
			expected = pile.TotalCount;
		if ( expected <= 0 )
			return false;

		int remaining = pile.CountRemainingCoinsInPile();
		if ( remaining < 0 )
			remaining = pile.RemainingCount;

		int dug = Mathf.Max( 0, expected - remaining );
		required = requiredOverride > 0 ? requiredOverride : expected;
		current = Mathf.Clamp( dug, 0, required );
		return true;
	}

	static bool TryGetDisplayCounts( ObjectiveDefinition objective, ObjectiveSubDefinition sub, out int current, out int required )
	{
		current = 0;
		required = 0;

		TypedDisplayTableInteractable typed = ResolveOnTarget<TypedDisplayTableInteractable>( sub.targetId );
		if ( typed == null )
			typed = FindUniqueCoinTableInVolume( objective );

		if ( typed != null )
		{
			required = sub.requiredCount > 0 ? sub.requiredCount : typed.Capacity;
			current = Mathf.Clamp( typed.CurrentCount, 0, Mathf.Max( 0, required ) );
			return required > 0;
		}

		ArtifactPresentationTableInteractable artifact = ResolveOnTarget<ArtifactPresentationTableInteractable>( sub.targetId );
		if ( artifact != null )
		{
			required = sub.requiredCount > 0 ? sub.requiredCount : artifact.Capacity;
			current = Mathf.Clamp( artifact.CurrentCount, 0, Mathf.Max( 0, required ) );
			return required > 0;
		}

		return false;
	}

	static CoinDisplayTableInteractable FindUniqueCoinTableInVolume( ObjectiveDefinition objective )
	{
		if ( objective == null || string.IsNullOrEmpty( objective.showVolumeId ) )
			return null;
		if ( !EventTargetRegistry.TryGetVolume( objective.showVolumeId, out QuestVolume volume ) || volume == null )
			return null;

		CoinDisplayTableInteractable unique = null;
		IReadOnlyList<CoinDisplayTableInteractable> tables = CoinDisplayTableInteractable.ActiveTables;
		for ( int i = 0; i < tables.Count; i++ )
		{
			CoinDisplayTableInteractable candidate = tables[ i ];
			if ( candidate == null || !volume.ContainsWorldPoint( candidate.transform.position ) )
				continue;
			if ( unique != null )
				return null;
			unique = candidate;
		}

		return unique;
	}

	static bool TryGetConstellationCounts( ObjectiveSubDefinition sub, out int current, out int required )
	{
		current = 0;
		required = 0;

		GemConstellationInteractable constellation = ResolveOnTarget<GemConstellationInteractable>( sub.targetId );
		if ( constellation == null )
			return false;

		required = sub.requiredCount > 0 ? sub.requiredCount : constellation.Capacity;
		current = Mathf.Clamp( constellation.CurrentCount, 0, Mathf.Max( 0, required ) );
		return required > 0;
	}

	static T ResolveOnTarget<T>( string targetId ) where T : Component
	{
		if ( string.IsNullOrEmpty( targetId ) )
			return null;
		if ( !EventTargetRegistry.TryGetTarget( targetId, out QuestTarget target ) || target == null )
			return null;

		T match = target.GetComponent<T>();
		if ( match == null )
			match = target.GetComponentInParent<T>();
		if ( match == null )
			match = target.GetComponentInChildren<T>( true );
		return match;
	}
}
