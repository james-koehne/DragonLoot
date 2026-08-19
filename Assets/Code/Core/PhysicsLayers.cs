using UnityEngine;

/// <summary>
/// Player vs Collectable never physically collide (CharacterController / Rigidbody).
/// Raycasts still hit Collectable via interact masks.
/// CharacterController ignores IgnoreLayerCollision, so collectables are also excluded on the controller.
/// Objects that should block the player (pile obstacles, tall coin stacks) use Default instead.
/// </summary>
public static class PhysicsLayers
{
	const string PlayerLayerName = "Player";
	const string CollectableLayerName = "Collectable";

	static bool _applied;

	public static int PlayerLayer => LayerMask.NameToLayer( PlayerLayerName );
	public static int CollectableLayer => LayerMask.NameToLayer( CollectableLayerName );

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

	/// <summary>
	/// CharacterController does not honor Physics.IgnoreLayerCollision — exclude Collectable explicitly.
	/// </summary>
	public static void EnsureCharacterControllerIgnoresCollectables( CharacterController controller )
	{
		if ( controller == null )
			return;

		int collectable = LayerMask.NameToLayer( CollectableLayerName );
		if ( collectable < 0 )
			return;

		controller.excludeLayers |= 1 << collectable;
	}

	/// <summary>
	/// Collectable is ignored by the player. Default collides. Does not recurse into children.
	/// </summary>
	public static void SetRootCollidesWithPlayer( GameObject root, bool collides )
	{
		if ( root == null )
			return;

		int layer = 0;
		if ( !collides )
		{
			layer = CollectableLayer;
			if ( layer < 0 )
				return;
		}

		if ( root.layer != layer )
			root.layer = layer;
	}
}
