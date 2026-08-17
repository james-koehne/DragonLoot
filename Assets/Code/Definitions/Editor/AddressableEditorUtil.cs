#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

/// <summary>
/// Shared Addressable registration helpers for treasure creation tools.
/// </summary>
public static class AddressableEditorUtil
{
	public static bool TryRegister( string assetPath, string address, string label = null )
	{
		AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
		if ( settings == null )
		{
			Debug.LogWarning( "AddressableAssetSettings missing; skipped registering " + assetPath );
			return false;
		}

		string guid = AssetDatabase.AssetPathToGUID( assetPath );
		if ( string.IsNullOrEmpty( guid ) )
			return false;

		AddressableAssetEntry entry = settings.FindAssetEntry( guid );
		if ( entry == null )
			entry = settings.CreateOrMoveEntry( guid, settings.DefaultGroup, readOnly: false, postEvent: false );

		entry.SetAddress( address );
		if ( !string.IsNullOrEmpty( label ) && !entry.labels.Contains( label ) )
			entry.SetLabel( label, true, true );

		settings.SetDirty( AddressableAssetSettings.ModificationEvent.EntryMoved, entry, true );
		return true;
	}

	public static void EnsureFolder( string path )
	{
		if ( AssetDatabase.IsValidFolder( path ) )
			return;

		string parent = System.IO.Path.GetDirectoryName( path );
		if ( parent != null )
			parent = parent.Replace( '\\', '/' );
		string leaf = System.IO.Path.GetFileName( path );
		if ( string.IsNullOrEmpty( parent ) || string.IsNullOrEmpty( leaf ) )
			return;

		if ( !AssetDatabase.IsValidFolder( parent ) )
			EnsureFolder( parent );

		AssetDatabase.CreateFolder( parent, leaf );
	}
}
#endif
