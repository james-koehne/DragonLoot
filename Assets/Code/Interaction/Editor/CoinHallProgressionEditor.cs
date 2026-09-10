using UnityEditor;
using UnityEngine;

[CustomEditor( typeof( CoinHallProgression ) )]
public class CoinHallProgressionEditor : Editor
{
	bool _previewObjects = true;

	public override void OnInspectorGUI()
	{
		DrawDefaultInspector();

		CoinHallProgression hall = target as CoinHallProgression;
		if ( hall == null )
			return;

		EditorGUILayout.Space();
		EditorGUILayout.LabelField( "Editor Level Preview", EditorStyles.boldLabel );
		EditorGUILayout.HelpBox(
			"Temporarily snap movers to a level's targets to plan poses. Restore returns to the pose captured before the first preview. Does not change save data.",
			MessageType.Info );

		using ( new EditorGUI.DisabledScope( Application.isPlaying ) )
		{
			_previewObjects = EditorGUILayout.ToggleLeft( "Also preview objects (enable / disable)", _previewObjects );

			int levelCount = hall.LevelCount;
			if ( levelCount <= 0 )
			{
				EditorGUILayout.HelpBox( "Add levels to enable preview buttons.", MessageType.Warning );
				return;
			}

			int previewLevel = hall.EditorPreviewLevelIndex;
			EditorGUILayout.LabelField(
				previewLevel >= 0 ? "Previewing level " + previewLevel : "Not previewing (live poses)",
				EditorStyles.miniLabel );

			const int buttonsPerRow = 4;
			EditorGUILayout.BeginHorizontal();
			for ( int i = 0; i < levelCount; i++ )
			{
				if ( i > 0 && i % buttonsPerRow == 0 )
				{
					EditorGUILayout.EndHorizontal();
					EditorGUILayout.BeginHorizontal();
				}

				string id = hall.EditorGetLevelId( i );
				string label = string.IsNullOrEmpty( id ) ? "L" + i : "L" + i + ": " + id;
				GUI.backgroundColor = previewLevel == i ? new Color( 0.55f, 0.85f, 0.55f ) : Color.white;
				if ( GUILayout.Button( label ) )
					hall.EditorPreviewLevel( i, _previewObjects );
				GUI.backgroundColor = Color.white;
			}

			EditorGUILayout.EndHorizontal();

			using ( new EditorGUI.DisabledScope( !hall.EditorHasCachedPose ) )
			{
				if ( GUILayout.Button( "Restore Live Poses" ) )
					hall.EditorRestorePreview();
			}
		}

		if ( Application.isPlaying )
			EditorGUILayout.HelpBox( "Level preview is edit-mode only. Use Debug → Coin Hall in Play Mode.", MessageType.None );
	}
}
