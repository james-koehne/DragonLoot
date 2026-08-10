/// <summary>
/// Receives a full minecart unload. Drop-to-world is the v1 implementation; workshop is stubbed.
/// </summary>
public interface IMinecartUnloadReceiver
{
	/// <summary>
	/// Attempts to accept and clear all cargo from <paramref name="cart"/>.
	/// Returns false if this receiver cannot handle the cart (cargo left unchanged).
	/// </summary>
	bool TryUnload( MinecartInteractable cart );
}
