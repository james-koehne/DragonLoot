using UnityEngine;

/// <summary>
/// Ensures Player and Collectable never physically collide (CharacterController / Rigidbody).
/// Raycasts still hit Collectable via interact masks.
/// </summary>
public static class PhysicsLayers
{
	const string PlayerLayerName = "Player";
	const string CollectableLayerName = "Collectable";

	static bool _applied;

	[RuntimeInitializeOnLoadMethod( RuntimeInitializeLoadType.BeforeSceneLoad )]
	static void Apply()
	{
		if ( _applied )
			return;

		int player = LayerMask.NameToLayer( PlayerLayerName );
		int collectable = LayerMask.NameToLayer( CollectableLayerName );
		if ( player < 0 || collectable < 0 )
		{
			Debug.LogWarning( "PhysicsLayers: missing Player or Collectable layer." );
			return;
		}

		Physics.IgnoreLayerCollision( player, collectable, true );
		_applied = true;
	}

	public static void EnsurePlayerLayer( GameObject root )
	{
		if ( root == null )
			return;

		int player = LayerMask.NameToLayer( PlayerLayerName );
		if ( player < 0 )
			return;

		if ( root.layer != player )
			root.layer = player;
	}
}
