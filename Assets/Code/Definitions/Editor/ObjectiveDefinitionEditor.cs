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
#endif
