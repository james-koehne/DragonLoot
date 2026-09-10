using System.Collections.Generic;

using UnityEngine;

public class DebugSessionSection : DebugOverlaySection
{
	readonly DebugOverlay _host;

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
		GUILayout.Label( "Spawn on play" );

		string selected = DebugSpawnRegistry.SelectedSpawnId;
		if ( DrawSpawnChoice( string.Empty, "Default (intro)", selected ) )
			DebugSpawnRegistry.SelectedSpawnId = string.Empty;

		IReadOnlyList<DebugSpawnPoint> points = DebugSpawnRegistry.GetAll();
		for ( int i = 0; i < points.Count; i++ )
		{
			DebugSpawnPoint point = points[ i ];
			if ( point == null || string.IsNullOrEmpty( point.Id ) )
				continue;

			string label = point.SkipIntro ? point.DisplayName + " (skip intro)" : point.DisplayName;
			if ( DrawSpawnChoice( point.Id, label, selected ) )
				DebugSpawnRegistry.SelectedSpawnId = point.Id;
		}

		if ( points.Count == 0 )
		{
			GUILayout.Label( "No DebugSpawnPoints — fallback volumes:" );
			for ( int i = 0; i < DebugSpawnRegistry.FallbackVolumes.Length; i++ )
			{
				DebugSpawnRegistry.FallbackVolume fallback = DebugSpawnRegistry.FallbackVolumes[ i ];
				if ( DrawSpawnChoice( fallback.VolumeId, fallback.DisplayName + " (skip intro)", selected ) )
					DebugSpawnRegistry.SelectedSpawnId = fallback.VolumeId;
			}
		}

		GUILayout.Space( 4f );
		GUILayout.Label( "Teleport now" );
		for ( int i = 0; i < points.Count; i++ )
		{
			DebugSpawnPoint point = points[ i ];
			if ( point == null || string.IsNullOrEmpty( point.Id ) )
				continue;

			if ( GUILayout.Button( "Go: " + point.DisplayName ) )
				DebugSpawnRegistry.TryTeleportTo( point.Id );
		}

		if ( points.Count == 0 )
		{
			for ( int i = 0; i < DebugSpawnRegistry.FallbackVolumes.Length; i++ )
			{
				DebugSpawnRegistry.FallbackVolume fallback = DebugSpawnRegistry.FallbackVolumes[ i ];
				if ( GUILayout.Button( "Go: " + fallback.DisplayName ) )
					DebugSpawnRegistry.TryTeleportTo( fallback.VolumeId );
			}
		}
	}

	static bool DrawSpawnChoice( string id, string label, string selected )
	{
		bool isSelected = selected == id;
		bool next = GUILayout.Toggle( isSelected, label );
		return next && !isSelected;
	}
}
