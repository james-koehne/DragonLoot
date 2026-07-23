using System;
using System.Collections.Generic;

using UnityEngine;

public class WeakEvent
{
	private readonly List<WeakReference<Action>> _listeners = new();
	private readonly object _lock = new();

	public void Subscribe( Action listener )
	{
		if ( listener == null )
			return;

		lock ( _lock )
		{
			Cleanup();
			_listeners.Add( new WeakReference<Action>( listener ) );
		}
	}

	public void Unsubscribe( Action listener )
	{
		if ( listener == null )
			return;

		lock ( _lock )
		{
			for ( int i = 0; i < _listeners.Count; i++ )
			{
				if ( _listeners[ i ].TryGetTarget( out Action l ) && l == listener )
				{
					_listeners.RemoveAt( i );
					break;
				}
			}

			Cleanup();
		}
	}

	public void Invoke()
	{
		List<Action> toCall = new();

		lock ( _lock )
		{
			Cleanup();

			foreach ( WeakReference<Action> wr in _listeners )
			{
				if ( wr.TryGetTarget( out Action listener ) )
				{
					var target = listener.Target;

					if ( target is UnityEngine.Object unityObj && unityObj == null )
						continue;

					toCall.Add( listener );
				}
			}
		}

		foreach ( Action listener in toCall )
		{
			try
			{
				listener();
			}
			catch ( Exception ex )
			{
				Debug.Log( $"WeakEvent listener threw: {ex}" );
			}
		}
	}

	public void Clear()
	{
		lock ( _lock )
		{
			_listeners.Clear();
		}
	}

	private void Cleanup()
	{
		_listeners.RemoveAll( wr =>
		{
			if ( !wr.TryGetTarget( out Action l ) )
				return true;

			if ( l.Target is UnityEngine.Object unityObj && unityObj == null )
				return true;

			return false;
		} );
	}
}

public class WeakEvent<T>
{
	private readonly List<WeakReference<Action<T>>> _listeners = new();
	private readonly object _lock = new();

	public void Subscribe( Action<T> listener )
	{
		if ( listener == null )
			return;

		lock ( _lock )
		{
			Cleanup();
			_listeners.Add( new WeakReference<Action<T>>( listener ) );
		}
	}

	public void Unsubscribe( Action<T> listener )
	{
		if ( listener == null )
			return;

		lock ( _lock )
		{
			for ( int i = 0; i < _listeners.Count; i++ )
			{
				if ( _listeners[ i ].TryGetTarget( out Action<T> l ) && l == listener )
				{
					_listeners.RemoveAt( i );
					break;
				}
			}

			Cleanup();
		}
	}

	public void Invoke( T arg )
	{
		List<Action<T>> toCall = new();

		lock ( _lock )
		{
			Cleanup();

			foreach ( WeakReference<Action<T>> wr in _listeners )
			{
				if ( wr.TryGetTarget( out Action<T> listener ) )
				{
					var target = listener.Target;

					if ( target is UnityEngine.Object unityObj && unityObj == null )
						continue;

					toCall.Add( listener );
				}
			}
		}

		foreach ( Action<T> listener in toCall )
		{
			try
			{
				listener( arg );
			}
			catch ( Exception ex )
			{
				Debug.Log( $"WeakEvent<{typeof( T ).Name}> listener threw: {ex}" );
			}
		}
	}

	public void Clear()
	{
		lock ( _lock )
		{
			_listeners.Clear();
		}
	}

	private void Cleanup()
	{
		_listeners.RemoveAll( wr =>
		{
			if ( !wr.TryGetTarget( out Action<T> l ) )
				return true;

			if ( l.Target is UnityEngine.Object unityObj && unityObj == null )
				return true;

			return false;
		} );
	}
}