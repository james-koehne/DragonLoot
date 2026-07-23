using UnityEngine;

[CreateAssetMenu( fileName = "PlayerInteractionDefinition", menuName = "Definitions/PlayerInteractionDefinition" )]
public class PlayerInteractionDefinition : ScriptableObject
{
	[Header( "Raycast" )]
	[Min( 0.1f )]
	public float interactRange = 8f;

	[Tooltip( "0 = DefaultRaycastLayers + Collectable at runtime." )]
	public LayerMask interactMask;

	[Header( "Pickup Input" )]
	[Tooltip( "Seconds between repeated pickup/interact while primary interact is held. Tap still picks up once immediately." )]
	[Min( 0.05f )]
	public float pickupRepeatInterval = 0.25f;

	[Header( "Release / Throw" )]
	[Min( 0f )]
	public float dropUpBias = 0.05f;

	[Tooltip( "Forward throw speed for normal treasure into empty space." )]
	[Min( 0f )]
	public float throwForce = 9f;

	[Tooltip( "Forward throw speed for heavy / exclusive treasure." )]
	[Min( 0f )]
	public float heavyThrowForce = 3.5f;

	[Min( 0f )]
	public float throwSpeed = 9f;

	[Min( 0f )]
	public float throwUpBias = 0.35f;

	[Tooltip( "Fraction of player planar velocity added to thrown treasure." )]
	[Range( 0f, 1f )]
	public float throwInheritPlanarScale = 1f;

	[Header( "Soft Release" )]
	[Tooltip( "Multiplier on throwSpeed for gentle drops (tables / soft place)." )]
	[Range( 0f, 1f )]
	public float softThrowSpeedScale = 0.25f;

	[Tooltip( "Multiplier on throwUpBias for soft release." )]
	[Range( 0f, 1f )]
	public float softThrowUpScale = 0.35f;

	[Header( "Throw / Place Input" )]
	[Tooltip( "Seconds between repeated throw/place while secondary interact is held. Tap still releases once immediately." )]
	[Min( 0.05f )]
	public float throwPlaceRepeatInterval = 0.25f;

	void OnValidate()
	{
		interactRange = Mathf.Max( 0.1f, interactRange );
		pickupRepeatInterval = Mathf.Max( 0.05f, pickupRepeatInterval );
		dropUpBias = Mathf.Max( 0f, dropUpBias );
		throwForce = Mathf.Max( 0f, throwForce );
		heavyThrowForce = Mathf.Max( 0f, heavyThrowForce );
		if ( throwSpeed <= 0f && throwForce > 0f )
			throwSpeed = throwForce;
		else if ( throwForce <= 0f && throwSpeed > 0f )
			throwForce = throwSpeed;
		throwSpeed = Mathf.Max( 0f, throwSpeed );
		throwUpBias = Mathf.Max( 0f, throwUpBias );
		throwInheritPlanarScale = Mathf.Clamp01( throwInheritPlanarScale );
		softThrowSpeedScale = Mathf.Clamp01( softThrowSpeedScale );
		softThrowUpScale = Mathf.Clamp01( softThrowUpScale );
		throwPlaceRepeatInterval = Mathf.Max( 0.05f, throwPlaceRepeatInterval );
	}
}
