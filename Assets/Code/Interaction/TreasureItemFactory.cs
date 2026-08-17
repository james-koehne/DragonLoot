using System.Collections.Generic;
using System.Threading.Tasks;

using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

/// <summary>
/// Addressables spawn/despawn seam for treasure items, plus a sync visual pool for coin flights.
/// </summary>
public static class TreasureItemFactory
{
	const int DefaultCoinPoolSize = 24;

	static readonly Dictionary<TreasureDefinition, Stack<TreasureItem>> CoinPools =
		new Dictionary<TreasureDefinition, Stack<TreasureItem>>();
	static readonly Dictionary<TreasureDefinition, GameObject> PrefabCache =
		new Dictionary<TreasureDefinition, GameObject>();
	static readonly HashSet<TreasureItem> PooledVisualCoins = new HashSet<TreasureItem>();
	static Transform _poolRoot;
	static int _poolCap = DefaultCoinPoolSize;

	public static void SetCoinVisualPoolCapacity( int capacity )
	{
		_poolCap = Mathf.Max( 4, capacity );
	}

	/// <summary>
	/// Sync rent of a visual coin for flight / Active hand. Uses real coin prefabs when available.
	/// </summary>
	public static TreasureItem RentVisualCoin(
		TreasureDefinition definition,
		Vector3 position,
		Quaternion rotation )
	{
		if ( definition == null || definition.category != TreasureCategory.Coin )
			return SpawnFallback( definition, position, rotation, null );

		EnsurePoolRoot();
		if ( CoinPools.TryGetValue( definition, out Stack<TreasureItem> stack ) )
		{
			while ( stack.Count > 0 )
			{
				TreasureItem rented = stack.Pop();
				if ( rented == null )
					continue;

				PrepareRentedCoin( rented, definition, position, rotation );
				return rented;
			}
		}

		TreasureItem created = CreatePooledPrefabCoin( definition, position, rotation );
		if ( created == null )
			created = CreateFallback( definition, position, rotation, null );

		if ( created != null )
			PooledVisualCoins.Add( created );
		return created;
	}

	/// <summary>Return a visual coin to the pool (or destroy if not pooled / pool full).</summary>
	public static void ReturnVisualCoin( TreasureItem item )
	{
		if ( item == null )
			return;

		if ( !PooledVisualCoins.Contains( item ) )
		{
			Despawn( item );
			return;
		}

		TreasureDefinition def = item.Definition;
		item.OnDespawned();
		item.EndFlight();
		item.transform.SetParent( _poolRoot, false );
		item.gameObject.SetActive( false );

		if ( def == null || def.category != TreasureCategory.Coin )
		{
			PooledVisualCoins.Remove( item );
			Object.Destroy( item.gameObject );
			return;
		}

		if ( !CoinPools.TryGetValue( def, out Stack<TreasureItem> stack ) )
		{
			stack = new Stack<TreasureItem>( 8 );
			CoinPools[ def ] = stack;
		}

		if ( stack.Count >= _poolCap )
		{
			PooledVisualCoins.Remove( item );
			Object.Destroy( item.gameObject );
			return;
		}

		stack.Push( item );
	}

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

	/// <summary>
	/// Synchronous Addressables spawn for gameplay start-fill. Uses WaitForCompletion, not Tasks.
	/// </summary>
	public static TreasureItem SpawnSync(
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
			GameObject result = handle.WaitForCompletion();
			if ( handle.Status == AsyncOperationStatus.Succeeded && result != null )
			{
				instance = result;
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

		if ( PooledVisualCoins.Contains( item ) )
		{
			ReturnVisualCoin( item );
			return;
		}

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

	static TreasureItem CreatePooledPrefabCoin(
		TreasureDefinition definition,
		Vector3 position,
		Quaternion rotation )
	{
		GameObject prefab = ResolvePrefab( definition );
		if ( prefab == null )
			return null;

		GameObject instance = Object.Instantiate( prefab, position, rotation );
		instance.name = definition != null && !string.IsNullOrEmpty( definition.displayName )
			? definition.displayName
			: prefab.name;

		TreasureItem item = EnsureItem( instance, definition, viaAddressables: false );
		item.ApplyWorldScale();
		item.OnSpawned();
		return item;
	}

	static GameObject ResolvePrefab( TreasureDefinition definition )
	{
		if ( definition == null )
			return null;

		if ( PrefabCache.TryGetValue( definition, out GameObject cached ) && cached != null )
			return cached;

		AssetReferenceGameObject prefabRef = definition.prefab;
		if ( prefabRef == null || !prefabRef.RuntimeKeyIsValid() )
			return null;

		AsyncOperationHandle<GameObject> handle = Addressables.LoadAssetAsync<GameObject>( prefabRef.RuntimeKey );
		GameObject prefab = handle.WaitForCompletion();
		if ( handle.Status != AsyncOperationStatus.Succeeded || prefab == null )
		{
			if ( handle.IsValid() )
				Addressables.Release( handle );
			return null;
		}

		PrefabCache[ definition ] = prefab;
		return prefab;
	}

	static void PrepareRentedCoin(
		TreasureItem item,
		TreasureDefinition definition,
		Vector3 position,
		Quaternion rotation )
	{
		GameObject go = item.gameObject;
		go.SetActive( true );
		go.transform.SetParent( null, false );
		go.transform.SetPositionAndRotation( position, rotation );
		item.EnsureComponents();
		item.Bind( definition, viaAddressables: false );
		item.ApplyWorldScale();
		item.SetMeshVisible( true );
		item.OnSpawned();
	}

	static void EnsurePoolRoot()
	{
		if ( _poolRoot != null )
			return;

		GameObject root = new GameObject( "CoinVisualPool" );
		Object.DontDestroyOnLoad( root );
		root.SetActive( false );
		_poolRoot = root.transform;
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
