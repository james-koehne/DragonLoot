using UnityEngine;

/// <summary>
/// Sip charges on a filled coffee cup. Station enables pickup after brew.
/// </summary>
public class CoffeeCupState : MonoBehaviour
{
	[SerializeField]
	[Min( 1 )]
	int maxSips = 4;

	int _remainingSips;
	bool _pickupEnabled;

	public int MaxSips => Mathf.Max( 1, maxSips );
	public int RemainingSips => _remainingSips;
	public bool HasSipsRemaining => _remainingSips > 0;
	public bool PickupEnabled => _pickupEnabled;

	void Awake()
	{
		if ( _remainingSips <= 0 )
			_remainingSips = MaxSips;
	}

	public void PrepareFilledCup()
	{
		_remainingSips = MaxSips;
		_pickupEnabled = true;
	}

	public void DisablePickup()
	{
		_pickupEnabled = false;
	}

	public bool TrySip()
	{
		if ( _remainingSips <= 0 )
			return false;

		_remainingSips--;
		return true;
	}

	public void ResetSips()
	{
		_remainingSips = MaxSips;
	}
}
