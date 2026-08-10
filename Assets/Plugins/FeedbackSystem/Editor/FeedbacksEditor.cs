using UnityEditor;
using UnityEngine;

using FeedbackSystem;

namespace FeedbackSystem.Editor
{
	[CustomEditor( typeof( Feedbacks ) )]
	public class FeedbacksEditor : UnityEditor.Editor
	{
		SerializedProperty _feedbacksProperty;

		void OnEnable()
		{
			_feedbacksProperty = serializedObject.FindProperty( "_feedbacks" );
		}

		public override void OnInspectorGUI()
		{
			serializedObject.Update();

			Feedbacks feedbacks = (Feedbacks)target;
			EditorGUILayout.Space( 2f );
			EditorGUILayout.LabelField( "FEEDBACKS", EditorStyles.boldLabel );

			if ( Application.isPlaying )
			{
				EditorGUILayout.BeginHorizontal();
				if ( GUILayout.Button( "Play" ) )
					feedbacks.Play();
				if ( GUILayout.Button( "Stop" ) )
					feedbacks.Stop();
				if ( GUILayout.Button( "Reset" ) )
					feedbacks.ResetFeedbacks();
				EditorGUILayout.EndHorizontal();
				EditorGUILayout.Space( 4f );
			}

			FeedbackListDrawer.Draw( _feedbacksProperty, string.Empty );
			serializedObject.ApplyModifiedProperties();
		}
	}
}
