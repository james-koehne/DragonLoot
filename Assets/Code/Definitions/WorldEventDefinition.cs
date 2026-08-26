using System;

using UnityEngine;

public enum WorldEventConditionType
{
	GameStarted = 0,
	EnterVolume = 1,
	PickupTreasure = 2
}

public enum WorldEventActionType
{
	Dialogue = 0,
	SpawnAddressable = 1,
	SetTutorialHud = 2
}

[Serializable]
public class DragonDialogueLine
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
public class WorldEventCondition
{
	public WorldEventConditionType type;

	[Tooltip( "EventVolume id for EnterVolume. Ignored by other types." )]
	public string targetId;

	[Tooltip( "Optional treasure filter for PickupTreasure. Null = any treasure." )]
	public TreasureDefinition requiredTreasure;

	[Tooltip( "When true, PickupTreasure also filters by requiredCategory." )]
	public bool filterByCategory;

	public TreasureCategory requiredCategory;

	[Tooltip( "When true, PickupTreasure ignores gold bars." )]
	public bool excludeGoldBars;

	[Tooltip( "When true, PickupTreasure only matches interleaved gold bars." )]
	public bool requireGoldBars;
}

[Serializable]
public class WorldEventAction
{
	public WorldEventActionType type;

	[Tooltip( "Dialogue lines when type is Dialogue." )]
	public DragonDialogueLine[] dialogue;

	[Tooltip( "Addressables key when type is SpawnAddressable." )]
	public string addressableKey;

	[Tooltip( "EventSpawnPoint id. Empty + useWorldPosition uses spawnWorldPosition." )]
	public string spawnPointId;

	public Vector3 spawnWorldPosition;

	public bool useWorldPosition;

	[Tooltip( "Tutorial HUD title when type is SetTutorialHud." )]
	public string tutorialTitle;

	[TextArea( 1, 4 )]
	[Tooltip( "One objective row per line when type is SetTutorialHud." )]
	public string tutorialObjectiveText;

	[Tooltip( "Marker / outline target id for SetTutorialHud. Empty = no marker." )]
	public string markerTargetId;
}

/// <summary>
/// One-shot world event. Asset name does not need to match type; resolved via <see cref="WorldEventCatalogDefinition"/>.
/// </summary>
[CreateAssetMenu( fileName = "WorldEventDefinition", menuName = "Definitions/WorldEventDefinition" )]
public class WorldEventDefinition : ScriptableObject
{
	public string id;

	[Tooltip( "Tags for future contextual tutorial (e.g. intro, movement)." )]
	public string[] tags;

	public WorldEventCondition[] conditions;

	public WorldEventAction[] actions;

	void OnValidate()
	{
		if ( string.IsNullOrEmpty( id ) && !string.IsNullOrEmpty( name ) )
			id = name;
	}
}
