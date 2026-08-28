using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Fallback contextual tutorial catalog when Addressables asset is missing or empty.
/// </summary>
public static class TutorialCatalogFallback
{
	static TutorialCatalogDefinition _runtime;

	public static TutorialCatalogDefinition GetOrCreate()
	{
		if ( _runtime != null )
			return _runtime;

		_runtime = ScriptableObject.CreateInstance<TutorialCatalogDefinition>();
		_runtime.name = "TutorialCatalogDefinition_Runtime";
		_runtime.tutorials = new List<TutorialDefinition>
		{
			CreateDigging(),
			CreateCoinPlacement(),
			CreateCoinStacking(),
			CreateCoinDisplays(),
			CreateGemConstellations(),
			CreateArtifacts()
		};
		return _runtime;
	}

	static TutorialDefinition CreateDigging()
	{
		TutorialDefinition t = Create( "tut_digging", "Digging" );
		t.tags = new[] { "digging" };
		t.trigger = TutorialTriggerType.TreasurePileDig;
		t.steps = new[]
		{
			Step(
				"Aim at the gold pile and dig to pull treasure into your hands.",
				"[{Interact}] Dig" ),
			Step(
				"Dug coins go into your coin pouch. Open it from the category slots when you need them again.",
				null )
		};
		return t;
	}

	static TutorialDefinition CreateCoinPlacement()
	{
		TutorialDefinition t = Create( "tut_coin_placement", "Coin Placement" );
		t.tags = new[] { "coins", "placement" };
		t.trigger = TutorialTriggerType.PlacementCompletedFloor;
		t.steps = new[]
		{
			Step(
				"Place held coins onto the floor or a surface with Place / Throw.",
				"[{SecondaryInteract}] Place" ),
			Step(
				"Rotate before placing to line coins up neatly.",
				"[{RotateLeft}] / [{RotateRight}] Rotate" )
		};
		return t;
	}

	static TutorialDefinition CreateCoinStacking()
	{
		TutorialDefinition t = Create( "tut_coin_stacking", "Coin Stacking" );
		t.tags = new[] { "coins", "stacking" };
		t.trigger = TutorialTriggerType.CoinStackChanged;
		t.steps = new[]
		{
			Step(
				"Drop matching coins onto each other to build a stack.",
				"[{SecondaryInteract}] Place on coin" ),
			Step(
				"Hold the stack pickup control to lift an entire stack at once.",
				"[{WholeStackPickup}] Whole stack" ),
			Step(
				"Hold the stack place control to set a whole stack back down.",
				"[{WholeStackPlace}] Place stack" )
		};
		return t;
	}

	static TutorialDefinition CreateCoinDisplays()
	{
		TutorialDefinition t = Create( "tut_coin_displays", "Coin Displays" );
		t.tags = new[] { "coins", "displays" };
		t.trigger = TutorialTriggerType.CoinDisplayTableChanged;
		t.steps = new[]
		{
			Step(
				"Coin display tables only accept their matching coin type.",
				"[{SecondaryInteract}] Place on display" ),
			Step(
				"Fill every slot to complete the display and progress that category.",
				null )
		};
		return t;
	}

	static TutorialDefinition CreateGemConstellations()
	{
		TutorialDefinition t = Create( "tut_gem_constellations", "Gem Constellations" );
		t.tags = new[] { "gems", "constellations" };
		t.trigger = TutorialTriggerType.GemConstellationChanged;
		t.steps = new[]
		{
			Step(
				"Place gems on constellation sockets that match their shape and type.",
				"[{SecondaryInteract}] Place gem" ),
			Step(
				"Connections light when both ends have gems. Fill the pattern to complete it.",
				null )
		};
		return t;
	}

	static TutorialDefinition CreateArtifacts()
	{
		TutorialDefinition t = Create( "tut_artifacts", "Artifacts" );
		t.tags = new[] { "artifacts" };
		t.trigger = TutorialTriggerType.ArtifactPresentationTableChanged;
		t.steps = new[]
		{
			Step(
				"Artifact presentation stands hold cleaned artifacts for display.",
				"[{SecondaryInteract}] Place artifact" ),
			Step(
				"Dirty artifacts may need polishing before they will sit properly on a stand.",
				"[{Clean}] Clean / Polish" )
		};
		return t;
	}

	static TutorialDefinition Create( string id, string title )
	{
		TutorialDefinition t = ScriptableObject.CreateInstance<TutorialDefinition>();
		t.id = id;
		t.name = id;
		t.title = title;
		return t;
	}

	static TutorialPopupStep Step( string body, string keybindHint )
	{
		return new TutorialPopupStep
		{
			body = body,
			keybindHint = keybindHint
		};
	}
}
