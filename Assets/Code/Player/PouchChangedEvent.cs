using UnityEngine;

public enum PouchChangeReason
{
	Switched,
	Reselected,
	ContentsAdded
}

public struct PouchChangedEvent
{
	public CarryBucketKind Bucket;
	public PlayerCarry Carry;
	public PouchChangeReason Reason;
}
