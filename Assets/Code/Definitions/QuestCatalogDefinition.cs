using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Ordered linear quest chain for the level.
/// Asset name must be <c>QuestCatalogDefinition</c> for <see cref="GameInstance.GetDefinition{T}"/>.
/// </summary>
[CreateAssetMenu( fileName = "QuestCatalogDefinition", menuName = "Definitions/QuestCatalogDefinition" )]
public class QuestCatalogDefinition : ScriptableObject
{
	public List<QuestDefinition> quests = new List<QuestDefinition>();

	public int Count => quests != null ? quests.Count : 0;

	public QuestDefinition GetAt( int index )
	{
		if ( quests == null || index < 0 || index >= quests.Count )
			return null;
		return quests[ index ];
	}

	public int IndexOfId( string questId )
	{
		if ( string.IsNullOrEmpty( questId ) || quests == null )
			return -1;

		for ( int i = 0; i < quests.Count; i++ )
		{
			QuestDefinition quest = quests[ i ];
			if ( quest != null && quest.id == questId )
				return i;
		}

		return -1;
	}

	public bool TryGetById( string questId, out QuestDefinition definition )
	{
		int index = IndexOfId( questId );
		if ( index < 0 )
		{
			definition = null;
			return false;
		}

		definition = quests[ index ];
		return definition != null;
	}
}
