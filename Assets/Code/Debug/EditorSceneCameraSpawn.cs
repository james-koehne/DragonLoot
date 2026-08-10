#if UNITY_EDITOR
using System.Globalization;

using UnityEditor;
using UnityEngine;

/// <summary>
/// Captures the Scene view camera when entering Play Mode. Live SceneView is often null
/// once the Game view takes focus, so pose is stored in SessionState across domain reload.
/// </summary>
public static class EditorSceneCameraSpawn
{
	const string HasKey = "DragonLoot.EditorSceneCameraSpawn.Has";
	const string PosKey = "DragonLoot.EditorSceneCameraSpawn.Pos";
	const string RotKey = "DragonLoot.EditorSceneCameraSpawn.Rot";

	[InitializeOnLoadMethod]
	static void Init()
	{
		EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
		EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
	}

	static void OnPlayModeStateChanged( PlayModeStateChange state )
	{
		if ( state == PlayModeStateChange.ExitingEditMode )
			CaptureToSessionState();
	}

	static void CaptureToSessionState()
	{
		if ( !TryReadLiveSceneView( out Vector3 position, out Quaternion rotation ) )
		{
			SessionState.SetBool( HasKey, false );
			return;
		}

		SessionState.SetBool( HasKey, true );
		SessionState.SetString( PosKey, EncodeVector3( position ) );
		SessionState.SetString( RotKey, EncodeQuaternion( rotation ) );
	}

	public static bool TryGetPose( out Vector3 position, out Quaternion rotation )
	{
		if ( TryReadLiveSceneView( out position, out rotation ) )
			return true;

		return TryReadSessionState( out position, out rotation );
	}

	static bool TryReadLiveSceneView( out Vector3 position, out Quaternion rotation )
	{
		position = default;
		rotation = default;

		SceneView sceneView = SceneView.lastActiveSceneView;
		if ( sceneView == null && SceneView.sceneViews != null && SceneView.sceneViews.Count > 0 )
			sceneView = SceneView.sceneViews[ 0 ] as SceneView;

		if ( sceneView == null )
			return false;

		if ( sceneView.camera != null )
		{
			Transform cam = sceneView.camera.transform;
			position = cam.position;
			rotation = cam.rotation;
			return true;
		}

		position = sceneView.pivot - sceneView.rotation * ( Vector3.forward * sceneView.cameraDistance );
		rotation = sceneView.rotation;
		return true;
	}

	static bool TryReadSessionState( out Vector3 position, out Quaternion rotation )
	{
		position = default;
		rotation = default;

		if ( !SessionState.GetBool( HasKey, false ) )
			return false;

		if ( !TryDecodeVector3( SessionState.GetString( PosKey, string.Empty ), out position ) )
			return false;

		if ( !TryDecodeQuaternion( SessionState.GetString( RotKey, string.Empty ), out rotation ) )
			return false;

		return true;
	}

	static string EncodeVector3( Vector3 value )
	{
		return value.x.ToString( "R", CultureInfo.InvariantCulture )
			+ "," + value.y.ToString( "R", CultureInfo.InvariantCulture )
			+ "," + value.z.ToString( "R", CultureInfo.InvariantCulture );
	}

	static string EncodeQuaternion( Quaternion value )
	{
		return value.x.ToString( "R", CultureInfo.InvariantCulture )
			+ "," + value.y.ToString( "R", CultureInfo.InvariantCulture )
			+ "," + value.z.ToString( "R", CultureInfo.InvariantCulture )
			+ "," + value.w.ToString( "R", CultureInfo.InvariantCulture );
	}

	static bool TryDecodeVector3( string raw, out Vector3 value )
	{
		value = default;
		if ( string.IsNullOrEmpty( raw ) )
			return false;

		string[] parts = raw.Split( ',' );
		if ( parts.Length != 3 )
			return false;

		if ( !float.TryParse( parts[ 0 ], NumberStyles.Float, CultureInfo.InvariantCulture, out float x ) )
			return false;
		if ( !float.TryParse( parts[ 1 ], NumberStyles.Float, CultureInfo.InvariantCulture, out float y ) )
			return false;
		if ( !float.TryParse( parts[ 2 ], NumberStyles.Float, CultureInfo.InvariantCulture, out float z ) )
			return false;

		value = new Vector3( x, y, z );
		return true;
	}

	static bool TryDecodeQuaternion( string raw, out Quaternion value )
	{
		value = default;
		if ( string.IsNullOrEmpty( raw ) )
			return false;

		string[] parts = raw.Split( ',' );
		if ( parts.Length != 4 )
			return false;

		if ( !float.TryParse( parts[ 0 ], NumberStyles.Float, CultureInfo.InvariantCulture, out float x ) )
			return false;
		if ( !float.TryParse( parts[ 1 ], NumberStyles.Float, CultureInfo.InvariantCulture, out float y ) )
			return false;
		if ( !float.TryParse( parts[ 2 ], NumberStyles.Float, CultureInfo.InvariantCulture, out float z ) )
			return false;
		if ( !float.TryParse( parts[ 3 ], NumberStyles.Float, CultureInfo.InvariantCulture, out float w ) )
			return false;

		value = new Quaternion( x, y, z, w );
		return true;
	}
}
#endif
