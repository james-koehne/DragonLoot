using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

using Newtonsoft.Json;

using UnityEngine;

/// <summary>
/// File-based named save slots. Debug menu is the first consumer; same API for future player saves.
/// </summary>
public static class GameSaveSystem
{
	static readonly IGameSaveContributor[] Contributors =
	{
		new GameSaveProgressContributor(),
		new GameSavePlayerContributor(),
		new GameSaveBuildablesContributor(),
		new GameSaveWorldTreasureContributor(),
		new GameSaveExtendedWorldContributor()
	};

	static string _pendingLoadSlotId;
	static GameSaveData _pendingApplyData;
	static bool _progressApplied;
	static bool _worldPlayerApplied;
	static bool _loadInProgress;

	public static bool HasPendingLoad => !string.IsNullOrEmpty( _pendingLoadSlotId ) || _pendingApplyData != null;

	public static bool ShouldSkipDefaultSpawn => HasPendingLoad && !_worldPlayerApplied;

	public static bool ShouldSkipCarryClearOnStart => HasPendingLoad && !_worldPlayerApplied;

	public static GameSaveData Capture( string label = null )
	{
		GameSaveData data = new GameSaveData
		{
			schemaVersion = GameSaveData.CurrentSchemaVersion,
			label = string.IsNullOrWhiteSpace( label ) ? "Save" : label.Trim(),
			savedAtUtc = DateTime.UtcNow.ToString( "o" ),
			levelScenePath = ResolveLevelScenePath(),
			player = new GameSavePlayerData(),
			world = new GameSaveWorldData()
		};

		for ( int i = 0; i < Contributors.Length; i++ )
			Contributors[ i ].Capture( data );

		return data;
	}

	public static bool TryWriteSlot( string slotId, string label, out string error )
	{
		error = null;
		string safeId = GameSavePaths.SanitizeSlotId( slotId );
		if ( string.IsNullOrEmpty( safeId ) )
		{
			error = "Invalid slot id";
			return false;
		}

		try
		{
			GameSaveData data = Capture( label );
			string path = GameSavePaths.GetSlotFilePath( safeId );
			string json = JsonConvert.SerializeObject( data, Formatting.Indented );
			File.WriteAllText( path, json );
			return true;
		}
		catch ( Exception ex )
		{
			error = ex.Message;
			Debug.LogWarning( "GameSaveSystem.TryWriteSlot failed: " + ex.Message );
			return false;
		}
	}

	public static bool TryQuickSave( out string error )
	{
		return TryWriteSlot( GameSavePaths.QuickSaveSlotId, "Quick Save", out error );
	}

	public static bool TryReadSlot( string slotId, out GameSaveData data, out string error )
	{
		data = null;
		error = null;
		string path = GameSavePaths.GetSlotFilePath( slotId );
		if ( !File.Exists( path ) )
		{
			error = "Slot not found";
			return false;
		}

		try
		{
			string json = File.ReadAllText( path );
			data = JsonConvert.DeserializeObject<GameSaveData>( json );
			if ( data == null )
			{
				error = "Empty save";
				return false;
			}

			if ( data.player == null )
				data.player = new GameSavePlayerData();
			if ( data.world == null )
				data.world = new GameSaveWorldData();
			return true;
		}
		catch ( Exception ex )
		{
			error = ex.Message;
			Debug.LogWarning( "GameSaveSystem.TryReadSlot failed: " + ex.Message );
			return false;
		}
	}

	public static bool TryDeleteSlot( string slotId, out string error )
	{
		error = null;
		string path = GameSavePaths.GetSlotFilePath( slotId );
		if ( !File.Exists( path ) )
		{
			error = "Slot not found";
			return false;
		}

		try
		{
			File.Delete( path );
			return true;
		}
		catch ( Exception ex )
		{
			error = ex.Message;
			return false;
		}
	}

	public static List<GameSaveSlotInfo> ListSlots()
	{
		List<GameSaveSlotInfo> list = new List<GameSaveSlotInfo>();
		string dir = GameSavePaths.SavesDirectory;
		if ( !Directory.Exists( dir ) )
			return list;

		string[] files = Directory.GetFiles( dir, "*.json" );
		Array.Sort( files, StringComparer.OrdinalIgnoreCase );
		for ( int i = 0; i < files.Length; i++ )
		{
			string path = files[ i ];
			string slotId = GameSavePaths.SlotIdFromFileName( Path.GetFileName( path ) );
			GameSaveSlotInfo info = new GameSaveSlotInfo
			{
				slotId = slotId,
				filePath = path,
				label = slotId,
				savedAtUtc = string.Empty
			};

			if ( TryReadSlot( slotId, out GameSaveData data, out _ ) && data != null )
			{
				if ( !string.IsNullOrEmpty( data.label ) )
					info.label = data.label;
				if ( !string.IsNullOrEmpty( data.savedAtUtc ) )
					info.savedAtUtc = data.savedAtUtc;
			}

			list.Add( info );
		}

		return list;
	}

	public static async Task RequestLoad( string slotId )
	{
		if ( _loadInProgress )
			return;

		if ( !TryReadSlot( slotId, out GameSaveData data, out string error ) )
		{
			Debug.LogWarning( "GameSaveSystem.RequestLoad failed: " + error );
			DebugOverlay.SetLastAction( "Load failed: " + error );
			return;
		}

		_loadInProgress = true;
		_pendingLoadSlotId = GameSavePaths.SanitizeSlotId( slotId );
		_pendingApplyData = data;
		_progressApplied = false;
		_worldPlayerApplied = false;

		try
		{
			GameplayRuntimeTeardown.Teardown();
			await GameInstance.RestartGame();
		}
		finally
		{
			_loadInProgress = false;
		}
	}

	public static async Task RestartClean()
	{
		if ( _loadInProgress )
			return;

		_loadInProgress = true;
		ClearPending();
		try
		{
			GameplayRuntimeTeardown.Teardown();
			await GameInstance.RestartGame();
		}
		finally
		{
			_loadInProgress = false;
		}
	}

	public static void ApplyPendingProgress()
	{
		if ( _pendingApplyData == null || _progressApplied )
			return;

		for ( int i = 0; i < Contributors.Length; i++ )
		{
			if ( Contributors[ i ] is GameSaveProgressContributor )
				Contributors[ i ].Apply( _pendingApplyData );
		}

		_progressApplied = true;
	}

	public static void ApplyPendingWorldAndPlayer()
	{
		if ( _pendingApplyData == null || _worldPlayerApplied )
			return;

		for ( int i = 0; i < Contributors.Length; i++ )
		{
			if ( Contributors[ i ] is GameSaveProgressContributor )
				continue;
			Contributors[ i ].Apply( _pendingApplyData );
		}

		_worldPlayerApplied = true;
		ClearPending();
	}

	static void ClearPending()
	{
		_pendingLoadSlotId = null;
		_pendingApplyData = null;
		_progressApplied = false;
		_worldPlayerApplied = false;
	}

	static string ResolveLevelScenePath()
	{
		if ( GameMode.Instance != null && GameMode.Instance.Game != null && !string.IsNullOrEmpty( GameMode.Instance.Game.levelScenePath ) )
			return GameMode.Instance.Game.levelScenePath;
		return "Assets/Scenes/Level.unity";
	}
}
