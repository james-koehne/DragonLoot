using System;

using UnityEngine;

public static class RuntimeDefinition
{
	public static T Resolve<T>( ref T cached ) where T : ScriptableObject
	{
		if ( cached != null )
			return cached;

		cached = GameInstance.GetDefinition<T>();
		return cached;
	}

	public static float Get<T>( T definition, Func<T, float> selector, float fallback ) where T : ScriptableObject
		=> definition != null ? selector( definition ) : fallback;

	public static int Get<T>( T definition, Func<T, int> selector, int fallback ) where T : ScriptableObject
		=> definition != null ? selector( definition ) : fallback;

	public static bool Get<T>( T definition, Func<T, bool> selector, bool fallback ) where T : ScriptableObject
		=> definition != null ? selector( definition ) : fallback;

	public static Vector3 GetVector3<T>( T definition, Func<T, Vector3> selector, Vector3 fallback ) where T : ScriptableObject
		=> definition != null ? selector( definition ) : fallback;

	public static LayerMask GetLayerMask<T>( T definition, Func<T, LayerMask> selector, LayerMask fallback ) where T : ScriptableObject
		=> definition != null ? selector( definition ) : fallback;
}
