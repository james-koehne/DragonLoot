using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Flat catalog of active one-shot world events.
/// Asset name must be <c>WorldEventCatalogDefinition</c> for <see cref="GameInstance.GetDefinition{T}"/>.
/// </summary>
[CreateAssetMenu( fileName = "WorldEventCatalogDefinition", menuName = "Definitions/WorldEventCatalogDefinition" )]
public class WorldEventCatalogDefinition : ScriptableObject
{
	public List<WorldEventDefinition> events = new List<WorldEventDefinition>();

	public int Count => events != null ? events.Count : 0;

	public WorldEventDefinition GetAt( int index )
	{
		if ( events == null || index < 0 || index >= events.Count )
			return null;
		return events[ index ];
	}

	public bool TryGetById( string eventId, out WorldEventDefinition definition )
	{
		definition = null;
		if ( string.IsNullOrEmpty( eventId ) || events == null )
			return false;

		for ( int i = 0; i < events.Count; i++ )
		{
			WorldEventDefinition entry = events[ i ];
			if ( entry == null || entry.id != eventId )
				continue;
			definition = entry;
			return true;
		}

		return false;
	}
}
