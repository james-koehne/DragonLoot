using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Keeps Physics treasure asleep when far from the player so only nearby bodies simulate.
/// </summary>
public class TreasureProximitySleep : MonoBehaviour
{
	const float DefaultRadius = 18f;

	static TreasureProximitySleep _instance;
	static readonly List<TreasureItem> PhysicsItems = new List<TreasureItem>();

	[SerializeField]
	[Min( 1f )]
	float simulateRadius = DefaultRadius;

	Transform _player;

	public static float SimulateRadius
	{
		get
		{
			if ( _instance != null )
				return _instance.simulateRadius;
			return DefaultRadius;
		}
	}

	public static void EnsureExists()
	{
		if ( _instance != null )
			return;

		GameObject go = new GameObject( "TreasureProximitySleep" );
		_instance = go.AddComponent<TreasureProximitySleep>();
		Object.DontDestroyOnLoad( go );
	}

	public static void SetPlayer( Transform player )
	{
		EnsureExists();
		_instance._player = player;
	}

	public static void ClearPlayer( Transform player )
	{
		if ( _instance == null )
			return;

		if ( player != null && _instance._player != player )
			return;

		_instance._player = null;
	}

	public static bool TryGetPlayerPosition( out Vector3 position )
	{
		EnsureExists();
		Transform player = _instance._player;
		if ( player == null )
		{
			position = default;
			return false;
		}

		position = player.position;
		return true;
	}

	public static bool ShouldSimulate( Vector3 worldPosition )
	{
		EnsureExists();
		Transform player = _instance._player;
		if ( player == null )
			return true;

		float radius = _instance.simulateRadius;
		return ( worldPosition - player.position ).sqrMagnitude <= radius * radius;
	}

	public static void RegisterPhysics( TreasureItem item )
	{
		if ( item == null )
			return;

		EnsureExists();
		if ( !PhysicsItems.Contains( item ) )
			PhysicsItems.Add( item );
	}

	/// <summary>Deprecated alias.</summary>
	public static void RegisterFree( TreasureItem item )
	{
		RegisterPhysics( item );
	}

	public static void Unregister( TreasureItem item )
	{
		if ( item == null )
			return;

		PhysicsItems.Remove( item );
	}

	void Awake()
	{
		if ( _instance != null && _instance != this )
		{
			Destroy( gameObject );
			return;
		}

		_instance = this;
	}

	void FixedUpdate()
	{
		for ( int i = PhysicsItems.Count - 1; i >= 0; i-- )
		{
			TreasureItem item = PhysicsItems[ i ];
			if ( item == null )
			{
				PhysicsItems.RemoveAt( i );
				continue;
			}

			item.ApplyProximitySimulation( ShouldSimulate( item.transform.position ) );
		}
	}

	void OnDestroy()
	{
		if ( _instance == this )
			_instance = null;
	}
}
