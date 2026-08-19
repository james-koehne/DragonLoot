using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Pure math for interleaved gold-bar stacks: two side by side, then two on top rotated 90°.
/// Local Y is up from the stack contact. Pair offset is along local +X at layer 0 (0° yaw).
/// Layer yaw alternates 0°, 90°, 0°, … regardless of mesh orientation.
/// </summary>
public static class GoldBarStackLattice
{
	public struct GoldBarSize
	{
		public float Length;
		public float Width;
		public float Height;
		public float BottomLift;
	}

	static readonly Dictionary<int, GoldBarSize> SizeCache = new Dictionary<int, GoldBarSize>( 8 );

	const float FallbackLength = 0.42f;
	const float FallbackWidth = 0.16f;
	const float FallbackHeight = 0.12f;

	public static GoldBarSize FallbackSize
	{
		get
		{
			GoldBarSize size = new GoldBarSize();
			size.Length = FallbackLength;
			size.Width = FallbackWidth;
			size.Height = FallbackHeight;
			size.BottomLift = FallbackHeight * 0.5f;
			return size;
		}
	}

	public static GoldBarSize Measure( TreasureDefinition definition, TreasureItem item )
	{
		if ( item != null )
			CacheFromItem( item );

		if ( definition != null && SizeCache.TryGetValue( definition.GetInstanceID(), out GoldBarSize cached ) )
			return cached;

		if ( item != null )
			return MeasureItemUncached( item );

		GoldBarSize fallback = FallbackSize;
		if ( definition != null )
		{
			Vector3 scale = definition.worldScale;
			float xz = Mathf.Max( Mathf.Abs( scale.x ), Mathf.Abs( scale.z ) );
			float y = Mathf.Abs( scale.y );
			if ( xz > 0.0001f )
			{
				fallback.Length = Mathf.Max( FallbackLength, xz * 0.22f );
				fallback.Width = Mathf.Max( FallbackWidth, xz * 0.08f );
			}
			if ( y > 0.0001f )
			{
				fallback.Height = Mathf.Max( FallbackHeight, y * 0.06f );
				fallback.BottomLift = fallback.Height * 0.5f;
			}
		}

		return fallback;
	}

	public static void CacheFromItem( TreasureItem item )
	{
		if ( item == null || item.Definition == null )
			return;

		SizeCache[ item.Definition.GetInstanceID() ] = MeasureItemUncached( item );
	}

	public static bool TryGetLocalSlot(
		int index,
		GoldBarSize size,
		GoldBarStackSettings settings,
		out Vector3 localPos,
		out float yawDegrees )
	{
		localPos = Vector3.zero;
		yawDegrees = 0f;
		if ( index < 0 )
			return false;

		int layer = index / 2;
		int pairSlot = index % 2;
		float pairGap = settings != null ? settings.pairGap : 0.012f;
		float layerGap = settings != null ? settings.layerGap : 0.004f;
		float pairOffset = ( pairSlot - 0.5f ) * ( size.Width + pairGap );
		yawDegrees = layer * 90f;
		Vector3 xz = Quaternion.Euler( 0f, yawDegrees, 0f ) * new Vector3( pairOffset, 0f, 0f );
		float layerY = layer * ( size.Height + layerGap );
		localPos = new Vector3( xz.x, layerY, xz.z );
		return true;
	}

	public static bool TryGetWorldPose(
		int index,
		TreasureItem item,
		Vector3 contact,
		Quaternion stackRotation,
		out Vector3 worldPos,
		out Quaternion worldRot )
	{
		TreasureDefinition definition = item != null ? item.Definition : null;
		return TryGetWorldPose( index, definition, item, contact, stackRotation, out worldPos, out worldRot );
	}

	public static bool TryGetWorldPose(
		int index,
		TreasureDefinition definition,
		TreasureItem item,
		Vector3 contact,
		Quaternion stackRotation,
		out Vector3 worldPos,
		out Quaternion worldRot )
	{
		worldPos = contact;
		worldRot = stackRotation;
		GoldBarSize size = Measure( definition, item );
		if ( !TryGetLocalSlot( index, size, GoldBarStack.Settings, out Vector3 local, out float yaw ) )
			return false;

		worldPos = contact + stackRotation * ( local + Vector3.up * size.BottomLift );
		worldRot = stackRotation * Quaternion.Euler( 0f, yaw, 0f );
		return true;
	}

	public static float GetStackHeight( int count, GoldBarSize size, GoldBarStackSettings settings )
	{
		if ( count <= 0 )
			return size.Height;

		int layers = ( count + 1 ) / 2;
		float layerGap = settings != null ? settings.layerGap : 0.004f;
		return layers * size.Height + Mathf.Max( 0, layers - 1 ) * layerGap;
	}

	public static float GetFootprintRadius( GoldBarSize size, GoldBarStackSettings settings )
	{
		float pairGap = settings != null ? settings.pairGap : 0.012f;
		float pairWidth = size.Width * 2f + pairGap;
		float span = Mathf.Max( size.Length, pairWidth );
		return span * 0.5f;
	}

	static GoldBarSize MeasureItemUncached( TreasureItem item )
	{
		GoldBarSize size = FallbackSize;
		if ( item == null )
			return size;

		if ( !TryMeasureLocalAabb( item, out Vector3 fullSize, out float lift ) )
			return size;

		size.Height = Mathf.Max( 0.02f, fullSize.y );
		size.BottomLift = Mathf.Max( 0f, lift );

		float a = Mathf.Max( 0.02f, fullSize.x );
		float b = Mathf.Max( 0.02f, fullSize.z );
		if ( a >= b )
		{
			size.Length = a;
			size.Width = b;
		}
		else
		{
			size.Length = b;
			size.Width = a;
		}

		return size;
	}

	static bool TryMeasureLocalAabb( TreasureItem item, out Vector3 fullSize, out float lift )
	{
		fullSize = Vector3.zero;
		lift = 0f;
		if ( item == null )
			return false;

		Transform root = item.transform;
		Vector3 placeScale = item.GetWorldScale();
		float minX = float.MaxValue;
		float minY = float.MaxValue;
		float minZ = float.MaxValue;
		float maxX = float.MinValue;
		float maxY = float.MinValue;
		float maxZ = float.MinValue;
		bool any = false;

		MeshFilter[] filters = item.GetComponentsInChildren<MeshFilter>();
		for ( int f = 0; f < filters.Length; f++ )
		{
			MeshFilter filter = filters[ f ];
			if ( filter == null || filter.sharedMesh == null )
				continue;

			Bounds lb = filter.sharedMesh.bounds;
			AccumulateCorners( root, filter.transform, lb.min, lb.max, placeScale, ref minX, ref minY, ref minZ, ref maxX, ref maxY, ref maxZ, ref any );
		}

		if ( !any )
		{
			MeshRenderer[] renderers = item.GetComponentsInChildren<MeshRenderer>();
			for ( int r = 0; r < renderers.Length; r++ )
			{
				MeshRenderer renderer = renderers[ r ];
				if ( renderer == null )
					continue;

				Bounds lb = renderer.localBounds;
				AccumulateCorners( root, renderer.transform, lb.min, lb.max, placeScale, ref minX, ref minY, ref minZ, ref maxX, ref maxY, ref maxZ, ref any );
			}
		}

		if ( !any )
			return false;

		fullSize = new Vector3( maxX - minX, maxY - minY, maxZ - minZ );
		lift = -minY;
		if ( lift < 0f )
			lift = 0f;
		return fullSize.x > 0.0001f && fullSize.y > 0.0001f && fullSize.z > 0.0001f;
	}

	static void AccumulateCorners(
		Transform root,
		Transform meshT,
		Vector3 min,
		Vector3 max,
		Vector3 placeScale,
		ref float minX,
		ref float minY,
		ref float minZ,
		ref float maxX,
		ref float maxY,
		ref float maxZ,
		ref bool any )
	{
		for ( int i = 0; i < 8; i++ )
		{
			Vector3 meshLocal = new Vector3(
				( i & 1 ) == 0 ? min.x : max.x,
				( i & 2 ) == 0 ? min.y : max.y,
				( i & 4 ) == 0 ? min.z : max.z );

			Vector3 worldCorner = meshT.TransformPoint( meshLocal );
			Vector3 rootLocal = root.InverseTransformPoint( worldCorner );
			Vector3 placed = Vector3.Scale( rootLocal, placeScale );
			if ( placed.x < minX )
				minX = placed.x;
			if ( placed.y < minY )
				minY = placed.y;
			if ( placed.z < minZ )
				minZ = placed.z;
			if ( placed.x > maxX )
				maxX = placed.x;
			if ( placed.y > maxY )
				maxY = placed.y;
			if ( placed.z > maxZ )
				maxZ = placed.z;
			any = true;
		}
	}
}
