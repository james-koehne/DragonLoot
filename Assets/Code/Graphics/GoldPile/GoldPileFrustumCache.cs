using UnityEngine;

/// <summary>
/// Frame-keyed frustum planes so loot streaming and artifact props share one CalculateFrustumPlanes.
/// </summary>
public static class GoldPileFrustumCache
{
	static readonly Plane[] Planes = new Plane[ 6 ];
	static int s_frame = -1;
	static int s_cameraId;

	public static bool TryGet( Camera camera, out Plane[] planes )
	{
		planes = Planes;
		if ( camera == null )
			return false;

		int frame = Time.frameCount;
		int camId = camera.GetInstanceID();
		if ( frame == s_frame && camId == s_cameraId )
			return true;

		GeometryUtility.CalculateFrustumPlanes( camera, Planes );
		s_frame = frame;
		s_cameraId = camId;
		return true;
	}
}
