using System.Collections.Generic;

using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

/// <summary>
/// Runtime display badges for a single-type coin table: world-scale coin visuals at indicator
/// anchors, plus a tinted edge-trim plane. Hidden when mixed column requirements are enabled.
/// </summary>
[DisallowMultipleComponent]
public class CoinDisplayTypeIndicators : MonoBehaviour
{
	static readonly int TrimColorId = Shader.PropertyToID( "_TrimColor" );

	[SerializeField]
	CoinDisplayTableInteractable table;

	[SerializeField]
	Transform[] indicatorAnchors;

	[SerializeField]
	Renderer trimRenderer;

	readonly List<GameObject> _spawnedVisuals = new List<GameObject>( 4 );
	MaterialPropertyBlock _trimBlock;
	bool _started;

	void Awake()
	{
		_trimBlock = new MaterialPropertyBlock();
		ResolveTable();
	}

	void OnEnable()
	{
		if ( _started )
			Refresh();
	}

	void Start()
	{
		_started = true;
		Refresh();
	}

	void OnDisable()
	{
		ClearSpawnedVisuals();
	}

	void OnDestroy()
	{
		ClearSpawnedVisuals();
	}

	public void Refresh()
	{
		ResolveTable();
		ClearSpawnedVisuals();

		if ( table == null || !Application.isPlaying )
		{
			SetIndicatorsVisible( false );
			return;
		}

		if ( table.UsesMixedColumnRequirements || table.AcceptedTreasure == null )
		{
			SetIndicatorsVisible( false );
			return;
		}

		SetIndicatorsVisible( true );
		ApplyTrimColor( table.AcceptedTreasure.uiColorHint );
		SpawnIndicatorCoins( table.AcceptedTreasure );
	}

	void ResolveTable()
	{
		if ( table != null )
			return;

		table = GetComponent<CoinDisplayTableInteractable>();
		if ( table == null )
			table = GetComponentInParent<CoinDisplayTableInteractable>();
	}

	void SetIndicatorsVisible( bool visible )
	{
		if ( indicatorAnchors != null )
		{
			for ( int i = 0; i < indicatorAnchors.Length; i++ )
			{
				Transform anchor = indicatorAnchors[ i ];
				if ( anchor == null )
					continue;
				anchor.gameObject.SetActive( visible );
			}
		}

		if ( trimRenderer != null )
			trimRenderer.gameObject.SetActive( visible );
	}

	void ApplyTrimColor( Color color )
	{
		if ( trimRenderer == null )
			return;

		if ( _trimBlock == null )
			_trimBlock = new MaterialPropertyBlock();

		trimRenderer.GetPropertyBlock( _trimBlock );
		_trimBlock.SetColor( TrimColorId, color );
		trimRenderer.SetPropertyBlock( _trimBlock );
	}

	void SpawnIndicatorCoins( TreasureDefinition definition )
	{
		if ( definition == null || indicatorAnchors == null )
			return;

		AssetReferenceGameObject prefabRef = definition.prefab;
		if ( prefabRef == null || !prefabRef.RuntimeKeyIsValid() )
			return;

		Vector3 heldScale = definition.heldScale;
		if ( heldScale.sqrMagnitude < 0.0001f )
			heldScale = Vector3.one * 0.2f;

		for ( int i = 0; i < indicatorAnchors.Length; i++ )
		{
			Transform anchor = indicatorAnchors[ i ];
			if ( anchor == null )
				continue;

			AsyncOperationHandle<GameObject> handle = prefabRef.InstantiateAsync( anchor.position, anchor.rotation );
			GameObject instance = handle.WaitForCompletion();
			if ( handle.Status != AsyncOperationStatus.Succeeded || instance == null )
				continue;

			Transform t = instance.transform;
			t.SetPositionAndRotation( anchor.position, anchor.rotation );
			t.localScale = heldScale;
			t.SetParent( anchor, true );
			instance.name = "CoinTypeVisual";
			StripGameplayComponents( instance );
			_spawnedVisuals.Add( instance );
		}
	}

	static void StripGameplayComponents( GameObject root )
	{
		if ( root == null )
			return;

		Collider[] colliders = root.GetComponentsInChildren<Collider>( true );
		for ( int i = 0; i < colliders.Length; i++ )
		{
			if ( colliders[ i ] != null )
				colliders[ i ].enabled = false;
		}

		Rigidbody[] bodies = root.GetComponentsInChildren<Rigidbody>( true );
		for ( int i = 0; i < bodies.Length; i++ )
		{
			Rigidbody body = bodies[ i ];
			if ( body == null )
				continue;
			body.isKinematic = true;
			body.detectCollisions = false;
			Destroy( body );
		}

		TreasureItemInteractable[] interactables = root.GetComponentsInChildren<TreasureItemInteractable>( true );
		for ( int i = 0; i < interactables.Length; i++ )
		{
			if ( interactables[ i ] != null )
				Destroy( interactables[ i ] );
		}

		TreasureItem[] items = root.GetComponentsInChildren<TreasureItem>( true );
		for ( int i = 0; i < items.Length; i++ )
		{
			if ( items[ i ] != null )
				Destroy( items[ i ] );
		}
	}

	void ClearSpawnedVisuals()
	{
		for ( int i = 0; i < _spawnedVisuals.Count; i++ )
		{
			GameObject go = _spawnedVisuals[ i ];
			if ( go == null )
				continue;
			Addressables.ReleaseInstance( go );
		}

		_spawnedVisuals.Clear();
	}
}
