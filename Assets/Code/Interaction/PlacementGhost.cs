using System.Collections.Generic;

using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Reused translucent ghost mesh for placement preview. Rebuilds when the held item
/// identity changes, and draws every MeshFilter/submesh from the source visual.
/// </summary>
public sealed class PlacementGhost
{
	static readonly Color ValidColor = new Color( 0.25f, 0.9f, 0.35f, 0.35f );
	static readonly Color InvalidColor = new Color( 0.95f, 0.2f, 0.2f, 0.35f );
	const float StackVolumeOversize = 1.1f;

	readonly GameObject _root;
	readonly Transform _rootTransform;
	Material _validMaterial;
	Material _invalidMaterial;
	Material _activeMaterial;
	readonly List<MeshRenderer> _renderers = new List<MeshRenderer>( 4 );
	TreasureItem _syncedItem;
	TreasureDefinition _syncedDefinition;
	bool _visible;
	bool _stackVolumeMode;
	GameObject _volumeChild;

	public PlacementGhost()
	{
		_root = new GameObject( "PlacementGhost" );
		_rootTransform = _root.transform;

		_validMaterial = CreateTintMaterial( ValidColor );
		_invalidMaterial = CreateTintMaterial( InvalidColor );
		_activeMaterial = _validMaterial;

		SetVisible( false );
	}

	public void Destroy()
	{
		if ( _validMaterial != null )
			Object.Destroy( _validMaterial );
		if ( _invalidMaterial != null )
			Object.Destroy( _invalidMaterial );
		_validMaterial = null;
		_invalidMaterial = null;

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

		if ( _stackVolumeMode )
			return;

		_rootTransform.SetPositionAndRotation( preview.Position, preview.Rotation );
		_rootTransform.localScale = preview.Scale;

		Material next = preview.IsValid ? _validMaterial : _invalidMaterial;
		if ( next == _activeMaterial )
			return;

		_activeMaterial = next;
		ApplyActiveMaterialToAllSlots();
	}

	/// <summary>
	/// Ghost as a slightly oversized vertical volume covering the whole stack plus next slot.
	/// </summary>
	public void UpdateStackVolume( Vector3 contactPosition, Quaternion rotation, float height, float diameter, bool valid )
	{
		if ( !_visible || _rootTransform == null )
			return;

		EnsureVolumeChild();
		_stackVolumeMode = true;
		ClearItemChildren();

		float safeHeight = Mathf.Max( 0.02f, height ) * StackVolumeOversize;
		float safeDiameter = Mathf.Max( 0.05f, diameter ) * StackVolumeOversize;

		_rootTransform.SetPositionAndRotation( contactPosition, rotation );
		_rootTransform.localScale = Vector3.one;

		if ( _volumeChild != null )
		{
			_volumeChild.SetActive( true );
			_volumeChild.transform.localPosition = Vector3.up * ( safeHeight * 0.5f );
			_volumeChild.transform.localRotation = Quaternion.identity;
			_volumeChild.transform.localScale = new Vector3( safeDiameter, safeHeight * 0.5f, safeDiameter );
		}

		Material next = valid ? _validMaterial : _invalidMaterial;
		if ( next != _activeMaterial )
		{
			_activeMaterial = next;
			ApplyActiveMaterialToAllSlots();
		}
		else
			ApplyActiveMaterialToAllSlots();
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

		MeshRenderer renderer = _volumeChild.GetComponent<MeshRenderer>();
		if ( renderer != null )
		{
			renderer.shadowCastingMode = ShadowCastingMode.Off;
			renderer.receiveShadows = false;
			_renderers.Add( renderer );
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
		if ( _volumeChild != null )
		{
			MeshRenderer volumeRenderer = _volumeChild.GetComponent<MeshRenderer>();
			if ( volumeRenderer != null )
				_renderers.Add( volumeRenderer );
		}
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

		ApplyActiveMaterialToAllSlots();
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
			mats[ i ] = _activeMaterial != null ? _activeMaterial : _validMaterial;
		renderer.sharedMaterials = mats;
		_renderers.Add( renderer );
	}

	void ApplyActiveMaterialToAllSlots()
	{
		if ( _activeMaterial == null )
			return;

		for ( int r = 0; r < _renderers.Count; r++ )
		{
			MeshRenderer renderer = _renderers[ r ];
			if ( renderer == null )
				continue;

			int count = renderer.sharedMaterials != null ? renderer.sharedMaterials.Length : 1;
			count = Mathf.Max( 1, count );
			Material[] mats = new Material[ count ];
			for ( int i = 0; i < count; i++ )
				mats[ i ] = _activeMaterial;
			renderer.sharedMaterials = mats;
		}
	}

	void ClearChildren()
	{
		ClearStackVolumeMode();
		_renderers.Clear();
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

	static Material CreateTintMaterial( Color color )
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
