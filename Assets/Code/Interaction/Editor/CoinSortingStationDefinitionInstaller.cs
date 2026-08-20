#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

[InitializeOnLoad]
static class CoinSortingStationDefinitionInstaller
{
	const string DefinitionPath = "Assets/Definitions/CoinSortingStationDefinition.asset";

	static CoinSortingStationDefinitionInstaller()
	{
		EditorApplication.delayCall += EnsureInstalled;
	}

	static void EnsureInstalled()
	{
		EnsureFolder( "Assets/Definitions" );

		CoinSortingStationDefinition definition = AssetDatabase.LoadAssetAtPath<CoinSortingStationDefinition>( DefinitionPath );
		if ( definition == null )
		{
			definition = ScriptableObject.CreateInstance<CoinSortingStationDefinition>();
			definition.name = "CoinSortingStationDefinition";
			AssetDatabase.CreateAsset( definition, DefinitionPath );
		}

		bool dirty = false;
		if ( definition.upgradeId != CoinSortingStationDefinition.DefaultUpgradeId )
		{
			definition.upgradeId = CoinSortingStationDefinition.DefaultUpgradeId;
			dirty = true;
		}
		if ( definition.baseHopperCapacity < 1 )
		{
			definition.baseHopperCapacity = 50;
			dirty = true;
		}
		if ( definition.level4HopperCapacity < definition.baseHopperCapacity )
		{
			definition.level4HopperCapacity = 150;
			dirty = true;
		}
		if ( definition.baseCoinsPerSecond < 0.01f )
		{
			definition.baseCoinsPerSecond = 4f;
			dirty = true;
		}
		if ( definition.level3CoinsPerSecond < definition.baseCoinsPerSecond )
		{
			definition.level3CoinsPerSecond = 10f;
			dirty = true;
		}
		if ( definition.crankHoldGrace < 0.05f )
		{
			definition.crankHoldGrace = 0.2f;
			dirty = true;
		}
		if ( definition.crankToSortMultiplier < 0.01f )
		{
			definition.crankToSortMultiplier = 2f;
			dirty = true;
		}
		if ( definition.maxReserveSeconds < 0.25f )
		{
			definition.maxReserveSeconds = 8f;
			dirty = true;
		}
		if ( definition.fullStackLateralOffset < 0.05f )
		{
			definition.fullStackLateralOffset = 0.35f;
			dirty = true;
		}

		if ( definition.crankLoopClips == null || definition.crankLoopClips.Length == 0 )
		{
			definition.crankLoopClips = LoadCrankLoopClips();
			if ( definition.crankLoopClips != null && definition.crankLoopClips.Length > 0 )
				dirty = true;
		}

		if ( definition.sortLoopClips == null || definition.sortLoopClips.Length == 0 )
		{
			definition.sortLoopClips = LoadSortLoopClips();
			if ( definition.sortLoopClips != null && definition.sortLoopClips.Length > 0 )
				dirty = true;
		}

		if ( dirty )
			EditorUtility.SetDirty( definition );

		AssetDatabase.SaveAssets();
		RegisterDefinitionAddressable( DefinitionPath, definition.name );
	}

	static readonly string[] DefaultCrankClipPaths =
	{
		"Assets/Audio/SFX/Coin Sorter/MACHINE_Cartoon_Clicking_loop_mono.wav"
	};

	static readonly string[] DefaultSortClipPaths =
	{
		"Assets/Audio/SFX/Coin Sorter/Prize wheel spin 1.wav",
		"Assets/Audio/SFX/Coin Sorter/Prize wheel spin 6.wav"
	};

	static AudioClip[] LoadCrankLoopClips()
	{
		return LoadClips( DefaultCrankClipPaths );
	}

	static AudioClip[] LoadSortLoopClips()
	{
		return LoadClips( DefaultSortClipPaths );
	}

	static AudioClip[] LoadClips( string[] paths )
	{
		if ( paths == null || paths.Length == 0 )
			return new AudioClip[ 0 ];

		var clips = new System.Collections.Generic.List<AudioClip>( paths.Length );
		for ( int i = 0; i < paths.Length; i++ )
		{
			AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>( paths[ i ] );
			if ( clip != null )
				clips.Add( clip );
		}

		return clips.ToArray();
	}

	static void EnsureFolder( string path )
	{
		if ( AssetDatabase.IsValidFolder( path ) )
			return;

		string parent = System.IO.Path.GetDirectoryName( path )?.Replace( '\\', '/' );
		string leaf = System.IO.Path.GetFileName( path );
		if ( string.IsNullOrEmpty( parent ) || string.IsNullOrEmpty( leaf ) )
			return;

		if ( !AssetDatabase.IsValidFolder( parent ) )
			EnsureFolder( parent );

		AssetDatabase.CreateFolder( parent, leaf );
	}

	static void RegisterDefinitionAddressable( string assetPath, string address )
	{
		AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
		if ( settings == null )
			return;

		string guid = AssetDatabase.AssetPathToGUID( assetPath );
		if ( string.IsNullOrEmpty( guid ) )
			return;

		AddressableAssetEntry entry = settings.FindAssetEntry( guid );
		if ( entry == null )
			entry = settings.CreateOrMoveEntry( guid, settings.DefaultGroup );

		entry.SetAddress( address );
		if ( !entry.labels.Contains( "Definition" ) )
			entry.SetLabel( "Definition", true, true );
		settings.SetDirty( AddressableAssetSettings.ModificationEvent.EntryModified, entry, true );
	}
}
#endif
