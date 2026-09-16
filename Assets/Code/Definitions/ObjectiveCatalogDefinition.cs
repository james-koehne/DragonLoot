using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Flat catalog of active objectives.
/// Asset name must be <c>ObjectiveCatalogDefinition</c> for <see cref="GameInstance.GetDefinition{T}"/>.
/// </summary>
[CreateAssetMenu( fileName = "ObjectiveCatalogDefinition", menuName = "Definitions/ObjectiveCatalogDefinition" )]
public class ObjectiveCatalogDefinition : ScriptableObject
{
	public List<ObjectiveDefinition> objectives = new List<ObjectiveDefinition>();

	public int Count => objectives != null ? objectives.Count : 0;

	public ObjectiveDefinition GetAt( int index )
	{
		if ( objectives == null || index < 0 || index >= objectives.Count )
			return null;
		return objectives[ index ];
	}

	public bool TryGetById( string objectiveId, out ObjectiveDefinition definition )
	{
		definition = null;
		if ( string.IsNullOrEmpty( objectiveId ) || objectives == null )
			return false;

		for ( int i = 0; i < objectives.Count; i++ )
		{
			ObjectiveDefinition entry = objectives[ i ];
			if ( entry == null || entry.id != objectiveId )
				continue;
			definition = entry;
			return true;
		}

		return false;
	}
}
