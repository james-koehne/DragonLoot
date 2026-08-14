using System;

using UnityEngine;

/// <summary>
/// Pure-data baked latent artifact poses for a treasure pile.
/// No gizmos or preview — safe to select in the Project window.
/// </summary>
[CreateAssetMenu( fileName = "TreasurePileLatentBake", menuName = "Definitions/TreasurePileLatentBake" )]
public class TreasurePileLatentBake : ScriptableObject
{
	[Serializable]
	public struct Pose
	{
		public TreasureDefinition definition;
		public Vector3 localPos;
		public Quaternion localRot;
		public float scale;
		public Vector3 boundsCenter;
		public Vector3 boundsSize;
	}

	[Tooltip( "Authored layout seed used when this bake was generated (0 = path-derived)." )]
	public int authoredLayoutSeed;

	[Tooltip( "Effective pile seed (world × layout) at bake time." )]
	public int effectivePileSeed;

	[Tooltip( "Heightfield layout fingerprint at bake time." )]
	public int heightFingerprint;

	[Tooltip( "Hash of treasureContents ids/counts at bake time." )]
	public int contentsFingerprint;

	[Tooltip( "Definition display name / asset name recorded at bake (informational)." )]
	public string sourceDefinitionName;

	[Tooltip( "Latent volume max attempts used when baking (informational / mismatch hint)." )]
	public int volumeMaxAttempts;

	[Tooltip( "Whether spatial hash was enabled when baking." )]
	public bool usedSpatialHash;

	[Tooltip( "Whether coin seat avoidance was enabled when baking." )]
	public bool avoidedCoinSeats;

	/// <summary>
	/// Hidden from inspectors — expanding thousands of entries crashes / freezes the editor.
	/// </summary>
	[HideInInspector]
	public Pose[] poses;

	public int PoseCount => poses != null ? poses.Length : 0;

	public bool MatchesFingerprint(
		int effectiveSeed,
		int heightFp,
		int contentsFp,
		int volumeAttempts,
		bool spatialHash,
		bool avoidCoins )
	{
		if ( effectivePileSeed != effectiveSeed )
			return false;
		if ( heightFingerprint != heightFp )
			return false;
		if ( contentsFingerprint != contentsFp )
			return false;
		if ( volumeMaxAttempts != volumeAttempts )
			return false;
		if ( usedSpatialHash != spatialHash )
			return false;
		if ( avoidedCoinSeats != avoidCoins )
			return false;
		return poses != null;
	}
}
