using UnityEngine;

public class GemInteractable : PickupInteractable
{
	protected override void Reset()
	{
		EnsureFallbackName( "Gem" );
	}

	protected override void Awake()
	{
		EnsureFallbackName( "Gem" );
	}
}
