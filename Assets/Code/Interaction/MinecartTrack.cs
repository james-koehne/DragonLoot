using System.Collections.Generic;

using Unity.Mathematics;

using UnityEngine;
using UnityEngine.Splines;

/// <summary>
/// Scene-authored rail path: Unity Spline plus editor-baked sleeper/rail mesh.
/// Carts sample this path by distance. Create via Dragon Loot → Minecart → Create Track.
/// </summary>
[RequireComponent( typeof( SplineContainer ) )]
public class MinecartTrack : MonoBehaviour
{
	public const string VisualChildName = "TrackVisual";
	public const string ColliderChildName = "TrackCollider";

	static readonly List<MinecartTrack> All = new List<MinecartTrack>( 16 );

	const float MinLength = 0.05f;
	const int NearestResolution = 6;
	const int NearestIterations = 3;

	[Header( "Rails" )]
	[SerializeField]
	[Min( 0.1f )]
	float railGauge = 0.9f;

	[SerializeField]
	[Min( 0.01f )]
	float railWidth = 0.06f;

	[SerializeField]
	[Min( 0.01f )]
	float railHeight = 0.08f;

	[Header( "Sleepers" )]
	[SerializeField]
	[Min( 0.1f )]
	float sleeperSpacing = 0.7f;

	[SerializeField]
	Vector3 sleeperSize = new Vector3( 1.2f, 0.08f, 0.18f );

	[SerializeField]
	[Min( 0.05f )]
	float sampleStep = 0.15f;

	[Header( "Collider" )]
	[Tooltip( "Simple walkable strip along the spline. Larger steps = fewer faces and less CharacterController jitter." )]
	[SerializeField]
	[Min( 0.2f )]
	float colliderSampleStep = 0.5f;

	[Header( "Materials" )]
	[SerializeField]
	Material railMaterial;

	[SerializeField]
	Material sleeperMaterial;

	[SerializeField]
	Mesh bakedMesh;

	SplineContainer _container;
	float _cachedLength;
	bool _lengthDirty = true;

	public static IReadOnlyList<MinecartTrack> ActiveTracks => All;

	public SplineContainer Container
	{
		get
		{
			EnsureContainer();
			return _container;
		}
	}

	public Spline Spline
	{
		get
		{
			EnsureContainer();
			return _container != null ? _container.Spline : null;
		}
	}

	public float RailGauge => Mathf.Max( 0.1f, railGauge );

	public float RailWidth => Mathf.Max( 0.01f, railWidth );

	public float RailHeight => Mathf.Max( 0.01f, railHeight );

	public float SleeperSpacing => Mathf.Max( 0.1f, sleeperSpacing );

	public Vector3 SleeperSize => sleeperSize;

	public float SampleStep => Mathf.Max( 0.05f, sampleStep );

	public float ColliderSampleStep => Mathf.Max( 0.2f, colliderSampleStep );

	public Material RailMaterial => railMaterial;

	public Material SleeperMaterial => sleeperMaterial;

	public Mesh BakedMesh => bakedMesh;

	public float Length
	{
		get
		{
			EnsureContainer();
			if ( !_lengthDirty )
				return _cachedLength;

			if ( _container == null || _container.Spline == null || _container.Spline.Count < 2 )
			{
				_cachedLength = 0f;
				_lengthDirty = false;
				return 0f;
			}

			_cachedLength = _container.CalculateLength();
			_lengthDirty = false;
			return _cachedLength;
		}
	}

	public bool IsClosed
	{
		get
		{
			EnsureContainer();
			if ( _container == null || _container.Spline == null )
				return false;

			return _container.Spline.Closed;
		}
	}

	public bool IsUsable => Length >= MinLength;

	void OnEnable()
	{
		if ( !All.Contains( this ) )
			All.Add( this );

		InvalidateLength();
		RebuildWalkCollider();
	}

	void OnDisable()
	{
		All.Remove( this );
	}

	void OnValidate()
	{
		railGauge = Mathf.Max( 0.1f, railGauge );
		railWidth = Mathf.Max( 0.01f, railWidth );
		railHeight = Mathf.Max( 0.01f, railHeight );
		sleeperSpacing = Mathf.Max( 0.1f, sleeperSpacing );
		sampleStep = Mathf.Max( 0.05f, sampleStep );
		sleeperSize.x = Mathf.Max( 0.05f, sleeperSize.x );
		sleeperSize.y = Mathf.Max( 0.02f, sleeperSize.y );
		sleeperSize.z = Mathf.Max( 0.04f, sleeperSize.z );
		colliderSampleStep = Mathf.Max( 0.2f, colliderSampleStep );
		InvalidateLength();
	}

	void InvalidateLength()
	{
		_lengthDirty = true;
	}

	void EnsureContainer()
	{
		if ( _container == null )
			_container = GetComponent<SplineContainer>();
	}

	static void EnsureRegistryPopulated()
	{
		if ( All.Count > 0 )
			return;

		MinecartTrack[] found = Object.FindObjectsByType<MinecartTrack>( FindObjectsInactive.Exclude, FindObjectsSortMode.None );
		for ( int i = 0; i < found.Length; i++ )
		{
			MinecartTrack candidate = found[ i ];
			if ( candidate != null && !All.Contains( candidate ) )
				All.Add( candidate );
		}
	}

	public void EditorSetMaterials( Material rail, Material sleeper )
	{
		railMaterial = rail;
		sleeperMaterial = sleeper;
	}

	public void EditorSetBakedMesh( Mesh mesh )
	{
		bakedMesh = mesh;
	}

	public void RebuildWalkCollider()
	{
		InvalidateLength();
		StripVisualMeshCollider();
		if ( !IsUsable )
			return;

		Transform child = transform.Find( ColliderChildName );
		GameObject go = child != null ? child.gameObject : null;
		if ( go == null )
		{
			go = new GameObject( ColliderChildName );
			go.transform.SetParent( transform, false );
		}

		MeshCollider collider = go.GetComponent<MeshCollider>();
		if ( collider == null )
			collider = go.AddComponent<MeshCollider>();

		Mesh previous = collider.sharedMesh;
		Mesh mesh = BuildWalkColliderMesh();
		if ( mesh == null )
			return;

		collider.sharedMesh = mesh;
		collider.convex = false;
		if ( previous != null && previous != mesh && previous != bakedMesh )
		{
			if ( Application.isPlaying )
				Destroy( previous );
			else
				DestroyImmediate( previous );
		}
	}

	void StripVisualMeshCollider()
	{
		Transform visual = transform.Find( VisualChildName );
		if ( visual == null )
			return;

		MeshCollider visualCollider = visual.GetComponent<MeshCollider>();
		if ( visualCollider == null )
			return;

		if ( Application.isPlaying )
			Destroy( visualCollider );
		else
			DestroyImmediate( visualCollider );
	}

	Mesh BuildWalkColliderMesh()
	{
		float length = Length;
		if ( length < MinLength )
			return null;

		float step = ColliderSampleStep;
		int samples = Mathf.Max( 2, Mathf.CeilToInt( length / step ) + 1 );
		if ( IsClosed )
			samples = Mathf.Max( 3, Mathf.RoundToInt( length / step ) );

		float halfW = SleeperSize.x * 0.5f;
		float halfH = Mathf.Max( 0.04f, SleeperSize.y * 0.5f );
		int rings = samples;
		int segs = IsClosed ? samples : samples - 1;
		int vertCount = rings * 4;
		Vector3[] verts = new Vector3[ vertCount ];
		List<int> tris = new List<int>( segs * 24 + 12 );

		for ( int i = 0; i < rings; i++ )
		{
			float dist = IsClosed
				? ( length * i ) / samples
				: ( length * i ) / ( samples - 1 );
			Vector3 pos;
			Vector3 tan;
			Vector3 up;
			if ( !Evaluate( dist, out pos, out tan, out up ) )
				return null;

			Vector3 right = Vector3.Cross( up, tan );
			if ( right.sqrMagnitude < 0.0001f )
				right = transform.right;
			else
				right.Normalize();

			Vector3 center = pos + up * halfH;
			int v = i * 4;
			verts[ v + 0 ] = transform.InverseTransformPoint( center - right * halfW - up * halfH );
			verts[ v + 1 ] = transform.InverseTransformPoint( center + right * halfW - up * halfH );
			verts[ v + 2 ] = transform.InverseTransformPoint( center + right * halfW + up * halfH );
			verts[ v + 3 ] = transform.InverseTransformPoint( center - right * halfW + up * halfH );
		}

		for ( int i = 0; i < segs; i++ )
		{
			int a = i * 4;
			int b = ( ( i + 1 ) % rings ) * 4;
			AddColliderQuad( tris, a + 3, a + 2, b + 2, b + 3 );
			AddColliderQuad( tris, a + 1, a + 0, b + 0, b + 1 );
			AddColliderQuad( tris, a + 0, a + 3, b + 3, b + 0 );
			AddColliderQuad( tris, a + 2, a + 1, b + 1, b + 2 );
		}

		if ( !IsClosed )
		{
			int last = ( rings - 1 ) * 4;
			AddColliderQuad( tris, 0, 1, 2, 3 );
			AddColliderQuad( tris, last + 1, last + 0, last + 3, last + 2 );
		}

		Mesh mesh = new Mesh();
		mesh.name = gameObject.name + "_WalkCollider";
		mesh.vertices = verts;
		mesh.SetTriangles( tris, 0 );
		mesh.RecalculateBounds();
		mesh.RecalculateNormals();
		return mesh;
	}

	static void AddColliderQuad( List<int> tris, int a, int b, int c, int d )
	{
		tris.Add( a );
		tris.Add( b );
		tris.Add( c );
		tris.Add( a );
		tris.Add( c );
		tris.Add( d );
	}

	public static bool TryGetNearest( Vector3 worldPosition, float maxRadius, out MinecartTrack track, out float distance )
	{
		EnsureRegistryPopulated();
		track = null;
		distance = 0f;
		float best = maxRadius >= 0f ? maxRadius * maxRadius : float.MaxValue;
		bool found = false;

		for ( int i = 0; i < All.Count; i++ )
		{
			MinecartTrack candidate = All[ i ];
			if ( candidate == null || !candidate.isActiveAndEnabled || !candidate.IsUsable )
				continue;

			float candDistance;
			Vector3 nearest;
			if ( !candidate.TryGetNearestPoint( worldPosition, out candDistance, out nearest ) )
				continue;

			float sqr = ( nearest - worldPosition ).sqrMagnitude;
			if ( sqr >= best )
				continue;

			best = sqr;
			track = candidate;
			distance = candDistance;
			found = true;
		}

		return found;
	}

	public bool TryGetNearestPoint( Vector3 worldPosition, out float distance, out Vector3 nearestWorld )
	{
		distance = 0f;
		nearestWorld = worldPosition;
		EnsureContainer();
		if ( _container == null || _container.Spline == null || _container.Spline.Count < 2 )
			return false;

		float3 local = _container.transform.InverseTransformPoint( worldPosition );
		float3 nearestLocal;
		float t;
		SplineUtility.GetNearestPoint( _container.Spline, local, out nearestLocal, out t, NearestResolution, NearestIterations );
		nearestWorld = _container.transform.TransformPoint( (Vector3)nearestLocal );
		distance = SplineUtility.ConvertIndexUnit( _container.Spline, t, PathIndexUnit.Normalized, PathIndexUnit.Distance );
		return true;
	}

	public float GetNearestDistance( Vector3 worldPosition )
	{
		float distance;
		Vector3 nearest;
		if ( !TryGetNearestPoint( worldPosition, out distance, out nearest ) )
			return 0f;

		return distance;
	}

	public bool Evaluate( float distance, out Vector3 position, out Vector3 tangent, out Vector3 up )
	{
		position = transform.position;
		tangent = transform.forward;
		up = Vector3.up;
		EnsureContainer();
		if ( _container == null || _container.Spline == null || _container.Spline.Count < 2 )
			return false;

		float length = Length;
		if ( length < MinLength )
			return false;

		float wrapped = WrapOrClamp( distance, length );
		float t = SplineUtility.ConvertIndexUnit( _container.Spline, wrapped, PathIndexUnit.Distance, PathIndexUnit.Normalized );
		float3 pos;
		float3 tan;
		float3 up3;
		SplineUtility.Evaluate( _container.Spline, t, out pos, out tan, out up3 );
		Transform containerTransform = _container.transform;
		position = containerTransform.TransformPoint( (Vector3)pos );
		tangent = containerTransform.TransformDirection( (Vector3)tan );
		up = containerTransform.TransformDirection( (Vector3)up3 );
		if ( tangent.sqrMagnitude < 0.0001f )
			tangent = transform.forward;
		else
			tangent.Normalize();

		if ( up.sqrMagnitude < 0.0001f )
			up = Vector3.up;
		else
			up.Normalize();

		return true;
	}

	public float WrapDistance( float distance )
	{
		return WrapOrClamp( distance, Length );
	}

	public float SignedAlong( float from, float to )
	{
		return SignedSeparation( from, to, Length );
	}

	public bool TryAdvance( float distance, float signedDelta, MinecartInteractable ignoreCart, out float next )
	{
		float length = Length;
		next = distance;
		if ( length < MinLength )
			return false;

		next = WrapOrClamp( distance + signedDelta, length );
		next = ClampAgainstCarts( distance, next, signedDelta, ignoreCart, length );
		return Mathf.Abs( next - distance ) > 0.00001f
			|| ( IsClosed && Mathf.Abs( signedDelta ) > 0.00001f && !Mathf.Approximately( next, distance ) );
	}

	float ClampAgainstCarts( float from, float proposed, float signedDelta, MinecartInteractable ignoreCart, float length )
	{
		if ( Mathf.Abs( signedDelta ) < 0.00001f )
			return from;

		float result = proposed;
		float half = ignoreCart != null ? ignoreCart.BlockingHalfLength : 0.8f;
		if ( signedDelta < 0f && ignoreCart != null && ignoreCart.IsConsistLead )
			half += ignoreCart.FollowerCount * ignoreCart.ConsistSpacing;
		IReadOnlyList<MinecartInteractable> carts = MinecartInteractable.ActiveCarts;
		for ( int i = 0; i < carts.Count; i++ )
		{
			MinecartInteractable other = carts[ i ];
			if ( other == null || other == ignoreCart || other.BoundTrack != this )
				continue;

			if ( ignoreCart != null && ignoreCart.SharesConsistWith( other ) )
				continue;

			float otherHalf = other.BlockingHalfLength;
			float needed = half + otherHalf;
			float sep = SignedSeparation( from, other.DistanceAlongTrack, length );
			if ( signedDelta > 0f )
			{
				if ( sep <= 0.0001f )
					continue;

				float allowed = Mathf.Max( 0f, sep - needed );
				float traveled = SignedSeparation( from, result, length );
				if ( traveled > allowed )
					result = WrapOrClamp( from + allowed, length );
			}
			else
			{
				if ( sep >= -0.0001f )
					continue;

				float allowed = Mathf.Max( 0f, -sep - needed );
				float traveled = -SignedSeparation( from, result, length );
				if ( traveled > allowed )
					result = WrapOrClamp( from - allowed, length );
			}
		}

		return result;
	}

	float SignedSeparation( float from, float to, float length )
	{
		float delta = to - from;
		if ( !IsClosed || length < MinLength )
			return delta;

		if ( delta > length * 0.5f )
			delta -= length;
		if ( delta < -length * 0.5f )
			delta += length;

		return delta;
	}

	float WrapOrClamp( float distance, float length )
	{
		if ( length < MinLength )
			return 0f;

		if ( IsClosed )
		{
			float wrapped = distance % length;
			if ( wrapped < 0f )
				wrapped += length;

			return wrapped;
		}

		return Mathf.Clamp( distance, 0f, length );
	}
}
