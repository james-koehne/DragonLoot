using System.Collections.Generic;

using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Drives one <see cref="CoinStackCylinderVisual"/> for coin columns (hand, ground,
/// display tables). Settled coin columns of at least <see cref="MinCountForCylinder"/> become
/// a single multi-type cylinder; shorter columns stay as individual meshes.
/// </summary>
public static class CoinColumnCylinderBinder
{
	public const string HostChildName = "CoinColumnCylinder";
	const string SegmentChildPrefix = "Segment_";

	/// <summary>Fallback minimum settled coins before bulk meshes are replaced by a cylinder.</summary>
	public const int DefaultMinCountForCylinder = 2;

	static readonly List<TreasureDefinition> MultiSlotBuffer = new List<TreasureDefinition>( 64 );

	public static int MinCountForCylinder
	{
		get
		{
			CoinStackVisualDefinition def = null;
			def = RuntimeDefinition.Resolve( ref def );
#if UNITY_EDITOR
			if ( def == null )
			{
				def = UnityEditor.AssetDatabase.LoadAssetAtPath<CoinStackVisualDefinition>(
					"Assets/Definitions/CoinStackVisualDefinition.asset" );
			}
#endif
			if ( def != null )
				return Mathf.Max( 1, def.minCountForCylinder );
			return DefaultMinCountForCylinder;
		}
	}

	public static bool IsCoin( TreasureDefinition definition )
	{
		return definition != null && definition.category == TreasureCategory.Coin;
	}

	/// <summary>
	/// True when <paramref name="rendererTransform"/> belongs to a binder-owned stack cylinder under <paramref name="treasureRoot"/>.
	/// </summary>
	public static bool IsBinderVisualRenderer( Transform rendererTransform, Transform treasureRoot )
	{
		if ( rendererTransform == null || treasureRoot == null )
			return false;

		Transform t = rendererTransform;
		while ( t != null && t != treasureRoot )
		{
			if ( IsBinderHostName( t.name ) )
				return true;

			t = t.parent;
		}

		return false;
	}

	public static bool IsBinderHostName( string transformName )
	{
		if ( string.IsNullOrEmpty( transformName ) )
			return false;

		return transformName.StartsWith( HostChildName );
	}

	static bool SameCoinType( TreasureDefinition a, TreasureDefinition b )
	{
		if ( a == b )
			return true;
		if ( a == null || b == null )
			return false;
		if ( !string.IsNullOrEmpty( a.id ) && a.id == b.id )
			return true;
		return false;
	}

	/// <summary>
	/// Longest settled run from the bottom of <paramref name="column"/> where every item is the same coin type.
	/// </summary>
	public static bool TryGetHomogeneousBottomCoinRun(
		IList<TreasureItem> column,
		out TreasureDefinition definition,
		out int runLength )
	{
		definition = null;
		runLength = 0;
		if ( column == null || column.Count == 0 )
			return false;

		for ( int i = 0; i < column.Count; i++ )
		{
			TreasureItem item = column[ i ];
			if ( item == null )
				break;

			TreasureDefinition def = item.Definition;
			if ( !IsCoin( def ) )
				break;

			if ( definition == null )
				definition = def;
			else if ( !SameCoinType( definition, def ) )
				break;

			runLength = i + 1;
		}

		return definition != null && runLength > 0;
	}

	public static bool TryGetHomogeneousCoinDefinition(
		IList<TreasureItem> items,
		out TreasureDefinition definition,
		out int settledCount )
	{
		definition = null;
		settledCount = 0;
		if ( items == null || items.Count == 0 )
			return false;

		for ( int i = 0; i < items.Count; i++ )
		{
			TreasureItem item = items[ i ];
			if ( item == null || item.IsInFlight )
				continue;

			TreasureDefinition def = item.Definition;
			if ( !IsCoin( def ) )
				return false;

			if ( definition == null )
			{
				definition = def;
			}
			else if ( !SameCoinType( definition, def ) )
			{
				return false;
			}

			settledCount++;
		}

		return definition != null && settledCount > 0;
	}

	public static CoinStackCylinderVisual EnsureVisual(
		Transform parent,
		ref CoinStackCylinderVisual cached,
		string childName = HostChildName )
	{
		if ( parent == null )
			return null;

		if ( string.IsNullOrEmpty( childName ) )
			childName = HostChildName;

		if ( cached != null )
			return cached;

		Transform container = EnsureContainer( parent, childName );
		if ( container == null )
			return null;

		return EnsureSegmentVisual( container, 0, ref cached );
	}

	public static void StripFromItem( TreasureItem item )
	{
		if ( item == null )
			return;

		Transform root = item.transform;
		for ( int i = root.childCount - 1; i >= 0; i-- )
		{
			Transform child = root.GetChild( i );
			if ( child == null )
				continue;

			if ( !IsBinderHostName( child.name ) && !IsOrphanVisualName( child.name ) )
				continue;

			// Binder meshes are excluded from TreasureItem.SetMeshVisible — hide before destroy
			// so deferred Destroy cannot leave a visible non-interactable coin behind.
			DisableRenderersUnder( child );
			DestroyGameObject( child.gameObject );
		}

		item.SetMeshVisible( true );
	}

	public static void Clear( ref CoinStackCylinderVisual visual, IList<TreasureItem> items )
	{
		RestoreMeshes( items );
		if ( visual == null )
			return;

		Transform container = ResolveContainer( visual.transform );
		if ( container != null )
			DestroyGameObject( container.gameObject );
		else
			visual.SetStack( null, 0 );

		visual = null;
	}

	public static void ClearAndDestroy( ref CoinStackCylinderVisual visual, IList<TreasureItem> items )
	{
		RestoreMeshes( items );
		if ( visual == null )
			return;

		Transform container = ResolveContainer( visual.transform );
		GameObject go = container != null ? container.gameObject : visual.gameObject;
		visual = null;
		DestroyGameObject( go );
	}

	/// <summary>
	/// Binds cylinder segment(s) on <paramref name="parent"/> for a coin column (mixed types supported).
	/// </summary>
	public static void Bind(
		ref CoinStackCylinderVisual visual,
		Transform parent,
		IList<TreasureItem> items,
		bool snap,
		float heightStep = -1f,
		float diameter = -1f,
		Vector3 localPosition = default,
		Quaternion localRotation = default,
		string hostName = HostChildName,
		bool useHeldDiameter = false )
	{
		if ( parent == null )
		{
			Clear( ref visual, items );
			return;
		}

		BindSegments(
			ref visual,
			parent,
			items,
			snap,
			heightStep,
			diameter,
			localPosition,
			localRotation,
			hostName,
			useHeldDiameter );
	}

	/// <summary>
	/// Ground / loose columns: cylinder segment(s) on the bottom coin.
	/// </summary>
	public static void BindToBottomItem( TreasureItem bottom, IList<TreasureItem> column, bool snap )
	{
		if ( bottom == null )
			return;

		CoinStackCylinderVisual visual = null;
		Transform existing = bottom.transform.Find( HostChildName );
		if ( existing != null && existing.childCount > 0 )
		{
			Transform seg = existing.Find( SegmentChildPrefix + "0" );
			if ( seg != null )
				visual = seg.GetComponent<CoinStackCylinderVisual>();
		}
		else if ( existing != null )
		{
			visual = existing.GetComponent<CoinStackCylinderVisual>();
		}

		BindSegments(
			ref visual,
			bottom.transform,
			column,
			snap,
			heightStep: -1f,
			diameter: -1f,
			localPosition: Vector3.zero,
			localRotation: Quaternion.identity,
			hostName: HostChildName,
			useHeldDiameter: false );
	}

	/// <summary>
	/// One multi-type cylinder from logical definitions (no per-coin meshes required).
	/// Fills <paramref name="cylinderCovered"/> with true for slots represented by the cylinder.
	/// When <paramref name="useHeldScale"/> is true, diameter and thickness use heldScale
	/// (heldScale.y / worldScale.y, typically 0.2/0.3).
	/// </summary>
	public static void BindDefinitions(
		ref CoinStackCylinderVisual primaryVisual,
		Transform parent,
		IList<TreasureDefinition> slots,
		bool snap,
		bool[] cylinderCovered,
		bool useHeldScale = false )
	{
		if ( cylinderCovered != null )
		{
			for ( int i = 0; i < cylinderCovered.Length; i++ )
				cylinderCovered[ i ] = false;
		}

		if ( parent == null || slots == null || slots.Count == 0 )
		{
			DestroyContainerOnParent( parent, HostChildName );
			primaryVisual = null;
			return;
		}

		int coinCount = 0;
		for ( int i = 0; i < slots.Count; i++ )
		{
			if ( !IsCoin( slots[ i ] ) )
				break;
			coinCount++;
		}

		if ( coinCount < MinCountForCylinder )
		{
			DestroyContainerOnParent( parent, HostChildName );
			primaryVisual = null;
			return;
		}

		Transform container = EnsureContainer( parent, HostChildName );
		if ( container == null )
		{
			primaryVisual = null;
			return;
		}

		container.localPosition = Vector3.zero;
		container.localRotation = Quaternion.identity;
		container.localScale = Vector3.one;

		MultiSlotBuffer.Clear();
		float runHeight = 0f;
		float maxDiameter = 0f;
		for ( int i = 0; i < coinCount; i++ )
		{
			TreasureDefinition def = slots[ i ];
			MultiSlotBuffer.Add( def );
			runHeight += useHeldScale
				? TreasureStackSpacing.GetHeldStep( def )
				: TreasureStackSpacing.GetStep( def );
			float d = ResolveRunDiameter( def, -1f, useHeldScale );
			if ( d > maxDiameter )
				maxDiameter = d;
		}

		CoinStackCylinderVisual segmentVisual = EnsureSegmentVisual( container, 0, ref primaryVisual );
		if ( segmentVisual == null )
		{
			DestroyGameObject( container.gameObject );
			primaryVisual = null;
			return;
		}

		Transform segmentHost = segmentVisual.transform;
		segmentHost.localPosition = Vector3.zero;
		segmentHost.localRotation = Quaternion.identity;
		segmentHost.localScale = Vector3.one;

		if ( snap )
			segmentVisual.SnapToCountMulti( MultiSlotBuffer, maxDiameter, runHeight );
		else
			segmentVisual.SetStackMulti( MultiSlotBuffer, snap: false, maxDiameter, runHeight );

		ApplyCylinderShadowCasting( segmentVisual, heldColumn: useHeldScale );
		DestroyExtraSegmentChildren( container, 1 );

		if ( cylinderCovered != null )
		{
			for ( int i = 0; i < coinCount && i < cylinderCovered.Length; i++ )
				cylinderCovered[ i ] = true;
		}
	}

	static void BindSegments(
		ref CoinStackCylinderVisual primaryVisual,
		Transform parent,
		IList<TreasureItem> items,
		bool snap,
		float heightStep,
		float diameter,
		Vector3 localPosition,
		Quaternion localRotation,
		string hostName,
		bool useHeldDiameter )
	{
		if ( items == null || items.Count == 0 || parent == null )
		{
			RestoreMeshes( items );
			DestroyContainerOnParent( parent, hostName );
			primaryVisual = null;
			return;
		}

		int settledCoinCount = CountSettledCoins( items );
		if ( settledCoinCount < MinCountForCylinder )
		{
			RestoreMeshes( items );
			DestroyContainerOnParent( parent, hostName );
			primaryVisual = null;
			return;
		}

		Transform container = EnsureContainer( parent, hostName );
		if ( container == null )
		{
			primaryVisual = null;
			return;
		}

		container.localPosition = localPosition;
		container.localRotation = localRotation == default ? Quaternion.identity : localRotation;
		container.localScale = Vector3.one;

		MultiSlotBuffer.Clear();
		float runHeight = 0f;
		float maxDiameter = diameter;
		for ( int i = 0; i < items.Count; i++ )
		{
			TreasureItem item = items[ i ];
			if ( item == null || item.IsInFlight )
				continue;

			TreasureDefinition def = item.Definition;
			if ( !IsCoin( def ) )
				break;

			MultiSlotBuffer.Add( def );
			if ( heightStep > 0.0001f )
				runHeight += heightStep;
			else if ( useHeldDiameter )
				runHeight += TreasureStackSpacing.GetHeldStep( item );
			else
				runHeight += TreasureStackSpacing.GetStep( item );
			float d = ResolveRunDiameter( def, diameter, useHeldDiameter );
			if ( d > maxDiameter )
				maxDiameter = d;
		}

		if ( MultiSlotBuffer.Count < MinCountForCylinder )
		{
			RestoreMeshes( items );
			DestroyGameObject( container.gameObject );
			primaryVisual = null;
			return;
		}

		primaryVisual = null;
		CoinStackCylinderVisual segmentVisual = EnsureSegmentVisual( container, 0, ref primaryVisual );
		if ( segmentVisual == null )
		{
			RestoreMeshes( items );
			DestroyGameObject( container.gameObject );
			primaryVisual = null;
			return;
		}

		Transform segmentHost = segmentVisual.transform;
		segmentHost.localPosition = Vector3.zero;
		segmentHost.localRotation = Quaternion.identity;
		segmentHost.localScale = Vector3.one;

		if ( snap )
			segmentVisual.SnapToCountMulti( MultiSlotBuffer, maxDiameter, runHeight );
		else
			segmentVisual.SetStackMulti( MultiSlotBuffer, snap: false, maxDiameter, runHeight );

		ApplyCylinderShadowCasting( segmentVisual, useHeldDiameter );
		DestroyExtraSegmentChildren( container, 1 );
		SyncCoinColumnMeshVisibilityMulti( items );
	}

	static int CountSettledCoins( IList<TreasureItem> items )
	{
		if ( items == null )
			return 0;

		int count = 0;
		for ( int i = 0; i < items.Count; i++ )
		{
			TreasureItem item = items[ i ];
			if ( item == null || item.IsInFlight )
				continue;
			if ( !IsCoin( item.Definition ) )
				break;
			count++;
		}

		return count;
	}

	static void ApplyCylinderShadowCasting( CoinStackCylinderVisual visual, bool heldColumn )
	{
		if ( visual == null )
			return;

		MeshRenderer renderer = visual.GetComponentInChildren<MeshRenderer>( true );
		if ( renderer == null )
			return;

		ShadowCastingMode mode = heldColumn ? ShadowCastingMode.Off : ShadowCastingMode.On;
		if ( renderer.shadowCastingMode != mode )
			renderer.shadowCastingMode = mode;
	}

	/// <summary>
	/// Shows/hides per-coin meshes without toggling every frame (avoids shadow/light flicker).
	/// </summary>
	static void SyncCoinColumnMeshVisibilityMulti( IList<TreasureItem> items )
	{
		if ( items == null )
			return;

		for ( int i = 0; i < items.Count; i++ )
		{
			TreasureItem item = items[ i ];
			if ( item == null )
				continue;

			bool shouldHide = !item.IsInFlight && IsCoin( item.Definition );
			item.SetMeshVisible( !shouldHide );
		}
	}

	static float ResolveRunDiameter( TreasureDefinition definition, float diameterOverride, bool useHeldDiameter )
	{
		if ( diameterOverride > 0.0001f )
			return diameterOverride;

		if ( definition == null )
			return -1f;

		Vector3 scale = useHeldDiameter ? definition.heldScale : definition.worldScale;
		float x = Mathf.Abs( scale.x );
		float z = Mathf.Abs( scale.z );
		float d = Mathf.Max( x, z );
		return d > 0.0001f ? d : -1f;
	}

	static Transform EnsureContainer( Transform parent, string hostName )
	{
		if ( parent == null )
			return null;

		if ( string.IsNullOrEmpty( hostName ) )
			hostName = HostChildName;

		Transform existing = parent.Find( hostName );
		if ( existing == null )
		{
			GameObject host = new GameObject( hostName );
			host.transform.SetParent( parent, false );
			host.transform.localPosition = Vector3.zero;
			host.transform.localRotation = Quaternion.identity;
			host.transform.localScale = Vector3.one;
			return host.transform;
		}

		StripLegacyVisualFromContainer( existing );
		return existing;
	}

	static void StripLegacyVisualFromContainer( Transform container )
	{
		if ( container == null )
			return;

		// Legacy layout put CoinStackCylinderVisual on the host; Segment_* is the current layout.
		// Destroying only the component left StackVisual children as collider-less orphan coins.
		for ( int i = container.childCount - 1; i >= 0; i-- )
		{
			Transform child = container.GetChild( i );
			if ( child == null || !IsOrphanVisualName( child.name ) )
				continue;

			DisableRenderersUnder( child );
			DestroyGameObject( child.gameObject );
		}

		CoinStackCylinderVisual legacy = container.GetComponent<CoinStackCylinderVisual>();
		if ( legacy == null )
			return;

		if ( Application.isPlaying )
			Object.Destroy( legacy );
		else
			Object.DestroyImmediate( legacy );
	}

	static CoinStackCylinderVisual EnsureSegmentVisual(
		Transform container,
		int segmentIndex,
		ref CoinStackCylinderVisual primaryVisual )
	{
		if ( container == null )
			return null;

		string segName = SegmentChildPrefix + segmentIndex;
		Transform segmentTransform = container.Find( segName );
		if ( segmentTransform == null )
		{
			GameObject segmentGo = new GameObject( segName );
			segmentGo.transform.SetParent( container, false );
			segmentTransform = segmentGo.transform;
		}

		CoinStackCylinderVisual visual = segmentTransform.GetComponent<CoinStackCylinderVisual>();
		if ( visual == null )
			visual = segmentTransform.gameObject.AddComponent<CoinStackCylinderVisual>();

		if ( primaryVisual == null )
			primaryVisual = visual;

		return visual;
	}

	static void DestroyExtraSegmentChildren( Transform container, int keepCount )
	{
		if ( container == null )
			return;

		for ( int i = container.childCount - 1; i >= 0; i-- )
		{
			Transform child = container.GetChild( i );
			if ( child == null )
				continue;

			if ( IsOrphanVisualName( child.name ) )
			{
				DisableRenderersUnder( child );
				DestroyGameObject( child.gameObject );
				continue;
			}

			if ( !child.name.StartsWith( SegmentChildPrefix ) )
				continue;

			string suffix = child.name.Substring( SegmentChildPrefix.Length );
			if ( !int.TryParse( suffix, out int index ) || index >= keepCount )
				DestroyGameObject( child.gameObject );
		}
	}

	static bool IsOrphanVisualName( string transformName )
	{
		return transformName == "StackVisual" || transformName == "CylinderVisual";
	}

	static void DisableRenderersUnder( Transform root )
	{
		if ( root == null )
			return;

		Renderer[] renderers = root.GetComponentsInChildren<Renderer>( true );
		if ( renderers == null )
			return;

		for ( int i = 0; i < renderers.Length; i++ )
		{
			Renderer renderer = renderers[ i ];
			if ( renderer != null )
				renderer.enabled = false;
		}

		root.gameObject.SetActive( false );
	}

	static void DestroyContainerOnParent( Transform parent, string hostName )
	{
		if ( parent == null || string.IsNullOrEmpty( hostName ) )
			return;

		Transform existing = parent.Find( hostName );
		if ( existing != null )
			DestroyGameObject( existing.gameObject );
	}

	static Transform ResolveContainer( Transform visualTransform )
	{
		if ( visualTransform == null )
			return null;

		Transform t = visualTransform;
		Transform container = null;
		while ( t != null )
		{
			if ( IsBinderHostName( t.name ) )
				container = t;

			t = t.parent;
		}

		return container;
	}

	static void RestoreMeshes( IList<TreasureItem> items )
	{
		if ( items == null )
			return;

		for ( int i = 0; i < items.Count; i++ )
		{
			TreasureItem item = items[ i ];
			if ( item != null )
				item.SetMeshVisible( true );
		}
	}

	static void DestroyGameObject( GameObject go )
	{
		if ( go == null )
			return;

		DisableRenderersUnder( go.transform );

		if ( Application.isPlaying )
			Object.Destroy( go );
		else
			Object.DestroyImmediate( go );
	}
}
