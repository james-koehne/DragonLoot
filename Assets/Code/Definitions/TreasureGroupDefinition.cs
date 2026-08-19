using System;

using UnityEngine;

[Serializable]
public struct TreasureGroupEntry
{
	public TreasureDefinition treasure;

	[Min( 0 )]
	public int count;
}

[CreateAssetMenu( fileName = "TreasureGroupDefinition", menuName = "Definitions/TreasureGroupDefinition" )]
public class TreasureGroupDefinition : ScriptableObject
{
	[Header( "Identity" )]
	public string displayName = "Treasure Group";

	[Header( "Contents" )]
	public TreasureGroupEntry[] entries;

	public int TotalUnits()
	{
		return SumUnits( entries );
	}

	public int TotalCoinUnits()
	{
		return SumUnitsByCategory( entries, TreasureCategory.Coin );
	}

	public int TotalNonCoinUnits()
	{
		int total = TotalUnits();
		return total - TotalCoinUnits();
	}

	public int ComputeContentFingerprint()
	{
		unchecked
		{
			uint h = 2166136261u;
			if ( entries == null )
				return ( int )h;

			for ( int i = 0; i < entries.Length; i++ )
			{
				TreasureGroupEntry entry = entries[ i ];
				TreasureDefinition def = entry.treasure;
				if ( def == null || entry.count <= 0 )
					continue;

				h = MixStableString( h, def.name );
				h = ( h ^ ( uint )( int )def.category ) * 16777619u;
				h = ( h ^ ( uint )entry.count ) * 16777619u;
			}

			return ( int )h;
		}
	}

	public TreasureGroupEntry[] GetCoinEntries()
	{
		return FilterEntries( entries, TreasureCategory.Coin );
	}

	public TreasureGroupEntry[] GetNonCoinEntries()
	{
		if ( entries == null || entries.Length == 0 )
			return Array.Empty<TreasureGroupEntry>();

		int count = 0;
		for ( int i = 0; i < entries.Length; i++ )
		{
			TreasureGroupEntry entry = entries[ i ];
			if ( entry.treasure != null && entry.count > 0 && entry.treasure.category != TreasureCategory.Coin )
				count++;
		}

		if ( count == 0 )
			return Array.Empty<TreasureGroupEntry>();

		TreasureGroupEntry[] result = new TreasureGroupEntry[ count ];
		int write = 0;
		for ( int i = 0; i < entries.Length; i++ )
		{
			TreasureGroupEntry entry = entries[ i ];
			if ( entry.treasure != null && entry.count > 0 && entry.treasure.category != TreasureCategory.Coin )
				result[ write++ ] = entry;
		}

		return result;
	}

	public static int[] ComputeLargestRemainderQuotas( TreasureGroupEntry[] source, int budget )
	{
		if ( source == null || source.Length == 0 )
			return Array.Empty<int>();

		int cap = Mathf.Max( 0, budget );
		int totalWeight = 0;
		for ( int i = 0; i < source.Length; i++ )
		{
			TreasureGroupEntry entry = source[ i ];
			if ( entry.treasure != null && entry.count > 0 )
				totalWeight += entry.count;
		}

		int[] targets = new int[ source.Length ];
		if ( totalWeight <= 0 || cap <= 0 )
			return targets;

		int assigned = 0;
		float[] remainders = new float[ source.Length ];
		for ( int i = 0; i < source.Length; i++ )
		{
			TreasureGroupEntry entry = source[ i ];
			if ( entry.treasure == null || entry.count <= 0 )
				continue;

			float exact = ( entry.count / ( float )totalWeight ) * cap;
			int floor = Mathf.FloorToInt( exact );
			targets[ i ] = floor;
			remainders[ i ] = exact - floor;
			assigned += floor;
		}

		int leftover = cap - assigned;
		while ( leftover > 0 )
		{
			int best = -1;
			float bestRem = -1f;
			for ( int i = 0; i < source.Length; i++ )
			{
				TreasureGroupEntry entry = source[ i ];
				if ( entry.treasure == null || entry.count <= 0 )
					continue;
				if ( remainders[ i ] > bestRem )
				{
					bestRem = remainders[ i ];
					best = i;
				}
			}

			if ( best < 0 )
				break;

			targets[ best ]++;
			remainders[ best ] = -1f;
			leftover--;
		}

		return targets;
	}

	static int SumUnits( TreasureGroupEntry[] source )
	{
		if ( source == null )
			return 0;

		int total = 0;
		for ( int i = 0; i < source.Length; i++ )
		{
			if ( source[ i ].treasure != null )
				total += Mathf.Max( 0, source[ i ].count );
		}

		return total;
	}

	static int SumUnitsByCategory( TreasureGroupEntry[] source, TreasureCategory category )
	{
		if ( source == null )
			return 0;

		int total = 0;
		for ( int i = 0; i < source.Length; i++ )
		{
			TreasureGroupEntry entry = source[ i ];
			if ( entry.treasure != null && entry.treasure.category == category )
				total += Mathf.Max( 0, entry.count );
		}

		return total;
	}

	static TreasureGroupEntry[] FilterEntries( TreasureGroupEntry[] source, TreasureCategory category )
	{
		if ( source == null || source.Length == 0 )
			return Array.Empty<TreasureGroupEntry>();

		int count = 0;
		for ( int i = 0; i < source.Length; i++ )
		{
			TreasureGroupEntry entry = source[ i ];
			if ( entry.treasure != null && entry.count > 0 && entry.treasure.category == category )
				count++;
		}

		if ( count == 0 )
			return Array.Empty<TreasureGroupEntry>();

		TreasureGroupEntry[] result = new TreasureGroupEntry[ count ];
		int write = 0;
		for ( int i = 0; i < source.Length; i++ )
		{
			TreasureGroupEntry entry = source[ i ];
			if ( entry.treasure != null && entry.count > 0 && entry.treasure.category == category )
				result[ write++ ] = entry;
		}

		return result;
	}

	static uint MixStableString( uint h, string value )
	{
		unchecked
		{
			if ( string.IsNullOrEmpty( value ) )
				return h;

			for ( int i = 0; i < value.Length; i++ )
				h = ( h ^ value[ i ] ) * 16777619u;
			return h;
		}
	}
}
