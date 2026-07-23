using System;

/// <summary>
/// Integer chunk index in the treasure surface grid.
/// </summary>
public readonly struct TreasureChunkCoord : IEquatable<TreasureChunkCoord>
{
	public readonly int X;
	public readonly int Z;

	public TreasureChunkCoord( int x, int z )
	{
		X = x;
		Z = z;
	}

	public bool Equals( TreasureChunkCoord other )
	{
		return X == other.X && Z == other.Z;
	}

	public override bool Equals( object obj )
	{
		return obj is TreasureChunkCoord other && Equals( other );
	}

	public override int GetHashCode()
	{
		unchecked
		{
			return ( X * 397 ) ^ Z;
		}
	}

	public override string ToString()
	{
		return $"({X},{Z})";
	}
}
