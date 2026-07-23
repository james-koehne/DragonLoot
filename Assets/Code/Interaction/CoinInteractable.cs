using UnityEngine;

public class CoinInteractable : PickupInteractable
{
	protected override void Reset()
	{
		EnsureFallbackName( "Coin" );
	}

	protected override void Awake()
	{
		EnsureFallbackName( "Coin" );
	}
}
