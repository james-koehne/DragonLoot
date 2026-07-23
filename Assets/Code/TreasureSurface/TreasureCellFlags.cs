using System;

[Flags]
public enum TreasureCellFlags : byte
{
	None = 0,
	Traversable = 1 << 0,
	Stable = 1 << 1
}
