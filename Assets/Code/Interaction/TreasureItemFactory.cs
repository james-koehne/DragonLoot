using System.Threading.Tasks;

using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

/// <summary>
/// Addressables spawn/despawn seam for treasure items. Ready for a future pool swap.
/// </summary>
public static class TreasureItemFactory
{
	public static async Task<TreasureItem> SpawnAsync(
		TreasureDefinition definition,
		Vector3 position,
		Quaternion rotation,
		Transform parent = null )
	{
		if ( definition == null )
			return CreateFallback( null, position, rotation, parent );

		GameObject instance = null;
		bool viaAddressables = false;

		AssetReferenceGameObject prefabRef = definition.prefab;
		if ( prefabRef != null && prefabRef.RuntimeKeyIsValid() )
		{
			AsyncOperationHandle<GameObject> handle = parent != null
				? prefabRef.InstantiateAsync( position, rotation, parent )
				: prefabRef.InstantiateAsync( position, rotation );
			await handle.Task;

			if ( handle.Status == AsyncOperationStatus.Succeeded )
			{
				instance = handle.Result;
				viaAddressables = true;
			}
		}

		if ( instance == null )
			return CreateFallback( definition, position, rotation, parent );

		TreasureItem item = EnsureItem( instance, definition, viaAddressables );
		LooseTreasureManager.EnsureExists();
		item.OnSpawned();
		return item;
	}

	public static TreasureItem SpawnFallback(
		TreasureDefinition definition,
		Vector3 position,
		Quaternion rotation,
		Transform parent = null )
	{
		return CreateFallback( definition, position, rotation, parent );
	}

	public static void Despawn( TreasureItem item )
	{
		if ( item == null )
			return;

		item.OnDespawned();
		GameObject go = item.gameObject;
		bool viaAddressables = item.ReleasedViaAddressables;

		if ( viaAddressables )
			Addressables.ReleaseInstance( go );
		else
			Object.Destroy( go );
	}

	public static TreasureItem EnsureItem( GameObject instance, TreasureDefinition definition, bool viaAddressables )
	{
		if ( instance == null )
			return null;

		TreasureItem item = instance.GetComponent<TreasureItem>();
		if ( item == null )
			item = instance.AddComponent<TreasureItem>();

		item.EnsureComponents();
		item.Bind( definition, viaAddressables );
		return item;
	}

	static TreasureItem CreateFallback(
		TreasureDefinition definition,
		Vector3 position,
		Quaternion rotation,
		Transform parent )
	{
		string label = definition != null && !string.IsNullOrEmpty( definition.displayName )
			? definition.displayName
			: "Treasure";

		PrimitiveType primitive = definition != null
			? definition.GetFallbackPrimitive()
			: PrimitiveType.Sphere;
		Vector3 scale = definition != null
			? definition.worldScale
			: Vector3.one * 0.35f;

		GameObject go = GameObject.CreatePrimitive( primitive );
		go.name = label;
		go.transform.SetPositionAndRotation( position, rotation );
		go.transform.localScale = scale;
		if ( parent != null )
			go.transform.SetParent( parent, true );

		Rigidbody body = go.GetComponent<Rigidbody>();
		if ( body == null )
			body = go.AddComponent<Rigidbody>();

		TreasureItem item = EnsureItem( go, definition, false );
		item.OnSpawned();
		return item;
	}
}
