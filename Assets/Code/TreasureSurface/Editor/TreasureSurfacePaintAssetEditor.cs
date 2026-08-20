#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor( typeof( TreasureSurfacePaintAsset ) )]
public class TreasureSurfacePaintAssetEditor : Editor
{
	public override void OnInspectorGUI()
	{
		// Never DrawDefaultInspector — paint must not be reflected as serialized arrays.
		TreasureSurfacePaintAsset paint = ( TreasureSurfacePaintAsset )target;

		EditorGUILayout.LabelField( "Cells", paint.CellsX + " × " + paint.CellsZ );
		EditorGUILayout.LabelField( "In Memory", paint.HasBuffers ? "Loaded" : "Empty" );
		EditorGUILayout.LabelField( "Dirty", paint.IsDirty ? "Yes" : "No" );

		string sidecar = paint.EditorSidecarAssetPath();
		EditorGUILayout.LabelField( "Sidecar", string.IsNullOrEmpty( sidecar ) ? "(unsaved asset)" : sidecar );

		EditorGUILayout.HelpBox(
			"Paint grids live in a .paintbin sidecar (not Unity-serialized).\n"
			+ "Selecting this asset stays fast because the Inspector never draws the grids.\n"
			+ "Player builds copy that sidecar into StreamingAssets automatically.",
			MessageType.Info );

		SerializedProperty baked = serializedObject.FindProperty( "bakedPaint" );
		if ( baked != null )
		{
			serializedObject.Update();
			EditorGUILayout.PropertyField( baked, new GUIContent( "Baked Paint (builds)" ) );
			serializedObject.ApplyModifiedProperties();
		}

		EditorGUILayout.BeginHorizontal();
		if ( GUILayout.Button( "Reload From Disk" ) )
		{
			if ( paint.EditorReloadFromDisk() )
				Debug.Log( "TreasureSurfacePaintAsset: reloaded " + sidecar );
			else
				Debug.LogWarning( "TreasureSurfacePaintAsset: no sidecar on disk." );
		}

		if ( GUILayout.Button( "Flush To Disk" ) )
		{
			if ( paint.EditorSaveToDisk() )
				Debug.Log( "TreasureSurfacePaintAsset: wrote " + sidecar );
			else
				Debug.LogWarning( "TreasureSurfacePaintAsset: nothing to flush." );
		}
		EditorGUILayout.EndHorizontal();

		if ( GUILayout.Button( "Bake .bytes For Builds" ) )
			paint.EditorBakeTextAsset();
	}
}

/// <summary>Flushes dirty paint sidecars whenever assets/scenes are saved.</summary>
class TreasureSurfacePaintAssetSaveHook : AssetModificationProcessor
{
	static string[] OnWillSaveAssets( string[] paths )
	{
		TreasureSurfacePaintAsset[] paints = Resources.FindObjectsOfTypeAll<TreasureSurfacePaintAsset>();
		for ( int i = 0; i < paints.Length; i++ )
		{
			TreasureSurfacePaintAsset paint = paints[ i ];
			if ( paint == null || !paint.IsDirty )
				continue;

			paint.EditorSaveToDisk();
		}

		return paths;
	}
}
#endif
