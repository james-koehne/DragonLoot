using System.Collections.Generic;

using UnityEngine;

public class DebugRecyclingSection : DebugOverlaySection
{
	static int s_machineIndex;
	static int s_recipeIndex;
	static string s_machineSearch = string.Empty;
	static string s_recipeSearch = string.Empty;
	static string s_lastStatus = string.Empty;
	static readonly List<RecyclingMachine> s_machines = new List<RecyclingMachine>( 8 );
	static readonly List<RecyclingRecipeDefinition> s_recipes = new List<RecyclingRecipeDefinition>( 16 );

	public string Title => "Recycling";

	public void Draw()
	{
		IReadOnlyList<RecyclingMachine> all = RecyclingMachine.All;
		s_machines.Clear();
		for ( int i = 0; i < all.Count; i++ )
		{
			if ( all[ i ] != null )
				s_machines.Add( all[ i ] );
		}

		GUILayout.Label( "Machines: " + s_machines.Count );
		if ( s_machines.Count == 0 )
		{
			GUILayout.Label( "No RecyclingMachine in scene" );
			return;
		}

		PlayerController player = DebugOverlay.GetPlayer();
		if ( player != null )
		{
			RecyclingMachine nearest = FindNearest( player.transform.position );
			if ( nearest != null && GUILayout.Button( "Select nearest" ) )
			{
				int idx = s_machines.IndexOf( nearest );
				if ( idx >= 0 )
					s_machineIndex = idx;
			}
		}

		if ( s_machineIndex >= s_machines.Count )
			s_machineIndex = 0;

		DebugOverlayGui.DrawFilteredPicker(
			s_machines,
			machine => machine.name,
			ref s_machineIndex,
			ref s_machineSearch,
			"No machines" );

		if ( s_machineIndex < 0 || s_machineIndex >= s_machines.Count )
			return;

		RecyclingMachine machine = s_machines[ s_machineIndex ];
		GUILayout.Label( "State: " + machine.State );
		GUILayout.Label( "Level: " + machine.StationLevel + "  Charge: " + machine.ChargeNormalized.ToString( "0.00" ) );
		GUILayout.Label( "Hopper: " + machine.HopperCount + " / " + machine.HopperCapacity );
		GUILayout.Label( "Can pull: " + machine.CanAcceptPull + "  Pulling: " + machine.IsPulling );

		GUILayout.BeginHorizontal();
		if ( GUILayout.Button( "Go" ) )
			TeleportNear( player, machine );
		if ( GUILayout.Button( "Pull x1" ) )
		{
			machine.DebugPullPulses( 1 );
			s_lastStatus = "Pull pulse x1";
			DebugOverlay.SetLastAction( s_lastStatus );
		}
		if ( GUILayout.Button( "Pull x5" ) )
		{
			machine.DebugPullPulses( 5 );
			s_lastStatus = "Pull pulse x5";
			DebugOverlay.SetLastAction( s_lastStatus );
		}
		GUILayout.EndHorizontal();

		GUILayout.BeginHorizontal();
		if ( GUILayout.Button( "Dump carry junk" ) )
		{
			PlayerCarry carry = player != null ? player.Carry : null;
			bool ok = machine.TryDumpCarryJunk( carry );
			s_lastStatus = ok ? "Dumped junk into hopper" : "Dump failed (empty/full/no recipes)";
			DebugOverlay.SetLastAction( s_lastStatus );
		}
		if ( GUILayout.Button( "Clear hopper" ) )
		{
			machine.DebugClearHopper();
			s_lastStatus = "Cleared hopper";
			DebugOverlay.SetLastAction( s_lastStatus );
		}
		GUILayout.EndHorizontal();

		DrawRecipeTools( machine, player );
		DebugOverlayGui.DrawStatus( s_lastStatus );
	}

	void DrawRecipeTools( RecyclingMachine machine, PlayerController player )
	{
		RecyclingRecipeCatalog catalog = machine.Catalog;
		s_recipes.Clear();
		if ( catalog != null && catalog.recipes != null )
		{
			for ( int i = 0; i < catalog.recipes.Count; i++ )
			{
				RecyclingRecipeDefinition recipe = catalog.recipes[ i ];
				if ( recipe != null && recipe.input != null )
					s_recipes.Add( recipe );
			}
		}

		GUILayout.Space( 4f );
		GUILayout.Label( "Recipes: " + s_recipes.Count );
		if ( s_recipes.Count == 0 )
		{
			GUILayout.Label( "No recipes on catalog" );
			return;
		}

		if ( s_recipeIndex >= s_recipes.Count )
			s_recipeIndex = 0;

		DebugOverlayGui.DrawFilteredPicker(
			s_recipes,
			FormatRecipe,
			ref s_recipeIndex,
			ref s_recipeSearch,
			"No recipes" );

		if ( s_recipeIndex < 0 || s_recipeIndex >= s_recipes.Count )
			return;

		RecyclingRecipeDefinition selected = s_recipes[ s_recipeIndex ];
		GUILayout.BeginHorizontal();
		if ( GUILayout.Button( "Give input to hands" ) )
		{
			int added = GiveInputToCarry( player, selected );
			s_lastStatus = added > 0 ? "Added " + added + "x " + FormatRecipe( selected ) : "Give failed";
			DebugOverlay.SetLastAction( s_lastStatus );
		}
		if ( GUILayout.Button( "Spawn into hopper" ) )
		{
			bool ok = SpawnIntoHopper( machine, selected );
			s_lastStatus = ok ? "Spawned into hopper: " + FormatRecipe( selected ) : "Hopper accept failed";
			DebugOverlay.SetLastAction( s_lastStatus );
		}
		GUILayout.EndHorizontal();

		if ( GUILayout.Button( "Fill hopper with recipe" ) )
		{
			int accepted = 0;
			while ( !machine.IsHopperFull )
			{
				if ( !SpawnIntoHopper( machine, selected ) )
					break;
				accepted++;
			}
			s_lastStatus = "Filled hopper with " + accepted + "x " + FormatRecipe( selected );
			DebugOverlay.SetLastAction( s_lastStatus );
		}
	}

	static string FormatRecipe( RecyclingRecipeDefinition recipe )
	{
		if ( recipe == null || recipe.input == null )
			return "(null)";
		if ( !string.IsNullOrEmpty( recipe.input.displayName ) )
			return recipe.input.displayName;
		if ( !string.IsNullOrEmpty( recipe.input.id ) )
			return recipe.input.id;
		return recipe.input.name;
	}

	static int GiveInputToCarry( PlayerController player, RecyclingRecipeDefinition recipe )
	{
		if ( player == null || player.Carry == null || recipe == null || recipe.input == null )
			return 0;
		return player.Carry.TryAdd( recipe.input ) ? 1 : 0;
	}

	static bool SpawnIntoHopper( RecyclingMachine machine, RecyclingRecipeDefinition recipe )
	{
		if ( machine == null || recipe == null || recipe.input == null )
			return false;
		if ( machine.IsHopperFull )
			return false;

		Vector3 pos = machine.transform.position + Vector3.up * 1.2f;
		TreasureItem item = TreasureItemFactory.SpawnSync( recipe.input, pos, Quaternion.identity, null );
		if ( item == null )
			return false;

		return machine.TryAcceptJunkItem( item, fromHand: false );
	}

	static RecyclingMachine FindNearest( Vector3 from )
	{
		IReadOnlyList<RecyclingMachine> all = RecyclingMachine.All;
		RecyclingMachine best = null;
		float bestSqr = float.MaxValue;
		for ( int i = 0; i < all.Count; i++ )
		{
			RecyclingMachine machine = all[ i ];
			if ( machine == null )
				continue;
			float sqr = ( machine.transform.position - from ).sqrMagnitude;
			if ( sqr < bestSqr )
			{
				bestSqr = sqr;
				best = machine;
			}
		}

		return best;
	}

	static void TeleportNear( PlayerController player, RecyclingMachine machine )
	{
		if ( player == null || machine == null )
		{
			DebugOverlay.SetLastAction( "Teleport failed" );
			return;
		}

		Vector3 pos = machine.transform.position - machine.transform.forward * 2f + Vector3.up * 0.1f;
		Quaternion rot = Quaternion.LookRotation( ( machine.transform.position - pos ).normalized, Vector3.up );
		player.TeleportTo( pos, rot );
		s_lastStatus = "Teleported near " + machine.name;
		DebugOverlay.SetLastAction( s_lastStatus );
	}
}
