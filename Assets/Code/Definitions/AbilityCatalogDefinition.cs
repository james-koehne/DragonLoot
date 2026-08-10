using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Ordered ability catalog. Asset name must be <c>AbilityCatalogDefinition</c>
/// for <see cref="GameInstance.GetDefinition{T}"/>.
/// </summary>
[CreateAssetMenu( fileName = "AbilityCatalogDefinition", menuName = "Definitions/AbilityCatalogDefinition" )]
public class AbilityCatalogDefinition : ScriptableObject
{
	public List<AbilityDefinition> abilities = new List<AbilityDefinition>();

	public int Count => abilities != null ? abilities.Count : 0;

	public AbilityDefinition GetById( string id )
	{
		if ( string.IsNullOrEmpty( id ) || abilities == null )
			return null;

		for ( int i = 0; i < abilities.Count; i++ )
		{
			AbilityDefinition def = abilities[ i ];
			if ( def != null && def.id == id )
				return def;
		}

		return null;
	}

	public bool TryGetById( string id, out AbilityDefinition definition )
	{
		definition = GetById( id );
		return definition != null;
	}
}
