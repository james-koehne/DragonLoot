using UnityEngine;

/// <summary>
/// Resolves per-metal coin sorter <see cref="QuestTarget"/> ids (gold / silver / copper).
/// Legacy <see cref="QuestSceneAutoWire.IdCoinSorter"/> matches any sorter in conditions.
/// </summary>
public static class QuestCoinSorterTargets
{
	public static bool IsLegacyTargetId( string targetId )
	{
		return targetId == QuestSceneAutoWire.IdCoinSorter;
	}

	public static bool IsCoinSorterTargetId( string targetId )
	{
		if ( string.IsNullOrEmpty( targetId ) )
			return false;
		return targetId == QuestSceneAutoWire.IdCoinSorterCopper ||
		       targetId == QuestSceneAutoWire.IdCoinSorterSilver ||
		       targetId == QuestSceneAutoWire.IdCoinSorterGold ||
		       IsLegacyTargetId( targetId );
	}

	public static string ResolveTargetId( TreasureDefinition coin )
	{
		if ( coin == null || coin.category != TreasureCategory.Coin )
			return null;

		if ( MatchesMetal( coin, "copper" ) )
			return QuestSceneAutoWire.IdCoinSorterCopper;
		if ( MatchesMetal( coin, "silver" ) )
			return QuestSceneAutoWire.IdCoinSorterSilver;
		if ( MatchesMetal( coin, "gold" ) )
			return QuestSceneAutoWire.IdCoinSorterGold;

		return QuestSceneAutoWire.IdCoinSorterGold;
	}

	public static string TryResolveHeldTargetId()
	{
		GameMode gameMode = GameMode.Instance;
		if ( gameMode == null )
			return null;

		PlayerController player = gameMode.Player;
		if ( player == null )
			return null;

		PlayerCarry carry = player.Carry;
		if ( carry == null || !carry.HasActive )
			return null;

		TreasureDefinition definition;
		if ( !carry.TryPeekActive( out definition ) || definition == null )
			return null;
		if ( definition.category != TreasureCategory.Coin )
			return null;

		return ResolveTargetId( definition );
	}

	/// <summary>Maps legacy coin_sorter markers to the held coin's sorter when possible.</summary>
	public static string ResolveOutlineTargetId( string targetId )
	{
		if ( !IsLegacyTargetId( targetId ) )
			return targetId;

		string resolved = TryResolveHeldTargetId();
		if ( !string.IsNullOrEmpty( resolved ) )
			return resolved;
		return targetId;
	}

	public static bool MatchesSorterTarget( QuestCondition condition, string actualTargetId )
	{
		if ( condition == null )
			return false;
		if ( string.IsNullOrEmpty( condition.targetId ) )
			return true;
		if ( condition.targetId == actualTargetId )
			return true;
		if ( IsLegacyTargetId( condition.targetId ) && IsCoinSorterTargetId( actualTargetId ) )
			return true;
		return false;
	}

	static bool MatchesMetal( TreasureDefinition coin, string metal )
	{
		if ( coin == null || string.IsNullOrEmpty( metal ) )
			return false;

		if ( !string.IsNullOrEmpty( coin.variant ) &&
		     coin.variant.IndexOf( metal, System.StringComparison.OrdinalIgnoreCase ) >= 0 )
			return true;
		if ( !string.IsNullOrEmpty( coin.id ) &&
		     coin.id.IndexOf( metal, System.StringComparison.OrdinalIgnoreCase ) >= 0 )
			return true;
		if ( !string.IsNullOrEmpty( coin.displayName ) &&
		     coin.displayName.IndexOf( metal, System.StringComparison.OrdinalIgnoreCase ) >= 0 )
			return true;
		return false;
	}
}
