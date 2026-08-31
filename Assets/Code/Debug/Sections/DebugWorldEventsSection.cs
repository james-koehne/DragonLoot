using System.Collections.Generic;

using UnityEngine;

public class DebugWorldEventsSection : DebugOverlaySection
{
	public string Title => "World Events";

	public void Draw()
	{
		WorldEventSystem system = WorldEventSystem.Instance;
		if ( system == null )
			system = WorldEventSystem.EnsureExists();

		if ( system == null )
		{
			GUILayout.Label( "No WorldEventSystem" );
			return;
		}

		WorldEventCatalogDefinition catalog = system.Catalog;
		int count = catalog != null ? catalog.Count : 0;
		GUILayout.Label( "Catalog events: " + count );
		GUILayout.Label( "Game started: " + system.GameStarted );

		ProfileSaveData save = ProfileManager.Instance != null ? ProfileManager.Instance.ProfileSaveData : null;
		int fired = 0;
		if ( save != null )
		{
			save.EnsureWorldEventProgress();
			fired = save.firedWorldEventIds != null ? save.firedWorldEventIds.Count : 0;
		}
		GUILayout.Label( "Fired (save): " + fired );

		GUILayout.Space( 6f );
		if ( GUILayout.Button( "Reset Fired Events" ) )
			system.DebugResetFiredEvents();

		GUILayout.Space( 4f );
		GUILayout.Label( "Fire intro:" );
		GUILayout.BeginHorizontal();
		if ( GUILayout.Button( "Welcome" ) )
			system.DebugFireEvent( "intro_welcome" );
		if ( GUILayout.Button( "Hallway" ) )
			system.DebugFireEvent( "intro_hallway" );
		if ( GUILayout.Button( "Ledge" ) )
			system.DebugFireEvent( "intro_ledge" );
		GUILayout.EndHorizontal();

		if ( GUILayout.Button( "Start Catalog" ) )
			system.StartCatalog();

		DrawLanternDebug();
		DrawHallwayFogDebug();
	}

	static void DrawHallwayFogDebug()
	{
		GUILayout.Space( 8f );
		GUILayout.Label( "Hallway Fog" );

		HallwayFogBlend blend = HallwayFogBlend.Active;
		if ( blend == null )
		{
			GUILayout.Label( "No HallwayFogBlend in scene" );
			return;
		}

		GUILayout.Label( "Locked: " + blend.IsLocked );
		GUILayout.Label( "Blend ready: " + blend.IsBlendReady );
		GUILayout.Label( "T: " + blend.CurrentT.ToString( "0.###" ) );
		GUILayout.Label( "Player local Z: " + blend.PlayerLocalZ.ToString( "0.###" ) + " (start " + blend.StartZ.ToString( "0.###" ) + ", end " + blend.EndZ.ToString( "0.###" ) + ")" );
		GUILayout.Label( "Density: " + blend.CurrentDensity.ToString( "0.#####" ) );
		GUILayout.Label( "Fog influence: " + blend.CurrentFogInfluenceScale.ToString( "0.###" ) );

		GUILayout.BeginHorizontal();
		if ( GUILayout.Button( "Reset Hallway Fog" ) )
			HallwayFogBlend.DebugResetAll();
		if ( GUILayout.Button( "Snap Start To Spawn" ) && HallwayFogBlend.Active != null )
			HallwayFogBlend.Active.SnapStartZToPlayerSpawn();
		GUILayout.EndHorizontal();
	}

	static void DrawLanternDebug()
	{
		GUILayout.Space( 8f );
		GUILayout.Label( "Lanterns" );

		if ( GUILayout.Button( "Ignite All Lanterns" ) )
		{
			IReadOnlyList<LanternActivator> activators = LanternActivatorRegistry.GetAll();
			for ( int i = 0; i < activators.Count; i++ )
			{
				LanternActivator activator = activators[ i ];
				if ( activator != null )
					activator.FadeToLit( true, activator.FadeDuration );
			}
		}

		if ( GUILayout.Button( "Extinguish All Lanterns" ) )
		{
			IReadOnlyList<LanternActivator> activators = LanternActivatorRegistry.GetAll();
			for ( int i = 0; i < activators.Count; i++ )
			{
				LanternActivator activator = activators[ i ];
				if ( activator != null )
					activator.FadeToLit( false, activator.FadeDuration );
			}
		}

		if ( GUILayout.Button( "Reset All Lanterns" ) )
		{
			IReadOnlyList<LanternActivator> activators = LanternActivatorRegistry.GetAll();
			for ( int i = 0; i < activators.Count; i++ )
			{
				LanternActivator activator = activators[ i ];
				if ( activator != null )
					activator.DebugReset();
			}
		}

		GUILayout.BeginHorizontal();
		if ( GUILayout.Button( "Reveal Sweep" ) )
		{
			DebugResetRevealControllers();
			LanternRevealSweepController.TryStartReveal( LanternActivator.IntroLedgeRevealId, default );
		}
		if ( GUILayout.Button( "Reset Reveal" ) )
			DebugResetRevealControllers();
		GUILayout.EndHorizontal();
	}

	static void DebugResetRevealControllers()
	{
		LanternRevealSweepController.DebugResetAll();
	}
}
