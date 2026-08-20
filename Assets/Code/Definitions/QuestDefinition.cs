using System;

using UnityEngine;

public enum QuestConditionType
{
	EnterVolume = 0,
	ClearPile = 1,
	PlaceOnOwner = 2,
	UseCoinSorter = 3,
	CompleteConstellation = 4,
	CompleteArtifactTable = 5,
	CompleteCoinDisplay = 6,
	SectionSorted = 7,
	CompleteGoldBarDisplay = 8,
	PickupTreasure = 9,
	CompleteGroundZoneGoldBars = 10,
	CompleteDoorPileCoins = 11
}

public enum QuestConditionMode
{
	All = 0
}

[Serializable]
public class QuestDialogueLine
{
	public string speaker = "Dragon";

	[TextArea( 1, 4 )]
	public string text;

	[Tooltip( "Seconds to wait after this line before the next (or before continuing)." )]
	[Min( 0f )]
	public float pauseAfter;

	[Tooltip( "Optional one-shot played when this line is shown." )]
	public AudioClip sfx;

	[Range( 0f, 1f )]
	public float sfxVol = 0.5f;
}

[Serializable]
public class QuestCondition
{
	public QuestConditionType type;

	[Tooltip( "QuestTarget / QuestVolume id for this condition. Empty + areaId = all matching in that area." )]
	public string targetId;

	[Tooltip( "When set, matches QuestTarget.areaId. Empty targetId means every matching station in the area." )]
	public string areaId;

	[Tooltip( "Required place count for PlaceOnOwner. Ignored by other types." )]
	[Min( 1 )]
	public int requiredCount = 1;

	[Tooltip( "Optional treasure filter for PlaceOnOwner / PickupTreasure. Null = any treasure." )]
	public TreasureDefinition requiredTreasure;

	[Tooltip( "When true, PlaceOnOwner / PickupTreasure also filter by requiredCategory." )]
	public bool filterByCategory;

	public TreasureCategory requiredCategory;

	[Tooltip( "When true, PickupTreasure / PlaceOnOwner ignore gold bars." )]
	public bool excludeGoldBars;

	[Tooltip( "When true, PickupTreasure / PlaceOnOwner only match interleaved gold bars." )]
	public bool requireGoldBars;

	[Tooltip( "Optional section id for SectionSorted. Empty = any section." )]
	public string sectionId;
}

[Serializable]
public class QuestObjective
{
	public string id;

	[TextArea( 1, 3 )]
	public string objectiveText;

	[Tooltip( "QuestTarget / QuestMarkerAnchor id for compass + world marker. Empty = hide marker." )]
	public string markerTargetId;

	[Tooltip( "Optional objectives never block parent / quest completion." )]
	public bool optional;

	public QuestDialogueLine[] onCompleteDialogue;

	public QuestConditionMode conditionMode = QuestConditionMode.All;

	public QuestCondition[] conditions;

	[Tooltip( "Parallel children of this objective. Required children must all complete." )]
	public QuestObjective[] subObjectives;
}

[Serializable]
public class QuestEvent
{
	public string id;

	public QuestCondition[] conditions;

	public QuestDialogueLine[] dialogue;

	[Tooltip( "Optional one-shot played when this event fires (FeedbackSystem)." )]
	public AudioClip stinger;

	[Range( 0f, 1f )]
	public float stingerVol = 0.8f;
}

/// <summary>
/// Quest or subquest. Asset name does not need to match type; resolved via <see cref="QuestCatalogDefinition"/>.
/// </summary>
[CreateAssetMenu( fileName = "QuestDefinition", menuName = "Definitions/QuestDefinition" )]
public class QuestDefinition : ScriptableObject
{
	public string id;

	public string displayTitle;

	public QuestDialogueLine[] onStartDialogue;

	public QuestDialogueLine[] onCompleteDialogue;

	public QuestObjective[] objectives;

	public QuestEvent[] events;

	public QuestDefinition[] subquests;

	[Tooltip( "When this asset is a subquest, reveal while the parent objective with this id is active." )]
	public string revealWhenParentObjectiveId;

	[Tooltip( "When this asset is a subquest, completing the parent row with this id also completes this subquest." )]
	public string linkedParentObjectiveId;

	public string ResolveTitle()
	{
		if ( !string.IsNullOrEmpty( displayTitle ) )
			return displayTitle;
		if ( !string.IsNullOrEmpty( id ) )
			return id;
		return name;
	}

	void OnValidate()
	{
		if ( string.IsNullOrEmpty( id ) && !string.IsNullOrEmpty( name ) )
			id = name;

		if ( string.IsNullOrEmpty( displayTitle ) && !string.IsNullOrEmpty( name ) )
			displayTitle = name;
	}
}
