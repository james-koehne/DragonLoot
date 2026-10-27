using UnityEngine;

public enum PouchChangeReason
{
	Switched,
	Reselected,
	ContentsAdded,
	/// <summary>Player cycled pouches via CyclePouch (not a direct category slot).</summary>
	Cycled
}

public struct PouchChangedEvent
{
	public CarryBucketKind Bucket;
	public PlayerCarry Carry;
	public PouchChangeReason Reason;
}
