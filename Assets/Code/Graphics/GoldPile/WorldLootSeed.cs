using UnityEngine;

/// <summary>
/// Scene/level-wide loot seed from <see cref="CoreDefinition"/>, mixed with a stable pile identity.
/// Never uses instance IDs, world position, or <see cref="string.GetHashCode"/> (non-deterministic).
/// </summary>
public static class WorldLootSeed
{
	const int FallbackSeed = 1;

	public static int GetWorldSeed()
	{
		CoreDefinition core = null;
		if ( GameMode.Instance != null )
			core = GameMode.Instance.CoreDefinition;

		if ( core == null )
			core = GameInstance.GetDefinition<CoreDefinition>();

		if ( core == null )
			return FallbackSeed;

		return core.lootWorldSeed == 0 ? FallbackSeed : core.lootWorldSeed;
	}

	/// <summary>
	/// Stable per-pile seed. Prefer an authored layout seed; otherwise FNV of hierarchy path.
	/// </summary>
	public static int GetPileEffectiveSeed( Transform pileRoot, int authoredLayoutSeed = 0 )
	{
		int worldSeed = GetWorldSeed();
		int pileId = authoredLayoutSeed != 0
			? authoredLayoutSeed
			: HashString( BuildHierarchyPath( pileRoot ) );

		return Mix( worldSeed, pileId );
	}

	public static int StableUnitIndex( int entryIndex, int unitInEntry )
	{
		return Mix( entryIndex + 1, unitInEntry + 1 );
	}

	public static int Mix( int a, int b )
	{
		return GoldPileChunkGrid.HashChunkSeed( a == 0 ? FallbackSeed : a, b, b * 397 );
	}

	public static int HashString( string value )
	{
		if ( string.IsNullOrEmpty( value ) )
			return FallbackSeed;

		unchecked
		{
			uint h = 2166136261u;
			for ( int i = 0; i < value.Length; i++ )
			{
				h ^= value[ i ];
				h *= 16777619u;
			}

			int result = ( int )h;
			return result == 0 ? FallbackSeed : result;
		}
	}

	public static string BuildHierarchyPath( Transform root )
	{
		if ( root == null )
			return "null";

		System.Text.StringBuilder sb = new System.Text.StringBuilder( 64 );
		Transform t = root;
		while ( t != null )
		{
			if ( sb.Length > 0 )
				sb.Insert( 0, '/' );
			sb.Insert( 0, t.name );
			t = t.parent;
		}

		return sb.ToString();
	}
}
