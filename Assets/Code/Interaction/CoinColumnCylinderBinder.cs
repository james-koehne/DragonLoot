using System.Collections.Generic;

using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Drives one or more <see cref="CoinStackCylinderVisual"/> segments for coin columns (hand, ground,
/// display tables). Contiguous same-type runs of at least <see cref="MinCountForCylinder"/> settled coins
/// become cylinders; shorter runs stay as individual meshes.
/// </summary>
public static class CoinColumnCylinderBinder
{
	public const string HostChildName = "CoinColumnCylinder";
	const string SegmentChildPrefix = "Segment_";

	/// <summary>Fallback minimum settled coins before bulk meshes are replaced by a cylinder.</summary>
	public const int DefaultMinCountForCylinder = 2;

	struct CoinRunSegment
	{
		public int Start;
		public int Length;
		public TreasureDefinition Definition;
	}

	static readonly List<CoinRunSegment> SegmentBuffer = new List<CoinRunSegment>();

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
	/// Cylinder segments for an owned ground stack from logical definitions (no per-coin meshes required).
	/// Fills <paramref name="cylinderCovered"/> with true for slots represented by a cylinder.
	/// </summary>
	public static void BindDefinitions(
		ref CoinStackCylinderVisual primaryVisual,
		Transform parent,
		IList<TreasureDefinition> slots,
		bool snap,
		bool[] cylinderCovered )
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

		CollectDefinitionRunSegments( slots, SegmentBuffer );

		Transform container = EnsureContainer( parent, HostChildName );
		if ( container == null )
		{
			primaryVisual = null;
			return;
		}

		container.localPosition = Vector3.zero;
		container.localRotation = Quaternion.identity;
		container.localScale = Vector3.one;

		int segmentVisualIndex = 0;
		primaryVisual = null;
		float parentSy = Mathf.Abs( parent.lossyScale.y );
		if ( parentSy < 0.0001f )
			parentSy = 1f;

		for ( int s = 0; s < SegmentBuffer.Count; s++ )
		{
			CoinRunSegment segment = SegmentBuffer[ s ];
			int end = segment.Start + segment.Length;
			if ( segment.Length < MinCountForCylinder )
				continue;

			float baseY = GetDefinitionOffset( slots, segment.Start );
			float runHeight = SumDefinitionStepHeight( slots, segment.Start, end );
			float step = TreasureStackSpacing.GetStep( segment.Definition );
			float runDiameter = ResolveRunDiameter( segment.Definition, -1f, useHeldDiameter: false );

			CoinStackCylinderVisual segmentVisual = EnsureSegmentVisual( container, segmentVisualIndex, ref primaryVisual );
			if ( segmentVisual == null )
				continue;

			Transform segmentHost = segmentVisual.transform;
			segmentHost.localPosition = Vector3.up * ( baseY / parentSy );
			segmentHost.localRotation = Quaternion.identity;
			segmentHost.localScale = Vector3.one;

			if ( snap )
				segmentVisual.SnapToCount( segment.Definition, segment.Length, step, runDiameter, runHeight );
			else
				segmentVisual.SetStack( segment.Definition, segment.Length, step, runDiameter, runHeight );

			ApplyCylinderShadowCasting( segmentVisual, heldColumn: false );
			segmentVisualIndex++;

			if ( cylinderCovered != null )
			{
				for ( int i = segment.Start; i < end && i < cylinderCovered.Length; i++ )
					cylinderCovered[ i ] = true;
			}
		}

		DestroyExtraSegmentChildren( container, segmentVisualIndex );

		if ( segmentVisualIndex == 0 )
		{
			DestroyGameObject( container.gameObject );
			primaryVisual = null;
		}
	}

	static void CollectDefinitionRunSegments( IList<TreasureDefinition> slots, List<CoinRunSegment> segments )
	{
		segments.Clear();
		if ( slots == null || slots.Count == 0 )
			return;

		int i = 0;
		while ( i < slots.Count )
		{
			TreasureDefinition runDef = slots[ i ];
			if ( !IsCoin( runDef ) )
				break;

			int start = i;
			i++;
			while ( i < slots.Count )
			{
				TreasureDefinition def = slots[ i ];
				if ( !IsCoin( def ) || !SameCoinType( runDef, def ) )
					break;
				i++;
			}

			int length = i - start;
			if ( length > 0 )
			{
				segments.Add( new CoinRunSegment
				{
					Start = start,
					Length = length,
					Definition = runDef
				} );
			}
		}
	}

	static float GetDefinitionOffset( IList<TreasureDefinition> slots, int index )
	{
		float height = 0f;
		if ( slots == null )
			return height;

		for ( int i = 0; i < index && i < slots.Count; i++ )
			height += TreasureStackSpacing.GetStep( slots[ i ] );

		return height;
	}

	static float SumDefinitionStepHeight( IList<TreasureDefinition> slots, int start, int endExclusive )
	{
		float height = 0f;
		if ( slots == null )
			return height;

		for ( int i = start; i < endExclusive && i < slots.Count; i++ )
			height += TreasureStackSpacing.GetStep( slots[ i ] );

		return height;
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

		CollectCoinRunSegments( items, SegmentBuffer );

		Transform container = EnsureContainer( parent, hostName );
		if ( container == null )
		{
			primaryVisual = null;
			return;
		}

		container.localPosition = localPosition;
		container.localRotation = localRotation == default ? Quaternion.identity : localRotation;
		container.localScale = Vector3.one;

		int segmentVisualIndex = 0;
		primaryVisual = null;

		for ( int s = 0; s < SegmentBuffer.Count; s++ )
		{
			CoinRunSegment segment = SegmentBuffer[ s ];
			int end = segment.Start + segment.Length;
			int runTotal = CountItemsInRange( items, segment.Start, end );
			if ( runTotal < MinCountForCylinder )
				continue;

			float baseY = GetRunBaseLocalY( items, segment.Start );
			float runHeight = SumRunStepHeight( items, segment.Start, end );
			float step = heightStep > 0.0001f
				? heightStep
				: TreasureStackSpacing.GetStep( segment.Definition );
			float runDiameter = ResolveRunDiameter( segment.Definition, diameter, useHeldDiameter );

			CoinStackCylinderVisual segmentVisual = EnsureSegmentVisual( container, segmentVisualIndex, ref primaryVisual );
			if ( segmentVisual == null )
				continue;

			Transform segmentHost = segmentVisual.transform;
			// baseY is world-meters of stack spacing; parent treasure scale is not 1, so convert.
			float parentSy = Mathf.Abs( parent.lossyScale.y );
			if ( parentSy < 0.0001f )
				parentSy = 1f;
			segmentHost.localPosition = Vector3.up * ( baseY / parentSy );
			segmentHost.localRotation = Quaternion.identity;
			segmentHost.localScale = Vector3.one;

			if ( snap )
				segmentVisual.SnapToCount( segment.Definition, runTotal, step, runDiameter, runHeight );
			else
				segmentVisual.SetStack( segment.Definition, runTotal, step, runDiameter, runHeight );

			ApplyCylinderShadowCasting( segmentVisual, useHeldDiameter );
			segmentVisualIndex++;
		}

		SyncCoinColumnMeshVisibility( items, SegmentBuffer );

		DestroyExtraSegmentChildren( container, segmentVisualIndex );

		if ( segmentVisualIndex == 0 )
		{
			RestoreMeshes( items );
			DestroyGameObject( container.gameObject );
			primaryVisual = null;
		}
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
	static void SyncCoinColumnMeshVisibility( IList<TreasureItem> items, List<CoinRunSegment> segments )
	{
		if ( items == null )
			return;

		bool[] hidden = null;
		if ( segments != null && segments.Count > 0 )
		{
			hidden = new bool[ items.Count ];
			for ( int s = 0; s < segments.Count; s++ )
			{
				CoinRunSegment segment = segments[ s ];
				int end = segment.Start + segment.Length;
				int settledInRun = CountSettledInRange( items, segment.Start, end );
				int runTotal = CountItemsInRange( items, segment.Start, end );
				if ( runTotal < MinCountForCylinder )
					continue;

				for ( int i = segment.Start; i < end && i < items.Count; i++ )
				{
					TreasureItem item = items[ i ];
					if ( item != null && !item.IsInFlight )
						hidden[ i ] = true;
				}
			}
		}

		for ( int i = 0; i < items.Count; i++ )
		{
			TreasureItem item = items[ i ];
			if ( item == null )
				continue;

			bool shouldHide = hidden != null && hidden[ i ];
			item.SetMeshVisible( !shouldHide );
		}
	}

	static float GetRunBaseLocalY( IList<TreasureItem> items, int runStart )
	{
		if ( items == null || runStart < 0 || runStart >= items.Count )
			return 0f;

		// Coin pivots sit on the contact plane (surface / coin below). Cylinder base matches the
		// run's first pivot — do not shift down by half thickness.
		return TreasureStackSpacing.GetOffsetForIndex( items, items[ runStart ], runStart );
	}

	static float SumRunStepHeight( IList<TreasureItem> items, int start, int endExclusive )
	{
		float height = 0f;
		if ( items == null )
			return height;

		for ( int i = start; i < endExclusive && i < items.Count; i++ )
		{
			TreasureItem item = items[ i ];
			if ( item != null )
				height += TreasureStackSpacing.GetStep( item );
		}

		return height;
	}

	static void CollectCoinRunSegments( IList<TreasureItem> column, List<CoinRunSegment> segments )
	{
		segments.Clear();
		if ( column == null || column.Count == 0 )
			return;

		int i = 0;
		while ( i < column.Count )
		{
			TreasureItem seed = column[ i ];
			if ( seed == null )
			{
				i++;
				continue;
			}

			TreasureDefinition runDef = seed.Definition;
			if ( !IsCoin( runDef ) )
				break;

			int start = i;
			i++;
			while ( i < column.Count )
			{
				TreasureItem item = column[ i ];
				if ( item == null )
					break;

				TreasureDefinition def = item.Definition;
				if ( !IsCoin( def ) || !SameCoinType( runDef, def ) )
					break;

				i++;
			}

			int length = i - start;
			if ( length > 0 )
			{
				segments.Add( new CoinRunSegment
				{
					Start = start,
					Length = length,
					Definition = runDef
				} );
			}
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

	static int CountSettledInRange( IList<TreasureItem> items, int start, int endExclusive )
	{
		int count = 0;
		if ( items == null )
			return count;

		for ( int i = start; i < endExclusive && i < items.Count; i++ )
		{
			TreasureItem item = items[ i ];
			if ( item != null && !item.IsInFlight )
				count++;
		}

		return count;
	}

	static int CountItemsInRange( IList<TreasureItem> items, int start, int endExclusive )
	{
		int count = 0;
		if ( items == null )
			return count;

		for ( int i = start; i < endExclusive && i < items.Count; i++ )
		{
			if ( items[ i ] != null )
				count++;
		}

		return count;
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
