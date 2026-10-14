using System;
using System.Collections.Generic;

using UnityEngine;

public class DebugSessionSection : DebugOverlaySection
{
	readonly DebugOverlay _host;
	static string s_spawnFilter = string.Empty;

	public DebugSessionSection( DebugOverlay host )
	{
		_host = host;
	}

	public string Title => "Session";

	public void Draw()
	{
		GUILayout.BeginHorizontal();
		if ( GUILayout.Button( "Respawn" ) )
			_host.RespawnPlayer();
		if ( GUILayout.Button( "Reload" ) )
			_host.ReloadGame();
		if ( GUILayout.Button( "Quit" ) )
			_host.QuitGame();
		GUILayout.EndHorizontal();

		float scale = Time.timeScale;
		GUILayout.Label( $"Time Scale: {scale:0.00}" );
		float next = GUILayout.HorizontalSlider( scale, 0f, 2f );
		if ( !Mathf.Approximately( next, scale ) )
			Time.timeScale = next;

		if ( GUILayout.Button( "Reset Time Scale (1)" ) )
			Time.timeScale = 1f;

		DrawSpawns();
	}

	static void DrawSpawns()
	{
		GUILayout.Space( 6f );
		GUILayout.Label( "Spawns (select = spawn on play, Go = teleport)" );

		IReadOnlyList<DebugSpawnPoint> points = DebugSpawnRegistry.GetAll();
		int totalChoices = 1 + ( points.Count > 0 ? points.Count : DebugSpawnRegistry.FallbackVolumes.Length );
		if ( totalChoices > 6 )
			DebugOverlayGui.DrawSearchField( ref s_spawnFilter, "Filter" );

		string selected = DebugSpawnRegistry.SelectedSpawnId;
		string filter = s_spawnFilter != null ? s_spawnFilter.Trim() : string.Empty;

		DrawSpawnRow( string.Empty, "Default (intro)", selected, filter );

		if ( points.Count > 0 )
		{
			for ( int i = 0; i < points.Count; i++ )
			{
				DebugSpawnPoint point = points[ i ];
				if ( point == null || string.IsNullOrEmpty( point.Id ) )
					continue;

				string label = point.SkipIntro ? point.DisplayName + " (skip intro)" : point.DisplayName;
				DrawSpawnRow( point.Id, label, selected, filter );
			}
		}
		else
		{
			GUILayout.Label( "No DebugSpawnPoints — fallback volumes:" );
			for ( int i = 0; i < DebugSpawnRegistry.FallbackVolumes.Length; i++ )
			{
				DebugSpawnRegistry.FallbackVolume fallback = DebugSpawnRegistry.FallbackVolumes[ i ];
				DrawSpawnRow( fallback.VolumeId, fallback.DisplayName + " (skip intro)", selected, filter );
			}
		}
	}

	static void DrawSpawnRow( string id, string label, string selected, string filter )
	{
		if ( !string.IsNullOrEmpty( filter ) &&
		     label.IndexOf( filter, StringComparison.OrdinalIgnoreCase ) < 0 &&
		     ( id == null || id.IndexOf( filter, StringComparison.OrdinalIgnoreCase ) < 0 ) )
			return;

		bool isSelected = selected == id;
		GUILayout.BeginHorizontal();
		bool next = GUILayout.Toggle( isSelected, label, GUILayout.ExpandWidth( true ) );
		if ( next && !isSelected )
			DebugSpawnRegistry.SelectedSpawnId = id;

		GUI.enabled = !string.IsNullOrEmpty( id );
		if ( GUILayout.Button( "Go", GUILayout.Width( 36f ) ) )
		{
			if ( DebugSpawnRegistry.TryTeleportTo( id ) )
			{
				DebugOverlay.RecordRecentTeleport( id );
				DebugOverlay.SetLastAction( "Teleported: " + label );
			}
			else
			{
				DebugOverlay.SetLastAction( "Teleport failed: " + label );
			}
		}
		GUI.enabled = true;
		GUILayout.EndHorizontal();
	}
}
