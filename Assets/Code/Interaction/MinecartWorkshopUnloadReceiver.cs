using UnityEngine;

/// <summary>
/// Stub for future workshop input. Does not accept cargo yet — leave DropWorld as the default receiver.
/// </summary>
public class MinecartWorkshopUnloadReceiver : MonoBehaviour, IMinecartUnloadReceiver
{
	public bool TryUnload( MinecartInteractable cart )
	{
		// Workshop input pipeline is not implemented. Prefer MinecartDropWorldUnloadReceiver.
		return false;
	}
}
