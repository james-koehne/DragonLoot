using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Flat catalog of contextual tutorials.
/// Asset name must be <c>TutorialCatalogDefinition</c> for <see cref="GameInstance.GetDefinition{T}"/>.
/// </summary>
[CreateAssetMenu( fileName = "TutorialCatalogDefinition", menuName = "Definitions/TutorialCatalogDefinition" )]
public class TutorialCatalogDefinition : ScriptableObject
{
	public List<TutorialDefinition> tutorials = new List<TutorialDefinition>();

	public int Count => tutorials != null ? tutorials.Count : 0;

	public TutorialDefinition GetAt( int index )
	{
		if ( tutorials == null || index < 0 || index >= tutorials.Count )
			return null;
		return tutorials[ index ];
	}

	public bool TryGetById( string tutorialId, out TutorialDefinition definition )
	{
		definition = null;
		if ( string.IsNullOrEmpty( tutorialId ) || tutorials == null )
			return false;

		for ( int i = 0; i < tutorials.Count; i++ )
		{
			TutorialDefinition entry = tutorials[ i ];
			if ( entry == null || entry.id != tutorialId )
				continue;
			definition = entry;
			return true;
		}

		return false;
	}
}
