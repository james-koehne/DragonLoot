#if UNITY_EDITOR
using System.Collections.Generic;

using UnityEditor;

using UnityEngine;

/// <summary>
/// Registers the per-island IgniteLanterns world events on the catalog and as Addressable Definitions.
/// </summary>
public static class WorldEventIslandCompleteBootstrap
{
	const string EventsFolder = "Assets/Definitions/WorldEvents";
	const string CatalogPath = "Assets/Definitions/WorldEventCatalogDefinition.asset";
	const string LegacyIslandLanternsPath = EventsFolder + "/WorldEvent_IslandLanterns.asset";

	static readonly IslandCompleteSpec[] Specs =
	{
		new IslandCompleteSpec( "WorldEvent_Island1Complete.asset", "island1_lanterns" ),
		new IslandCompleteSpec( "WorldEvent_Island2Complete.asset", "island2_lanterns" ),
		new IslandCompleteSpec( "WorldEvent_Island3Complete.asset", "island3_lanterns" ),
		new IslandCompleteSpec( "WorldEvent_Island4Complete.asset", "island4_lanterns" )
	};

	[InitializeOnLoadMethod]
	static void QueueEnsure()
	{
		EditorApplication.delayCall += EnsureEvents;
	}

	[MenuItem( DragonLootMenus.DefinitionsEnsureIslandCompleteWorldEvents )]
	static void MenuEnsureEvents()
	{
		EnsureEvents();
		Debug.Log( "Island complete world events registered on WorldEventCatalogDefinition." );
	}

	static void EnsureEvents()
	{
		AddressableEditorUtil.EnsureFolder( EventsFolder );
		RemoveLegacyIslandLanterns();

		WorldEventCatalogDefinition catalog = AssetDatabase.LoadAssetAtPath<WorldEventCatalogDefinition>( CatalogPath );
		if ( catalog == null )
			return;

		if ( catalog.events == null )
			catalog.events = new List<WorldEventDefinition>();

		bool catalogDirty = RemoveCatalogEntryById( catalog, "island_lanterns" );
		for ( int i = 0; i < Specs.Length; i++ )
		{
			WorldEventDefinition worldEvent = EnsureEventAsset( Specs[ i ] );
			if ( worldEvent == null )
				continue;

			AddressableEditorUtil.TryRegister( EventsFolder + "/" + Specs[ i ].FileName, EventsFolder + "/" + Specs[ i ].FileName, "Definition" );
			catalogDirty |= EnsureCatalogEntry( catalog, worldEvent );
		}

		if ( catalogDirty )
		{
			EditorUtility.SetDirty( catalog );
			AssetDatabase.SaveAssets();
		}
	}

	static WorldEventDefinition EnsureEventAsset( IslandCompleteSpec spec )
	{
		string path = EventsFolder + "/" + spec.FileName;
		WorldEventDefinition worldEvent = AssetDatabase.LoadAssetAtPath<WorldEventDefinition>( path );
		if ( worldEvent == null )
			return null;

		bool dirty = false;
		if ( string.IsNullOrEmpty( worldEvent.id ) )
		{
			worldEvent.id = spec.EventId;
			dirty = true;
		}

		if ( worldEvent.actions == null || worldEvent.actions.Length == 0 )
		{
			worldEvent.actions = new[]
			{
				new WorldEventAction
				{
					type = WorldEventActionType.IgniteLanterns,
					lanternGroupId = spec.EventId
				}
			};
			dirty = true;
		}

		if ( !worldEvent.manualOnly )
		{
			worldEvent.manualOnly = true;
			dirty = true;
		}

		if ( dirty )
		{
			EditorUtility.SetDirty( worldEvent );
			AssetDatabase.SaveAssets();
		}

		return worldEvent;
	}

	static bool EnsureCatalogEntry( WorldEventCatalogDefinition catalog, WorldEventDefinition worldEvent )
	{
		if ( worldEvent == null )
			return false;

		for ( int i = 0; i < catalog.events.Count; i++ )
		{
			WorldEventDefinition entry = catalog.events[ i ];
			if ( entry == worldEvent || ( entry != null && entry.id == worldEvent.id ) )
			{
				if ( entry != worldEvent )
				{
					catalog.events[ i ] = worldEvent;
					return true;
				}

				return false;
			}
		}

		catalog.events.Add( worldEvent );
		return true;
	}

	static bool RemoveCatalogEntryById( WorldEventCatalogDefinition catalog, string eventId )
	{
		if ( catalog.events == null || string.IsNullOrEmpty( eventId ) )
			return false;

		bool removed = false;
		for ( int i = catalog.events.Count - 1; i >= 0; i-- )
		{
			WorldEventDefinition entry = catalog.events[ i ];
			if ( entry == null || entry.id != eventId )
				continue;

			catalog.events.RemoveAt( i );
			removed = true;
		}

		return removed;
	}

	static void RemoveLegacyIslandLanterns()
	{
		if ( !System.IO.File.Exists( LegacyIslandLanternsPath ) )
			return;

		AssetDatabase.DeleteAsset( LegacyIslandLanternsPath );
	}

	readonly struct IslandCompleteSpec
	{
		public readonly string FileName;
		public readonly string EventId;

		public IslandCompleteSpec( string fileName, string eventId )
		{
			FileName = fileName;
			EventId = eventId;
		}
	}
}
#endif
