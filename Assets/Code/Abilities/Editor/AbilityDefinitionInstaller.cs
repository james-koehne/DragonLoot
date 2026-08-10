#if UNITY_EDITOR
using System.Collections.Generic;

using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

[InitializeOnLoad]
static class AbilityDefinitionInstaller
{
	const string AbilitiesFolder = "Assets/Definitions/Abilities";
	const string CatalogPath = "Assets/Definitions/AbilityCatalogDefinition.asset";
	const string BehaviourPath = AbilitiesFolder + "/PlaceholderAbilityBehaviour.asset";
	const string LockpickBehaviourPath = AbilitiesFolder + "/LockpickAbilityBehaviour.asset";
	const string SparkPath = AbilitiesFolder + "/Ability_PlaceholderSpark.asset";
	const string EchoPath = AbilitiesFolder + "/Ability_PlaceholderEcho.asset";
	const string LockpickPath = AbilitiesFolder + "/Ability_Lockpick.asset";

	static AbilityDefinitionInstaller()
	{
		EditorApplication.delayCall += EnsureInstalled;
	}

	static void EnsureInstalled()
	{
		EnsureFolder( "Assets/Definitions" );
		EnsureFolder( AbilitiesFolder );

		PlaceholderAbilityBehaviour behaviour = EnsurePlaceholderBehaviour();
		LockpickAbilityBehaviour lockpickBehaviour = EnsureLockpickBehaviour();

		AbilityDefinition spark = EnsureAbility(
			SparkPath,
			"placeholder_spark",
			"Placeholder Spark",
			"Debug placeholder ability used to verify activation and cooldowns.",
			2f,
			behaviour );
		AbilityDefinition echo = EnsureAbility(
			EchoPath,
			"placeholder_echo",
			"Placeholder Echo",
			"Second debug ability left locked by default for unlock testing.",
			3f,
			behaviour );
		AbilityDefinition lockpick = EnsureAbility(
			LockpickPath,
			ChestInteractable.LockpickAbilityId,
			"Lockpick",
			"Begins a timer-based lockpick on a focused locked chest. Player may leave while it finishes.",
			1f,
			lockpickBehaviour );

		AbilityCatalogDefinition catalog = EnsureCatalog( spark, echo, lockpick );
		RegisterDefinitionAddressable( CatalogPath, catalog.name );
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

	static PlaceholderAbilityBehaviour EnsurePlaceholderBehaviour()
	{
		PlaceholderAbilityBehaviour behaviour = AssetDatabase.LoadAssetAtPath<PlaceholderAbilityBehaviour>( BehaviourPath );
		if ( behaviour != null )
			return behaviour;

		behaviour = ScriptableObject.CreateInstance<PlaceholderAbilityBehaviour>();
		behaviour.name = "PlaceholderAbilityBehaviour";
		AssetDatabase.CreateAsset( behaviour, BehaviourPath );
		AssetDatabase.SaveAssets();
		return behaviour;
	}

	static LockpickAbilityBehaviour EnsureLockpickBehaviour()
	{
		LockpickAbilityBehaviour behaviour = AssetDatabase.LoadAssetAtPath<LockpickAbilityBehaviour>( LockpickBehaviourPath );
		if ( behaviour != null )
			return behaviour;

		behaviour = ScriptableObject.CreateInstance<LockpickAbilityBehaviour>();
		behaviour.name = "LockpickAbilityBehaviour";
		behaviour.defaultDuration = 8f;
		AssetDatabase.CreateAsset( behaviour, LockpickBehaviourPath );
		AssetDatabase.SaveAssets();
		return behaviour;
	}

	static AbilityDefinition EnsureAbility(
		string path,
		string id,
		string displayName,
		string description,
		float cooldown,
		AbilityBehaviour behaviour )
	{
		AbilityDefinition definition = AssetDatabase.LoadAssetAtPath<AbilityDefinition>( path );
		if ( definition == null )
		{
			definition = ScriptableObject.CreateInstance<AbilityDefinition>();
			definition.name = System.IO.Path.GetFileNameWithoutExtension( path );
			AssetDatabase.CreateAsset( definition, path );
		}

		bool dirty = false;
		if ( definition.id != id )
		{
			definition.id = id;
			dirty = true;
		}
		if ( definition.displayName != displayName )
		{
			definition.displayName = displayName;
			dirty = true;
		}
		if ( definition.description != description )
		{
			definition.description = description;
			dirty = true;
		}
		if ( !Mathf.Approximately( definition.cooldown, cooldown ) )
		{
			definition.cooldown = cooldown;
			dirty = true;
		}
		if ( !definition.enabled )
		{
			definition.enabled = true;
			dirty = true;
		}
		if ( definition.behaviour != behaviour )
		{
			definition.behaviour = behaviour;
			dirty = true;
		}

		if ( dirty )
			EditorUtility.SetDirty( definition );

		AssetDatabase.SaveAssets();
		return definition;
	}

	static AbilityCatalogDefinition EnsureCatalog(
		AbilityDefinition spark,
		AbilityDefinition echo,
		AbilityDefinition lockpick )
	{
		AbilityCatalogDefinition catalog = AssetDatabase.LoadAssetAtPath<AbilityCatalogDefinition>( CatalogPath );
		if ( catalog == null )
		{
			catalog = ScriptableObject.CreateInstance<AbilityCatalogDefinition>();
			catalog.name = "AbilityCatalogDefinition";
			AssetDatabase.CreateAsset( catalog, CatalogPath );
		}

		if ( catalog.abilities == null )
			catalog.abilities = new List<AbilityDefinition>();

		bool dirty = false;
		EnsureCatalogEntry( catalog, spark, 0, ref dirty );
		EnsureCatalogEntry( catalog, echo, 1, ref dirty );
		EnsureCatalogEntry( catalog, lockpick, 2, ref dirty );

		// Keep placeholders + lockpick; do not wipe additional entries designers add later.
		if ( dirty )
			EditorUtility.SetDirty( catalog );

		AssetDatabase.SaveAssets();
		return catalog;
	}

	static void EnsureCatalogEntry(
		AbilityCatalogDefinition catalog,
		AbilityDefinition ability,
		int preferredIndex,
		ref bool dirty )
	{
		if ( ability == null )
			return;

		int existing = catalog.abilities.IndexOf( ability );
		if ( existing >= 0 )
			return;

		for ( int i = 0; i < catalog.abilities.Count; i++ )
		{
			AbilityDefinition entry = catalog.abilities[ i ];
			if ( entry != null && entry.id == ability.id )
			{
				if ( catalog.abilities[ i ] != ability )
				{
					catalog.abilities[ i ] = ability;
					dirty = true;
				}
				return;
			}
		}

		if ( preferredIndex >= 0 && preferredIndex < catalog.abilities.Count && catalog.abilities[ preferredIndex ] == null )
		{
			catalog.abilities[ preferredIndex ] = ability;
			dirty = true;
			return;
		}

		catalog.abilities.Add( ability );
		dirty = true;
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
