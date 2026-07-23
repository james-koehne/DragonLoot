using UnityEngine;

/// <summary>
/// How many pile units one interact removes (carve + loot). Upgrades raise <see cref="UnitsPerInteract"/>.
/// </summary>
[DisallowMultipleComponent]
public class PlayerTreasurePilePull : MonoBehaviour
{
	[SerializeField]
	int unitsPerInteract = 1;

	public int UnitsPerInteract => Mathf.Max( 1, unitsPerInteract );

	public void SetUnitsPerInteract( int value )
	{
		unitsPerInteract = Mathf.Max( 1, value );
	}
}
