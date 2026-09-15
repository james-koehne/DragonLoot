#if UNITY_EDITOR
using UnityEditor;

using UnityEngine;

[CustomEditor( typeof( WorldEventDefinition ) )]
public class WorldEventDefinitionEditor : Editor
{
	SerializedProperty _id;
	SerializedProperty _tags;
	SerializedProperty _conditions;
	SerializedProperty _actions;

	void OnEnable()
	{
		_id = serializedObject.FindProperty( "id" );
		_tags = serializedObject.FindProperty( "tags" );
		_conditions = serializedObject.FindProperty( "conditions" );
		_actions = serializedObject.FindProperty( "actions" );
	}

	public override void OnInspectorGUI()
	{
		serializedObject.Update();

		EditorGUILayout.PropertyField( _id );
		EditorGUILayout.PropertyField( _tags );
		EditorGUILayout.PropertyField( _conditions, includeChildren: true );

		EditorGUILayout.Space( 8f );
		DrawSequenceSummary( (WorldEventDefinition)target );

		EditorGUILayout.Space( 4f );
		EditorGUILayout.PropertyField( _actions, includeChildren: true );

		serializedObject.ApplyModifiedProperties();
	}

	static void DrawSequenceSummary( WorldEventDefinition definition )
	{
		EditorGUILayout.LabelField( "Action Sequence", EditorStyles.boldLabel );
		WorldEventAction[] actions = definition.actions;
		if ( actions == null || actions.Length == 0 )
		{
			EditorGUILayout.HelpBox( "No actions.", MessageType.Info );
			return;
		}

		System.Text.StringBuilder sb = new System.Text.StringBuilder();
		float sequentialTime = 0f;
		for ( int i = 0; i < actions.Length; i++ )
		{
			WorldEventAction action = actions[ i ];
			if ( action == null )
				continue;

			float delay = Mathf.Max( 0f, action.delayBefore );
			string when;
			if ( action.onlyDelayThisAction && delay > 0f )
				when = "solo +" + delay.ToString( "0.##" ) + "s";
			else
			{
				sequentialTime += delay;
				when = "t=" + sequentialTime.ToString( "0.##" ) + "s";
			}

			string detail = DescribeAction( action );
			string wait = action.waitUntilFinished ? " [wait]" : string.Empty;
			if ( sb.Length > 0 )
				sb.Append( "\n" );
			sb.Append( when ).Append( "  " ).Append( detail ).Append( wait );

			if ( action.type == WorldEventActionType.CinematicPresentation &&
			     !string.IsNullOrEmpty( action.cinematicPresentationId ) )
				AppendCinematicCueSummary( sb, action.cinematicPresentationId );
		}

		EditorGUILayout.HelpBox( sb.ToString(), MessageType.None );
	}

	static string DescribeAction( WorldEventAction action )
	{
		switch ( action.type )
		{
			case WorldEventActionType.Dialogue:
				int lines = action.dialogue != null ? action.dialogue.Length : 0;
				return "Dialogue (" + lines + " line" + ( lines == 1 ? "" : "s" ) + ")";
			case WorldEventActionType.SpawnAddressable:
				return "Spawn " + ( string.IsNullOrEmpty( action.addressableKey ) ? "(no key)" : action.addressableKey );
			case WorldEventActionType.SetTutorialHud:
				return "Tutorial HUD";
			case WorldEventActionType.PlayAudio:
				return "PlayAudio " + ( action.audioClip != null ? action.audioClip.name : "(no clip)" );
			case WorldEventActionType.LanternRevealSweep:
				return "LanternReveal " + ( string.IsNullOrEmpty( action.lanternRevealId ) ? "(no id)" : action.lanternRevealId );
			case WorldEventActionType.CinematicPresentation:
				return "Cinematic " + ( string.IsNullOrEmpty( action.cinematicPresentationId ) ? "(no id)" : action.cinematicPresentationId );
			case WorldEventActionType.BrakePlayerMovement:
				return "Brake " + action.playerBrakeDuration.ToString( "0.##" ) + "s";
			default:
				return action.type.ToString();
		}
	}

	static void AppendCinematicCueSummary( System.Text.StringBuilder sb, string presentationId )
	{
		CinematicPresentationController controller = FindControllerInOpenScenes( presentationId );
		if ( controller == null )
		{
			sb.Append( "\n    (open a scene with this cinematic controller to see cues)" );
			return;
		}

		float total = Mathf.Max( 0.01f, controller.EstimateContentDurationForEditor() );
		sb.Append( "\n    phases ~" ).Append( total.ToString( "0.##" ) ).Append( "s" );
		sb.Append( " (walk/enter/tour/return)" );

		CinematicCue[] cues = controller.Cues;
		if ( cues == null || cues.Length == 0 )
		{
			sb.Append( "\n    no cues on controller" );
			return;
		}

		for ( int i = 0; i < cues.Length; i++ )
		{
			CinematicCue cue = cues[ i ];
			if ( cue == null )
				continue;
			string name = cue.type == CinematicCueType.LanternReveal ? "LanternReveal" : "PlayAudio";
			sb.Append( "\n    cue " ).Append( name ).Append( " @" ).Append( cue.delay.ToString( "0.##" ) ).Append( "s" );
		}
	}

	static CinematicPresentationController FindControllerInOpenScenes( string presentationId )
	{
		CinematicPresentationController[] controllers =
			Object.FindObjectsByType<CinematicPresentationController>( FindObjectsInactive.Exclude, FindObjectsSortMode.None );
		for ( int i = 0; i < controllers.Length; i++ )
		{
			CinematicPresentationController controller = controllers[ i ];
			if ( controller != null && controller.PresentationId == presentationId )
				return controller;
		}

		return null;
	}
}

[CustomPropertyDrawer( typeof( WorldEventAction ) )]
public class WorldEventActionDrawer : PropertyDrawer
{
	public override float GetPropertyHeight( SerializedProperty property, GUIContent label )
	{
		if ( !property.isExpanded )
			return EditorGUIUtility.singleLineHeight;

		float line = EditorGUIUtility.singleLineHeight + 2f;
		float height = line; // foldout
		height += line * 4f; // type + delay + onlyDelay + wait

		WorldEventActionType type = (WorldEventActionType)property.FindPropertyRelative( "type" ).enumValueIndex;
		switch ( type )
		{
			case WorldEventActionType.Dialogue:
				height += EditorGUI.GetPropertyHeight( property.FindPropertyRelative( "dialogue" ), true ) + 2f;
				break;
			case WorldEventActionType.SpawnAddressable:
				height += line * 4f;
				break;
			case WorldEventActionType.SetTutorialHud:
				height += line * 3f;
				break;
			case WorldEventActionType.PlayAudio:
				height += line * 12f;
				break;
			case WorldEventActionType.LanternRevealSweep:
				height += line + EditorGUIUtility.singleLineHeight * 2f + 4f;
				break;
			case WorldEventActionType.CinematicPresentation:
				height += line + EditorGUIUtility.singleLineHeight * 2f + 4f;
				break;
			case WorldEventActionType.BrakePlayerMovement:
				height += line;
				break;
		}

		return height + 6f;
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

		SerializedProperty typeProp = property.FindPropertyRelative( "type" );
		EditorGUI.PropertyField( row, typeProp );
		row.y += EditorGUIUtility.singleLineHeight + 2f;

		EditorGUI.PropertyField( row, property.FindPropertyRelative( "delayBefore" ) );
		row.y += EditorGUIUtility.singleLineHeight + 2f;
		EditorGUI.PropertyField( row, property.FindPropertyRelative( "onlyDelayThisAction" ) );
		row.y += EditorGUIUtility.singleLineHeight + 2f;
		EditorGUI.PropertyField( row, property.FindPropertyRelative( "waitUntilFinished" ) );
		row.y += EditorGUIUtility.singleLineHeight + 2f;

		WorldEventActionType type = (WorldEventActionType)typeProp.enumValueIndex;
		switch ( type )
		{
			case WorldEventActionType.Dialogue:
				row = DrawRelative( row, property, "dialogue", true );
				break;
			case WorldEventActionType.SpawnAddressable:
				row = DrawRelative( row, property, "addressableKey" );
				row = DrawRelative( row, property, "spawnPointId" );
				row = DrawRelative( row, property, "useWorldPosition" );
				row = DrawRelative( row, property, "spawnWorldPosition" );
				break;
			case WorldEventActionType.SetTutorialHud:
				row = DrawRelative( row, property, "tutorialTitle" );
				row = DrawRelative( row, property, "tutorialObjectiveText" );
				row = DrawRelative( row, property, "markerTargetId" );
				break;
			case WorldEventActionType.PlayAudio:
				row = DrawRelative( row, property, "audioClip" );
				row = DrawRelative( row, property, "audioVolumeMin" );
				row = DrawRelative( row, property, "audioVolumeMax" );
				row = DrawRelative( row, property, "audioPitchMin" );
				row = DrawRelative( row, property, "audioPitchMax" );
				row = DrawRelative( row, property, "audioSpatialBlend" );
				row = DrawRelative( row, property, "audioMinDistance" );
				row = DrawRelative( row, property, "audioMaxDistance" );
				row = DrawRelative( row, property, "audioAtPlayer" );
				row = DrawRelative( row, property, "audioUseWorldPosition" );
				row = DrawRelative( row, property, "spawnPointId" );
				row = DrawRelative( row, property, "spawnWorldPosition" );
				break;
			case WorldEventActionType.LanternRevealSweep:
				row = DrawRelative( row, property, "lanternRevealId" );
				{
					Rect help = new Rect( row.x, row.y, row.width, EditorGUIUtility.singleLineHeight * 2f );
					EditorGUI.HelpBox( help, "Timing lives on LanternRevealSweepController.", MessageType.Info );
					row.y += help.height + 2f;
				}
				break;
			case WorldEventActionType.CinematicPresentation:
				row = DrawRelative( row, property, "cinematicPresentationId" );
				{
					Rect help = new Rect( row.x, row.y, row.width, EditorGUIUtility.singleLineHeight * 2f );
					EditorGUI.HelpBox( help,
						"Phases and cues live on CinematicPresentationController. Use waitUntilFinished to block later actions.",
						MessageType.Info );
					row.y += help.height + 2f;
				}
				break;
			case WorldEventActionType.BrakePlayerMovement:
				row = DrawRelative( row, property, "playerBrakeDuration" );
				break;
		}

		EditorGUI.indentLevel--;
		EditorGUI.EndProperty();
	}

	static Rect DrawRelative( Rect row, SerializedProperty property, string name, bool includeChildren = false )
	{
		SerializedProperty child = property.FindPropertyRelative( name );
		if ( child == null )
			return row;

		float height = includeChildren
			? EditorGUI.GetPropertyHeight( child, true )
			: EditorGUIUtility.singleLineHeight;
		Rect field = new Rect( row.x, row.y, row.width, height );
		EditorGUI.PropertyField( field, child, includeChildren );
		row.y += height + 2f;
		return row;
	}
}
#endif
