using UnityEngine.Events;

public static class UnityEventEx
{
	public static void AddOneShotListener( this UnityEvent unityEvent, UnityAction callback )
	{
		UnityAction wrapper = null;
		wrapper = () =>
		{
			callback();
			unityEvent.RemoveListener( wrapper );
		};
		unityEvent.AddListener( wrapper );
	}

	public static void AddOneShotListener<T1>( this UnityEvent<T1> unityEvent, UnityAction<T1> callback )
	{
		UnityAction<T1> wrapper = null;
		wrapper = ( arg1 ) =>
		{
			callback( arg1 );
			unityEvent.RemoveListener( wrapper );
		};
		unityEvent.AddListener( wrapper );
	}

	public static void AddOneShotListener<T1, T2>( this UnityEvent<T1, T2> unityEvent, UnityAction<T1, T2> callback )
	{
		UnityAction<T1, T2> wrapper = null;
		wrapper = ( arg1, arg2 ) =>
		{
			callback( arg1, arg2 );
			unityEvent.RemoveListener( wrapper );
		};
		unityEvent.AddListener( wrapper );
	}

	public static void AddOneShotListener<T1, T2, T3>( this UnityEvent<T1, T2, T3> unityEvent, UnityAction<T1, T2, T3> callback )
	{
		UnityAction<T1, T2, T3> wrapper = null;
		wrapper = ( arg1, arg2, arg3 ) =>
		{
			callback( arg1, arg2, arg3 );
			unityEvent.RemoveListener( wrapper );
		};
		unityEvent.AddListener( wrapper );
	}

	public static void AddOneShotListener<T1, T2, T3, T4>( this UnityEvent<T1, T2, T3, T4> unityEvent, UnityAction<T1, T2, T3, T4> callback )
	{
		UnityAction<T1, T2, T3, T4> wrapper = null;
		wrapper = ( arg1, arg2, arg3, arg4 ) =>
		{
			callback( arg1, arg2, arg3, arg4 );
			unityEvent.RemoveListener( wrapper );
		};
		unityEvent.AddListener( wrapper );
	}
}