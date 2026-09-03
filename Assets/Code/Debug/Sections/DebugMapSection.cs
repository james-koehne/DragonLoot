using System.Collections.Generic;

using UnityEngine;

public class DebugMapSection : DebugOverlaySection
{
	readonly List<MapRegionVolume> _volumes = new List<MapRegionVolume>( 32 );
	readonly List<MapLabelMarker> _labels = new List<MapLabelMarker>( 32 );

	public string Title => "Map";

	public void Draw()
	{
		MapSystem map = MapSystem.Instance;
		if ( map == null )
		{
			GUILayout.Label( "MapSystem not running" );
			if ( GUILayout.Button( "EnsureExists" ) )
				MapSystem.EnsureExists();
			return;
		}

		MapDefinition def = map.GetDefinition();
		GUILayout.Label( "UI open: " + MapUI.IsOpen );
		GUILayout.Label( "Base ready: " + map.IsBaseReady );
		GUILayout.Label( "Bake phase: " + map.BakePhaseName );
		GUILayout.Label( $"Last bake slice: {map.LastBakeElapsedMs:0.00} ms" );
		GUILayout.Label( $"Resolution: {map.Resolution}" );

		if ( map.HasBounds )
		{
			Bounds b = map.MapBounds;
			GUILayout.Label( $"Bounds: {b.size.x:0.0} x {b.size.z:0.0} m" );
			GUILayout.Label( $"Center: ({b.center.x:0.0}, {b.center.z:0.0})" );
		}
		else
		{
			GUILayout.Label( "Bounds: none (no walkable paint)" );
		}

		GUILayout.Label( $"Discovery: {map.GetDiscoveryCoverage01() * 100f:0.0}%" );
		GUILayout.Label( $"Piles stamped: {map.CachedPileCount}" );

		MapOverlayRegistrar.CollectVolumes( _volumes );
		MapOverlayRegistrar.CollectLabels( _labels );
		GUILayout.Label( $"Region volumes: {_volumes.Count}" );
		GUILayout.Label( $"Labels: {_labels.Count}" );

		PlayerController player = DebugOverlay.GetPlayer();
		if ( player != null && map.TryWorldToUv( player.transform.position, out Vector2 uv ) )
			GUILayout.Label( $"Player UV: ({uv.x:0.00}, {uv.y:0.00})" );
		else
			GUILayout.Label( "Player UV: off map" );

		GUILayout.Space( 6f );
		GUILayout.BeginHorizontal();
		if ( GUILayout.Button( MapUI.IsOpen ? "Close Map" : "Open Map" ) )
			MapUI.DebugSetOpen( !MapUI.IsOpen );
		if ( GUILayout.Button( "Force Rebuild" ) )
			map.DebugForceRebuild();
		GUILayout.EndHorizontal();

		GUILayout.BeginHorizontal();
		if ( GUILayout.Button( "Discover Whole Map" ) )
			map.DebugDiscoverAll();
		if ( GUILayout.Button( "Clear Discovery" ) )
			map.DebugClearDiscovery();
		GUILayout.EndHorizontal();

		bool ignoreFog = GUILayout.Toggle( map.DebugIgnoreDiscovery, "Preview Without Fog" );
		if ( ignoreFog != map.DebugIgnoreDiscovery )
			map.DebugSetIgnoreDiscovery( ignoreFog );

		if ( def == null )
		{
			GUILayout.Label( "No MapDefinition" );
			return;
		}

		GUILayout.Space( 6f );
		GUILayout.Label( "Runtime tuning (session)" );

		float radius = def.discoveryRadius;
		GUILayout.Label( $"Discovery radius: {radius:0.0} m" );
		float nextRadius = GUILayout.HorizontalSlider( radius, 1f, 80f );
		if ( !Mathf.Approximately( nextRadius, radius ) )
			def.discoveryRadius = nextRadius;

		float soft = def.discoverySoftness;
		GUILayout.Label( $"Discovery soft: {soft:0.00}" );
		float nextSoft = GUILayout.HorizontalSlider( soft, 0f, 1f );
		if ( !Mathf.Approximately( nextSoft, soft ) )
			def.discoverySoftness = nextSoft;

		float budget = def.bakeBudgetMs;
		GUILayout.Label( $"Bake budget: {budget:0.00} ms" );
		float nextBudget = GUILayout.HorizontalSlider( budget, 0.25f, 8f );
		if ( !Mathf.Approximately( nextBudget, budget ) )
			def.bakeBudgetMs = nextBudget;

		float pad = def.boundsPadding;
		GUILayout.Label( $"Bounds padding: {pad:0.0} m" );
		float nextPad = GUILayout.HorizontalSlider( pad, 0f, 20f );
		if ( !Mathf.Approximately( nextPad, pad ) )
		{
			def.boundsPadding = nextPad;
			map.DebugForceRebuild();
		}

		GUILayout.Space( 4f );
		if ( map.DisplayTexture != null )
		{
			GUILayout.Label( "Preview" );
			Rect rect = GUILayoutUtility.GetRect( 180f, 180f, GUILayout.ExpandWidth( false ) );
			GUI.DrawTexture( rect, map.DisplayTexture, ScaleMode.ScaleToFit, false );
		}
	}
}
