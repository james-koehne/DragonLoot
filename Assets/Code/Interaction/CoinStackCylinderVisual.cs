using UnityEngine;

/// <summary>
/// Single stretched coin mesh for a homogeneous tower. Height and side banding follow coin count;
/// materials and mesh come from <see cref="CoinStackVisualDefinition"/>.
/// </summary>
[DisallowMultipleComponent]
public class CoinStackCylinderVisual : MonoBehaviour
{
	const string CoinCountProp = "_CoinCount";
	const string MeshBoundsMinYProp = "_MeshBoundsMinY";
	const string MeshBoundsSizeYProp = "_MeshBoundsSizeY";

	[SerializeField]
	CoinStackVisualDefinition visualDefinition;

	[SerializeField]
	Transform visualRoot;

	[SerializeField]
	MeshRenderer meshRenderer;

	[SerializeField]
	MeshFilter meshFilter;

	MaterialPropertyBlock _mpb;
	CoinStackVisualDefinition _resolvedDefinition;
	TreasureDefinition _treasure;
	int _targetCount;
	float _targetHeight;
	float _targetDiameter;
	float _meshRefDiameter = 1f;
	float _meshRefHeight = 0.1f;
	float _meshBoundsMinY;
	float _meshBoundsSizeY = 0.1f;
	float _diameterScale = 1f;

	public int DisplayedCount => _targetCount;

	CoinStackVisualDefinition Definition
	{
		get
		{
			if ( visualDefinition != null )
				return visualDefinition;

			visualDefinition = RuntimeDefinition.Resolve( ref _resolvedDefinition );
			if ( visualDefinition != null )
				return visualDefinition;

#if UNITY_EDITOR
			if ( !Application.isPlaying )
			{
				visualDefinition = UnityEditor.AssetDatabase.LoadAssetAtPath<CoinStackVisualDefinition>(
					"Assets/Definitions/CoinStackVisualDefinition.asset" );
				return visualDefinition;
			}

			// Play Mode in editor: Addressables may not be ready yet for Test scenes.
			if ( visualDefinition == null )
			{
				visualDefinition = UnityEditor.AssetDatabase.LoadAssetAtPath<CoinStackVisualDefinition>(
					"Assets/Definitions/CoinStackVisualDefinition.asset" );
			}
#endif
			return visualDefinition;
		}
	}

	void Awake()
	{
		EnsureVisual();
		ApplyDefinitionSettings();
	}

	public void SetStack( TreasureDefinition definition, int count )
	{
		SetStack( definition, count, heightStep: -1f, diameter: -1f );
	}

	public void SetStack( TreasureDefinition definition, int count, float heightStep, float diameter )
	{
		SetStack( definition, count, heightStep, diameter, totalStackHeight: -1f );
	}

	public void SetStack(
		TreasureDefinition definition,
		int count,
		float heightStep,
		float diameter,
		float totalStackHeight )
	{
		EnsureVisual();
		ApplyDefinitionSettings();

		_treasure = definition;
		_targetCount = Mathf.Max( 0, count );

		float thickness = heightStep > 0.0001f
			? heightStep
			: TreasureStackSpacing.GetStep( definition );
		_targetHeight = totalStackHeight > 0.0001f
			? totalStackHeight
			: thickness * _targetCount;
		_targetDiameter = diameter > 0.0001f ? diameter : ResolveDiameter( definition );

		ApplyMesh();
		ApplyMaterial( definition );
		ApplyMpb( _targetCount );

		bool visible = _targetCount > 0 && definition != null;
		if ( meshRenderer != null )
			meshRenderer.enabled = visible;

		ApplyTransform( visible ? _targetHeight : 0f );
	}

	public void SnapToCount( TreasureDefinition definition, int count )
	{
		SnapToCount( definition, count, heightStep: -1f, diameter: -1f );
	}

	public void SnapToCount( TreasureDefinition definition, int count, float heightStep, float diameter )
	{
		SnapToCount( definition, count, heightStep, diameter, totalStackHeight: -1f );
	}

	public void SnapToCount(
		TreasureDefinition definition,
		int count,
		float heightStep,
		float diameter,
		float totalStackHeight )
	{
		SetStack( definition, count, heightStep, diameter, totalStackHeight );
	}

	void ApplyDefinitionSettings()
	{
		CoinStackVisualDefinition def = Definition;
		if ( def == null )
			return;

		_diameterScale = def.diameterScale;
		def.GetMeshReferenceSize( out _meshRefDiameter, out _meshRefHeight );
		def.GetMeshBoundsY( out _meshBoundsMinY, out _meshBoundsSizeY );
	}

	void EnsureVisual()
	{
		if ( visualRoot == null )
		{
			Transform existing = transform.Find( "StackVisual" );
			if ( existing == null )
				existing = transform.Find( "CylinderVisual" );

			if ( existing != null )
			{
				visualRoot = existing;
			}
			else
			{
				GameObject child = new GameObject( "StackVisual" );
				child.transform.SetParent( transform, false );
				visualRoot = child.transform;
				meshFilter = child.AddComponent<MeshFilter>();
				meshRenderer = child.AddComponent<MeshRenderer>();
			}
		}

		if ( meshFilter == null )
			meshFilter = visualRoot.GetComponent<MeshFilter>();
		if ( meshFilter == null )
			meshFilter = visualRoot.gameObject.AddComponent<MeshFilter>();

		if ( meshRenderer == null )
			meshRenderer = visualRoot.GetComponent<MeshRenderer>();
		if ( meshRenderer == null )
			meshRenderer = visualRoot.gameObject.AddComponent<MeshRenderer>();

		Collider builtIn = visualRoot.GetComponent<Collider>();
		if ( builtIn != null )
		{
			if ( Application.isPlaying )
				Destroy( builtIn );
			else
				DestroyImmediate( builtIn );
		}

		if ( _mpb == null )
			_mpb = new MaterialPropertyBlock();
	}

	void ApplyMesh()
	{
		if ( meshFilter == null )
			return;

		CoinStackVisualDefinition def = Definition;
		Mesh mesh = def != null ? def.stackMesh : null;
		if ( mesh != null && meshFilter.sharedMesh != mesh )
			meshFilter.sharedMesh = mesh;
	}

	void ApplyMaterial( TreasureDefinition definition )
	{
		if ( meshRenderer == null )
			return;

		CoinStackVisualDefinition def = Definition;
		Material mat = def != null ? def.ResolveMaterial( definition ) : null;
		if ( mat != null && meshRenderer.sharedMaterial != mat )
			meshRenderer.sharedMaterial = mat;
	}

	void ApplyMpb( int count )
	{
		if ( meshRenderer == null )
			return;

		if ( _mpb == null )
			_mpb = new MaterialPropertyBlock();

		meshRenderer.GetPropertyBlock( _mpb );
		_mpb.SetFloat( CoinCountProp, Mathf.Max( 1, count ) );
		_mpb.SetFloat( MeshBoundsMinYProp, _meshBoundsMinY );
		_mpb.SetFloat( MeshBoundsSizeYProp, _meshBoundsSizeY );
		meshRenderer.SetPropertyBlock( _mpb );
	}

	void ApplyTransform( float height )
	{
		if ( visualRoot == null )
			return;

		float safeHeight = Mathf.Max( 0f, height );
		float diameter = Mathf.Max( 0.001f, _targetDiameter * _diameterScale );
		float refDiameter = Mathf.Max( 0.0001f, _meshRefDiameter );
		float refHeight = Mathf.Max( 0.0001f, _meshRefHeight );

		Transform scaleParent = visualRoot.parent != null ? visualRoot.parent : transform;
		Vector3 parentLossy = scaleParent.lossyScale;

		float scaleY = SafeDivScale( safeHeight / refHeight, parentLossy.y );

		visualRoot.localRotation = Quaternion.identity;
		visualRoot.localPosition = Vector3.up * ( -_meshBoundsMinY * scaleY );
		visualRoot.localScale = new Vector3(
			SafeDivScale( diameter / refDiameter, parentLossy.x ),
			scaleY,
			SafeDivScale( diameter / refDiameter, parentLossy.z ) );
	}

	static float SafeDivScale( float value, float parentAxis )
	{
		return Mathf.Abs( parentAxis ) < 0.0001f ? value : value / parentAxis;
	}

	float ResolveDiameter( TreasureDefinition definition )
	{
		if ( definition == null )
			return 0.2f;

		float x = Mathf.Abs( definition.worldScale.x );
		float z = Mathf.Abs( definition.worldScale.z );
		float diameter = Mathf.Max( x, z );
		return diameter > 0.0001f ? diameter : 0.2f;
	}
}
