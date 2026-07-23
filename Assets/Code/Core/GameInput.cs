using UnityEngine;
using UnityEngine.InputSystem;

public sealed class GameInput : System.IDisposable
{
	readonly InputActionMap _game;

	public InputAction PointerPosition { get; }
	public InputAction Move { get; }
	public InputAction Jump { get; }
	public InputAction Sprint { get; }
	public InputAction CameraDelta { get; }
	public InputAction Interact { get; }
	public InputAction SecondaryInteract { get; }
	public InputAction ScrollWheel { get; }

	public GameInput( InputActionAsset asset )
	{
		_game = asset.FindActionMap( "Game", throwIfNotFound: true );

		PointerPosition = _game.FindAction( "PointerPosition", throwIfNotFound: true );
		Move = _game.FindAction( "Move", throwIfNotFound: true );
		Jump = _game.FindAction( "Jump", throwIfNotFound: true );
		Sprint = _game.FindAction( "Sprint", throwIfNotFound: true );
		CameraDelta = _game.FindAction( "CameraDelta", throwIfNotFound: true );
		Interact = _game.FindAction( "Interact", throwIfNotFound: true );
		SecondaryInteract = _game.FindAction( "SecondaryInteract", throwIfNotFound: true );
		ScrollWheel = _game.FindAction( "ScrollWheel", throwIfNotFound: true );
	}

	public void Enable()
	{
		_game.Enable();
	}

	public void Disable()
	{
		_game.Disable();
	}

	public Vector2 GetPointerScreenPosition()
	{
		return PointerPosition.ReadValue<Vector2>();
	}

	public void Dispose()
	{
		Disable();
	}
}
