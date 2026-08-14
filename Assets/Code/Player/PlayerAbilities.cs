using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Polls ability slot keybinds and forwards activation to <see cref="AbilitySystem"/>.
/// </summary>
public class PlayerAbilities : MonoBehaviour
{
	PlayerController _player;
	AbilitySystem _system;
	bool _inputEnabled = true;

	public AbilitySystem System => _system;

	public void Setup( PlayerController player )
	{
		_player = player;
		_system = AbilitySystem.Ensure( player );
	}

	public void SetInputEnabled( bool enabled )
	{
		_inputEnabled = enabled;
	}

	void OnDestroy()
	{
		AbilitySystem.ClearInstanceIfOwner( _player );
	}

	void Update()
	{
		if ( _system == null )
			return;

		_system.Tick( Time.deltaTime );

		if ( !_inputEnabled )
			return;

		// Ability slot keys 1-3 are rebound to category switch this pass; leave slots unbound.
		GameInput input = GetGameInput();
		if ( input == null || input.AbilitySlots == null )
			return;

		InputAction[] slots = input.AbilitySlots;
		int count = Mathf.Min( slots.Length, AbilitySystem.SlotCount );
		for ( int i = 0; i < count; i++ )
		{
			InputAction action = slots[ i ];
			if ( action == null )
				continue;
			if ( !action.WasPressedThisFrame() )
				continue;

			_system.TryActivateSlot( i );
		}
	}

	static GameInput GetGameInput()
	{
		return InputController.Instance != null ? InputController.Instance.GameInput : null;
	}
}
