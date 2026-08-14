#if UNITY_EDITOR
using System.Collections.Generic;

using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

[InitializeOnLoad]
static class AudioDefinitionInstaller
{
	const string DefinitionAssetPath = "Assets/Definitions/AudioDefinition.asset";

	static readonly string[] DefaultMusicPaths =
	{
		"Assets/Audio/Music/Full Soundtracks/OST Pro - The Journey Begins.wav",
		"Assets/Audio/Music/Full Soundtracks/OST Pro - New Hope.wav",
		"Assets/Audio/Music/Full Soundtracks/OST Pro - See The Light.wav",
		"Assets/Audio/Music/Full Soundtracks/OST Pro - The March.wav",
		"Assets/Audio/Music/Full Soundtracks/OST Pro - To Mars And Back.wav"
	};

	static readonly string[] DefaultAmbiencePaths =
	{
		"Assets/Audio/SFX/Magical Ambiance/Magical Ambiance Loop 1.wav",
		"Assets/Audio/SFX/Magical Ambiance/Magical Ambiance Loop 2.wav",
		"Assets/Audio/SFX/Magical Ambiance/Magical Ambiance Loop 3.wav",
		"Assets/Audio/SFX/Magical Ambiance/Magical Ambiance Loop 4.wav",
		"Assets/Audio/SFX/Magical Ambiance/Magical Ambiance Loop 5.wav"
	};

	static readonly string[] DefaultGoldPileStepPaths =
	{
		"Assets/Audio/SFX/Coin Steps/Coins.wav",
		"Assets/Audio/SFX/Coin Steps/Coins 2.wav",
		"Assets/Audio/SFX/Coin Steps/Coins 3.wav",
		"Assets/Audio/SFX/Coin Steps/Coins 4.wav",
		"Assets/Audio/SFX/Coin Steps/Coins 5.wav",
		"Assets/Audio/SFX/Coin Steps/Coins 6.wav",
		"Assets/Audio/SFX/Coin Steps/Coins 7.wav",
		"Assets/Audio/SFX/Coin Steps/Coins 8.wav",
		"Assets/Audio/SFX/Coin Steps/Coins 9.wav",
		"Assets/Audio/SFX/Coin Steps/Coins 10.wav"
	};

	static AudioDefinitionInstaller()
	{
		EditorApplication.delayCall += EnsureInstalled;
	}

	[MenuItem( DragonLootMenus.AudioInstallDefinition, priority = 220 )]
	static void InstallFromMenu()
	{
		AudioDefinition definition = EnsureDefinitionAsset( forceDefaults: false );
		if ( definition != null )
		{
			Selection.activeObject = definition;
			EditorGUIUtility.PingObject( definition );
		}
	}

	[MenuItem( DragonLootMenus.AudioResetClipLists, priority = 221 )]
	static void ResetClipListsFromMenu()
	{
		AudioDefinition definition = EnsureDefinitionAsset( forceDefaults: true );
		if ( definition != null )
		{
			Selection.activeObject = definition;
			EditorGUIUtility.PingObject( definition );
			Debug.Log( "Reset AudioDefinition clip lists to defaults." );
		}
	}

	static void EnsureInstalled()
	{
		EnsureDefinitionAsset( forceDefaults: false );
	}

	static AudioDefinition EnsureDefinitionAsset( bool forceDefaults )
	{
		if ( !AssetDatabase.IsValidFolder( "Assets/Definitions" ) )
			AssetDatabase.CreateFolder( "Assets", "Definitions" );

		AudioDefinition definition = AssetDatabase.LoadAssetAtPath<AudioDefinition>( DefinitionAssetPath );
		bool created = false;
		if ( definition == null )
		{
			definition = ScriptableObject.CreateInstance<AudioDefinition>();
			definition.name = "AudioDefinition";
			ApplyDefaultClips( definition );
			AssetDatabase.CreateAsset( definition, DefinitionAssetPath );
			AssetDatabase.SaveAssets();
			created = true;
			Debug.Log( "Created " + DefinitionAssetPath );
		}
		else if ( forceDefaults || NeedsDefaultClips( definition ) )
		{
			Undo.RecordObject( definition, "Reset Audio Definition Clips" );
			ApplyDefaultClips( definition );
			EditorUtility.SetDirty( definition );
			AssetDatabase.SaveAssets();
		}

		if ( created )
		{
			EditorUtility.SetDirty( definition );
			AssetDatabase.SaveAssets();
		}

		RegisterDefinitionAddressable( DefinitionAssetPath );
		return definition;
	}

	static bool NeedsDefaultClips( AudioDefinition definition )
	{
		if ( definition == null )
			return true;

		bool musicEmpty = definition.musicTracks == null || definition.musicTracks.Length == 0;
		bool ambienceEmpty = definition.ambienceTracks == null || definition.ambienceTracks.Length == 0;
		bool goldStepsEmpty = definition.goldPileStepClips == null || definition.goldPileStepClips.Length == 0;
		return musicEmpty && ambienceEmpty && goldStepsEmpty;
	}

	static void ApplyDefaultClips( AudioDefinition definition )
	{
		definition.musicTracks = LoadClips( DefaultMusicPaths );
		definition.ambienceTracks = LoadClips( DefaultAmbiencePaths );
		definition.goldPileStepClips = LoadClips( DefaultGoldPileStepPaths );
		if ( definition.groundStepClips == null )
			definition.groundStepClips = new AudioClip[0];
	}

	static AudioClip[] LoadClips( string[] paths )
	{
		List<AudioClip> clips = new List<AudioClip>( paths.Length );
		for ( int i = 0; i < paths.Length; i++ )
		{
			AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>( paths[i] );
			if ( clip != null )
				clips.Add( clip );
			else
				Debug.LogWarning( "AudioDefinitionInstaller: missing clip at " + paths[i] );
		}

		return clips.ToArray();
	}

	static void RegisterDefinitionAddressable( string assetPath )
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

		entry.SetAddress( "AudioDefinition" );
		if ( !entry.labels.Contains( "Definition" ) )
			entry.SetLabel( "Definition", true, true );
		settings.SetDirty( AddressableAssetSettings.ModificationEvent.EntryModified, entry, true );
	}
}
#endif
