using System;

using UnityEngine;

/// <summary>Condition that shows a contextual tutorial while true (and incomplete).</summary>
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
	TreasureCollected = 8,
	HoldingCategory = 9,
	AimTreasurePile = 10,
	GameStart = 11,
	AimCoinStack = 12,
	ManyCoins = 13
}

/// <summary>Gameplay action that ticks off one tutorial task.</summary>
public enum TutorialTaskCompleteType
{
	None = 0,
	Dig = 1,
	PlaceCoinFloor = 2,
	ThrowCoin = 3,
	StackCoins = 4,
	WholeStackPickup = 5,
	WholeStackPlace = 6,
	PlaceCoinDisplay = 7,
	PlaceArtifactStand = 8,
	PlaceGemConstellation = 9,
	OpenMap = 10,
	EnterVolume = 11,
	Move = 12,
	Look = 13,
	UseCoinSorter = 14
}

[Serializable]
public class TutorialTask
{
	public string id;

	public string label;

	public TutorialTaskCompleteType completeTrigger;

	[Tooltip( "When completeTrigger is EnterVolume: volume id that completes this task." )]
	public string completeVolumeId;
}

/// <summary>
/// One contextual tutorial: show context + explanation + concurrent tasks.
/// Asset name does not need to match id; resolved via <see cref="TutorialCatalogDefinition"/>.
/// </summary>
[CreateAssetMenu( fileName = "TutorialDefinition", menuName = "Definitions/TutorialDefinition" )]
public class TutorialDefinition : ScriptableObject
{
	public string id;

	public string title;

	public string[] tags;

	public TutorialTriggerType trigger;

	[Tooltip( "When trigger is HoldingCategory: which treasure family must be held (selected bucket with items)." )]
	public TreasureCategory holdingCategory;

	[Tooltip( "Volume id when trigger is EnterVolume, or optional fallback volume for any trigger." )]
	public string volumeId;

	[Tooltip( "When set, being inside this volume also counts as active show context (OR with primary trigger)." )]
	public string fallbackVolumeId;

	[Tooltip( "When true, aiming at a gold/treasure pile also counts as active show context." )]
	public bool alsoShowWhenAimingTreasurePile;

	[Tooltip( "When true, aiming at a ground coin stack (2+ coins) also counts as active show context." )]
	public bool alsoShowWhenAimingCoinStack;

	[Tooltip( "When trigger is ManyCoins: show if this many coins are placed in the world (ground stacks)." )]
	public int minWorldCoinsToShow = 100;

	[Tooltip( "When trigger is ManyCoins: show if this many coins are carried." )]
	public int minCarriedCoinsToShow = 50;

	[Tooltip( "When a UseCoinSorter task is active: complete after this many coins are sorted (whichever comes first with sorting a stack)." )]
	public int sorterCoinsToComplete = 30;

	public string[] prerequisiteTutorialIds;

	[Tooltip( "Optional map label text to highlight while this tutorial is active (e.g. Coin Hall)." )]
	public string highlightMapLabel;

	[TextArea( 2, 8 )]
	public string body;

	[Tooltip( "Optional hint using {Interact}, {SecondaryInteract}, {WholeStackPickup}, {WholeStackPlace}, {RotateLeft}, {RotateRight}." )]
	public string keybindHint;

	public TutorialTask[] tasks;

	void OnValidate()
	{
		if ( string.IsNullOrEmpty( id ) && !string.IsNullOrEmpty( name ) )
			id = name;
	}
}
