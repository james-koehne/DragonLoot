using System;

using UnityEditor;
using UnityEngine;

namespace FeedbackSystem.Editor
{
	public static class FeedbackListDrawer
	{
		const float ButtonWidth = 22f;

		public static void Draw( SerializedProperty listProperty, string header )
		{
			if ( listProperty == null )
				return;

			if ( !string.IsNullOrEmpty( header ) )
				EditorGUILayout.LabelField( header, EditorStyles.boldLabel );

			if ( !listProperty.isArray )
			{
				EditorGUILayout.HelpBox( "Feedback list is not an array.", MessageType.Error );
				return;
			}

			for ( int i = 0; i < listProperty.arraySize; i++ )
				DrawElement( listProperty, i );

			EditorGUILayout.Space( 4f );
			if ( GUILayout.Button( "+ Add Feedback", GUILayout.Height( 24f ) ) )
				ShowAddMenu( listProperty, listProperty.arraySize );
		}

		static void DrawElement( SerializedProperty listProperty, int index )
		{
			SerializedProperty element = listProperty.GetArrayElementAtIndex( index );
			if ( element == null || element.propertyType != SerializedPropertyType.ManagedReference )
			{
				EditorGUILayout.HelpBox( "Invalid feedback entry.", MessageType.Warning );
				return;
			}

			Type type = GetManagedType( element );
			string title = type != null ? FeedbackTypeCache.GetDisplayName( type ) : "(Missing Feedback)";

			EditorGUILayout.BeginVertical( EditorStyles.helpBox );

			EditorGUILayout.BeginHorizontal();

			SerializedProperty enabledProp = element.FindPropertyRelative( "_enabled" );
			if ( enabledProp != null )
				enabledProp.boolValue = EditorGUILayout.Toggle( enabledProp.boolValue, GUILayout.Width( 16f ) );

			element.isExpanded = EditorGUILayout.Foldout( element.isExpanded, title, true );

			GUI.enabled = index > 0;
			if ( GUILayout.Button( "\u25B2", EditorStyles.miniButtonLeft, GUILayout.Width( ButtonWidth ) ) )
			{
				listProperty.MoveArrayElement( index, index - 1 );
				GUI.enabled = true;
				EditorGUILayout.EndHorizontal();
				EditorGUILayout.EndVertical();
				return;
			}

			GUI.enabled = index < listProperty.arraySize - 1;
			if ( GUILayout.Button( "\u25BC", EditorStyles.miniButtonMid, GUILayout.Width( ButtonWidth ) ) )
			{
				listProperty.MoveArrayElement( index, index + 1 );
				GUI.enabled = true;
				EditorGUILayout.EndHorizontal();
				EditorGUILayout.EndVertical();
				return;
			}

			GUI.enabled = true;
			if ( GUILayout.Button( "D", EditorStyles.miniButtonMid, GUILayout.Width( ButtonWidth ) ) )
			{
				element.DuplicateCommand();
				EditorGUILayout.EndHorizontal();
				EditorGUILayout.EndVertical();
				return;
			}

			if ( GUILayout.Button( "X", EditorStyles.miniButtonRight, GUILayout.Width( ButtonWidth ) ) )
			{
				DeleteElement( listProperty, index );
				EditorGUILayout.EndHorizontal();
				EditorGUILayout.EndVertical();
				return;
			}

			EditorGUILayout.EndHorizontal();

			if ( element.isExpanded && type != null )
				DrawFields( element );

			EditorGUILayout.EndVertical();
		}

		static void DrawFields( SerializedProperty element )
		{
			SerializedProperty iterator = element.Copy();
			SerializedProperty end = iterator.GetEndProperty();
			bool enterChildren = true;
			while ( iterator.NextVisible( enterChildren ) && !SerializedProperty.EqualContents( iterator, end ) )
			{
				enterChildren = false;
				if ( iterator.name == "_enabled" )
					continue;

				if ( iterator.isArray && IsFeedbackList( iterator ) )
				{
					EditorGUI.indentLevel++;
					Draw( iterator, ObjectNames.NicifyVariableName( iterator.displayName ) );
					EditorGUI.indentLevel--;
					continue;
				}

				EditorGUILayout.PropertyField( iterator, true );
			}
		}

		static void ShowAddMenu( SerializedProperty listProperty, int insertIndex )
		{
			GenericMenu menu = new GenericMenu();
			FeedbackTypeCache.Entry[] entries = FeedbackTypeCache.GetEntries();
			for ( int i = 0; i < entries.Length; i++ )
			{
				FeedbackTypeCache.Entry entry = entries[i];
				Type type = entry.Type;
				menu.AddItem( new GUIContent( entry.Path ), false, () =>
				{
					listProperty.serializedObject.Update();
					int index = insertIndex;
					if ( index < 0 || index > listProperty.arraySize )
						index = listProperty.arraySize;
					listProperty.arraySize++;
					if ( index < listProperty.arraySize - 1 )
						listProperty.MoveArrayElement( listProperty.arraySize - 1, index );
					SerializedProperty element = listProperty.GetArrayElementAtIndex( index );
					element.managedReferenceValue = Activator.CreateInstance( type );
					element.isExpanded = true;
					listProperty.serializedObject.ApplyModifiedProperties();
				} );
			}

			if ( entries.Length == 0 )
				menu.AddDisabledItem( new GUIContent( "No Feedback types found" ) );

			menu.ShowAsContext();
		}

		static void DeleteElement( SerializedProperty listProperty, int index )
		{
			SerializedProperty element = listProperty.GetArrayElementAtIndex( index );
			element.managedReferenceValue = null;
			listProperty.DeleteArrayElementAtIndex( index );
		}

		static bool IsFeedbackList( SerializedProperty property )
		{
			if ( property.arraySize > 0 )
			{
				SerializedProperty first = property.GetArrayElementAtIndex( 0 );
				return first != null && first.propertyType == SerializedPropertyType.ManagedReference;
			}

			return property.arrayElementType == "managedReference";
		}

		static Type GetManagedType( SerializedProperty property )
		{
			string typeName = property.managedReferenceFullTypename;
			if ( string.IsNullOrEmpty( typeName ) )
				return null;

			int space = typeName.IndexOf( ' ' );
			if ( space < 0 )
				return null;

			string assemblyName = typeName.Substring( 0, space );
			string className = typeName.Substring( space + 1 );
			return Type.GetType( className + ", " + assemblyName );
		}
	}
}
