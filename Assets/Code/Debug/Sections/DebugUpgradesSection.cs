using System.Collections.Generic;

using UnityEngine;

public class DebugUpgradesSection : DebugOverlaySection
{
	static Vector2 s_listScroll;
	static readonly Dictionary<string, string> s_levelFieldText = new Dictionary<string, string>();

	public string Title => "Upgrades";

	public void Draw()
	{
		PlayerController player = DebugOverlay.GetPlayer();
		UpgradeSystem system = ResolveSystem( player );
		if ( system == null )
		{
			GUILayout.Label( "No upgrade system" );
			return;
		}

		IReadOnlyList<UpgradeDefinition> catalog = system.GetCatalogUpgrades();
		GUILayout.Label( $"Catalog: {catalog.Count}" );

		GUILayout.BeginHorizontal();
		if ( GUILayout.Button( "Unlock All" ) )
			system.UnlockAll();
		if ( GUILayout.Button( "Reset All" ) )
			system.ResetAllProgress();
		GUILayout.EndHorizontal();

		GUILayout.Space( 6f );
		GUILayout.Label( "Catalog" );
		s_listScroll = GUILayout.BeginScrollView( s_listScroll, GUILayout.MaxHeight( 280f ) );
		for ( int i = 0; i < catalog.Count; i++ )
		{
			UpgradeDefinition def = catalog[ i ];
			if ( def == null )
				continue;
			DrawUpgradeRow( system, def );
		}
		GUILayout.EndScrollView();
	}

	static UpgradeSystem ResolveSystem( PlayerController player )
	{
		if ( player != null )
			UpgradeSystem.Ensure( player );
		return UpgradeSystem.Instance;
	}

	static void DrawUpgradeRow( UpgradeSystem system, UpgradeDefinition def )
	{
		string id = def.id;
		int level = system.GetUpgradeLevel( id );
		int max = system.GetMaxLevel( id );
		UpgradeStatus status = system.GetStatus( id );
		bool unlocked = system.IsUnlocked( id );

		GUILayout.BeginVertical( GUI.skin.box );
		GUILayout.Label( $"{def.ResolveDisplayName()} ({id})" );
		GUILayout.Label( $"Status: {status}  Level: {level} / {max}" );

		GUILayout.BeginHorizontal();
		GUI.enabled = !unlocked;
		if ( GUILayout.Button( "Unlock" ) )
		{
			system.UnlockUpgrade( id );
			s_levelFieldText[ id ] = system.GetUpgradeLevel( id ).ToString();
		}
		GUI.enabled = true;

		GUI.enabled = unlocked && level < max;
		if ( GUILayout.Button( "+1" ) )
		{
			system.PurchaseNextLevel( id );
			s_levelFieldText[ id ] = system.GetUpgradeLevel( id ).ToString();
		}
		GUI.enabled = true;

		if ( GUILayout.Button( "Max" ) )
		{
			system.MaxUpgrade( id );
			s_levelFieldText[ id ] = system.GetUpgradeLevel( id ).ToString();
		}

		if ( GUILayout.Button( "Reset" ) )
		{
			system.ResetUpgrade( id );
			s_levelFieldText[ id ] = "0";
		}
		GUILayout.EndHorizontal();

		GUILayout.BeginHorizontal();
		GUILayout.Label( "Set Level", GUILayout.Width( 70f ) );
		if ( !s_levelFieldText.TryGetValue( id, out string fieldText ) )
			fieldText = level.ToString();
		string edited = GUILayout.TextField( fieldText, GUILayout.Width( 40f ) );
		s_levelFieldText[ id ] = edited;
		if ( GUILayout.Button( "Apply", GUILayout.Width( 50f ) ) )
		{
			if ( int.TryParse( edited, out int parsed ) )
			{
				system.SetUpgradeLevel( id, parsed );
				s_levelFieldText[ id ] = system.GetUpgradeLevel( id ).ToString();
			}
		}
		GUILayout.EndHorizontal();

		GUILayout.EndVertical();
	}
}
