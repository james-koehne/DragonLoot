using UnityEngine;

/// <summary>
/// Marks a scene-authored large prop as curated pile loot (artifacts, chests, keys, etc.).
/// Poses stay on the scene object; bake/runtime treat them as pinned occupancy.
/// The Addressable *Visual prefab (Collectable layer) is enough in the editor — assign <see cref="definition"/>.
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

	/// <summary>
	/// Treasure visual prefabs and pickups live on Collectable. Scene props on any other
	/// layer are not auto-absorbed as authored pile loot.
	/// </summary>
	public static bool IsOnCollectableLayer( GameObject go )
	{
		if ( go == null )
			return false;

		int collectable = PhysicsLayers.CollectableLayer;
		if ( collectable < 0 )
			return false;

		return go.layer == collectable;
	}

	/// <summary>
	/// Objects the editor absorber may parent under a pile's _AuthoredLoot.
	/// Explicit markers always qualify. Unmarked objects must be Collectable;
	/// a TreasureItem must also be curatable (no coins/gems).
	/// </summary>
	public static bool CanBecomeAuthoredLoot( GameObject go )
	{
		if ( go == null )
			return false;
		if ( go.GetComponent<TreasurePileAuthoredItem>() != null )
			return true;
		if ( !IsOnCollectableLayer( go ) )
			return false;

		TreasureItem item = go.GetComponent<TreasureItem>();
		if ( item != null )
			return IsCuratable( item.Definition );

		return true;
	}
}
