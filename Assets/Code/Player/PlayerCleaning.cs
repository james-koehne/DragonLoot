using UnityEngine;

/// <summary>
/// Hold Clean (E) while carrying a dirty artifact to polish it. Dirt MPB is the only feedback.
/// </summary>
public class PlayerCleaning : MonoBehaviour
{
	PlayerController _player;
	TreasureCleaningDefinition _cleaningDefinition;
	bool _inputEnabled = true;

	public float CleaningProgress
	{
		get
		{
			if ( _player == null || _player.Carry == null )
				return 0f;
			if ( !_player.Carry.TryPeekActive( out TreasureItem item ) || item == null )
				return 0f;
			return item.CleanProgress;
		}
	}

	public void Setup( PlayerController player )
	{
		_player = player;
	}

	public void SetInputEnabled( bool enabled )
	{
		_inputEnabled = enabled;
	}

	void Update()
	{
		// Clean is unbound this pass (E is whole-stack pickup). Manual polish disabled until rebound.
		if ( !_inputEnabled || _player == null )
			return;

		GameInput input = GetGameInput();
		if ( input == null || input.Clean == null )
			return;

		if ( !input.Clean.IsPressed() )
			return;

		PlayerCarry carry = _player.Carry;
		if ( carry == null || !carry.TryPeekActive( out TreasureItem item ) || item == null )
			return;

		if ( !item.IsDirty )
			return;

		TreasureCleaningDefinition def = RuntimeDefinition.Resolve( ref _cleaningDefinition );
		float duration = RuntimeDefinition.Get( def, d => d.manualCleanDurationSeconds, 2.5f );
		if ( duration < 0.01f )
			duration = 0.01f;

		item.ApplyCleaning( Time.deltaTime / duration );
	}

	static GameInput GetGameInput()
	{
		return InputController.Instance != null ? InputController.Instance.GameInput : null;
	}
}
