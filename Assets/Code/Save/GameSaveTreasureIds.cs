using System.Collections.Generic;

using UnityEngine;

public static class GameSaveTreasureIds
{
	static Dictionary<string, TreasureDefinition> _byId;

	public static void InvalidateCache()
	{
		_byId = null;
	}

	public static TreasureDefinition Resolve( string id )
	{
		if ( string.IsNullOrEmpty( id ) )
			return null;

		EnsureCache();
		if ( _byId.TryGetValue( id, out TreasureDefinition def ) )
			return def;
		return null;
	}

	public static string GetId( TreasureDefinition definition )
	{
		if ( definition == null )
			return null;
		if ( !string.IsNullOrEmpty( definition.id ) )
			return definition.id;
		return definition.name;
	}

	static void EnsureCache()
	{
		if ( _byId != null )
			return;

		_byId = new Dictionary<string, TreasureDefinition>( 256 );
		TreasureDefinition[] all = Resources.FindObjectsOfTypeAll<TreasureDefinition>();
		if ( all == null )
			return;

		for ( int i = 0; i < all.Length; i++ )
		{
			TreasureDefinition def = all[ i ];
			if ( def == null )
				continue;
			string key = GetId( def );
			if ( string.IsNullOrEmpty( key ) )
				continue;
			if ( !_byId.ContainsKey( key ) )
				_byId[ key ] = def;
		}
	}
}
