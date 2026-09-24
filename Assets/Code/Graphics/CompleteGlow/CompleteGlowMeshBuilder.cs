using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Builds a procedurally generated complete-glow frustum mesh (shells, optional core, ray cards).
/// Runtime-safe mesh generation used by the Complete Glow editor tools.
/// </summary>
public static class CompleteGlowMeshBuilder
{
	struct RingPoint
	{
		public Vector2 Position;
		public float PerimeterU;
		public float EdgeT;
	}

	public static Mesh Build( CompleteGlowSettings settings, Vector2 footprintWorld, Mesh reuse = null )
	{
		if ( settings == null )
			settings = CompleteGlowSettings.CreateDefault();

		float halfX = Mathf.Max( 0.005f, footprintWorld.x * 0.5f );
		float halfZ = Mathf.Max( 0.005f, footprintWorld.y * 0.5f );

		List<Vector3> positions = new List<Vector3>( 512 );
		List<Vector3> normals = new List<Vector3>( 512 );
		List<Vector2> uvs = new List<Vector2>( 512 );
		List<Color> colors = new List<Color>( 512 );
		List<int> triangles = new List<int>( 1024 );

		int layerCount = Mathf.Clamp( settings.LayerCount, 1, 6 );
		for ( int layer = 0; layer < layerCount; layer++ )
		{
			float outward = layer * Mathf.Max( 0f, settings.LayerOutwardStep );
			float heightScale = 1f + layer * Mathf.Max( 0f, settings.LayerHeightScaleStep );
			float flare = settings.FlareAngleDegrees + layer * settings.LayerFlareStep;
			float alpha = Mathf.Pow( Mathf.Clamp( settings.LayerAlphaFalloff, 0.05f, 1f ), layer );
			AddShell( positions, normals, uvs, colors, triangles, settings, halfX + outward, halfZ + outward, settings.Height * heightScale, flare, new Color( 1f, 1f, 1f, alpha ), false );
		}

		if ( settings.InnerCore )
		{
			float coreHalfX = halfX * Mathf.Clamp( settings.CoreScale, 0.05f, 1f );
			float coreHalfZ = halfZ * Mathf.Clamp( settings.CoreScale, 0.05f, 1f );
			float coreHeight = settings.Height * Mathf.Clamp( settings.CoreHeightScale, 0.1f, 1f );
			float intensity = Mathf.Max( 0f, settings.CoreIntensity );
			AddShell( positions, normals, uvs, colors, triangles, settings, coreHalfX, coreHalfZ, coreHeight, settings.FlareAngleDegrees * 0.5f, new Color( intensity, intensity, intensity, 1f ), false );
		}

		if ( settings.RayCardCount > 0 )
			AddRayCards( positions, normals, uvs, colors, triangles, settings, halfX, halfZ );

		Mesh mesh = reuse != null ? reuse : new Mesh();
		mesh.name = "CompleteGlow";
		mesh.Clear();
		mesh.SetVertices( positions );
		mesh.SetNormals( normals );
		mesh.SetUVs( 0, uvs );
		mesh.SetColors( colors );
		mesh.SetTriangles( triangles, 0, true );
		mesh.RecalculateBounds();
		return mesh;
	}

	static void AddShell( List<Vector3> positions, List<Vector3> normals, List<Vector2> uvs, List<Color> colors, List<int> triangles, CompleteGlowSettings settings, float halfX, float halfZ, float height, float flareDegrees, Color tint, bool forceCap )
	{
		height = Mathf.Max( 0.01f, height );
		int heightSegments = Mathf.Clamp( settings.HeightSegments, 1, 32 );
		RingPoint[] baseRing = BuildRing( settings, halfX, halfZ );
		int ringCount = baseRing.Length;
		if ( ringCount < 3 )
			return;

		float flareRadians = flareDegrees * Mathf.Deg2Rad;
		float flareTan = Mathf.Tan( flareRadians );

		for ( int h = 0; h < heightSegments; h++ )
		{
			float t0 = (float)h / heightSegments;
			float t1 = (float)( h + 1 ) / heightSegments;
			float y0 = t0 * height;
			float y1 = t1 * height;
			float flare0 = EvaluateFlare( settings, t0 ) * flareTan * y0;
			float flare1 = EvaluateFlare( settings, t1 ) * flareTan * y1;

			RingPoint[] ring0 = BuildRing( settings, halfX + flare0, halfZ + flare0 );
			RingPoint[] ring1 = BuildRing( settings, halfX + flare1, halfZ + flare1 );
			if ( ring0.Length != ringCount || ring1.Length != ringCount )
				continue;

			for ( int i = 0; i < ringCount; i++ )
			{
				int iNext = ( i + 1 ) % ringCount;
				RingPoint a0r = ring0[ i ];
				RingPoint b0r = ring0[ iNext ];
				RingPoint a1r = ring1[ i ];
				RingPoint b1r = ring1[ iNext ];

				Vector3 p00 = new Vector3( a0r.Position.x, y0, a0r.Position.y );
				Vector3 p10 = new Vector3( b0r.Position.x, y0, b0r.Position.y );
				Vector3 p01 = new Vector3( a1r.Position.x, y1, a1r.Position.y );
				Vector3 p11 = new Vector3( b1r.Position.x, y1, b1r.Position.y );

				Vector3 faceNormal = Vector3.Cross( p10 - p00, p01 - p00 );
				if ( faceNormal.sqrMagnitude < 1e-10f )
					faceNormal = new Vector3( a0r.Position.x, 0f, a0r.Position.y );
				faceNormal.Normalize();

				float alpha00 = EvaluateVertexAlpha( settings, t0, a0r.EdgeT, y0, tint.a );
				float alpha10 = EvaluateVertexAlpha( settings, t0, b0r.EdgeT, y0, tint.a );
				float alpha01 = EvaluateVertexAlpha( settings, t1, a1r.EdgeT, y1, tint.a );
				float alpha11 = EvaluateVertexAlpha( settings, t1, b1r.EdgeT, y1, tint.a );

				Color c00 = new Color( tint.r, tint.g, tint.b, alpha00 );
				Color c10 = new Color( tint.r, tint.g, tint.b, alpha10 );
				Color c01 = new Color( tint.r, tint.g, tint.b, alpha01 );
				Color c11 = new Color( tint.r, tint.g, tint.b, alpha11 );

				AddQuad( positions, normals, uvs, colors, triangles,
					p00, p10, p01, p11,
					faceNormal,
					new Vector2( a0r.PerimeterU, t0 ), new Vector2( b0r.PerimeterU, t0 ), new Vector2( a1r.PerimeterU, t1 ), new Vector2( b1r.PerimeterU, t1 ),
					c00, c10, c01, c11 );
			}
		}

		if ( settings.BottomCap || forceCap )
			AddBottomCap( positions, normals, uvs, colors, triangles, baseRing, tint, settings );
	}

	static void AddBottomCap( List<Vector3> positions, List<Vector3> normals, List<Vector2> uvs, List<Color> colors, List<int> triangles, RingPoint[] ring, Color tint, CompleteGlowSettings settings )
	{
		Vector3 center = Vector3.zero;
		Color centerColor = new Color( tint.r, tint.g, tint.b, EvaluateVertexAlpha( settings, 0f, 0f, 0f, tint.a ) );
		int centerIndex = positions.Count;
		positions.Add( center );
		normals.Add( Vector3.down );
		uvs.Add( new Vector2( 0f, 0f ) );
		colors.Add( centerColor );

		for ( int i = 0; i < ring.Length; i++ )
		{
			int iNext = ( i + 1 ) % ring.Length;
			RingPoint a = ring[ i ];
			RingPoint b = ring[ iNext ];
			Vector3 pa = new Vector3( a.Position.x, 0f, a.Position.y );
			Vector3 pb = new Vector3( b.Position.x, 0f, b.Position.y );
			float alphaA = EvaluateVertexAlpha( settings, 0f, a.EdgeT, 0f, tint.a );
			float alphaB = EvaluateVertexAlpha( settings, 0f, b.EdgeT, 0f, tint.a );
			Color ca = new Color( tint.r, tint.g, tint.b, alphaA );
			Color cb = new Color( tint.r, tint.g, tint.b, alphaB );

			int i0 = positions.Count;
			positions.Add( pa );
			normals.Add( Vector3.down );
			uvs.Add( new Vector2( a.PerimeterU, 0f ) );
			colors.Add( ca );

			int i1 = positions.Count;
			positions.Add( pb );
			normals.Add( Vector3.down );
			uvs.Add( new Vector2( b.PerimeterU, 0f ) );
			colors.Add( cb );

			triangles.Add( centerIndex );
			triangles.Add( i1 );
			triangles.Add( i0 );
		}
	}

	static void AddRayCards( List<Vector3> positions, List<Vector3> normals, List<Vector2> uvs, List<Color> colors, List<int> triangles, CompleteGlowSettings settings, float halfX, float halfZ )
	{
		int count = Mathf.Clamp( settings.RayCardCount, 0, 8 );
		if ( count <= 0 )
			return;

		float height = Mathf.Max( 0.01f, settings.Height * Mathf.Max( 0.05f, settings.RayCardHeightScale ) );
		float widthScale = Mathf.Max( 0.05f, settings.RayCardWidthScale );
		float radius = Mathf.Max( halfX, halfZ ) * widthScale;
		float flareTan = Mathf.Tan( settings.FlareAngleDegrees * Mathf.Deg2Rad );
		float topRadius = radius + height * flareTan * EvaluateFlare( settings, 1f );
		float baseAngle = settings.RayCardRandomRotation + ( settings.Seed * 17.13f );
		float alpha = Mathf.Clamp01( settings.RayCardAlpha );

		for ( int i = 0; i < count; i++ )
		{
			float angle = ( baseAngle + ( 180f / count ) * i ) * Mathf.Deg2Rad;
			Vector3 right = new Vector3( Mathf.Cos( angle ), 0f, Mathf.Sin( angle ) );
			Vector3 normal = new Vector3( -right.z, 0f, right.x );

			Vector3 p00 = -right * radius;
			Vector3 p10 = right * radius;
			Vector3 p01 = -right * topRadius + Vector3.up * height;
			Vector3 p11 = right * topRadius + Vector3.up * height;

			Color c0 = new Color( 1f, 1f, 1f, EvaluateVertexAlpha( settings, 0f, 0f, 0f, alpha ) );
			Color c1 = new Color( 1f, 1f, 1f, EvaluateVertexAlpha( settings, 1f, 0f, height, alpha ) );

			AddQuad( positions, normals, uvs, colors, triangles,
				p00, p10, p01, p11,
				normal,
				new Vector2( 0f, 0f ), new Vector2( radius * 2f, 0f ), new Vector2( 0f, 1f ), new Vector2( topRadius * 2f, 1f ),
				c0, c0, c1, c1 );
		}
	}

	static void AddQuad( List<Vector3> positions, List<Vector3> normals, List<Vector2> uvs, List<Color> colors, List<int> triangles, Vector3 p00, Vector3 p10, Vector3 p01, Vector3 p11, Vector3 normal, Vector2 uv00, Vector2 uv10, Vector2 uv01, Vector2 uv11, Color c00, Color c10, Color c01, Color c11 )
	{
		int start = positions.Count;
		positions.Add( p00 );
		positions.Add( p10 );
		positions.Add( p01 );
		positions.Add( p11 );
		normals.Add( normal );
		normals.Add( normal );
		normals.Add( normal );
		normals.Add( normal );
		uvs.Add( uv00 );
		uvs.Add( uv10 );
		uvs.Add( uv01 );
		uvs.Add( uv11 );
		colors.Add( c00 );
		colors.Add( c10 );
		colors.Add( c01 );
		colors.Add( c11 );
		triangles.Add( start );
		triangles.Add( start + 2 );
		triangles.Add( start + 1 );
		triangles.Add( start + 1 );
		triangles.Add( start + 2 );
		triangles.Add( start + 3 );
	}

	static float EvaluateFlare( CompleteGlowSettings settings, float t )
	{
		if ( settings.FlareCurve == null || settings.FlareCurve.length == 0 )
			return t;
		return Mathf.Max( 0f, settings.FlareCurve.Evaluate( Mathf.Clamp01( t ) ) );
	}

	static float EvaluateVertexAlpha( CompleteGlowSettings settings, float heightT, float edgeT, float y, float layerAlpha )
	{
		float heightAlpha = 1f;
		if ( settings.HeightFalloffCurve != null && settings.HeightFalloffCurve.length > 0 )
			heightAlpha = Mathf.Clamp01( settings.HeightFalloffCurve.Evaluate( Mathf.Clamp01( heightT ) ) );

		float baseFade = 1f;
		if ( settings.BaseFadeIn > 0.0001f )
			baseFade = Mathf.Clamp01( y / settings.BaseFadeIn );

		float edgeSoft = Mathf.Clamp01( settings.EdgeSoftness );
		float edgeAlpha = 1f;
		if ( edgeSoft > 0.0001f )
		{
			float softStart = 1f - edgeSoft;
			edgeAlpha = 1f - Mathf.SmoothStep( softStart, 1f, Mathf.Clamp01( edgeT ) );
		}

		return Mathf.Clamp01( heightAlpha * baseFade * edgeAlpha * layerAlpha );
	}

	static RingPoint[] BuildRing( CompleteGlowSettings settings, float halfX, float halfZ )
	{
		switch ( settings.CrossSection )
		{
			case CompleteGlowCrossSection.Circle:
				return BuildCircleRing( halfX, halfZ, settings.CircleSegments );
			case CompleteGlowCrossSection.RoundedSquare:
				return BuildRoundedSquareRing( halfX, halfZ, settings.CornerRadius, settings.CornerSegments );
			default:
				return BuildSquareRing( halfX, halfZ );
		}
	}

	static RingPoint[] BuildSquareRing( float halfX, float halfZ )
	{
		Vector2[] corners =
		{
			new Vector2( -halfX, -halfZ ),
			new Vector2( halfX, -halfZ ),
			new Vector2( halfX, halfZ ),
			new Vector2( -halfX, halfZ )
		};

		RingPoint[] ring = new RingPoint[ 8 ];
		float perimeter = 0f;
		int write = 0;
		for ( int i = 0; i < 4; i++ )
		{
			Vector2 a = corners[ i ];
			Vector2 b = corners[ ( i + 1 ) % 4 ];
			ring[ write++ ] = new RingPoint { Position = a, PerimeterU = perimeter, EdgeT = 1f };
			perimeter += Vector2.Distance( a, b ) * 0.5f;
			ring[ write++ ] = new RingPoint { Position = Vector2.Lerp( a, b, 0.5f ), PerimeterU = perimeter, EdgeT = 0f };
			perimeter += Vector2.Distance( a, b ) * 0.5f;
		}

		return ring;
	}

	static RingPoint[] BuildRoundedSquareRing( float halfX, float halfZ, float cornerRadius, int cornerSegments )
	{
		cornerSegments = Mathf.Clamp( cornerSegments, 1, 8 );
		float radius = Mathf.Min( cornerRadius, halfX, halfZ );
		if ( radius <= 0.0001f )
			return BuildSquareRing( halfX, halfZ );

		List<RingPoint> points = new List<RingPoint>( 32 );
		float perimeter = 0f;
		Vector2 prev = Vector2.zero;
		bool hasPrev = false;

		AddRoundedCorner( points, ref perimeter, ref prev, ref hasPrev, new Vector2( halfX - radius, -halfZ + radius ), radius, 270f, 360f, cornerSegments );
		AddRoundedCorner( points, ref perimeter, ref prev, ref hasPrev, new Vector2( halfX - radius, halfZ - radius ), radius, 0f, 90f, cornerSegments );
		AddRoundedCorner( points, ref perimeter, ref prev, ref hasPrev, new Vector2( -halfX + radius, halfZ - radius ), radius, 90f, 180f, cornerSegments );
		AddRoundedCorner( points, ref perimeter, ref prev, ref hasPrev, new Vector2( -halfX + radius, -halfZ + radius ), radius, 180f, 270f, cornerSegments );

		return points.ToArray();
	}

	static void AddRoundedCorner( List<RingPoint> points, ref float perimeter, ref Vector2 prev, ref bool hasPrev, Vector2 center, float radius, float startDeg, float endDeg, int segments )
	{
		for ( int i = 0; i <= segments; i++ )
		{
			float t = (float)i / segments;
			float deg = Mathf.Lerp( startDeg, endDeg, t );
			float rad = deg * Mathf.Deg2Rad;
			Vector2 p = center + new Vector2( Mathf.Cos( rad ), Mathf.Sin( rad ) ) * radius;
			float edgeT = 1f - Mathf.Abs( t * 2f - 1f );
			if ( hasPrev )
				perimeter += Vector2.Distance( prev, p );
			points.Add( new RingPoint { Position = p, PerimeterU = perimeter, EdgeT = edgeT } );
			prev = p;
			hasPrev = true;
		}
	}

	static RingPoint[] BuildCircleRing( float halfX, float halfZ, int segments )
	{
		segments = Mathf.Clamp( segments, 8, 64 );
		RingPoint[] ring = new RingPoint[ segments ];
		float perimeter = 0f;
		Vector2 prev = Vector2.zero;
		for ( int i = 0; i < segments; i++ )
		{
			float t = (float)i / segments;
			float rad = t * Mathf.PI * 2f;
			Vector2 p = new Vector2( Mathf.Cos( rad ) * halfX, Mathf.Sin( rad ) * halfZ );
			if ( i > 0 )
				perimeter += Vector2.Distance( prev, p );
			ring[ i ] = new RingPoint { Position = p, PerimeterU = perimeter, EdgeT = 0f };
			prev = p;
		}

		return ring;
	}
}
