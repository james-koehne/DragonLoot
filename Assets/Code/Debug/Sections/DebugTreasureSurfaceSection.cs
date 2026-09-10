using System.Collections.Generic;

using UnityEngine;

public class DebugTreasureSurfaceSection : DebugOverlaySection
{
	public string Title => "Treasure Surface";

	public void Draw()
	{
		TreasureSurfaceWorld world = TreasureSurfaceWorld.Instance;
		if ( world == null || !world.IsInitialized )
		{
			GUILayout.Label( "Surface not initialized" );
			if ( GUILayout.Button( "EnsureExists" ) )
				TreasureSurfaceWorld.EnsureExists();
			return;
		}

		TreasureSurfaceDefinition def = world.Definition;
		GUILayout.Label( $"World: {def.worldSizeX:0}x{def.worldSizeZ:0}m  Chunk: {def.chunkSize:0}m" );
		GUILayout.Label( $"Cells/chunk: {def.cellsPerChunk}  Cell: {def.CellSize:0.00}m" );
		GUILayout.Label( $"Loaded: {world.LoadedChunkCount}" );
		GUILayout.Label( $"Active: {world.ActiveChunkCount}  Frozen: {world.SleepingChunkCount}  Dirty: {world.DirtyChunkCount}" );
		GUILayout.Label( $"Rebuild: {world.LastChunkRebuildMs:0.00} ms" );
		GUILayout.Label( $"Surface tick: {world.LastSurfaceUpdateMs:0.00} ms" );

		TreasureSurfaceSimulator sim = world.Simulator;
		if ( sim != null )
		{
			GUILayout.Space( 4f );
			GUILayout.Label( $"Active objects: {sim.ActiveCount}" );
			GUILayout.Label( $"Sleeping objects: {sim.SleepingCount}" );
			GUILayout.Label( $"Avg roll: {sim.AverageRollingMs:0.000} ms" );
			GUILayout.Label( $"Recoveries: {sim.RecoveryCount}" );
			GUILayout.Label( $"Outside bounds: {sim.OutsideBoundsCount}" );
			if ( GUILayout.Button( "Reset recovery stats" ) )
				sim.ResetStats();
		}

		GUILayout.Space( 4f );
		def.relaxIterations = Mathf.RoundToInt( GUILayout.HorizontalSlider( def.relaxIterations, 0, 8 ) );
		GUILayout.Label( $"Relax iterations: {def.relaxIterations}" );

		if ( GUILayout.Button( "Restamp all pile bridges" ) )
		{
			IReadOnlyList<TreasurePileSurfaceBridge> bridges = TreasurePileSurfaceBridge.Active;
			for ( int i = 0; i < bridges.Count; i++ )
			{
				if ( bridges[ i ] != null )
					bridges[ i ].RestampFull();
			}
		}
	}
}
