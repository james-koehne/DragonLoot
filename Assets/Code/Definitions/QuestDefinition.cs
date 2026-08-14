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
	SectionSorted = 7
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

	[Tooltip( "QuestTarget / QuestVolume id for this condition." )]
	public string targetId;

	[Tooltip( "Required place count for PlaceOnOwner. Ignored by other types." )]
	[Min( 1 )]
	public int requiredCount = 1;

	[Tooltip( "Optional treasure filter for PlaceOnOwner. Null = any treasure." )]
	public TreasureDefinition requiredTreasure;

	[Tooltip( "Optional section id for SectionSorted. Empty = any section." )]
	public string sectionId;
}

[Serializable]
public class QuestStep
{
	public string id;

	[TextArea( 1, 3 )]
	public string objectiveText;

	[Tooltip( "QuestTarget / QuestMarkerAnchor id for compass + world marker. Empty = hide marker." )]
	public string markerTargetId;

	public QuestDialogueLine[] onEnterDialogue;

	public QuestDialogueLine[] onCompleteDialogue;

	public QuestConditionMode conditionMode = QuestConditionMode.All;

	public QuestCondition[] conditions;
}

/// <summary>
/// One linear quest. Asset name does not need to match type; resolved via <see cref="QuestCatalogDefinition"/>.
/// </summary>
[CreateAssetMenu( fileName = "QuestDefinition", menuName = "Definitions/QuestDefinition" )]
public class QuestDefinition : ScriptableObject
{
	public string id;

	public string displayTitle;

	public QuestDialogueLine[] onStartDialogue;

	public QuestDialogueLine[] onCompleteDialogue;

	public QuestStep[] steps;

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
