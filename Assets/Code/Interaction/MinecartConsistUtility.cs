using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

/// <summary>
/// Runtime spawn of locked consist cars (debug overlay / play-mode inspector).
/// </summary>
public static class MinecartConsistUtility
{
	public const string CargoAddress = "Minecart/Minecart";
	public const string DriveAddress = "Minecart/MinecartDrive";
	public const string CallPostAddress = "Minecart/CallPost";

	public static bool TryAddCar( MinecartInteractable lead, bool drive, out MinecartInteractable spawned )
	{
		spawned = null;
		if ( lead == null )
			return false;

		lead = lead.ConsistLead;
		string key = drive ? DriveAddress : CargoAddress;
		AsyncOperationHandle<GameObject> handle = Addressables.InstantiateAsync( key );
		GameObject go = handle.WaitForCompletion();
		if ( go == null )
			return false;

		spawned = go.GetComponent<MinecartInteractable>();
		if ( spawned == null )
		{
			Addressables.ReleaseInstance( go );
			return false;
		}

		if ( !lead.TryAttachFollower( spawned ) )
		{
			Addressables.ReleaseInstance( go );
			spawned = null;
			return false;
		}

		return true;
	}
}
