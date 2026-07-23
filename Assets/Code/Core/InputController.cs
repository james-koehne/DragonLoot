using UnityEngine;
using UnityEngine.InputSystem;

public class InputController : MonoBehaviour
{
	public static InputController Instance { get; private set; }

	[SerializeField] InputActionAsset inputActions;

	public GameInput GameInput { get; private set; }
	public bool InputEnabled { get; private set; } = true;

	public void Awake()
	{
		Instance = this;

		if ( inputActions == null )
		{
			Debug.LogError( $"{nameof( InputController )} is missing an {nameof( InputActionAsset )} reference.", this );
			return;
		}

		GameInput = new GameInput( Instantiate( inputActions ) );
		InputEnabled = false;
		SetInputEnabled( true );
	}

	void OnDestroy()
	{
		if ( Instance == this )
			Instance = null;

		GameInput?.Dispose();
		GameInput = null;
	}

	/// <summary>
	/// Enable or disable all game input. Use when e.g. the Steam virtual keyboard is open.
	/// </summary>
	public void SetInputEnabled( bool enabled )
	{
		if ( InputEnabled == enabled || GameInput == null )
			return;

		InputEnabled = enabled;

		if ( enabled )
			GameInput.Enable();
		else
			GameInput.Disable();
	}
}
