#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Gathers flattened game-text rows from definitions and scene/prefab components.</summary>
public static class GameTextCollectors
{
	public const string CategoryTreasure = "Treasure";
	public const string CategoryObjective = "Objective";
	public const string CategoryTutorial = "Tutorial";
	public const string CategoryWorldEvent = "WorldEvent";
	public const string CategoryAbility = "Ability";
	public const string CategoryUpgrade = "Upgrade";
	public const string CategoryBuildable = "Buildable";
	public const string CategoryChest = "Chest";
	public const string CategoryMapLabel = "MapLabel";
	public const string CategoryMapMarker = "MapMarker";
	public const string CategoryFairy = "Fairy";

	public static List<GameTextRow> CollectAll()
	{
		List<GameTextRow> rows = new List<GameTextRow>( 1024 );
		CollectTreasures( rows );
		CollectObjectives( rows );
		CollectTutorials( rows );
		CollectWorldEvents( rows );
		CollectAbilities( rows );
		CollectUpgrades( rows );
		CollectBuildables( rows );
		CollectChests( rows );
		CollectMapLabelsAndMarkers( rows );
		CollectFairies( rows );
		return rows;
	}

	static void Add( List<GameTextRow> rows, string category, string id, string field, string text, string assetPath )
	{
		if ( string.IsNullOrEmpty( id ) )
			return;

		rows.Add( new GameTextRow
		{
			Category = category,
			Id = id,
			Field = field,
			Text = text != null ? text : string.Empty,
			AssetPath = assetPath != null ? assetPath : string.Empty
		} );
	}

	static string AssetPathOf( Object obj )
	{
		if ( obj == null )
			return string.Empty;
		return AssetDatabase.GetAssetPath( obj );
	}

	static void CollectTreasures( List<GameTextRow> rows )
	{
		string[] guids = AssetDatabase.FindAssets( "t:TreasureDefinition" );
		for ( int i = 0; i < guids.Length; i++ )
		{
			string path = AssetDatabase.GUIDToAssetPath( guids[i] );
			TreasureDefinition def = AssetDatabase.LoadAssetAtPath<TreasureDefinition>( path );
			if ( def == null )
				continue;
			Add( rows, CategoryTreasure, def.id, "displayName", def.displayName, path );
		}
	}

	static void CollectObjectives( List<GameTextRow> rows )
	{
		string[] guids = AssetDatabase.FindAssets( "t:ObjectiveDefinition" );
		for ( int i = 0; i < guids.Length; i++ )
		{
			string path = AssetDatabase.GUIDToAssetPath( guids[i] );
			ObjectiveDefinition def = AssetDatabase.LoadAssetAtPath<ObjectiveDefinition>( path );
			if ( def == null )
				continue;

			string id = def.id;
			Add( rows, CategoryObjective, id, "title", def.title, path );
			Add( rows, CategoryObjective, id, "rewardLabel", def.rewardLabel, path );
			Add( rows, CategoryObjective, id, "completionToast", def.completionToast, path );

			if ( def.subs == null )
				continue;

			for ( int s = 0; s < def.subs.Length; s++ )
			{
				ObjectiveSubDefinition sub = def.subs[s];
				if ( sub == null )
					continue;
				string subKey = !string.IsNullOrEmpty( sub.id ) ? sub.id : s.ToString();
				Add( rows, CategoryObjective, id, "subs." + subKey + ".label", sub.label, path );
			}
		}
	}

	static void CollectTutorials( List<GameTextRow> rows )
	{
		string[] guids = AssetDatabase.FindAssets( "t:TutorialDefinition" );
		for ( int i = 0; i < guids.Length; i++ )
		{
			string path = AssetDatabase.GUIDToAssetPath( guids[i] );
			TutorialDefinition def = AssetDatabase.LoadAssetAtPath<TutorialDefinition>( path );
			if ( def == null )
				continue;

			string id = def.id;
			Add( rows, CategoryTutorial, id, "title", def.title, path );
			Add( rows, CategoryTutorial, id, "body", def.body, path );
			Add( rows, CategoryTutorial, id, "highlightMapLabel", def.highlightMapLabel, path );
			Add( rows, CategoryTutorial, id, "keybindHint", def.keybindHint, path );

			if ( def.tasks == null )
				continue;

			for ( int t = 0; t < def.tasks.Length; t++ )
			{
				TutorialTask task = def.tasks[t];
				if ( task == null )
					continue;
				string taskKey = !string.IsNullOrEmpty( task.id ) ? task.id : t.ToString();
				Add( rows, CategoryTutorial, id, "tasks." + taskKey + ".label", task.label, path );
				Add( rows, CategoryTutorial, id, "tasks." + taskKey + ".keybindHint", task.keybindHint, path );
			}
		}
	}

	static void CollectWorldEvents( List<GameTextRow> rows )
	{
		string[] guids = AssetDatabase.FindAssets( "t:WorldEventDefinition" );
		for ( int i = 0; i < guids.Length; i++ )
		{
			string path = AssetDatabase.GUIDToAssetPath( guids[i] );
			WorldEventDefinition def = AssetDatabase.LoadAssetAtPath<WorldEventDefinition>( path );
			if ( def == null || def.actions == null )
				continue;

			string id = def.id;
			for ( int a = 0; a < def.actions.Length; a++ )
			{
				WorldEventAction action = def.actions[a];
				if ( action == null )
					continue;

				string prefix = "actions[" + a + "]";
				Add( rows, CategoryWorldEvent, id, prefix + ".tutorialTitle", action.tutorialTitle, path );
				Add( rows, CategoryWorldEvent, id, prefix + ".tutorialObjectiveText", action.tutorialObjectiveText, path );
				Add( rows, CategoryWorldEvent, id, prefix + ".fairySpeechLine", action.fairySpeechLine, path );
				Add( rows, CategoryWorldEvent, id, prefix + ".fairyArrivalSpeechLine", action.fairyArrivalSpeechLine, path );

				if ( action.dialogue == null )
					continue;

				for ( int d = 0; d < action.dialogue.Length; d++ )
				{
					DragonDialogueLine line = action.dialogue[d];
					if ( line == null )
						continue;
					string linePrefix = prefix + ".dialogue[" + d + "]";
					Add( rows, CategoryWorldEvent, id, linePrefix + ".speaker", line.speaker, path );
					Add( rows, CategoryWorldEvent, id, linePrefix + ".text", line.text, path );
				}
			}
		}
	}

	static void CollectAbilities( List<GameTextRow> rows )
	{
		string[] guids = AssetDatabase.FindAssets( "t:AbilityDefinition" );
		for ( int i = 0; i < guids.Length; i++ )
		{
			string path = AssetDatabase.GUIDToAssetPath( guids[i] );
			AbilityDefinition def = AssetDatabase.LoadAssetAtPath<AbilityDefinition>( path );
			if ( def == null )
				continue;
			Add( rows, CategoryAbility, def.id, "displayName", def.displayName, path );
			Add( rows, CategoryAbility, def.id, "description", def.description, path );
		}
	}

	static void CollectUpgrades( List<GameTextRow> rows )
	{
		string[] guids = AssetDatabase.FindAssets( "t:UpgradeDefinition" );
		for ( int i = 0; i < guids.Length; i++ )
		{
			string path = AssetDatabase.GUIDToAssetPath( guids[i] );
			UpgradeDefinition def = AssetDatabase.LoadAssetAtPath<UpgradeDefinition>( path );
			if ( def == null )
				continue;
			Add( rows, CategoryUpgrade, def.id, "displayName", def.displayName, path );
			Add( rows, CategoryUpgrade, def.id, "description", def.description, path );
			if ( def.levelUnlockLabels == null )
				continue;
			for ( int l = 0; l < def.levelUnlockLabels.Length; l++ )
				Add( rows, CategoryUpgrade, def.id, "levelUnlockLabels[" + l + "]", def.levelUnlockLabels[l], path );
		}
	}

	static void CollectBuildables( List<GameTextRow> rows )
	{
		string[] guids = AssetDatabase.FindAssets( "t:BuildableDefinition" );
		for ( int i = 0; i < guids.Length; i++ )
		{
			string path = AssetDatabase.GUIDToAssetPath( guids[i] );
			BuildableDefinition def = AssetDatabase.LoadAssetAtPath<BuildableDefinition>( path );
			if ( def == null )
				continue;
			Add( rows, CategoryBuildable, def.id, "displayName", def.displayName, path );
			Add( rows, CategoryBuildable, def.id, "description", def.description, path );
		}
	}

	static void CollectChests( List<GameTextRow> rows )
	{
		string[] guids = AssetDatabase.FindAssets( "t:ChestDefinition" );
		for ( int i = 0; i < guids.Length; i++ )
		{
			string path = AssetDatabase.GUIDToAssetPath( guids[i] );
			ChestDefinition def = AssetDatabase.LoadAssetAtPath<ChestDefinition>( path );
			if ( def == null )
				continue;
			Add( rows, CategoryChest, def.id, "displayName", def.displayName, path );
		}
	}

	static void CollectMapLabelsAndMarkers( List<GameTextRow> rows )
	{
		HashSet<string> seenMapLabels = new HashSet<string>();
		HashSet<string> seenMarkers = new HashSet<string>();

		string[] prefabGuids = AssetDatabase.FindAssets( "t:Prefab" );
		for ( int i = 0; i < prefabGuids.Length; i++ )
		{
			string path = AssetDatabase.GUIDToAssetPath( prefabGuids[i] );
			GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>( path );
			if ( prefab == null )
				continue;
			CollectMarkersOnRoot( rows, prefab.transform, path, seenMapLabels, seenMarkers );
		}

		for ( int s = 0; s < SceneManager.sceneCount; s++ )
		{
			Scene scene = SceneManager.GetSceneAt( s );
			if ( !scene.isLoaded )
				continue;
			string scenePath = scene.path;
			GameObject[] roots = scene.GetRootGameObjects();
			for ( int r = 0; r < roots.Length; r++ )
				CollectMarkersOnRoot( rows, roots[r].transform, scenePath, seenMapLabels, seenMarkers );
		}
	}

	static void CollectMarkersOnRoot(
		List<GameTextRow> rows,
		Transform root,
		string assetPath,
		HashSet<string> seenMapLabels,
		HashSet<string> seenMarkers )
	{
		MapLabelMarker[] labels = root.GetComponentsInChildren<MapLabelMarker>( true );
		for ( int i = 0; i < labels.Length; i++ )
		{
			MapLabelMarker label = labels[i];
			if ( label == null )
				continue;
			if ( IsPrefabAssetPreviewOrHidden( label.gameObject ) )
				continue;

			string hierarchy = GetHierarchyPath( label.transform );
			string id = assetPath + "|" + hierarchy;
			if ( !seenMapLabels.Add( id ) )
				continue;

			SerializedObject so = new SerializedObject( label );
			SerializedProperty prop = so.FindProperty( "_label" );
			string text = prop != null ? prop.stringValue : label.Label;
			Add( rows, CategoryMapLabel, id, "label", text, assetPath );
		}

		TutorialMapMarker[] markers = root.GetComponentsInChildren<TutorialMapMarker>( true );
		for ( int i = 0; i < markers.Length; i++ )
		{
			TutorialMapMarker marker = markers[i];
			if ( marker == null )
				continue;
			if ( IsPrefabAssetPreviewOrHidden( marker.gameObject ) )
				continue;

			string markerId = marker.Id;
			if ( string.IsNullOrEmpty( markerId ) )
				continue;
			if ( !seenMarkers.Add( markerId ) )
				continue;

			SerializedObject so = new SerializedObject( marker );
			SerializedProperty prop = so.FindProperty( "_mapLabel" );
			string text = prop != null ? prop.stringValue : marker.MapLabel;
			Add( rows, CategoryMapMarker, markerId, "mapLabel", text, assetPath );
		}
	}

	static void CollectFairies( List<GameTextRow> rows )
	{
		HashSet<string> seen = new HashSet<string>();
		string[] prefabGuids = AssetDatabase.FindAssets( "t:Prefab" );
		for ( int i = 0; i < prefabGuids.Length; i++ )
		{
			string path = AssetDatabase.GUIDToAssetPath( prefabGuids[i] );
			GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>( path );
			if ( prefab == null )
				continue;

			FairyHelper[] fairies = prefab.GetComponentsInChildren<FairyHelper>( true );
			for ( int f = 0; f < fairies.Length; f++ )
			{
				FairyHelper fairy = fairies[f];
				if ( fairy == null )
					continue;
				if ( !seen.Add( path ) )
					continue;

				SerializedObject so = new SerializedObject( fairy );
				AddFairyField( rows, path, so, "guideFollowLine" );
				AddFairyField( rows, path, so, "guideArriveLine" );
				AddFairyField( rows, path, so, "deliveryOfferLine" );
				AddFairyField( rows, path, so, "deliveryTakenLine" );
				AddFairyField( rows, path, so, "worldTalkLine" );
			}
		}
	}

	static void AddFairyField( List<GameTextRow> rows, string path, SerializedObject so, string fieldName )
	{
		SerializedProperty prop = so.FindProperty( fieldName );
		if ( prop == null || prop.propertyType != SerializedPropertyType.String )
			return;
		Add( rows, CategoryFairy, path, fieldName, prop.stringValue, path );
	}

	public static string GetHierarchyPath( Transform transform )
	{
		List<string> parts = new List<string>( 8 );
		Transform current = transform;
		while ( current != null )
		{
			parts.Add( current.name );
			current = current.parent;
		}

		parts.Reverse();
		return string.Join( "/", parts );
	}

	static bool IsPrefabAssetPreviewOrHidden( GameObject go )
	{
		if ( go == null )
			return true;
		// Prefab stage / asset roots loaded via AssetDatabase are fine; skip objects that are
		// only present as assets under DontSaveInEditor when pulled via scene search duplicates.
		HideFlags flags = go.hideFlags;
		if ( ( flags & HideFlags.HideAndDontSave ) == HideFlags.HideAndDontSave )
			return true;
		return false;
	}

	public static T FindDefinitionById<T>( string id ) where T : ScriptableObject
	{
		if ( string.IsNullOrEmpty( id ) )
			return null;

		string typeName = typeof( T ).Name;
		string[] guids = AssetDatabase.FindAssets( "t:" + typeName );
		for ( int i = 0; i < guids.Length; i++ )
		{
			string path = AssetDatabase.GUIDToAssetPath( guids[i] );
			T def = AssetDatabase.LoadAssetAtPath<T>( path );
			if ( def == null )
				continue;

			string defId = ReadDefinitionId( def );
			if ( defId == id )
				return def;
		}

		return null;
	}

	static string ReadDefinitionId( ScriptableObject def )
	{
		SerializedObject so = new SerializedObject( def );
		SerializedProperty idProp = so.FindProperty( "id" );
		if ( idProp != null && idProp.propertyType == SerializedPropertyType.String )
			return idProp.stringValue;
		return string.Empty;
	}

	public static bool TryParseMapLabelId( string id, out string assetPath, out string hierarchyPath )
	{
		assetPath = string.Empty;
		hierarchyPath = string.Empty;
		if ( string.IsNullOrEmpty( id ) )
			return false;

		int sep = id.IndexOf( '|' );
		if ( sep <= 0 || sep >= id.Length - 1 )
			return false;

		assetPath = id.Substring( 0, sep );
		hierarchyPath = id.Substring( sep + 1 );
		return true;
	}

	public static Transform FindByHierarchyPath( Transform root, string hierarchyPath )
	{
		if ( root == null || string.IsNullOrEmpty( hierarchyPath ) )
			return null;

		string rootPath = GetHierarchyPath( root );
		if ( rootPath == hierarchyPath )
			return root;

		Transform[] all = root.GetComponentsInChildren<Transform>( true );
		for ( int i = 0; i < all.Length; i++ )
		{
			if ( GetHierarchyPath( all[i] ) == hierarchyPath )
				return all[i];
		}

		return null;
	}

	public static Scene EnsureSceneLoaded( string scenePath )
	{
		for ( int i = 0; i < SceneManager.sceneCount; i++ )
		{
			Scene open = SceneManager.GetSceneAt( i );
			if ( open.path == scenePath && open.isLoaded )
				return open;
		}

		return EditorSceneManager.OpenScene( scenePath, OpenSceneMode.Additive );
	}
}
#endif
