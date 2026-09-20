using System;

using UnityEngine;

/// <summary>Gameplay action that completes one objective sub-step.</summary>
public enum ObjectiveSubCompleteType
{
	None = 0,
	DisplayComplete = 1,
	PileEmptied = 2,
	ConstellationComplete = 3,
	EnterVolume = 4,
	WorldEventFired = 5
}

public enum ObjectiveRewardType
{
	Ability = 0,
	Upgrade = 1
}

[Serializable]
public class ObjectiveSubDefinition
{
	public string id;

	public string label;

	public ObjectiveSubCompleteType completeType;

	[Tooltip( "QuestTarget id for display / pile / constellation, volume id for EnterVolume, or world event id for WorldEventFired. Empty = any matching event of that type. PileEmptied with an empty id uses piles inside showVolumeId." )]
	public string targetId;

	[Tooltip( "Count shown as label: current/required. 0 = use the target's capacity or pile size." )]
	[Min( 0 )]
	public int requiredCount;
}

[Serializable]
public class ObjectiveReward
{
	public ObjectiveRewardType type;

	[Tooltip( "Ability catalog id when type is Ability." )]
	public string abilityId;

	[Tooltip( "Upgrade catalog id when type is Upgrade." )]
	public string upgradeId;

	[Tooltip( "Upgrade level to set when type is Upgrade. 0 = unlock only." )]
	[Min( 0 )]
	public int upgradeLevel;
}

/// <summary>
/// Authored objective: main goal completes when all sub-objectives complete.
/// Asset name does not need to match id; resolved via <see cref="ObjectiveCatalogDefinition"/>.
/// </summary>
[CreateAssetMenu( fileName = "ObjectiveDefinition", menuName = "Definitions/ObjectiveDefinition" )]
public class ObjectiveDefinition : ScriptableObject
{
	public string id;

	public string title;

	[Tooltip( "QuestVolume id for the island / area. When set, the nearby HUD shows this objective while the player is inside that volume." )]
	public string showVolumeId;

	[Tooltip( "Fallback when showVolumeId is empty: horizontal distance (m) to the first unresolved sub target." )]
	[Min( 1f )]
	public float showRadius = 40f;

	public string[] prerequisiteObjectiveIds;

	public ObjectiveSubDefinition[] subs;

	public ObjectiveReward[] rewards;

	[Tooltip( "Optional HUD reward line override (e.g. Platforms). Empty = built from ability/upgrade rewards." )]
	public string rewardLabel;

	[Tooltip( "When true, show rewardLabel on the HUD even if this objective only fires world events / platforms. Ability and upgrade rewards always show." )]
	public bool showReward;

	[Tooltip( "Optional unlock toast when this objective completes (e.g. Island complete — platforms activated). Empty = no toast." )]
	public string completionToast;

	[Tooltip( "World event ids to fire (if not already fired) when this objective completes." )]
	public string[] onCompleteWorldEventIds;

	public string ResolveTitle()
	{
		if ( !string.IsNullOrEmpty( title ) )
			return title;
		if ( !string.IsNullOrEmpty( id ) )
			return id;
		return name;
	}

	public bool HasGrantableReward()
	{
		if ( rewards == null )
			return false;

		for ( int i = 0; i < rewards.Length; i++ )
		{
			ObjectiveReward reward = rewards[ i ];
			if ( reward == null )
				continue;
			if ( reward.type == ObjectiveRewardType.Ability && !string.IsNullOrEmpty( reward.abilityId ) )
				return true;
			if ( reward.type == ObjectiveRewardType.Upgrade && !string.IsNullOrEmpty( reward.upgradeId ) )
				return true;
		}

		return false;
	}

	public bool ShouldShowReward()
	{
		if ( HasGrantableReward() )
			return true;
		return showReward && !string.IsNullOrEmpty( rewardLabel );
	}

	public string ResolveRewardLabel()
	{
		if ( !string.IsNullOrEmpty( rewardLabel ) )
			return PrefixReward( rewardLabel );
		return ResolveFirstGrantableRewardLabel();
	}

	string ResolveFirstGrantableRewardLabel()
	{
		if ( rewards == null )
			return string.Empty;

		for ( int i = 0; i < rewards.Length; i++ )
		{
			string line = FormatRewardLine( rewards[ i ] );
			if ( !string.IsNullOrEmpty( line ) )
				return line;
		}

		return string.Empty;
	}

	public static string FormatRewardLine( ObjectiveReward reward )
	{
		if ( reward == null )
			return string.Empty;

		if ( reward.type == ObjectiveRewardType.Ability && !string.IsNullOrEmpty( reward.abilityId ) )
		{
			AbilitySystem abilities = AbilitySystem.Instance;
			if ( abilities != null && abilities.TryGetDefinition( reward.abilityId, out AbilityDefinition ability ) && ability != null )
				return PrefixReward( "Unlock " + ability.ResolveDisplayName() );
			return PrefixReward( "Unlock " + reward.abilityId );
		}

		if ( reward.type == ObjectiveRewardType.Upgrade && !string.IsNullOrEmpty( reward.upgradeId ) )
		{
			UpgradeSystem upgrades = UpgradeSystem.Instance;
			if ( upgrades != null && upgrades.TryGetDefinition( reward.upgradeId, out UpgradeDefinition upgrade ) && upgrade != null )
				return PrefixReward( "Unlock " + upgrade.ResolveDisplayName() );
			return PrefixReward( "Unlock " + reward.upgradeId );
		}

		return string.Empty;
	}

	static string PrefixReward( string label )
	{
		if ( string.IsNullOrEmpty( label ) )
			return string.Empty;
		if ( label.StartsWith( "Reward:" ) )
			return label;
		return "Reward: " + label;
	}

	void OnValidate()
	{
		if ( string.IsNullOrEmpty( id ) && !string.IsNullOrEmpty( name ) )
			id = name;
	}
}
