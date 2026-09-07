using System;

using UnityEngine;

/// <summary>
/// Pure-data baked GPU coin seats for a treasure pile.
/// No gizmos or preview — safe to select in the Project window.
/// </summary>
[CreateAssetMenu( fileName = "TreasurePileCoinSeatBake", menuName = "Definitions/TreasurePileCoinSeatBake" )]
public class TreasurePileCoinSeatBake : ScriptableObject
{
	[Serializable]
	public struct Seat
	{
		public TreasureDefinition definition;
		public int entryIndex;
		public Vector3 localPos;
		public Quaternion localRot;
		public float scale;
		public float embedDepth;
		public float placeSurfaceHeight;
		public float probeRadius;
		public bool fixedVolumePose;
		public bool coinVisual;
	}

	[Tooltip( "Authored layout seed used when this bake was generated (0 = path-derived)." )]
	public int authoredLayoutSeed;

	[Tooltip( "Effective pile seed (world × layout) at bake time." )]
	public int effectivePileSeed;

	[Tooltip( "Heightfield layout fingerprint at bake time." )]
	public int heightFingerprint;

	[Tooltip( "Hash of coinContents ids/counts + maxVisibleTotal at bake time." )]
	public int contentsFingerprint;

	[Tooltip( "Hash of placement-affecting stream / definition knobs at bake time." )]
	public int placementFingerprint;

	[Tooltip( "maxVisibleTotal at bake time." )]
	public int maxVisibleTotal;

	[Tooltip( "Steady near-surface budget at bake time." )]
	public int steadyVisibleBudget;

	[Tooltip( "Definition display name / asset name recorded at bake (informational)." )]
	public string sourceDefinitionName;

	/// <summary>
	/// Hidden from inspectors — expanding thousands of entries crashes / freezes the editor.
	/// </summary>
	[HideInInspector]
	public Seat[] seats;

	public int SeatCount => seats != null ? seats.Length : 0;

	public bool MatchesFingerprint(
		int layoutSeed,
		int heightFp,
		int contentsFp,
		int placementFp,
		int maxVisible,
		int steadyBudget )
	{
		if ( authoredLayoutSeed != layoutSeed )
			return false;
		if ( heightFingerprint != heightFp )
			return false;
		if ( contentsFingerprint != contentsFp )
			return false;
		if ( placementFingerprint != placementFp )
			return false;
		if ( maxVisibleTotal != maxVisible )
			return false;
		if ( steadyVisibleBudget != steadyBudget )
			return false;
		return seats != null;
	}

	public string DescribeFingerprintMismatch(
		int layoutSeed,
		int heightFp,
		int contentsFp,
		int placementFp,
		int maxVisible,
		int steadyBudget )
	{
		if ( seats == null )
			return "missing seats";

		string reason = null;
		if ( authoredLayoutSeed != layoutSeed )
			reason = AppendMismatch( reason, $"pile seed (bake {authoredLayoutSeed}, this pile {layoutSeed})" );
		if ( heightFingerprint != heightFp )
			reason = AppendMismatch( reason, "mound height" );
		if ( contentsFingerprint != contentsFp )
			reason = AppendMismatch( reason, "coin contents" );
		if ( placementFingerprint != placementFp )
			reason = AppendMismatch( reason, "coin placement settings" );
		if ( maxVisibleTotal != maxVisible )
			reason = AppendMismatch( reason, "maxVisibleTotal" );
		if ( steadyVisibleBudget != steadyBudget )
			reason = AppendMismatch( reason, "steady visible budget" );
		return reason;
	}

	static string AppendMismatch( string reason, string part )
	{
		if ( reason == null || reason.Length == 0 )
			return part;
		return reason + "; " + part;
	}
}
