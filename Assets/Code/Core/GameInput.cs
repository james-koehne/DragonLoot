using UnityEngine;
using UnityEngine.InputSystem;

public sealed class GameInput : System.IDisposable
{
	public const int AbilitySlotCount = 4;
	public const int CategorySlotCount = 3;

	readonly InputActionMap _game;

	public InputAction PointerPosition { get; }
	public InputAction Move { get; }
	public InputAction Jump { get; }
	public InputAction Sprint { get; }
	public InputAction CameraDelta { get; }
	public InputAction Interact { get; }
	public InputAction SecondaryInteract { get; }
	public InputAction Clean { get; }
	public InputAction WholeStackPickup { get; }
	public InputAction WholeStackPlace { get; }
	public InputAction ScrollWheel { get; }
	public InputAction RotateLeft { get; }
	public InputAction RotateRight { get; }
	public InputAction[] AbilitySlots { get; }
	public InputAction[] CategorySlots { get; }

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
		Clean = _game.FindAction( "Clean", throwIfNotFound: false );
		WholeStackPickup = _game.FindAction( "WholeStackPickup", throwIfNotFound: true );
		WholeStackPlace = _game.FindAction( "WholeStackPlace", throwIfNotFound: true );
		ScrollWheel = _game.FindAction( "ScrollWheel", throwIfNotFound: true );
		RotateLeft = _game.FindAction( "RotateLeft", throwIfNotFound: false );
		RotateRight = _game.FindAction( "RotateRight", throwIfNotFound: false );

		AbilitySlots = new InputAction[ AbilitySlotCount ];
		for ( int i = 0; i < AbilitySlotCount; i++ )
			AbilitySlots[ i ] = _game.FindAction( "AbilitySlot" + ( i + 1 ), throwIfNotFound: false );

		CategorySlots = new InputAction[ CategorySlotCount ];
		for ( int i = 0; i < CategorySlotCount; i++ )
			CategorySlots[ i ] = _game.FindAction( "CategorySlot" + ( i + 1 ), throwIfNotFound: true );
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
