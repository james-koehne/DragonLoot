using System.Collections;

using UnityEngine;

/// <summary>
/// Durable host for treasure flight coroutines so motion is not cancelled when an
/// individual item is re-parented, picked up, or otherwise interrupted.
/// </summary>
public sealed class TreasureMotionHost : MonoBehaviour
{
	static TreasureMotionHost _instance;

	public static Coroutine Run( IEnumerator routine )
	{
		if ( routine == null )
			return null;

		EnsureExists();
		return _instance.StartCoroutine( routine );
	}

	static void EnsureExists()
	{
		if ( _instance != null )
			return;

		GameObject go = new GameObject( "TreasureMotionHost" );
		Object.DontDestroyOnLoad( go );
		_instance = go.AddComponent<TreasureMotionHost>();
	}
}
