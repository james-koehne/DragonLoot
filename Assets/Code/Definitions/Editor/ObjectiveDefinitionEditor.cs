#if UNITY_EDITOR
using UnityEditor;

using UnityEngine;

[CustomEditor( typeof( ObjectiveDefinition ) )]
public class ObjectiveDefinitionEditor : Editor
{
	SerializedProperty _id;
	SerializedProperty _title;
	SerializedProperty _showVolumeId;
	SerializedProperty _showRadius;
	SerializedProperty _prerequisiteObjectiveIds;
	SerializedProperty _subs;
	SerializedProperty _rewards;
	SerializedProperty _rewardLabel;
	SerializedProperty _showReward;
	SerializedProperty _completionToast;
	SerializedProperty _onCompleteWorldEventIds;

	void OnEnable()
	{
		_id = serializedObject.FindProperty( "id" );
		_title = serializedObject.FindProperty( "title" );
		_showVolumeId = serializedObject.FindProperty( "showVolumeId" );
		_showRadius = serializedObject.FindProperty( "showRadius" );
		_prerequisiteObjectiveIds = serializedObject.FindProperty( "prerequisiteObjectiveIds" );
		_subs = serializedObject.FindProperty( "subs" );
		_rewards = serializedObject.FindProperty( "rewards" );
		_rewardLabel = serializedObject.FindProperty( "rewardLabel" );
		_showReward = serializedObject.FindProperty( "showReward" );
		_completionToast = serializedObject.FindProperty( "completionToast" );
		_onCompleteWorldEventIds = serializedObject.FindProperty( "onCompleteWorldEventIds" );
	}

	public override void OnInspectorGUI()
	{
		serializedObject.Update();

		EditorGUILayout.PropertyField( _id );
		EditorGUILayout.PropertyField( _title );
		EditorGUILayout.PropertyField( _showVolumeId );
		EditorGUILayout.PropertyField( _showRadius );
		EditorGUILayout.PropertyField( _prerequisiteObjectiveIds, includeChildren: true );
		EditorGUILayout.PropertyField( _subs, includeChildren: true );
		EditorGUILayout.PropertyField( _rewards, includeChildren: true );
		EditorGUILayout.PropertyField( _rewardLabel );
		EditorGUILayout.PropertyField( _showReward );
		EditorGUILayout.PropertyField( _completionToast );
		EditorGUILayout.PropertyField( _onCompleteWorldEventIds, includeChildren: true );

		serializedObject.ApplyModifiedProperties();
	}
}

[CustomPropertyDrawer( typeof( ObjectiveSubDefinition ) )]
public class ObjectiveSubDefinitionDrawer : PropertyDrawer
{
	public override float GetPropertyHeight( SerializedProperty property, GUIContent label )
	{
		if ( !property.isExpanded )
			return EditorGUIUtility.singleLineHeight;

		float line = EditorGUIUtility.singleLineHeight + 2f;
		float height = line; // foldout
		height += line * 3f; // id + label + completeType

		ObjectiveSubCompleteType type = (ObjectiveSubCompleteType)property.FindPropertyRelative( "completeType" ).enumValueIndex;
		if ( type != ObjectiveSubCompleteType.None )
			height += line;
		if ( ObjectiveProgress.HasCountProgress( type ) )
			height += line;

		return height + 4f;
	}

	public override void OnGUI( Rect position, SerializedProperty property, GUIContent label )
	{
		EditorGUI.BeginProperty( position, label, property );
		Rect row = new Rect( position.x, position.y, position.width, EditorGUIUtility.singleLineHeight );
		property.isExpanded = EditorGUI.Foldout( row, property.isExpanded, label, true );
		if ( !property.isExpanded )
		{
			EditorGUI.EndProperty();
			return;
		}

		EditorGUI.indentLevel++;
		row.y += EditorGUIUtility.singleLineHeight + 2f;
		row = DrawRelative( row, property, "id" );
		row = DrawRelative( row, property, "label" );
		row = DrawRelative( row, property, "completeType" );

		ObjectiveSubCompleteType type = (ObjectiveSubCompleteType)property.FindPropertyRelative( "completeType" ).enumValueIndex;
		if ( type != ObjectiveSubCompleteType.None )
			row = DrawRelative( row, property, "targetId" );
		if ( ObjectiveProgress.HasCountProgress( type ) )
			DrawRelative( row, property, "requiredCount" );

		EditorGUI.indentLevel--;
		EditorGUI.EndProperty();
	}

	static Rect DrawRelative( Rect row, SerializedProperty property, string name )
	{
		SerializedProperty child = property.FindPropertyRelative( name );
		if ( child == null )
			return row;

		Rect field = new Rect( row.x, row.y, row.width, EditorGUIUtility.singleLineHeight );
		EditorGUI.PropertyField( field, child );
		row.y += EditorGUIUtility.singleLineHeight + 2f;
		return row;
	}
}

[CustomPropertyDrawer( typeof( ObjectiveReward ) )]
public class ObjectiveRewardDrawer : PropertyDrawer
{
	public override float GetPropertyHeight( SerializedProperty property, GUIContent label )
	{
		if ( !property.isExpanded )
			return EditorGUIUtility.singleLineHeight;

		float line = EditorGUIUtility.singleLineHeight + 2f;
		float height = line; // foldout
		height += line; // type

		ObjectiveRewardType type = (ObjectiveRewardType)property.FindPropertyRelative( "type" ).enumValueIndex;
		if ( type == ObjectiveRewardType.Ability )
			height += line;
		else
			height += line * 2f;

		return height + 4f;
	}

	public override void OnGUI( Rect position, SerializedProperty property, GUIContent label )
	{
		EditorGUI.BeginProperty( position, label, property );
		Rect row = new Rect( position.x, position.y, position.width, EditorGUIUtility.singleLineHeight );
		property.isExpanded = EditorGUI.Foldout( row, property.isExpanded, label, true );
		if ( !property.isExpanded )
		{
			EditorGUI.EndProperty();
			return;
		}

		EditorGUI.indentLevel++;
		row.y += EditorGUIUtility.singleLineHeight + 2f;
		row = DrawRelative( row, property, "type" );

		ObjectiveRewardType type = (ObjectiveRewardType)property.FindPropertyRelative( "type" ).enumValueIndex;
		if ( type == ObjectiveRewardType.Ability )
			DrawRelative( row, property, "abilityId" );
		else
		{
			row = DrawRelative( row, property, "upgradeId" );
			DrawRelative( row, property, "upgradeLevel" );
		}

		EditorGUI.indentLevel--;
		EditorGUI.EndProperty();
	}

	static Rect DrawRelative( Rect row, SerializedProperty property, string name )
	{
		SerializedProperty child = property.FindPropertyRelative( name );
		if ( child == null )
			return row;

		Rect field = new Rect( row.x, row.y, row.width, EditorGUIUtility.singleLineHeight );
		EditorGUI.PropertyField( field, child );
		row.y += EditorGUIUtility.singleLineHeight + 2f;
		return row;
	}
}

/// <summary>
/// Ensures <see cref="ObjectiveCatalogDefinition"/> exists under Definitions and is Addressable with the Definition label.
/// Also creates the four tutorial-island objective assets and registers them on the catalog.
/// </summary>
public static class ObjectiveCatalogBootstrap
{
	const string CatalogPath = "Assets/Definitions/ObjectiveCatalogDefinition.asset";
	const string ObjectivesFolder = "Assets/Definitions/Objectives";
	const string CompletionToast = "Island complete — platforms activated";

	[InitializeOnLoadMethod]
	static void QueueEnsure()
	{
		EditorApplication.delayCall += EnsureCatalog;
		EditorApplication.delayCall += EnsureIslandObjectives;
	}

	[MenuItem( DragonLootMenus.DefinitionsEnsureIslandObjectives )]
	static void MenuEnsureIslandObjectives()
	{
		EnsureCatalog();
		EnsureIslandObjectives();
		Debug.Log( "Tutorial island objectives ensured and registered on ObjectiveCatalogDefinition." );
	}

	static void EnsureCatalog()
	{
		AddressableEditorUtil.EnsureFolder( "Assets/Definitions" );

		ObjectiveCatalogDefinition catalog = AssetDatabase.LoadAssetAtPath<ObjectiveCatalogDefinition>( CatalogPath );
		if ( catalog == null )
		{
			catalog = ScriptableObject.CreateInstance<ObjectiveCatalogDefinition>();
			catalog.name = "ObjectiveCatalogDefinition";
			AssetDatabase.CreateAsset( catalog, CatalogPath );
			AssetDatabase.SaveAssets();
		}

		AddressableEditorUtil.TryRegister( CatalogPath, "ObjectiveCatalogDefinition", "Definition" );
	}

	static void EnsureIslandObjectives()
	{
		AddressableEditorUtil.EnsureFolder( ObjectivesFolder );

		ObjectiveDefinition island1 = EnsureObjective(
			"Objective_Island1_Copper.asset",
			"obj_island_1_copper",
			"Dig and Display Copper",
			null,
			new ObjectiveSubDefinition
			{
				id = "dig_copper",
				label = "Dig copper coins",
				completeType = ObjectiveSubCompleteType.PileEmptied,
				targetId = "island1_copper_pile"
			},
			new ObjectiveSubDefinition
			{
				id = "display_copper",
				label = "Display copper coins",
				completeType = ObjectiveSubCompleteType.DisplayComplete,
				targetId = "island1_coin_display"
			} );

		ObjectiveDefinition island2 = EnsureObjective(
			"Objective_Island2_Sorting.asset",
			"obj_island_2_sorting",
			"Sort and Display Coins",
			new[] { "obj_island_1_copper" },
			new ObjectiveSubDefinition
			{
				id = "display_copper",
				label = "Display copper coins",
				completeType = ObjectiveSubCompleteType.DisplayComplete,
				targetId = "island2_coin_display_copper"
			},
			new ObjectiveSubDefinition
			{
				id = "display_silver",
				label = "Display silver coins",
				completeType = ObjectiveSubCompleteType.DisplayComplete,
				targetId = "island2_coin_display_silver"
			} );

		ObjectiveDefinition island3 = EnsureObjective(
			"Objective_Island3_Constellation.asset",
			"obj_island_3_constellation",
			"Complete the Constellation",
			new[] { "obj_island_2_sorting" },
			new ObjectiveSubDefinition
			{
				id = "fill_display",
				label = "Display coins",
				completeType = ObjectiveSubCompleteType.DisplayComplete,
				targetId = "island3_coin_display"
			},
			new ObjectiveSubDefinition
			{
				id = "complete_constellation",
				label = "Place gems",
				completeType = ObjectiveSubCompleteType.ConstellationComplete,
				targetId = "island3_constellation"
			} );

		ObjectiveDefinition island4 = EnsureObjective(
			"Objective_Island4_Artifacts.asset",
			"obj_island_4_artifacts",
			"Present the Artifacts",
			new[] { "obj_island_3_constellation" },
			new ObjectiveSubDefinition
			{
				id = "fill_display",
				label = "Display coins",
				completeType = ObjectiveSubCompleteType.DisplayComplete,
				targetId = "island4_coin_display"
			},
			new ObjectiveSubDefinition
			{
				id = "present_artifacts",
				label = "Present artifacts",
				completeType = ObjectiveSubCompleteType.DisplayComplete,
				targetId = "island4_artifact_presentation"
			} );

		ObjectiveCatalogDefinition catalog = AssetDatabase.LoadAssetAtPath<ObjectiveCatalogDefinition>( CatalogPath );
		if ( catalog == null )
			return;

		if ( catalog.objectives == null )
			catalog.objectives = new System.Collections.Generic.List<ObjectiveDefinition>();

		bool dirty = false;
		dirty |= EnsureCatalogEntry( catalog, island1 );
		dirty |= EnsureCatalogEntry( catalog, island2 );
		dirty |= EnsureCatalogEntry( catalog, island3 );
		dirty |= EnsureCatalogEntry( catalog, island4 );

		if ( dirty )
		{
			EditorUtility.SetDirty( catalog );
			AssetDatabase.SaveAssets();
		}
	}

	static ObjectiveDefinition EnsureObjective(
		string fileName,
		string id,
		string title,
		string[] prerequisites,
		params ObjectiveSubDefinition[] subs )
	{
		string path = ObjectivesFolder + "/" + fileName;
		ObjectiveDefinition def = AssetDatabase.LoadAssetAtPath<ObjectiveDefinition>( path );
		if ( def != null )
			return def;

		def = ScriptableObject.CreateInstance<ObjectiveDefinition>();
		def.name = System.IO.Path.GetFileNameWithoutExtension( fileName );
		ApplyIslandObjectiveFields( def, id, title, prerequisites, subs );
		AssetDatabase.CreateAsset( def, path );
		EditorUtility.SetDirty( def );
		AssetDatabase.SaveAssets();
		return def;
	}

	static void ApplyIslandObjectiveFields(
		ObjectiveDefinition def,
		string id,
		string title,
		string[] prerequisites,
		ObjectiveSubDefinition[] subs )
	{
		def.id = id;
		def.title = title;
		def.showRadius = 40f;
		if ( id == "obj_island_1_copper" )
			def.showVolumeId = "volume_island_1";
		else if ( id == "obj_island_2_sorting" )
			def.showVolumeId = "volume_island_2";
		else if ( id == "obj_island_3_constellation" )
			def.showVolumeId = "volume_island_3";
		else if ( id == "obj_island_4_artifacts" )
			def.showVolumeId = "volume_island_4";
		def.prerequisiteObjectiveIds = prerequisites ?? System.Array.Empty<string>();
		def.subs = subs;
		def.rewards = System.Array.Empty<ObjectiveReward>();
		if ( id == "obj_island_1_copper" )
		{
			def.rewards = new[]
			{
				new ObjectiveReward
				{
					type = ObjectiveRewardType.Ability,
					abilityId = "jump"
				}
			};
			def.showReward = false;
		}
		def.rewardLabel = id == "obj_island_1_copper" ? "Unlock Jumping" : "Platforms";
		if ( def.rewards == null || def.rewards.Length == 0 )
			def.showReward = true;
		def.completionToast = CompletionToast;
		def.onCompleteWorldEventIds = System.Array.Empty<string>();
	}

	static bool EnsureCatalogEntry( ObjectiveCatalogDefinition catalog, ObjectiveDefinition def )
	{
		if ( def == null )
			return false;

		for ( int i = 0; i < catalog.objectives.Count; i++ )
		{
			ObjectiveDefinition entry = catalog.objectives[ i ];
			if ( entry == def || ( entry != null && entry.id == def.id ) )
			{
				if ( entry != def )
				{
					catalog.objectives[ i ] = def;
					return true;
				}
				return false;
			}
		}

		catalog.objectives.Add( def );
		return true;
	}
}
#endif
