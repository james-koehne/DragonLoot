using System;

using UnityEngine;

/// <summary>
/// Integer chunk index within a single treasure pile's grid.
/// </summary>
public readonly struct GoldPileChunkCoord : IEquatable<GoldPileChunkCoord>
{
	public readonly int X;
	public readonly int Z;

	public GoldPileChunkCoord( int x, int z )
	{
		X = x;
		Z = z;
	}

	public bool Equals( GoldPileChunkCoord other )
	{
		return X == other.X && Z == other.Z;
	}

	public override bool Equals( object obj )
	{
		return obj is GoldPileChunkCoord other && Equals( other );
	}

	public override int GetHashCode()
	{
		return ( X * 73856093 ) ^ ( Z * 19349663 );
	}

	public override string ToString()
	{
		return $"({X},{Z})";
	}

	public static bool operator ==( GoldPileChunkCoord a, GoldPileChunkCoord b )
	{
		return a.Equals( b );
	}

	public static bool operator !=( GoldPileChunkCoord a, GoldPileChunkCoord b )
	{
		return !a.Equals( b );
	}
}
