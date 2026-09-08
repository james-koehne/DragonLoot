using System.Collections.Generic;

using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Reused translucent ghost mesh for placement preview. Rebuilds when the held item
/// identity changes, and draws every MeshFilter/submesh from the source visual.
/// </summary>
public sealed class PlacementGhost
{
	static readonly int BaseColorId = Shader.PropertyToID( "_BaseColor" );
	static readonly int ColorId = Shader.PropertyToID( "_Color" );
	static readonly int RimColorId = Shader.PropertyToID( "_RimColor" );
	static readonly int CoreColorId = Shader.PropertyToID( "_CoreColor" );
	static readonly int FresnelPowerId = Shader.PropertyToID( "_FresnelPower" );
	static readonly int FresnelBoostId = Shader.PropertyToID( "_FresnelBoost" );
	static readonly int RimIntensityId = Shader.PropertyToID( "_RimIntensity" );
	static readonly int CoreIntensityId = Shader.PropertyToID( "_CoreIntensity" );
	static readonly int PulseSpeedId = Shader.PropertyToID( "_PulseSpeed" );
	static readonly int PulseAmountId = Shader.PropertyToID( "_PulseAmount" );

	readonly GameObject _root;
	readonly Transform _rootTransform;
	readonly MaterialPropertyBlock _propertyBlock = new MaterialPropertyBlock();
	Material _material;
	readonly List<MeshRenderer> _renderers = new List<MeshRenderer>( 4 );
	TreasureItem _syncedItem;
	TreasureDefinition _syncedDefinition;
	Transform _syncedTransform;
	int _syncedTransformId;
	bool _visible;
	bool _lastValid = true;

	Color _validColor = PlacementFeedbackColors.ValidGhost;
	Color _invalidColor = PlacementFeedbackColors.InvalidGhost;
	float _fresnelPower = 2.4f;
	float _fresnelBoost = 0.7f;
	float _pulseAmount = 0.12f;
	float _pulseSpeed = 0.85f;
	float _rimIntensity = 1.15f;
	float _coreIntensity = 0.28f;

	public PlacementGhost() : this( null )
	{
	}

	public PlacementGhost( Shader shader )
	{
		_root = new GameObject( "PlacementGhost" );
		_rootTransform = _root.transform;
		_material = CreateFresnelMaterial( shader );
		SetVisible( false );
		ApplyTint( true );
	}

	public void ConfigureVisuals(
		Color validColor,
		Color invalidColor,
		float fresnelPower,
		float fresnelBoost,
		float pulseAmount,
		float pulseSpeed,
		float rimIntensity,
		float coreIntensity )
	{
		_validColor = validColor;
		_invalidColor = invalidColor;
		_fresnelPower = fresnelPower;
		_fresnelBoost = fresnelBoost;
		_pulseAmount = pulseAmount;
		_pulseSpeed = pulseSpeed;
		_rimIntensity = rimIntensity;
		_coreIntensity = coreIntensity;
		ApplyTint( _lastValid );
	}

	public void Destroy()
	{
		if ( _material != null )
			Object.Destroy( _material );
		_material = null;

		if ( _root != null )
			Object.Destroy( _root );

		_renderers.Clear();
	}

	public void SetVisible( bool visible )
	{
		_visible = visible;
		if ( _root != null && _root.activeSelf != visible )
			_root.SetActive( visible );
	}

	public void UpdatePose( in PlacementPreview preview )
	{
		if ( !_visible || _rootTransform == null )
			return;

		_rootTransform.SetPositionAndRotation( preview.Position, preview.Rotation );
		_rootTransform.localScale = preview.Scale;
		ApplyTint( preview.IsValid );
	}

	public void UpdatePose( Vector3 position, Quaternion rotation, Vector3 scale, bool valid )
	{
		if ( !_visible || _rootTransform == null )
			return;

		_rootTransform.SetPositionAndRotation( position, rotation );
		_rootTransform.localScale = scale;
		ApplyTint( valid );
	}

	public void SyncFromItem( TreasureItem item )
	{
		if ( item == null )
		{
			_syncedItem = null;
			_syncedDefinition = null;
			_syncedTransform = null;
			_syncedTransformId = 0;
			ClearChildren();
			return;
		}

		TreasureDefinition definition = item.Definition;
		if ( item == _syncedItem
			&& definition == _syncedDefinition
			&& HasGhostMeshRenderers() )
			return;

		_syncedItem = item;
		_syncedDefinition = definition;
		_syncedTransform = null;
		_syncedTransformId = 0;
		RebuildFromItem( item );
	}

	/// <summary>
	/// Clones MeshFilters under <paramref name="sourceRoot"/> (skips GroundCoinStack children).
	/// </summary>
	public void SyncFromTransform( Transform sourceRoot )
	{
		if ( sourceRoot == null )
		{
			_syncedItem = null;
			_syncedDefinition = null;
			_syncedTransform = null;
			_syncedTransformId = 0;
			ClearChildren();
			return;
		}

		int id = sourceRoot.GetInstanceID();
		if ( _syncedTransform == sourceRoot
			&& _syncedTransformId == id
			&& HasGhostMeshRenderers() )
			return;

		_syncedItem = null;
		_syncedDefinition = null;
		_syncedTransform = sourceRoot;
		_syncedTransformId = id;
		RebuildFromTransform( sourceRoot );
	}

	bool HasGhostMeshRenderers()
	{
		for ( int i = 0; i < _renderers.Count; i++ )
		{
			MeshRenderer renderer = _renderers[ i ];
			if ( renderer != null )
				return true;
		}

		return false;
	}

	void RebuildFromItem( TreasureItem item )
	{
		ClearChildren();
		if ( item == null )
			return;

		RebuildFromFilters( item.GetComponentsInChildren<MeshFilter>( true ), item.transform );

		if ( _renderers.Count == 0 )
		{
			MeshCollider meshCollider = item.GetComponentInChildren<MeshCollider>();
			if ( meshCollider != null && meshCollider.sharedMesh != null )
			{
				AddChildVisual(
					"GhostPart_0",
					meshCollider.sharedMesh,
					Vector3.zero,
					Quaternion.identity,
					Vector3.one,
					1 );
			}
		}

		if ( _renderers.Count == 0 )
		{
			AddChildVisual(
				"GhostPart_0",
				GetBuiltinCube(),
				Vector3.zero,
				Quaternion.identity,
				Vector3.one,
				1 );
		}

		ApplyTint( _lastValid );
	}

	void RebuildFromTransform( Transform sourceRoot )
	{
		ClearChildren();
		if ( sourceRoot == null )
			return;

		RebuildFromFilters( sourceRoot.GetComponentsInChildren<MeshFilter>( true ), sourceRoot );

		if ( _renderers.Count == 0 )
		{
			AddChildVisual(
				"GhostPart_0",
				GetBuiltinCube(),
				Vector3.zero,
				Quaternion.identity,
				Vector3.one,
				1 );
		}

		ApplyTint( _lastValid );
	}

	void RebuildFromFilters( MeshFilter[] filters, Transform itemRoot )
	{
		if ( filters == null || filters.Length == 0 || itemRoot == null )
			return;

		for ( int i = 0; i < filters.Length; i++ )
		{
			MeshFilter filter = filters[ i ];
			if ( filter == null || filter.sharedMesh == null )
				continue;

			if ( ShouldSkipGhostSource( filter.transform ) )
				continue;

			MeshRenderer sourceRenderer = filter.GetComponent<MeshRenderer>();
			int materialSlots = 1;
			if ( sourceRenderer != null && sourceRenderer.sharedMaterials != null )
				materialSlots = Mathf.Max( 1, sourceRenderer.sharedMaterials.Length );
			materialSlots = Mathf.Max( materialSlots, filter.sharedMesh.subMeshCount );

			Vector3 localPos = itemRoot.InverseTransformPoint( filter.transform.position );
			Quaternion localRot = Quaternion.Inverse( itemRoot.rotation ) * filter.transform.rotation;
			Vector3 localScale = filter.transform.localScale;
			if ( filter.transform.parent != itemRoot )
			{
				Vector3 lossy = filter.transform.lossyScale;
				Vector3 parentLossy = itemRoot.lossyScale;
				localScale = new Vector3(
					SafeDiv( lossy.x, parentLossy.x ),
					SafeDiv( lossy.y, parentLossy.y ),
					SafeDiv( lossy.z, parentLossy.z ) );
			}

			AddChildVisual(
				"GhostPart_" + i,
				filter.sharedMesh,
				localPos,
				localRot,
				localScale,
				materialSlots );
		}
	}

	static bool ShouldSkipGhostSource( Transform t )
	{
		if ( t == null )
			return true;

		// Held coins/gems live on Collectable — do not skip that layer or the ghost falls back to a cube.
		return t.GetComponentInParent<GroundCoinStack>() != null;
	}

	void AddChildVisual(
		string name,
		Mesh mesh,
		Vector3 localPosition,
		Quaternion localRotation,
		Vector3 localScale,
		int materialSlotCount )
	{
		GameObject child = new GameObject( name );
		child.transform.SetParent( _rootTransform, false );
		child.transform.localPosition = localPosition;
		child.transform.localRotation = localRotation;
		child.transform.localScale = localScale;

		MeshFilter filter = child.AddComponent<MeshFilter>();
		filter.sharedMesh = mesh;
		MeshRenderer renderer = child.AddComponent<MeshRenderer>();
		renderer.shadowCastingMode = ShadowCastingMode.Off;
		renderer.receiveShadows = false;

		int slots = Mathf.Max( 1, materialSlotCount );
		Material[] mats = new Material[ slots ];
		for ( int i = 0; i < slots; i++ )
			mats[ i ] = _material;
		renderer.sharedMaterials = mats;
		_renderers.Add( renderer );
	}

	void ApplyTint( bool valid )
	{
		_lastValid = valid;
		ApplyFresnelTint( valid );
	}

	void ApplyFresnelTint( bool valid )
	{
		if ( _material == null )
			return;

		Color tint = valid ? _validColor : _invalidColor;
		Color rim = Color.Lerp( tint, Color.white, 0.35f );
		rim.a = 1f;
		Color core = tint * 0.35f;
		core.a = 1f;

		// Player builds ignore MaterialPropertyBlock for UnityPerMaterial CBUFFER on some
		// URP/SRP-batcher paths — write the same values onto the material instance.
		_material.SetColor( BaseColorId, tint );
		if ( _material.HasProperty( ColorId ) )
			_material.SetColor( ColorId, tint );
		_material.SetColor( RimColorId, rim );
		_material.SetColor( CoreColorId, core );
		_material.SetFloat( FresnelPowerId, _fresnelPower );
		_material.SetFloat( FresnelBoostId, _fresnelBoost );
		_material.SetFloat( RimIntensityId, _rimIntensity );
		_material.SetFloat( CoreIntensityId, _coreIntensity );
		_material.SetFloat( PulseSpeedId, _pulseSpeed );
		_material.SetFloat( PulseAmountId, _pulseAmount );

		_propertyBlock.Clear();
		_propertyBlock.SetColor( BaseColorId, tint );
		_propertyBlock.SetColor( ColorId, tint );
		_propertyBlock.SetColor( RimColorId, rim );
		_propertyBlock.SetColor( CoreColorId, core );
		_propertyBlock.SetFloat( FresnelPowerId, _fresnelPower );
		_propertyBlock.SetFloat( FresnelBoostId, _fresnelBoost );
		_propertyBlock.SetFloat( RimIntensityId, _rimIntensity );
		_propertyBlock.SetFloat( CoreIntensityId, _coreIntensity );
		_propertyBlock.SetFloat( PulseSpeedId, _pulseSpeed );
		_propertyBlock.SetFloat( PulseAmountId, _pulseAmount );

		ApplyPropertyBlockToRenderers();
	}

	void ApplyPropertyBlockToRenderers()
	{
		for ( int r = 0; r < _renderers.Count; r++ )
		{
			MeshRenderer renderer = _renderers[ r ];
			if ( renderer == null )
				continue;

			int count = renderer.sharedMaterials != null ? renderer.sharedMaterials.Length : 1;
			count = Mathf.Max( 1, count );
			for ( int m = 0; m < count; m++ )
				renderer.SetPropertyBlock( _propertyBlock, m );
		}
	}

	void ClearChildren()
	{
		_renderers.Clear();
		if ( _rootTransform == null )
			return;

		for ( int i = _rootTransform.childCount - 1; i >= 0; i-- )
		{
			Transform child = _rootTransform.GetChild( i );
			if ( child != null )
				Object.Destroy( child.gameObject );
		}
	}

	static float SafeDiv( float a, float b )
	{
		return Mathf.Abs( b ) < 0.0001f ? a : a / b;
	}

	static Mesh _builtinCube;

	static Mesh GetBuiltinCube()
	{
		if ( _builtinCube != null )
			return _builtinCube;

		GameObject temp = GameObject.CreatePrimitive( PrimitiveType.Cube );
		_builtinCube = temp.GetComponent<MeshFilter>().sharedMesh;
		Object.Destroy( temp );
		return _builtinCube;
	}

	static Material CreateFresnelMaterial( Shader shader )
	{
		if ( shader == null )
			shader = Shader.Find( "DragonLoot/Placement Ghost" );
		if ( shader == null )
			shader = Shader.Find( "Universal Render Pipeline/Unlit" );
		if ( shader == null )
			shader = Shader.Find( "Universal Render Pipeline/Lit" );
		if ( shader == null )
			shader = Shader.Find( "Sprites/Default" );
		if ( shader == null )
			shader = Shader.Find( "Standard" );

		Material mat = new Material( shader );
		mat.enableInstancing = false;
		Color color = PlacementFeedbackColors.ValidGhost;
		mat.color = color;

		if ( mat.HasProperty( "_BaseColor" ) )
			mat.SetColor( "_BaseColor", color );
		if ( mat.HasProperty( "_Color" ) )
			mat.SetColor( "_Color", color );
		if ( mat.HasProperty( "_RimColor" ) )
			mat.SetColor( "_RimColor", Color.Lerp( color, Color.white, 0.35f ) );
		if ( mat.HasProperty( "_CoreColor" ) )
			mat.SetColor( "_CoreColor", color * 0.35f );
		if ( mat.HasProperty( "_FresnelPower" ) )
			mat.SetFloat( "_FresnelPower", 2.4f );
		if ( mat.HasProperty( "_FresnelBoost" ) )
			mat.SetFloat( "_FresnelBoost", 0.7f );
		if ( mat.HasProperty( "_RimIntensity" ) )
			mat.SetFloat( "_RimIntensity", 1.15f );
		if ( mat.HasProperty( "_CoreIntensity" ) )
			mat.SetFloat( "_CoreIntensity", 0.28f );
		if ( mat.HasProperty( "_PulseSpeed" ) )
			mat.SetFloat( "_PulseSpeed", 0.85f );
		if ( mat.HasProperty( "_PulseAmount" ) )
			mat.SetFloat( "_PulseAmount", 0.12f );

		if ( mat.HasProperty( "_Surface" ) )
			mat.SetFloat( "_Surface", 1f );
		if ( mat.HasProperty( "_Blend" ) )
			mat.SetFloat( "_Blend", 0f );
		if ( mat.HasProperty( "_ZWrite" ) )
			mat.SetFloat( "_ZWrite", 0f );

		mat.renderQueue = (int)RenderQueue.Transparent;
		mat.EnableKeyword( "_SURFACE_TYPE_TRANSPARENT" );
		return mat;
	}
}
