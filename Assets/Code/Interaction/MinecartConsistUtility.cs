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

	const float DefaultSpawnTrackRadius = 24f;

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

	/// <summary>
	/// Spawn a standalone cargo (auto) or drive cart on the nearest travel-ready track to <paramref name="nearWorld"/>.
	/// </summary>
	public static bool TrySpawnOnNearestTrack( Vector3 nearWorld, bool drive, out MinecartInteractable spawned, float maxRadius = DefaultSpawnTrackRadius )
	{
		spawned = null;
		MinecartTrack track;
		float along;
		if ( !MinecartTrack.TryGetNearest( nearWorld, maxRadius, out track, out along ) )
			return false;

		Vector3 position;
		Vector3 tangent;
		Vector3 up;
		if ( !track.Evaluate( along, out position, out tangent, out up ) )
			return false;

		Quaternion rotation = tangent.sqrMagnitude > 0.0001f
			? Quaternion.LookRotation( tangent.normalized, Vector3.up )
			: Quaternion.identity;

		string key = drive ? DriveAddress : CargoAddress;
		AsyncOperationHandle<GameObject> handle = Addressables.InstantiateAsync( key, position, rotation );
		GameObject go = handle.WaitForCompletion();
		if ( go == null )
			return false;

		spawned = go.GetComponent<MinecartInteractable>();
		if ( spawned == null )
		{
			Addressables.ReleaseInstance( go );
			return false;
		}

		spawned.BindToTrack( track, along, tangent );
		return true;
	}
}