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

	[Tooltip( "QuestTarget id for display / pile / constellation, volume id for EnterVolume, or world event id for WorldEventFired. Empty = any matching event of that type." )]
	public string targetId;
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

	[Tooltip( "Optional HUD reward line override (e.g. Unlock Glide). Empty = built from rewards." )]
	public string rewardLabel;

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

	public string ResolveRewardLabel()
	{
		if ( !string.IsNullOrEmpty( rewardLabel ) )
			return rewardLabel;

		if ( rewards == null || rewards.Length == 0 )
			return string.Empty;

		for ( int i = 0; i < rewards.Length; i++ )
		{
			ObjectiveReward reward = rewards[ i ];
			if ( reward == null )
				continue;

			if ( reward.type == ObjectiveRewardType.Ability && !string.IsNullOrEmpty( reward.abilityId ) )
			{
				AbilitySystem abilities = AbilitySystem.Instance;
				if ( abilities != null && abilities.TryGetDefinition( reward.abilityId, out AbilityDefinition ability ) && ability != null )
					return "Reward: " + ability.ResolveDisplayName();
				return "Reward: " + reward.abilityId;
			}

			if ( reward.type == ObjectiveRewardType.Upgrade && !string.IsNullOrEmpty( reward.upgradeId ) )
			{
				UpgradeSystem upgrades = UpgradeSystem.Instance;
				if ( upgrades != null && upgrades.TryGetDefinition( reward.upgradeId, out UpgradeDefinition upgrade ) && upgrade != null )
					return "Reward: " + upgrade.ResolveDisplayName();
				return "Reward: " + reward.upgradeId;
			}
		}

		return string.Empty;
	}

	void OnValidate()
	{
		if ( string.IsNullOrEmpty( id ) && !string.IsNullOrEmpty( name ) )
			id = name;
	}
}
