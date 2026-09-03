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

	[Tooltip( "Whether remainder latents were seated near the mound surface (from TreasurePileLatentBakeSettings)." )]
	public bool bakedNearSurface;

	[Tooltip( "Fingerprint of curated authored props (definitions + local poses) at bake time." )]
	public int authoredFingerprint;

	/// <summary>
	/// Hidden from inspectors — expanding thousands of entries crashes / freezes the editor.
	/// </summary>
	[HideInInspector]
	public Pose[] poses;

	public int PoseCount => poses != null ? poses.Length : 0;

	public bool MatchesFingerprint(
		int layoutSeed,
		int heightFp,
		int contentsFp,
		int volumeAttempts,
		bool spatialHash,
		bool avoidCoins,
		int authoredFp = 0,
		bool nearSurface = false )
	{
		if ( authoredLayoutSeed != layoutSeed )
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
		if ( authoredFingerprint != authoredFp )
			return false;
		if ( bakedNearSurface != nearSurface )
			return false;
		return poses != null;
	}

	public string DescribeFingerprintMismatch(
		int layoutSeed,
		int heightFp,
		int contentsFp,
		int volumeAttempts,
		bool spatialHash,
		bool avoidCoins,
		int authoredFp = 0,
		bool nearSurface = false )
	{
		if ( poses == null )
			return "missing poses";

		string reason = null;
		if ( authoredLayoutSeed != layoutSeed )
			reason = AppendMismatch( reason, $"pile seed (bake {authoredLayoutSeed}, this pile {layoutSeed})" );
		if ( heightFingerprint != heightFp )
			reason = AppendMismatch( reason, "mound height" );
		if ( contentsFingerprint != contentsFp )
			reason = AppendMismatch( reason, "treasure contents" );
		if ( volumeMaxAttempts != volumeAttempts )
			reason = AppendMismatch( reason, "volume attempts" );
		if ( usedSpatialHash != spatialHash )
			reason = AppendMismatch( reason, "spatial hash" );
		if ( avoidedCoinSeats != avoidCoins )
			reason = AppendMismatch( reason, "coin-seat avoidance" );
		if ( authoredFingerprint != authoredFp )
			reason = AppendMismatch( reason, "authored props" );
		if ( bakedNearSurface != nearSurface )
			reason = AppendMismatch( reason, "near-surface setting" );
		return reason;
	}

	static string AppendMismatch( string reason, string part )
	{
		if ( reason == null || reason.Length == 0 )
			return part;
		return reason + "; " + part;
	}
}
