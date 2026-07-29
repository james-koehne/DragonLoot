using System.Collections.Generic;

using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Reused translucent ghost mesh for placement preview. Rebuilds when the held item
/// identity changes, and draws every MeshFilter/submesh from the source visual.
/// Stack-volume previews register an invisible volume mesh for the hover outline renderer feature.
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
	bool _visible;
	bool _stackVolumeMode;
	bool _lastValid = true;
	GameObject _volumeChild;
	MeshRenderer _volumeRenderer;

	Color _validColor = PlacementFeedbackColors.ValidGhost;
	Color _invalidColor = PlacementFeedbackColors.InvalidGhost;
	float _fresnelPower = 2.4f;
	float _fresnelBoost = 0.7f;
	float _pulseAmount = 0.12f;
	float _pulseSpeed = 0.85f;
	float _rimIntensity = 1.15f;
	float _coreIntensity = 0.28f;
	float _stackVolumeOversize = 1.1f;
	HoverOutlineVisualSettings _stackOutlineSettings = HoverOutlineVisualSettings.DefaultStack();
	Color _validRgb = PlacementFeedbackColors.ValidRgb;
	Color _invalidRgb = PlacementFeedbackColors.InvalidRgb;

	public PlacementGhost()
	{
		_root = new GameObject( "PlacementGhost" );
		_rootTransform = _root.transform;
		_material = CreateFresnelMaterial();
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
		float coreIntensity,
		float stackVolumeOversize,
		HoverOutlineVisualSettings stackOutline )
	{
		_validColor = validColor;
		_invalidColor = invalidColor;
		_validRgb = new Color( validColor.r, validColor.g, validColor.b, 1f );
		_invalidRgb = new Color( invalidColor.r, invalidColor.g, invalidColor.b, 1f );
		_fresnelPower = fresnelPower;
		_fresnelBoost = fresnelBoost;
		_pulseAmount = pulseAmount;
		_pulseSpeed = pulseSpeed;
		_rimIntensity = rimIntensity;
		_coreIntensity = coreIntensity;
		_stackVolumeOversize = stackVolumeOversize;
		_stackOutlineSettings = stackOutline != null ? stackOutline.Clone() : HoverOutlineVisualSettings.DefaultStack();
		_stackOutlineSettings.Validate();
		ApplyTint( _lastValid );
	}

	public bool TryGetStackVolumeOutline( bool valid, out Renderer renderer, out HoverOutlineVisualSettings settings )
	{
		renderer = _volumeRenderer;
		settings = _stackOutlineSettings;
		if ( !_stackVolumeMode || _volumeRenderer == null )
		{
			settings = null;
			return false;
		}

		return true;
	}

	public void Destroy()
	{
		if ( _material != null )
			Object.Destroy( _material );
		_material = null;

		if ( _root != null )
			Object.Destroy( _root );

		_renderers.Clear();
		_volumeRenderer = null;
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

		if ( _stackVolumeMode )
			return;

		_rootTransform.SetPositionAndRotation( preview.Position, preview.Rotation );
		_rootTransform.localScale = preview.Scale;
		ApplyTint( preview.IsValid );
	}

	public void UpdateStackVolume( Vector3 contactPosition, Quaternion rotation, float height, float diameter, bool valid )
	{
		if ( !_visible || _rootTransform == null )
			return;

		EnsureVolumeChild();
		_stackVolumeMode = true;
		ClearItemChildren();

		float safeHeight = Mathf.Max( 0.02f, height );
		float safeDiameter = Mathf.Max( 0.05f, diameter ) * _stackVolumeOversize;

		_rootTransform.SetPositionAndRotation( contactPosition, rotation );
		_rootTransform.localScale = Vector3.one;

		if ( _volumeChild != null )
		{
			_volumeChild.SetActive( true );
			_volumeChild.transform.localPosition = Vector3.up * ( safeHeight * 0.5f );
			_volumeChild.transform.localRotation = Quaternion.identity;
			_volumeChild.transform.localScale = new Vector3( safeDiameter, safeHeight * 0.5f, safeDiameter );
		}

		_lastValid = valid;
	}

	public void ClearStackVolumeMode()
	{
		_stackVolumeMode = false;
		if ( _volumeChild != null )
			_volumeChild.SetActive( false );
	}

	public void SyncFromItem( TreasureItem item )
	{
		ClearStackVolumeMode();
		if ( item == null )
		{
			_syncedItem = null;
			_syncedDefinition = null;
			ClearChildren();
			return;
		}

		TreasureDefinition definition = item.Definition;
		if ( item == _syncedItem && definition == _syncedDefinition && _renderers.Count > 0 && !_stackVolumeMode )
			return;

		_syncedItem = item;
		_syncedDefinition = definition;
		RebuildFromItem( item );
	}

	void EnsureVolumeChild()
	{
		if ( _volumeChild != null )
			return;

		_volumeChild = GameObject.CreatePrimitive( PrimitiveType.Cylinder );
		_volumeChild.name = "StackVolume";
		Object.Destroy( _volumeChild.GetComponent<Collider>() );
		_volumeChild.transform.SetParent( _rootTransform, false );

		_volumeRenderer = _volumeChild.GetComponent<MeshRenderer>();
		if ( _volumeRenderer != null )
		{
			_volumeRenderer.shadowCastingMode = ShadowCastingMode.Off;
			_volumeRenderer.receiveShadows = false;
			_volumeRenderer.sharedMaterial = CreateInvisibleMaterial();
			_volumeRenderer.enabled = true;
			_renderers.Add( _volumeRenderer );
		}
	}

	void ClearItemChildren()
	{
		if ( _rootTransform == null )
			return;

		for ( int i = _rootTransform.childCount - 1; i >= 0; i-- )
		{
			Transform child = _rootTransform.GetChild( i );
			if ( child == null || child.gameObject == _volumeChild )
				continue;

			Object.Destroy( child.gameObject );
		}

		_renderers.Clear();
		if ( _volumeRenderer != null )
			_renderers.Add( _volumeRenderer );
	}

	void RebuildFromItem( TreasureItem item )
	{
		ClearChildren();
		if ( item == null )
			return;

		MeshFilter[] filters = item.GetComponentsInChildren<MeshFilter>( true );
		if ( filters != null && filters.Length > 0 )
		{
			Transform itemRoot = item.transform;
			for ( int i = 0; i < filters.Length; i++ )
			{
				MeshFilter filter = filters[ i ];
				if ( filter == null || filter.sharedMesh == null )
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
		if ( _stackVolumeMode )
			return;

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
		ClearStackVolumeMode();
		_renderers.Clear();
		_volumeRenderer = null;
		if ( _rootTransform == null )
			return;

		for ( int i = _rootTransform.childCount - 1; i >= 0; i-- )
		{
			Transform child = _rootTransform.GetChild( i );
			if ( child != null )
				Object.Destroy( child.gameObject );
		}

		_volumeChild = null;
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

	static Material _invisibleMaterial;

	static Material CreateInvisibleMaterial()
	{
		if ( _invisibleMaterial != null )
			return _invisibleMaterial;

		Shader shader = Shader.Find( "DragonLoot/Hover Outline Invisible" );
		if ( shader == null )
			shader = Shader.Find( "Universal Render Pipeline/Unlit" );

		_invisibleMaterial = new Material( shader );
		if ( _invisibleMaterial.HasProperty( "_BaseColor" ) )
			_invisibleMaterial.SetColor( "_BaseColor", Color.clear );
		if ( _invisibleMaterial.HasProperty( "_Color" ) )
			_invisibleMaterial.SetColor( "_Color", Color.clear );
		_invisibleMaterial.renderQueue = (int)RenderQueue.Geometry + 10;
		return _invisibleMaterial;
	}

	static Material CreateFresnelMaterial()
	{
		Shader shader = Shader.Find( "DragonLoot/Placement Ghost" );
		if ( shader == null )
			shader = Shader.Find( "Universal Render Pipeline/Unlit" );
		if ( shader == null )
			shader = Shader.Find( "Universal Render Pipeline/Lit" );
		if ( shader == null )
			shader = Shader.Find( "Sprites/Default" );
		if ( shader == null )
			shader = Shader.Find( "Standard" );

		Material mat = new Material( shader );
		Color color = PlacementFeedbackColors.ValidGhost;
		mat.color = color;

		if ( mat.HasProperty( "_BaseColor" ) )
			mat.SetColor( "_BaseColor", color );
		if ( mat.HasProperty( "_Color" ) )
			mat.SetColor( "_Color", color );

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
