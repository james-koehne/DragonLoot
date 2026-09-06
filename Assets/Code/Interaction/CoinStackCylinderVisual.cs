using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Stretched coin mesh for a stack cylinder. Homogeneous stacks use per-metal materials;
/// mixed stacks use the multi-type material with a per-band type lookup texture.
/// </summary>
[DisallowMultipleComponent]
public class CoinStackCylinderVisual : MonoBehaviour
{
	const string CoinCountProp = "_CoinCount";
	const string MeshBoundsMinYProp = "_MeshBoundsMinY";
	const string MeshBoundsSizeYProp = "_MeshBoundsSizeY";
	const string CoinTypeMapProp = "_CoinTypeMap";
	const string TypeCountProp = "_TypeCount";
	const string TypeFresnelProp = "_TypeFresnelColor";
	const string VariationSeedProp = "_VariationSeed";
	const string UseBakedCoinIndexProp = "_UseBakedCoinIndex";
	const string ChunkBaseIndexProp = "_ChunkBaseIndex";

	static readonly Vector4[] TypeFresnelScratch = new Vector4[CoinStackVisualDefinition.MaxTypeSlots];

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
	float _displayedHeight;
	float _heightSmoothVelocity;
	bool _smoothHeight;
	float _targetDiameter;
	float _meshRefDiameter = 1f;
	float _meshRefHeight = 0.1f;
	float _meshBoundsMinY;
	float _meshBoundsSizeY = 0.1f;
	float _diameterScale = 1f;
	float _variationSeed = 1f;
	Texture2D _typeMap;
	Color[] _typeMapPixels;
	int _typeMapBaseIndex;

	public int DisplayedCount => _targetCount;

	public MeshRenderer MeshRenderer => meshRenderer;
	public float VariationSeed => _variationSeed;
	public Texture2D TypeMap => _typeMap;

	public void SetVariationSeed( float seed )
	{
		_variationSeed = Mathf.Abs( seed ) < 0.0001f ? 1f : seed;
		if ( meshRenderer != null && _mpb != null )
		{
			meshRenderer.GetPropertyBlock( _mpb );
			_mpb.SetFloat( VariationSeedProp, _variationSeed );
			meshRenderer.SetPropertyBlock( _mpb );
		}
	}

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
		StripSparkleMaskContributor();
	}

	void OnEnable()
	{
		StripSparkleMaskContributor();
	}

	void OnDestroy()
	{
		if ( _typeMap != null )
		{
			if ( Application.isPlaying )
				Destroy( _typeMap );
			else
				DestroyImmediate( _typeMap );
			_typeMap = null;
		}
	}

	void StripSparkleMaskContributor()
	{
		TreasureSparkleMaskContributor contributor = GetComponent<TreasureSparkleMaskContributor>();
		if ( contributor == null )
			return;

		if ( Application.isPlaying )
			Destroy( contributor );
		else
			DestroyImmediate( contributor );
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

		SetDisplayedHeight( visible ? _targetHeight : 0f, animate: false );
	}

	/// <summary>
	/// Multi-type cylinder: one mesh covering every slot, band materials from type LUT.
	/// </summary>
	public void SetStackMulti(
		IList<TreasureDefinition> slots,
		bool snap,
		float diameter = -1f,
		float totalStackHeight = -1f )
	{
		SetStackMulti( slots, snap, diameter, totalStackHeight, typeMapSlots: null, typeMapBaseIndex: 0 );
	}

	/// <param name="typeMapSlots">
	/// Full stack type sequence for the LUT. When null, <paramref name="slots"/> is used.
	/// </param>
	/// <param name="typeMapBaseIndex">
	/// Global slot index of the first coin in this cylinder (for upper runs above imperfect chunks).
	/// </param>
	public void SetStackMulti(
		IList<TreasureDefinition> slots,
		bool snap,
		float diameter,
		float totalStackHeight,
		IList<TreasureDefinition> typeMapSlots,
		int typeMapBaseIndex )
	{
		EnsureVisual();
		ApplyDefinitionSettings();

		int count = slots != null ? slots.Count : 0;
		_treasure = count > 0 ? slots[ 0 ] : null;
		_targetCount = Mathf.Max( 0, count );
		_typeMapBaseIndex = Mathf.Max( 0, typeMapBaseIndex );

		float height = 0f;
		float maxDiameter = 0f;
		CoinStackVisualDefinition def = Definition;
		for ( int i = 0; i < count; i++ )
		{
			TreasureDefinition slot = slots[ i ];
			height += TreasureStackSpacing.GetStep( slot );
			float d = ResolveDiameter( slot );
			if ( d > maxDiameter )
				maxDiameter = d;
		}

		_targetHeight = totalStackHeight > 0.0001f ? totalStackHeight : height;
		_targetDiameter = diameter > 0.0001f ? diameter : ( maxDiameter > 0.0001f ? maxDiameter : 0.2f );

		ApplyMesh();
		Material multiMat = def != null ? def.multiStackMaterial : null;
		ApplyMultiMaterial();
		bool usingMulti = multiMat != null && meshRenderer != null && meshRenderer.sharedMaterial == multiMat;
		if ( usingMulti )
		{
			IList<TreasureDefinition> mapSource = typeMapSlots != null ? typeMapSlots : slots;
			UploadTypeMap( mapSource, def );
			ApplyMpbMulti( _targetCount, def, useBakedCoinIndex: false, chunkBaseIndex: _typeMapBaseIndex );
		}
		else
		{
			ApplyMpb( _targetCount );
		}

		bool visible = _targetCount > 0;
		if ( meshRenderer != null )
			meshRenderer.enabled = visible;

		SetDisplayedHeight( visible ? _targetHeight : 0f, animate: !snap );
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

	public void SnapToCountMulti( IList<TreasureDefinition> slots, float diameter = -1f, float totalStackHeight = -1f )
	{
		SetStackMulti( slots, snap: true, diameter, totalStackHeight );
	}

	public void SnapToCountMulti(
		IList<TreasureDefinition> slots,
		float diameter,
		float totalStackHeight,
		IList<TreasureDefinition> typeMapSlots,
		int typeMapBaseIndex )
	{
		SetStackMulti( slots, snap: true, diameter, totalStackHeight, typeMapSlots, typeMapBaseIndex );
	}

	/// <summary>
	/// Uploads the shared multi-type LUT without changing cylinder height/mesh (used when
	/// imperfect chunks cover the whole stack).
	/// </summary>
	public void EnsureSharedTypeMap( IList<TreasureDefinition> slots )
	{
		EnsureVisual();
		ApplyDefinitionSettings();
		ApplyMultiMaterial();
		CoinStackVisualDefinition def = Definition;
		UploadTypeMap( slots, def );
	}

	/// <summary>
	/// Applies the shared multi-type MPB to an imperfect chunk renderer.
	/// </summary>
	public void ApplyImperfectChunkMpb(
		MeshRenderer target,
		int chunkCoinCount,
		int chunkBaseIndex,
		CoinStackVisualDefinition def )
	{
		if ( target == null )
			return;

		if ( _mpb == null )
			_mpb = new MaterialPropertyBlock();

		FillMultiMpb( _mpb, Mathf.Max( 1, chunkCoinCount ), def, useBakedCoinIndex: true, chunkBaseIndex );
		target.SetPropertyBlock( _mpb );
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

	void ApplyMultiMaterial()
	{
		if ( meshRenderer == null )
			return;

		CoinStackVisualDefinition def = Definition;
		Material mat = def != null ? def.multiStackMaterial : null;
		if ( mat == null )
		{
			// Fallback: homogeneous material from first slot type.
			ApplyMaterial( _treasure );
			return;
		}

		if ( meshRenderer.sharedMaterial != mat )
			meshRenderer.sharedMaterial = mat;
	}

	void UploadTypeMap( IList<TreasureDefinition> slots, CoinStackVisualDefinition def )
	{
		int count = Mathf.Max( 1, slots != null ? slots.Count : 0 );
		EnsureTypeMapCapacity( count );

		for ( int i = 0; i < count; i++ )
		{
			int typeIndex = 0;
			if ( slots != null && i < slots.Count && def != null )
				typeIndex = def.ResolveTypeIndex( slots[ i ] );
			_typeMapPixels[ i ] = new Color( typeIndex, 0f, 0f, 1f );
		}

		for ( int i = count; i < _typeMapPixels.Length; i++ )
			_typeMapPixels[ i ] = _typeMapPixels[ count - 1 ];

		_typeMap.SetPixels( _typeMapPixels );
		_typeMap.Apply( false, false );
	}

	void EnsureTypeMapCapacity( int count )
	{
		int width = Mathf.NextPowerOfTwo( Mathf.Max( 1, count ) );
		if ( _typeMap != null && _typeMap.width >= width )
		{
			if ( _typeMapPixels == null || _typeMapPixels.Length != _typeMap.width )
				_typeMapPixels = new Color[ _typeMap.width ];
			return;
		}

		if ( _typeMap != null )
		{
			if ( Application.isPlaying )
				Destroy( _typeMap );
			else
				DestroyImmediate( _typeMap );
		}

		_typeMap = new Texture2D( width, 1, TextureFormat.RFloat, false, true )
		{
			name = "CoinStackTypeMap",
			filterMode = FilterMode.Point,
			wrapMode = TextureWrapMode.Clamp,
			anisoLevel = 0
		};
		_typeMapPixels = new Color[ width ];
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
		_mpb.SetFloat( VariationSeedProp, _variationSeed );
		_mpb.SetFloat( UseBakedCoinIndexProp, 0f );
		_mpb.SetFloat( ChunkBaseIndexProp, 0f );
		meshRenderer.SetPropertyBlock( _mpb );
	}

	void ApplyMpbMulti( int count, CoinStackVisualDefinition def, bool useBakedCoinIndex, int chunkBaseIndex )
	{
		if ( meshRenderer == null )
			return;

		if ( _mpb == null )
			_mpb = new MaterialPropertyBlock();

		FillMultiMpb( _mpb, count, def, useBakedCoinIndex, chunkBaseIndex );
		meshRenderer.SetPropertyBlock( _mpb );
	}

	void FillMultiMpb(
		MaterialPropertyBlock mpb,
		int count,
		CoinStackVisualDefinition def,
		bool useBakedCoinIndex,
		int chunkBaseIndex )
	{
		mpb.Clear();
		mpb.SetFloat( CoinCountProp, Mathf.Max( 1, count ) );
		mpb.SetFloat( MeshBoundsMinYProp, _meshBoundsMinY );
		mpb.SetFloat( MeshBoundsSizeYProp, _meshBoundsSizeY );
		mpb.SetFloat( VariationSeedProp, _variationSeed );
		mpb.SetFloat( UseBakedCoinIndexProp, useBakedCoinIndex ? 1f : 0f );
		mpb.SetFloat( ChunkBaseIndexProp, Mathf.Max( 0, chunkBaseIndex ) );

		if ( _typeMap != null )
			mpb.SetTexture( CoinTypeMapProp, _typeMap );

		int typeCount = 3;
		if ( def != null )
			typeCount = def.typeCount > 0
				? Mathf.Clamp( def.typeCount, 1, CoinStackVisualDefinition.MaxTypeSlots )
				: 3;
		mpb.SetFloat( TypeCountProp, typeCount );

		FillTypeFresnelArray( def, typeCount );
		mpb.SetVectorArray( TypeFresnelProp, TypeFresnelScratch );
	}

	static void FillTypeFresnelArray( CoinStackVisualDefinition def, int typeCount )
	{
		for ( int i = 0; i < TypeFresnelScratch.Length; i++ )
			TypeFresnelScratch[ i ] = new Vector4( 1f, 0.82f, 0.45f, 1f );

		if ( def == null )
			return;

		if ( def.goldStackMaterial != null && def.goldStackMaterial.HasProperty( "_FresnelColor" ) )
			TypeFresnelScratch[ Mathf.Clamp( def.goldTypeIndex, 0, TypeFresnelScratch.Length - 1 ) ] =
				def.goldStackMaterial.GetColor( "_FresnelColor" );
		if ( def.silverStackMaterial != null && def.silverStackMaterial.HasProperty( "_FresnelColor" ) )
			TypeFresnelScratch[ Mathf.Clamp( def.silverTypeIndex, 0, TypeFresnelScratch.Length - 1 ) ] =
				def.silverStackMaterial.GetColor( "_FresnelColor" );
		if ( def.copperStackMaterial != null && def.copperStackMaterial.HasProperty( "_FresnelColor" ) )
			TypeFresnelScratch[ Mathf.Clamp( def.copperTypeIndex, 0, TypeFresnelScratch.Length - 1 ) ] =
				def.copperStackMaterial.GetColor( "_FresnelColor" );

		_ = typeCount;
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

	void SetDisplayedHeight( float height, bool animate )
	{
		_targetHeight = Mathf.Max( 0f, height );
		if ( !animate || !Application.isPlaying )
		{
			_smoothHeight = false;
			_heightSmoothVelocity = 0f;
			_displayedHeight = _targetHeight;
			ApplyTransform( _displayedHeight );
			return;
		}

		_smoothHeight = true;
	}

	void LateUpdate()
	{
		if ( !_smoothHeight )
			return;

		_displayedHeight = Mathf.SmoothDamp(
			_displayedHeight,
			_targetHeight,
			ref _heightSmoothVelocity,
			0.08f,
			Mathf.Infinity,
			Time.deltaTime );
		ApplyTransform( _displayedHeight );

		if ( Mathf.Abs( _displayedHeight - _targetHeight ) < 0.0005f
			&& Mathf.Abs( _heightSmoothVelocity ) < 0.0005f )
		{
			_displayedHeight = _targetHeight;
			_heightSmoothVelocity = 0f;
			_smoothHeight = false;
			ApplyTransform( _displayedHeight );
		}
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
