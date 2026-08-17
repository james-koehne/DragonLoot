using UnityEngine;

/// <summary>
/// Marks a scene-authored large prop as curated pile loot (artifacts, chests, keys, etc.).
/// Poses stay on the scene object; bake/runtime treat them as pinned occupancy.
/// </summary>
[DisallowMultipleComponent]
public class TreasurePileAuthoredItem : MonoBehaviour
{
	[SerializeField]
	TreasureItem treasureItem;

	public TreasureItem Item
	{
		get
		{
			if ( treasureItem == null )
				treasureItem = GetComponent<TreasureItem>();
			return treasureItem;
		}
	}

	public TreasureDefinition Definition
	{
		get
		{
			TreasureItem item = Item;
			return item != null ? item.Definition : null;
		}
	}

	void OnValidate()
	{
		if ( treasureItem == null )
			treasureItem = GetComponent<TreasureItem>();
	}

	public void BindItem( TreasureItem item )
	{
		treasureItem = item;
	}

	public static bool IsCuratable( TreasureDefinition definition )
	{
		if ( definition == null )
			return false;
		if ( !GoldPileArtifactProps.IsLargeProp( definition ) )
			return false;
		if ( definition.category == TreasureCategory.Gem )
			return false;
		return true;
	}
}
