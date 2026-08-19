using UnityEngine;

/// <summary>
/// Identification and shared settings for interleaved gold-bar stacks.
/// </summary>
public static class GoldBarStack
{
	static GoldBarStackSettings _settings;

	public static GoldBarStackSettings Settings
	{
		get { return RuntimeDefinition.Resolve( ref _settings ); }
	}

	public static bool IsStackable( TreasureDefinition definition )
	{
		return definition != null && definition.usesInterleavedBarStack;
	}

	public static bool IsStackable( TreasureItem item )
	{
		return item != null && IsStackable( item.Definition );
	}

	public static bool AreSameType( TreasureDefinition a, TreasureDefinition b )
	{
		if ( a == b )
			return true;
		if ( a == null || b == null )
			return false;
		if ( !string.IsNullOrEmpty( a.id ) && a.id == b.id )
			return true;
		return false;
	}

	public static float ResolveJoinRadius( TreasureDefinition definition )
	{
		GoldBarStackSettings settings = Settings;
		float radius = settings != null ? settings.joinRadius : GoldBarStackSettings.DefaultJoinRadius;
		GoldBarStackLattice.GoldBarSize size = GoldBarStackLattice.Measure( definition, null );
		float footprint = Mathf.Max( size.Length, size.Width * 2f );
		return Mathf.Max( radius, footprint * 0.65f );
	}

	public static int GroundMaxHeight
	{
		get
		{
			GoldBarStackSettings settings = Settings;
			if ( settings != null && settings.groundMaxStackHeight > 0 )
				return settings.groundMaxStackHeight;
			return GoldBarStackSettings.DefaultMaxHeight;
		}
	}
}
