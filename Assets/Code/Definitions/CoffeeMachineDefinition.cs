using UnityEngine;

[CreateAssetMenu( fileName = "CoffeeMachineDefinition", menuName = "Definitions/CoffeeMachineDefinition" )]
public class CoffeeMachineDefinition : ScriptableObject
{
	[Header( "Craft timing" )]
	[Min( 0.1f )]
	public float holdSeconds = 1f;

	[Min( 0.05f )]
	public float moveSeconds = 0.35f;

	[Min( 0f )]
	public float moveArcHeight = 0.08f;

	[Min( 0.05f )]
	public float tampPressSeconds = 0.22f;

	[Min( 0f )]
	public float tampHoldSeconds = 0.12f;

	[Min( 0.05f )]
	public float lockTwistSeconds = 0.4f;

	[Min( 0.05f )]
	public float resetMoveSeconds = 0.22f;

	[Header( "Sip buff" )]
	[Min( 1f )]
	public float sipBuffMultiplier = 1.25f;

	[Min( 0.1f )]
	public float sipBuffDurationSeconds = 30f;

	[Header( "Sip motion (camera-local)" )]
	[Tooltip( "Camera-local position while sipping (middle of view)." )]
	public Vector3 sipLocalPosition = new Vector3( 0.08f, -0.12f, 0.32f );

	[Tooltip( "Camera-local euler while sipping (tilt back toward face)." )]
	public Vector3 sipLocalEuler = new Vector3( -35f, 15f, 0f );

	[Min( 0.05f )]
	public float sipRaiseSeconds = 0.18f;

	[Min( 0f )]
	public float sipHoldSeconds = 0.22f;

	[Min( 0.05f )]
	public float sipReturnSeconds = 0.2f;

	[Header( "Sip SFX" )]
	[Tooltip( "Volume can exceed 1 for boost (e.g. 1.5 = +50%)." )]
	[Min( 0f )]
	public float sipVolumeMin = 1.5f;

	[Min( 0f )]
	public float sipVolumeMax = 1.5f;

	public AudioClip sipClipA;
	public AudioClip sipClipB;

	void OnValidate()
	{
		holdSeconds = Mathf.Max( 0.1f, holdSeconds );
		moveSeconds = Mathf.Max( 0.05f, moveSeconds );
		moveArcHeight = Mathf.Max( 0f, moveArcHeight );
		tampPressSeconds = Mathf.Max( 0.05f, tampPressSeconds );
		tampHoldSeconds = Mathf.Max( 0f, tampHoldSeconds );
		lockTwistSeconds = Mathf.Max( 0.05f, lockTwistSeconds );
		resetMoveSeconds = Mathf.Max( 0.05f, resetMoveSeconds );
		sipBuffMultiplier = Mathf.Max( 1f, sipBuffMultiplier );
		sipBuffDurationSeconds = Mathf.Max( 0.1f, sipBuffDurationSeconds );
		sipRaiseSeconds = Mathf.Max( 0.05f, sipRaiseSeconds );
		sipHoldSeconds = Mathf.Max( 0f, sipHoldSeconds );
		sipReturnSeconds = Mathf.Max( 0.05f, sipReturnSeconds );
		sipVolumeMin = Mathf.Max( 0f, sipVolumeMin );
		sipVolumeMax = Mathf.Max( 0f, sipVolumeMax );
	}
}
