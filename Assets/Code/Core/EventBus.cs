using System;
using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Decouples systems: publish/subscribe by event type without direct references.
/// Uses multicast <see cref="Action{T}"/> per type; <see cref="Publish{T}"/> invokes with zero per-call allocations.
/// </summary>
public static class EventBus
{
	static readonly Dictionary<Type, Delegate> _subscribers = new Dictionary<Type, Delegate>();

#if UNITY_EDITOR
	[RuntimeInitializeOnLoadMethod( RuntimeInitializeLoadType.SubsystemRegistration )]
	static void ResetStatics()
	{
		_subscribers.Clear();
	}
#endif

	/// <summary>Registers a listener for events of type <typeparamref name="T"/>.</summary>
	public static void Subscribe<T>( Action<T> callback )
	{
		if ( callback == null )
			throw new ArgumentNullException( nameof( callback ) );

		var key = typeof( T );
		if ( _subscribers.TryGetValue( key, out var existing ) )
			_subscribers[ key ] = Delegate.Combine( existing, callback );
		else
			_subscribers[ key ] = callback;
	}

	/// <summary>Removes a previously registered listener.</summary>
	public static void Unsubscribe<T>( Action<T> callback )
	{
		if ( callback == null )
			return;

		var key = typeof( T );
		if ( !_subscribers.TryGetValue( key, out var existing ) )
			return;

		var next = Delegate.Remove( existing, callback );
		if ( next == null )
			_subscribers.Remove( key );
		else
			_subscribers[ key ] = next;
	}

	/// <summary>Notifies all subscribers. Event data is a struct copy (no heap alloc for the payload).</summary>
	public static void Publish<T>( T eventData )
	{
		if ( !_subscribers.TryGetValue( typeof( T ), out var del ) || del == null )
			return;

		if ( del is Action<T> action )
			action.Invoke( eventData );
	}
}
