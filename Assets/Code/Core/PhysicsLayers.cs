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
	static int _playerLayer = -1;
	static int _collectableLayer = -1;
	static int _defaultAndCollectableMask;

	public static int PlayerLayer
	{
		get
		{
			EnsureCached();
			return _playerLayer;
		}
	}

	public static int CollectableLayer
	{
		get
		{
			EnsureCached();
			return _collectableLayer;
		}
	}

	/// <summary>
	/// Default (layer 0) plus Collectable. Tall ground stacks move to Default when they block the player.
	/// </summary>
	public static int DefaultAndCollectableMask
	{
		get
		{
			EnsureCached();
			return _defaultAndCollectableMask;
		}
	}

	static void EnsureCached()
	{
		if ( _playerLayer >= 0 && _collectableLayer >= 0 )
			return;

		_playerLayer = LayerMask.NameToLayer( PlayerLayerName );
		_collectableLayer = LayerMask.NameToLayer( CollectableLayerName );
		int mask = 1 << 0;
		if ( _collectableLayer >= 0 )
			mask |= 1 << _collectableLayer;
		_defaultAndCollectableMask = mask;
	}

	[RuntimeInitializeOnLoadMethod( RuntimeInitializeLoadType.BeforeSceneLoad )]
	static void Apply()
	{
		if ( _applied )
			return;

		EnsureCached();
		if ( _playerLayer < 0 || _collectableLayer < 0 )
		{
			Debug.LogWarning( "PhysicsLayers: missing Player or Collectable layer." );
			return;
		}

		Physics.IgnoreLayerCollision( _playerLayer, _collectableLayer, true );
		_applied = true;
	}

	public static void EnsurePlayerLayer( GameObject root )
	{
		if ( root == null )
			return;

		int player = PlayerLayer;
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

		int collectable = CollectableLayer;
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
