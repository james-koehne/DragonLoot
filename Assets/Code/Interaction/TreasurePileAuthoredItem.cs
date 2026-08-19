using UnityEngine;

/// <summary>
/// Marks a scene-authored large prop as curated pile loot (artifacts, chests, keys, etc.).
/// Poses stay on the scene object; bake/runtime treat them as pinned occupancy.
/// The Addressable *Visual prefab is enough in the editor — assign <see cref="definition"/>.
/// </summary>
[DisallowMultipleComponent]
public class TreasurePileAuthoredItem : MonoBehaviour
{
	[SerializeField]
	TreasureDefinition definition;

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
			if ( definition != null )
				return definition;

			TreasureItem item = Item;
			return item != null ? item.Definition : null;
		}
	}

	void OnValidate()
	{
		if ( treasureItem == null )
			treasureItem = GetComponent<TreasureItem>();
		if ( definition == null && treasureItem != null )
			definition = treasureItem.Definition;
	}

	public void BindItem( TreasureItem item )
	{
		treasureItem = item;
		if ( definition == null && item != null )
			definition = item.Definition;
	}

	public void BindDefinition( TreasureDefinition treasure )
	{
		definition = treasure;
	}

	public Bounds GetWorldBounds()
	{
		return TreasureItem.GetCombinedRendererWorldBounds( transform, transform.position );
	}

	public static bool IsCuratable( TreasureDefinition treasure )
	{
		if ( treasure == null )
			return false;
		if ( !GoldPileArtifactProps.IsLargeProp( treasure ) )
			return false;
		if ( treasure.category == TreasureCategory.Gem )
			return false;
		return true;
	}

	public bool IsValidCurated()
	{
		return IsCuratable( Definition );
	}
}
