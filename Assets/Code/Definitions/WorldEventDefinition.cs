using System;

using UnityEngine;

public enum WorldEventConditionType
{
	GameStarted = 0,
	EnterVolume = 1,
	PickupTreasure = 2,
	PlayerGameplayInput = 3,
	ElapsedUnscaledSeconds = 4,
	TutorialCompleted = 5,
	WorldEventCompleted = 6,
	ObjectiveCompleted = 7
}

public enum WorldEventActionType
{
	Dialogue = 0,
	SpawnAddressable = 1,
	SetTutorialHud = 2,
	PlayAudio = 3,
	LanternRevealSweep = 4,
	CinematicPresentation = 5,
	BrakePlayerMovement = 6,
	IgniteLanterns = 7
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

	[Tooltip( "EventVolume id for EnterVolume, tutorial id for TutorialCompleted (e.g. tut_movement), world event id for WorldEventCompleted, or objective id for ObjectiveCompleted. Ignored by other types." )]
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

	[Tooltip( "Seconds since catalog start for ElapsedUnscaledSeconds. Ignored by other types." )]
	[Min( 0f )]
	public float delaySeconds = 2f;
}

[Serializable]
public class WorldEventAction
{
	public WorldEventActionType type;

	[Tooltip( "Seconds to wait before this action starts. By default later actions wait too (list order). Enable onlyDelayThisAction to delay only this action." )]
	[Min( 0f )]
	public float delayBefore;

	[Tooltip( "When true, delayBefore only delays this action; later actions continue immediately without waiting for the delay." )]
	public bool onlyDelayThisAction;

	[Tooltip( "When true, later actions wait until this one finishes. Dialogue waits until lines complete; audio waits clip length; cinematic waits until presentation ends; brake waits brake duration; IgniteLanterns waits fade duration." )]
	public bool waitUntilFinished;

	[Tooltip( "Dialogue lines when type is Dialogue." )]
	public DragonDialogueLine[] dialogue;

	[Tooltip( "Addressables key when type is SpawnAddressable." )]
	public string addressableKey;

	[Tooltip( "EventSpawnPoint id. Empty + useWorldPosition uses spawnWorldPosition." )]
	public string spawnPointId;

	public Vector3 spawnWorldPosition;

	public bool useWorldPosition;

	[Tooltip( "When true, SpawnAddressable uses the player transform (ignores spawn point / world position)." )]
	public bool spawnAtPlayer;

	[Tooltip( "Tutorial HUD title when type is SetTutorialHud." )]
	public string tutorialTitle;

	[TextArea( 1, 4 )]
	[Tooltip( "One objective row per line when type is SetTutorialHud." )]
	public string tutorialObjectiveText;

	[Tooltip( "Marker / outline target id for SetTutorialHud. Empty = no marker." )]
	public string markerTargetId;

	[Tooltip( "Clip when type is PlayAudio." )]
	public AudioClip audioClip;

	[Range( 0f, 1f )]
	public float audioVolumeMin = 1f;

	[Range( 0f, 1f )]
	public float audioVolumeMax = 1f;

	[Range( -3f, 3f )]
	public float audioPitchMin = 1f;

	[Range( -3f, 3f )]
	public float audioPitchMax = 1f;

	[Range( 0f, 1f )]
	[Tooltip( "0 = 2D, 1 = full 3D at the play position." )]
	public float audioSpatialBlend;

	[Min( 0.01f )]
	public float audioMinDistance = 1f;

	[Min( 0.01f )]
	public float audioMaxDistance = 20f;

	[Tooltip( "When true, PlayAudio uses the player position. Otherwise uses spawn point / world position." )]
	public bool audioAtPlayer;

	[Tooltip( "When true, PlayAudio uses spawnWorldPosition instead of spawnPointId." )]
	public bool audioUseWorldPosition;

	[Tooltip( "Reveal id for LanternRevealSweep. Matches LanternRevealSweepController / reveal lanterns. Timing lives on the controller." )]
	public string lanternRevealId;

	[Tooltip( "LanternActivator Group Id or Reveal Id for IgniteLanterns. Lights every matching lantern; use Manual mode so they do not also ignite by distance." )]
	public string lanternGroupId;

	[Tooltip( "Fade seconds for IgniteLanterns. 0 = instant." )]
	[Min( 0f )]
	public float lanternIgniteFadeDuration;

	[Tooltip( "Presentation id for CinematicPresentation. Matches CinematicPresentationController. Timing and cues live on the controller." )]
	public string cinematicPresentationId;

	[Tooltip( "Seconds to interpolate planar velocity to zero when type is BrakePlayerMovement. 0 = instant stop." )]
	[Min( 0f )]
	public float playerBrakeDuration = 1f;
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

	[Tooltip( "When true, firing only marks progress for this session (not saved). Use for repeatable spawns like companions." )]
	public bool sessionOnly;

	[Tooltip( "When true, conditions are not evaluated. Fire from objective On Complete World Event Ids or debug." )]
	public bool manualOnly;

	void OnValidate()
	{
		if ( string.IsNullOrEmpty( id ) && !string.IsNullOrEmpty( name ) )
			id = name;
	}
}
