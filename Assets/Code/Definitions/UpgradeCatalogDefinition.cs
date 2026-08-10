using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Ordered upgrade catalog. Asset name must be <c>UpgradeCatalogDefinition</c>
/// for <see cref="GameInstance.GetDefinition{T}"/>.
/// </summary>
[CreateAssetMenu( fileName = "UpgradeCatalogDefinition", menuName = "Definitions/UpgradeCatalogDefinition" )]
public class UpgradeCatalogDefinition : ScriptableObject
{
	public List<UpgradeDefinition> upgrades = new List<UpgradeDefinition>();

	public int Count => upgrades != null ? upgrades.Count : 0;

	public UpgradeDefinition GetById( string id )
	{
		if ( string.IsNullOrEmpty( id ) || upgrades == null )
			return null;

		for ( int i = 0; i < upgrades.Count; i++ )
		{
			UpgradeDefinition def = upgrades[ i ];
			if ( def != null && def.id == id )
				return def;
		}

		return null;
	}

	public bool TryGetById( string id, out UpgradeDefinition definition )
	{
		definition = GetById( id );
		return definition != null;
	}
}
