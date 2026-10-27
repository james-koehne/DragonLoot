using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// World-space join arrow for ground-stack place aim.
/// Beside-aim: points from the floor ghost toward the stack top.
/// Direct-aim: hangs above the stack top and points straight down onto it.
/// </summary>
public sealed class StackJoinAimArrow
{
	static readonly int BaseColorId = Shader.PropertyToID( "_BaseColor" );
	static readonly int ColorId = Shader.PropertyToID( "_Color" );

	readonly GameObject _root;
	readonly Transform _rootTransform;
	readonly Material _material;

	const float ShaftLength = 0.22f;
	const float ShaftRadius = 0.022f;
	const float HeadLength = 0.09f;
	const float HeadRadius = 0.05f;
	const float DefaultTotalLength = ShaftLength + HeadLength;
	/// <summary>Raise above the floor coin ghost so the shaft does not clip through it.</summary>
	const float Lift = 0.28f;
	/// <summary>Keep the tip short of the stack top so it does not bury into the pile.</summary>
	const float TipClearance = 0.14f;
	/// <summary>How high above the join slot the direct-aim arrow starts.</summary>
	const float DirectHover = 0.38f;
	/// <summary>Fixed visual length when pointing straight down onto a stack.</summary>
	const float DirectLength = 0.26f;

	public StackJoinAimArrow()
	{
		_root = new GameObject( "StackJoinAimArrow" );
		_rootTransform = _root.transform;

		Shader shader = Shader.Find( "Universal Render Pipeline/Unlit" );
		if ( shader == null )
			shader = Shader.Find( "Unlit/Color" );
		if ( shader == null )
			shader = Shader.Find( "Sprites/Default" );

		_material = new Material( shader );
		ApplyColor( PlacementFeedbackColors.JoinRgb );

		CreateShaft();
		CreateHead();
		SetVisible( false );
	}

	void CreateShaft()
	{
		GameObject shaft = GameObject.CreatePrimitive( PrimitiveType.Cylinder );
		shaft.name = "Shaft";
		Object.Destroy( shaft.GetComponent<Collider>() );
		shaft.transform.SetParent( _rootTransform, false );
		shaft.transform.localPosition = new Vector3( 0f, 0f, ShaftLength * 0.5f );
		shaft.transform.localRotation = Quaternion.Euler( 90f, 0f, 0f );
		shaft.transform.localScale = new Vector3( ShaftRadius * 2f, ShaftLength * 0.5f, ShaftRadius * 2f );
		ConfigureRenderer( shaft.GetComponent<MeshRenderer>() );
	}

	void CreateHead()
	{
		GameObject head = new GameObject( "Head" );
		head.transform.SetParent( _rootTransform, false );
		head.transform.localPosition = new Vector3( 0f, 0f, ShaftLength );
		head.transform.localRotation = Quaternion.identity;

		MeshFilter filter = head.AddComponent<MeshFilter>();
		filter.sharedMesh = BuildConeMesh( HeadRadius, HeadLength, 12 );
		MeshRenderer renderer = head.AddComponent<MeshRenderer>();
		ConfigureRenderer( renderer );
	}

	void ConfigureRenderer( MeshRenderer renderer )
	{
		if ( renderer == null )
			return;

		renderer.sharedMaterial = _material;
		renderer.shadowCastingMode = ShadowCastingMode.Off;
		renderer.receiveShadows = false;
	}

	static Mesh BuildConeMesh( float radius, float length, int segments )
	{
		segments = Mathf.Max( 3, segments );
		Vector3[] vertices = new Vector3[ segments + 2 ];
		vertices[ 0 ] = new Vector3( 0f, 0f, length );
		vertices[ 1 ] = Vector3.zero;
		for ( int i = 0; i < segments; i++ )
		{
			float angle = ( i / (float)segments ) * Mathf.PI * 2f;
			vertices[ i + 2 ] = new Vector3( Mathf.Cos( angle ) * radius, Mathf.Sin( angle ) * radius, 0f );
		}

		int[] triangles = new int[ segments * 6 ];
		int t = 0;
		for ( int i = 0; i < segments; i++ )
		{
			int a = i + 2;
			int b = ( i + 1 ) % segments + 2;
			triangles[ t++ ] = 0;
			triangles[ t++ ] = a;
			triangles[ t++ ] = b;
			triangles[ t++ ] = 1;
			triangles[ t++ ] = b;
			triangles[ t++ ] = a;
		}

		Mesh mesh = new Mesh { name = "StackJoinArrowCone" };
		mesh.vertices = vertices;
		mesh.triangles = triangles;
		mesh.RecalculateNormals();
		mesh.RecalculateBounds();
		return mesh;
	}

	void ApplyColor( Color color )
	{
		if ( _material == null )
			return;

		Color opaque = color;
		opaque.a = 1f;
		_material.color = opaque;
		if ( _material.HasProperty( BaseColorId ) )
			_material.SetColor( BaseColorId, opaque );
		if ( _material.HasProperty( ColorId ) )
			_material.SetColor( ColorId, opaque );
	}

	public void SetColor( Color color )
	{
		ApplyColor( color );
	}

	public void Show( Vector3 from, Vector3 toward, Color color )
	{
		SetColor( color );

		Vector3 origin = from + Vector3.up * Lift;
		Vector3 toTop = toward - origin;
		float dist = toTop.magnitude;
		if ( dist < 0.04f )
		{
			Hide();
			return;
		}

		Vector3 dir = toTop / dist;
		float usable = Mathf.Max( 0.06f, dist - TipClearance );
		float lengthScale = Mathf.Clamp( usable / DefaultTotalLength, 0.2f, 1f );

		_rootTransform.localScale = new Vector3( 1f, 1f, lengthScale );
		_rootTransform.SetPositionAndRotation( origin, Quaternion.LookRotation( dir, Vector3.up ) );
		SetVisible( true );
	}

	/// <summary>Arrow above <paramref name="toward"/> pointing straight down onto the join slot.</summary>
	public void ShowPointingDown( Vector3 toward, Color color )
	{
		SetColor( color );

		float lengthScale = Mathf.Clamp( DirectLength / DefaultTotalLength, 0.2f, 1f );
		Vector3 origin = toward + Vector3.up * DirectHover;
		_rootTransform.localScale = new Vector3( 1f, 1f, lengthScale );
		_rootTransform.SetPositionAndRotation( origin, Quaternion.LookRotation( Vector3.down, Vector3.forward ) );
		SetVisible( true );
	}

	public void Hide()
	{
		SetVisible( false );
	}

	void SetVisible( bool visible )
	{
		if ( _root != null )
			_root.SetActive( visible );
	}

	public void Destroy()
	{
		if ( _material != null )
			Object.Destroy( _material );
		if ( _root != null )
			Object.Destroy( _root );
	}
}
