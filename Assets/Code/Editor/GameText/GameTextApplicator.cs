#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Applies flattened CSV rows onto existing assets. Never creates assets or array elements.</summary>
public static class GameTextApplicator
{
	public struct Result
	{
		public int Updated;
		public int Unchanged;
		public int Missing;
		public int ParseErrors;
	}

	public static Result Apply( IReadOnlyList<GameTextRow> rows, int parseErrors )
	{
		Result result = new Result { ParseErrors = parseErrors };
		bool anySceneDirty = false;

		for ( int i = 0; i < rows.Count; i++ )
		{
			GameTextRow row = rows[i];
			ApplyStatus status = ApplyRow( row, ref anySceneDirty );
			switch ( status )
			{
				case ApplyStatus.Updated:
					result.Updated++;
					break;
				case ApplyStatus.Unchanged:
					result.Unchanged++;
					break;
				default:
					result.Missing++;
					break;
			}
		}

		AssetDatabase.SaveAssets();
		if ( anySceneDirty )
			EditorSceneManager.MarkAllScenesDirty();

		return result;
	}

	enum ApplyStatus
	{
		Updated,
		Unchanged,
		Missing
	}

	static ApplyStatus ApplyRow( GameTextRow row, ref bool anySceneDirty )
	{
		switch ( row.Category )
		{
			case GameTextCollectors.CategoryTreasure:
				return ApplyTreasure( row );
			case GameTextCollectors.CategoryObjective:
				return ApplyObjective( row );
			case GameTextCollectors.CategoryTutorial:
				return ApplyTutorial( row );
			case GameTextCollectors.CategoryWorldEvent:
				return ApplyWorldEvent( row );
			case GameTextCollectors.CategoryAbility:
				return ApplyAbility( row );
			case GameTextCollectors.CategoryUpgrade:
				return ApplyUpgrade( row );
			case GameTextCollectors.CategoryBuildable:
				return ApplyBuildable( row );
			case GameTextCollectors.CategoryChest:
				return ApplyChest( row );
			case GameTextCollectors.CategoryMapLabel:
				return ApplyMapLabel( row, ref anySceneDirty );
			case GameTextCollectors.CategoryMapMarker:
				return ApplyMapMarker( row, ref anySceneDirty );
			case GameTextCollectors.CategoryFairy:
				return ApplyFairy( row );
			default:
				return ApplyStatus.Missing;
		}
	}

	static ApplyStatus SetStringIfChanged( Object target, ref string field, string newValue )
	{
		string current = field != null ? field : string.Empty;
		string next = newValue != null ? newValue : string.Empty;
		if ( current == next )
			return ApplyStatus.Unchanged;

		Undo.RecordObject( target, "Import Game Text" );
		field = next;
		EditorUtility.SetDirty( target );
		return ApplyStatus.Updated;
	}

	static ApplyStatus SetSerializedString( Object target, string propertyName, string newValue, ref bool dirtyExtra )
	{
		SerializedObject so = new SerializedObject( target );
		SerializedProperty prop = so.FindProperty( propertyName );
		if ( prop == null || prop.propertyType != SerializedPropertyType.String )
			return ApplyStatus.Missing;

		string next = newValue != null ? newValue : string.Empty;
		if ( prop.stringValue == next )
			return ApplyStatus.Unchanged;

		Undo.RecordObject( target, "Import Game Text" );
		prop.stringValue = next;
		so.ApplyModifiedProperties();
		EditorUtility.SetDirty( target );
		dirtyExtra = true;
		return ApplyStatus.Updated;
	}

	static ApplyStatus ApplyTreasure( GameTextRow row )
	{
		TreasureDefinition def = GameTextCollectors.FindDefinitionById<TreasureDefinition>( row.Id );
		if ( def == null )
			return ApplyStatus.Missing;
		if ( row.Field != "displayName" )
			return ApplyStatus.Missing;
		return SetStringIfChanged( def, ref def.displayName, row.Text );
	}

	static ApplyStatus ApplyAbility( GameTextRow row )
	{
		AbilityDefinition def = GameTextCollectors.FindDefinitionById<AbilityDefinition>( row.Id );
		if ( def == null )
			return ApplyStatus.Missing;
		if ( row.Field == "displayName" )
			return SetStringIfChanged( def, ref def.displayName, row.Text );
		if ( row.Field == "description" )
			return SetStringIfChanged( def, ref def.description, row.Text );
		return ApplyStatus.Missing;
	}

	static ApplyStatus ApplyBuildable( GameTextRow row )
	{
		BuildableDefinition def = GameTextCollectors.FindDefinitionById<BuildableDefinition>( row.Id );
		if ( def == null )
			return ApplyStatus.Missing;
		if ( row.Field == "displayName" )
			return SetStringIfChanged( def, ref def.displayName, row.Text );
		if ( row.Field == "description" )
			return SetStringIfChanged( def, ref def.description, row.Text );
		return ApplyStatus.Missing;
	}

	static ApplyStatus ApplyChest( GameTextRow row )
	{
		ChestDefinition def = GameTextCollectors.FindDefinitionById<ChestDefinition>( row.Id );
		if ( def == null )
			return ApplyStatus.Missing;
		if ( row.Field != "displayName" )
			return ApplyStatus.Missing;
		return SetStringIfChanged( def, ref def.displayName, row.Text );
	}

	static ApplyStatus ApplyUpgrade( GameTextRow row )
	{
		UpgradeDefinition def = GameTextCollectors.FindDefinitionById<UpgradeDefinition>( row.Id );
		if ( def == null )
			return ApplyStatus.Missing;

		if ( row.Field == "displayName" )
			return SetStringIfChanged( def, ref def.displayName, row.Text );
		if ( row.Field == "description" )
			return SetStringIfChanged( def, ref def.description, row.Text );

		int labelIndex;
		if ( TryParseIndexedField( row.Field, "levelUnlockLabels", out labelIndex ) )
		{
			if ( def.levelUnlockLabels == null || labelIndex < 0 || labelIndex >= def.levelUnlockLabels.Length )
				return ApplyStatus.Missing;
			string current = def.levelUnlockLabels[labelIndex] != null ? def.levelUnlockLabels[labelIndex] : string.Empty;
			string next = row.Text != null ? row.Text : string.Empty;
			if ( current == next )
				return ApplyStatus.Unchanged;
			Undo.RecordObject( def, "Import Game Text" );
			def.levelUnlockLabels[labelIndex] = next;
			EditorUtility.SetDirty( def );
			return ApplyStatus.Updated;
		}

		return ApplyStatus.Missing;
	}

	static ApplyStatus ApplyObjective( GameTextRow row )
	{
		ObjectiveDefinition def = GameTextCollectors.FindDefinitionById<ObjectiveDefinition>( row.Id );
		if ( def == null )
			return ApplyStatus.Missing;

		if ( row.Field == "title" )
			return SetStringIfChanged( def, ref def.title, row.Text );
		if ( row.Field == "rewardLabel" )
			return SetStringIfChanged( def, ref def.rewardLabel, row.Text );
		if ( row.Field == "completionToast" )
			return SetStringIfChanged( def, ref def.completionToast, row.Text );

		string subId;
		string subField;
		if ( TryParseNamedNested( row.Field, "subs", out subId, out subField ) )
		{
			if ( subField != "label" || def.subs == null )
				return ApplyStatus.Missing;

			for ( int i = 0; i < def.subs.Length; i++ )
			{
				ObjectiveSubDefinition sub = def.subs[i];
				if ( sub == null )
					continue;
				string key = !string.IsNullOrEmpty( sub.id ) ? sub.id : i.ToString();
				if ( key != subId )
					continue;
				return SetStringIfChanged( def, ref sub.label, row.Text );
			}

			return ApplyStatus.Missing;
		}

		return ApplyStatus.Missing;
	}

	static ApplyStatus ApplyTutorial( GameTextRow row )
	{
		TutorialDefinition def = GameTextCollectors.FindDefinitionById<TutorialDefinition>( row.Id );
		if ( def == null )
			return ApplyStatus.Missing;

		if ( row.Field == "title" )
			return SetStringIfChanged( def, ref def.title, row.Text );
		if ( row.Field == "body" )
			return SetStringIfChanged( def, ref def.body, row.Text );
		if ( row.Field == "highlightMapLabel" )
			return SetStringIfChanged( def, ref def.highlightMapLabel, row.Text );
		if ( row.Field == "keybindHint" )
			return SetStringIfChanged( def, ref def.keybindHint, row.Text );

		string taskId;
		string taskField;
		if ( TryParseNamedNested( row.Field, "tasks", out taskId, out taskField ) )
		{
			if ( def.tasks == null )
				return ApplyStatus.Missing;

			for ( int i = 0; i < def.tasks.Length; i++ )
			{
				TutorialTask task = def.tasks[i];
				if ( task == null )
					continue;
				string key = !string.IsNullOrEmpty( task.id ) ? task.id : i.ToString();
				if ( key != taskId )
					continue;

				if ( taskField == "label" )
					return SetStringIfChanged( def, ref task.label, row.Text );
				if ( taskField == "keybindHint" )
					return SetStringIfChanged( def, ref task.keybindHint, row.Text );
				return ApplyStatus.Missing;
			}

			return ApplyStatus.Missing;
		}

		return ApplyStatus.Missing;
	}

	static ApplyStatus ApplyWorldEvent( GameTextRow row )
	{
		WorldEventDefinition def = GameTextCollectors.FindDefinitionById<WorldEventDefinition>( row.Id );
		if ( def == null || def.actions == null )
			return ApplyStatus.Missing;

		// actions[N].field or actions[N].dialogue[M].field
		if ( !row.Field.StartsWith( "actions[" ) )
			return ApplyStatus.Missing;

		int close = row.Field.IndexOf( ']' );
		if ( close <= 8 )
			return ApplyStatus.Missing;

		int actionIndex;
		if ( !int.TryParse( row.Field.Substring( 8, close - 8 ), out actionIndex ) )
			return ApplyStatus.Missing;
		if ( actionIndex < 0 || actionIndex >= def.actions.Length )
			return ApplyStatus.Missing;

		WorldEventAction action = def.actions[actionIndex];
		if ( action == null )
			return ApplyStatus.Missing;

		string rest = row.Field.Substring( close + 1 );
		if ( rest.StartsWith( "." ) )
			rest = rest.Substring( 1 );

		if ( rest == "tutorialTitle" )
			return SetStringIfChanged( def, ref action.tutorialTitle, row.Text );
		if ( rest == "tutorialObjectiveText" )
			return SetStringIfChanged( def, ref action.tutorialObjectiveText, row.Text );
		if ( rest == "fairySpeechLine" )
			return SetStringIfChanged( def, ref action.fairySpeechLine, row.Text );
		if ( rest == "fairyArrivalSpeechLine" )
			return SetStringIfChanged( def, ref action.fairyArrivalSpeechLine, row.Text );

		if ( rest.StartsWith( "dialogue[" ) )
		{
			int dClose = rest.IndexOf( ']' );
			if ( dClose <= 9 )
				return ApplyStatus.Missing;
			int dialogueIndex;
			if ( !int.TryParse( rest.Substring( 9, dClose - 9 ), out dialogueIndex ) )
				return ApplyStatus.Missing;
			if ( action.dialogue == null || dialogueIndex < 0 || dialogueIndex >= action.dialogue.Length )
				return ApplyStatus.Missing;

			DragonDialogueLine line = action.dialogue[dialogueIndex];
			if ( line == null )
				return ApplyStatus.Missing;

			string lineField = rest.Substring( dClose + 1 );
			if ( lineField.StartsWith( "." ) )
				lineField = lineField.Substring( 1 );

			if ( lineField == "speaker" )
				return SetStringIfChanged( def, ref line.speaker, row.Text );
			if ( lineField == "text" )
				return SetStringIfChanged( def, ref line.text, row.Text );
		}

		return ApplyStatus.Missing;
	}

	static ApplyStatus ApplyMapLabel( GameTextRow row, ref bool anySceneDirty )
	{
		if ( row.Field != "label" )
			return ApplyStatus.Missing;

		string assetPath;
		string hierarchyPath;
		if ( !GameTextCollectors.TryParseMapLabelId( row.Id, out assetPath, out hierarchyPath ) )
			return ApplyStatus.Missing;

		MapLabelMarker marker = FindMapLabel( assetPath, hierarchyPath );
		if ( marker == null )
			return ApplyStatus.Missing;

		bool dirty = false;
		ApplyStatus status = SetSerializedString( marker, "_label", row.Text, ref dirty );
		if ( status == ApplyStatus.Updated )
		{
			if ( assetPath.EndsWith( ".unity" ) )
				anySceneDirty = true;
			TrySavePrefabIfAsset( assetPath );
		}

		return status;
	}

	static ApplyStatus ApplyMapMarker( GameTextRow row, ref bool anySceneDirty )
	{
		if ( row.Field != "mapLabel" )
			return ApplyStatus.Missing;

		TutorialMapMarker marker = FindMapMarkerById( row.Id, row.AssetPath );
		if ( marker == null )
			return ApplyStatus.Missing;

		bool dirty = false;
		ApplyStatus status = SetSerializedString( marker, "_mapLabel", row.Text, ref dirty );
		if ( status == ApplyStatus.Updated )
		{
			string path = AssetDatabase.GetAssetPath( PrefabUtility.GetCorrespondingObjectFromSource( marker.gameObject ) );
			if ( string.IsNullOrEmpty( path ) )
				path = AssetDatabase.GetAssetPath( marker.gameObject );
			if ( string.IsNullOrEmpty( path ) )
				path = marker.gameObject.scene.path;
			if ( !string.IsNullOrEmpty( row.AssetPath ) )
				path = row.AssetPath;

			if ( path != null && path.EndsWith( ".unity" ) )
				anySceneDirty = true;
			TrySavePrefabIfAsset( path );
		}

		return status;
	}

	static ApplyStatus ApplyFairy( GameTextRow row )
	{
		if ( row.Field != "guideFollowLine"
			&& row.Field != "guideArriveLine"
			&& row.Field != "deliveryOfferLine"
			&& row.Field != "deliveryTakenLine"
			&& row.Field != "worldTalkLine" )
			return ApplyStatus.Missing;

		GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>( row.Id );
		if ( prefab == null )
			return ApplyStatus.Missing;

		FairyHelper fairy = prefab.GetComponentInChildren<FairyHelper>( true );
		if ( fairy == null )
			return ApplyStatus.Missing;

		bool dirty = false;
		ApplyStatus status = SetSerializedString( fairy, row.Field, row.Text, ref dirty );
		if ( status == ApplyStatus.Updated )
			PrefabUtility.SavePrefabAsset( prefab );

		return status;
	}

	static MapLabelMarker FindMapLabel( string assetPath, string hierarchyPath )
	{
		if ( assetPath.EndsWith( ".prefab" ) )
		{
			GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>( assetPath );
			if ( prefab == null )
				return null;
			Transform t = GameTextCollectors.FindByHierarchyPath( prefab.transform, hierarchyPath );
			if ( t == null )
				return null;
			return t.GetComponent<MapLabelMarker>();
		}

		if ( assetPath.EndsWith( ".unity" ) )
		{
			Scene scene = GameTextCollectors.EnsureSceneLoaded( assetPath );
			if ( !scene.IsValid() || !scene.isLoaded )
				return null;

			GameObject[] roots = scene.GetRootGameObjects();
			for ( int i = 0; i < roots.Length; i++ )
			{
				Transform t = GameTextCollectors.FindByHierarchyPath( roots[i].transform, hierarchyPath );
				if ( t == null )
					continue;
				MapLabelMarker marker = t.GetComponent<MapLabelMarker>();
				if ( marker != null )
					return marker;
			}
		}

		return null;
	}

	static TutorialMapMarker FindMapMarkerById( string markerId, string hintAssetPath )
	{
		if ( !string.IsNullOrEmpty( hintAssetPath ) )
		{
			TutorialMapMarker hinted = FindMapMarkerInAsset( markerId, hintAssetPath );
			if ( hinted != null )
				return hinted;
		}

		string[] prefabGuids = AssetDatabase.FindAssets( "t:Prefab" );
		for ( int i = 0; i < prefabGuids.Length; i++ )
		{
			string path = AssetDatabase.GUIDToAssetPath( prefabGuids[i] );
			TutorialMapMarker found = FindMapMarkerInAsset( markerId, path );
			if ( found != null )
				return found;
		}

		for ( int s = 0; s < SceneManager.sceneCount; s++ )
		{
			Scene scene = SceneManager.GetSceneAt( s );
			if ( !scene.isLoaded )
				continue;
			GameObject[] roots = scene.GetRootGameObjects();
			for ( int r = 0; r < roots.Length; r++ )
			{
				TutorialMapMarker[] markers = roots[r].GetComponentsInChildren<TutorialMapMarker>( true );
				for ( int m = 0; m < markers.Length; m++ )
				{
					if ( markers[m] != null && markers[m].Id == markerId )
						return markers[m];
				}
			}
		}

		return null;
	}

	static TutorialMapMarker FindMapMarkerInAsset( string markerId, string assetPath )
	{
		if ( assetPath.EndsWith( ".prefab" ) )
		{
			GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>( assetPath );
			if ( prefab == null )
				return null;
			TutorialMapMarker[] markers = prefab.GetComponentsInChildren<TutorialMapMarker>( true );
			for ( int m = 0; m < markers.Length; m++ )
			{
				if ( markers[m] != null && markers[m].Id == markerId )
					return markers[m];
			}

			return null;
		}

		if ( assetPath.EndsWith( ".unity" ) )
		{
			Scene scene = GameTextCollectors.EnsureSceneLoaded( assetPath );
			if ( !scene.IsValid() || !scene.isLoaded )
				return null;
			GameObject[] roots = scene.GetRootGameObjects();
			for ( int r = 0; r < roots.Length; r++ )
			{
				TutorialMapMarker[] markers = roots[r].GetComponentsInChildren<TutorialMapMarker>( true );
				for ( int m = 0; m < markers.Length; m++ )
				{
					if ( markers[m] != null && markers[m].Id == markerId )
						return markers[m];
				}
			}
		}

		return null;
	}

	static void TrySavePrefabIfAsset( string assetPath )
	{
		if ( string.IsNullOrEmpty( assetPath ) || !assetPath.EndsWith( ".prefab" ) )
			return;

		GameObject prefabRoot = AssetDatabase.LoadAssetAtPath<GameObject>( assetPath );
		if ( prefabRoot != null )
			PrefabUtility.SavePrefabAsset( prefabRoot );
	}

	static bool TryParseIndexedField( string field, string arrayName, out int index )
	{
		index = -1;
		string prefix = arrayName + "[";
		if ( !field.StartsWith( prefix ) || !field.EndsWith( "]" ) )
			return false;
		string inner = field.Substring( prefix.Length, field.Length - prefix.Length - 1 );
		return int.TryParse( inner, out index );
	}

	/// <summary>Parses "subs.{id}.label" / "tasks.{id}.keybindHint".</summary>
	static bool TryParseNamedNested( string field, string parentName, out string nestedId, out string nestedField )
	{
		nestedId = string.Empty;
		nestedField = string.Empty;
		string prefix = parentName + ".";
		if ( !field.StartsWith( prefix ) )
			return false;

		string rest = field.Substring( prefix.Length );
		int dot = rest.IndexOf( '.' );
		if ( dot <= 0 || dot >= rest.Length - 1 )
			return false;

		nestedId = rest.Substring( 0, dot );
		nestedField = rest.Substring( dot + 1 );
		return true;
	}
}
#endif
