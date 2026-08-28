using System;

using UnityEngine;

/// <summary>Gameplay EventBus trigger that starts a contextual tutorial sequence.</summary>
public enum TutorialTriggerType
{
	None = 0,
	EnterVolume = 1,
	TreasurePileDig = 2,
	PlacementCompletedFloor = 3,
	CoinStackChanged = 4,
	CoinDisplayTableChanged = 5,
	GemConstellationChanged = 6,
	ArtifactPresentationTableChanged = 7,
	TreasureCollected = 8
}

[Serializable]
public class TutorialPopupStep
{
	[TextArea( 2, 6 )]
	public string body;

	[Tooltip( "Optional hint using {Interact}, {SecondaryInteract}, {WholeStackPickup}, {WholeStackPlace}, {RotateLeft}, {RotateRight}." )]
	public string keybindHint;
}

/// <summary>
/// One contextual tutorial: trigger + ordered popup steps.
/// Asset name does not need to match id; resolved via <see cref="TutorialCatalogDefinition"/>.
/// </summary>
[CreateAssetMenu( fileName = "TutorialDefinition", menuName = "Definitions/TutorialDefinition" )]
public class TutorialDefinition : ScriptableObject
{
	public string id;

	public string title;

	public string[] tags;

	public TutorialTriggerType trigger;

	[Tooltip( "Volume id when trigger is EnterVolume, or optional fallback volume for any trigger." )]
	public string volumeId;

	[Tooltip( "When set, entering this volume can start the tutorial if not yet completed (fallback)." )]
	public string fallbackVolumeId;

	public string[] prerequisiteTutorialIds;

	public TutorialPopupStep[] steps;

	void OnValidate()
	{
		if ( string.IsNullOrEmpty( id ) && !string.IsNullOrEmpty( name ) )
			id = name;
	}
}
