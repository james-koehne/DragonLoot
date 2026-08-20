#if UNITY_EDITOR
using System.IO;

using UnityEditor;
using UnityEditor.Build;

using UnityEngine;

/// <summary>
/// Ships each <see cref="TreasureSurfacePaintAsset"/> .paintbin sidecar into the player
/// StreamingAssets folder. Paint is not Unity-serialized, so without this the player
/// gets an empty non-traversable surface and ground/pile loot never places.
/// </summary>
public class TreasureSurfacePaintBuildProcessor : BuildPlayerProcessor
{
	public override int callbackOrder => -50;

	public override void PrepareForBuild( BuildPlayerContext buildPlayerContext )
	{
		string[] guids = AssetDatabase.FindAssets( "t:TreasureSurfacePaintAsset" );
		int copied = 0;
		for ( int i = 0; i < guids.Length; i++ )
		{
			string assetPath = AssetDatabase.GUIDToAssetPath( guids[ i ] );
			TreasureSurfacePaintAsset paint = AssetDatabase.LoadAssetAtPath<TreasureSurfacePaintAsset>( assetPath );
			if ( paint == null )
				continue;

			if ( paint.IsDirty )
				paint.EditorSaveToDisk();

			string sidecarAbs = paint.EditorSidecarAbsolutePath();
			if ( string.IsNullOrEmpty( sidecarAbs ) || !File.Exists( sidecarAbs ) )
			{
				if ( paint.CellsX <= 0 || paint.CellsZ <= 0 )
					continue;

				throw new BuildFailedException(
					"Treasure surface paint sidecar is missing for '"
					+ paint.name
					+ "'. Expected "
					+ sidecarAbs
					+ ". Paint the floor in the editor so ground loot and pile gems/artifacts can spawn in builds." );
			}

			string destRel = paint.StreamingAssetsRelativePath.Replace( '\\', '/' );
			buildPlayerContext.AddAdditionalPathToStreamingAssets( sidecarAbs, destRel );
			copied++;
		}

		if ( copied == 0 )
		{
			Debug.LogWarning(
				"TreasureSurfacePaintBuildProcessor: no .paintbin sidecars were added to StreamingAssets." );
			return;
		}

		Debug.Log(
			"TreasureSurfacePaintBuildProcessor: copied "
			+ copied
			+ " paint sidecar(s) into player StreamingAssets/"
			+ TreasureSurfacePaintAsset.StreamingFolder
			+ "." );
	}
}
#endif
