using UnityEngine;
using System.Collections.Generic;

public class FunkButtonGroup : MonoBehaviour
{
	private List<FunkButton> _buttons = new();

	public void Register( FunkButton button )
	{
		if ( !_buttons.Contains( button ) )
			_buttons.Add( button );
	}

	public void NotifyButtonSelected( FunkButton selected )
	{
		foreach ( FunkButton b in _buttons )
		{
			if ( b != selected )
				b.SetSelected( false, true );
		}
	}
}
