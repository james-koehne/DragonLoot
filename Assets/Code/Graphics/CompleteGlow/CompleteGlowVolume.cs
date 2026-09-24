using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Holds config and references for a procedurally generated complete-glow frustum.
/// Mesh rebuild is editor-driven; this component applies MaterialPropertyBlock tint at runtime.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public class CompleteGlowVolume : MonoBehaviour
{
	public const string ChildName = "CompleteGlow";
	public const string DefaultMaterialPath = "Assets/Materials/Shaders/SoftShaft/M_DisplayShaft.mat";

	static readonly int ColorId = Shader.PropertyToID( "_Color" );
	static readonly int ExposureId = Shader.PropertyToID( "_Exposure" );
	static readonly int AlphaId = Shader.PropertyToID( "_Alpha" );
	static readonly int HeightMinId = Shader.PropertyToID( "_HeightMin" );
	static readonly int HeightMaxId = Shader.PropertyToID( "_HeightMax" );
	static readonly int NoiseStrengthId = Shader.PropertyToID( "_NoiseStrength" );
	static readonly int NoiseScrollSpeedId = Shader.PropertyToID( "_NoiseScrollSpeed" );
	static readonly int NoiseTilingId = Shader.PropertyToID( "_NoiseTiling" );
	static readonly int PulseSpeedId = Shader.PropertyToID( "_PulseSpeed" );
	static readonly int PulseAmountId = Shader.PropertyToID( "_PulseAmount" );
	static readonly int DepthFadeDistanceId = Shader.PropertyToID( "_DepthFadeDistance" );

	[SerializeField]
	MeshFilter _sourceMeshFilter;

	[SerializeField]
	CompleteGlowPreset _preset;

	[SerializeField]
	CompleteGlowSettings _settings = new CompleteGlowSettings();

	[SerializeField]
	string _meshAssetId;

	[SerializeField]
	MeshFilter _meshFilter;

	[SerializeField]
	MeshRenderer _meshRenderer;

	[SerializeField]
	Light _pointLight;

	[SerializeField]
	GameObject _dustMotesInstance;

	MaterialPropertyBlock _propertyBlock;

	public MeshFilter SourceMeshFilter
	{
		get { return _sourceMeshFilter; }
		set { _sourceMeshFilter = value; }
	}

	public CompleteGlowPreset Preset
	{
		get { return _preset; }
		set { _preset = value; }
	}

	public CompleteGlowSettings Settings
	{
		get { return _settings; }
	}

	public string MeshAssetId
	{
		get { return _meshAssetId; }
		set { _meshAssetId = value; }
	}

	public MeshFilter MeshFilter
	{
		get { return _meshFilter; }
		set { _meshFilter = value; }
	}

	public MeshRenderer MeshRenderer
	{
		get { return _meshRenderer; }
		set { _meshRenderer = value; }
	}

	public Light PointLight
	{
		get { return _pointLight; }
		set { _pointLight = value; }
	}

	public GameObject DustMotesInstance
	{
		get { return _dustMotesInstance; }
		set { _dustMotesInstance = value; }
	}

	public void ApplyPresetSettings()
	{
		if ( _preset == null || _settings == null )
			return;
		_preset.ApplyTo( _settings );
	}

	public void EnsureComponents()
	{
		if ( _meshFilter == null )
			_meshFilter = GetComponent<MeshFilter>();
		if ( _meshFilter == null )
			_meshFilter = gameObject.AddComponent<MeshFilter>();

		if ( _meshRenderer == null )
			_meshRenderer = GetComponent<MeshRenderer>();
		if ( _meshRenderer == null )
			_meshRenderer = gameObject.AddComponent<MeshRenderer>();

		_meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
		_meshRenderer.receiveShadows = false;
		_meshRenderer.lightProbeUsage = LightProbeUsage.Off;
		_meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
		_meshRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
	}

	public void ApplyAppearance()
	{
		if ( _settings == null )
			return;

		EnsureComponents();

		if ( _meshRenderer == null )
			return;

		if ( _propertyBlock == null )
			_propertyBlock = new MaterialPropertyBlock();

		_meshRenderer.GetPropertyBlock( _propertyBlock );
		_propertyBlock.SetColor( ColorId, _settings.Color );
		_propertyBlock.SetFloat( ExposureId, _settings.Exposure );
		_propertyBlock.SetFloat( AlphaId, _settings.AlphaMultiplier );
		_propertyBlock.SetFloat( HeightMinId, 0f );
		_propertyBlock.SetFloat( HeightMaxId, Mathf.Max( 0.01f, _settings.Height ) );
		_propertyBlock.SetFloat( NoiseStrengthId, _settings.NoiseStrength );
		_propertyBlock.SetFloat( NoiseScrollSpeedId, _settings.NoiseScrollSpeed );
		_propertyBlock.SetFloat( NoiseTilingId, _settings.NoiseTiling );
		_propertyBlock.SetFloat( PulseSpeedId, _settings.PulseSpeed );
		_propertyBlock.SetFloat( PulseAmountId, _settings.PulseAmount );
		_propertyBlock.SetFloat( DepthFadeDistanceId, _settings.DepthFadeDistance );
		_meshRenderer.SetPropertyBlock( _propertyBlock );

		_meshRenderer.sortingOrder = _settings.SortingOrder;

		if ( _pointLight != null )
		{
			_pointLight.enabled = _settings.AddPointLight;
			if ( _settings.AddPointLight )
			{
				_pointLight.intensity = _settings.LightIntensity;
				_pointLight.range = _settings.LightRange;
				if ( _settings.LightColorFromGlow )
					_pointLight.color = _settings.Color;
			}
		}
	}

	public Vector2 ResolveFootprintWorld()
	{
		if ( _settings == null )
			return Vector2.one;

		Vector2 footprint;
		if ( _settings.FootprintSource == CompleteGlowFootprintSource.Custom || _sourceMeshFilter == null || _sourceMeshFilter.sharedMesh == null )
		{
			footprint = _settings.CustomFootprint;
		}
		else
		{
			Bounds bounds = _sourceMeshFilter.sharedMesh.bounds;
			Vector3 lossy = _sourceMeshFilter.transform.lossyScale;
			footprint = new Vector2( Mathf.Abs( bounds.size.x * lossy.x ), Mathf.Abs( bounds.size.z * lossy.z ) );
		}

		footprint.x = Mathf.Max( 0.01f, footprint.x + _settings.FootprintPadding.x );
		footprint.y = Mathf.Max( 0.01f, footprint.y + _settings.FootprintPadding.y );
		return footprint;
	}

	public Vector3 ResolveLocalAnchorPosition()
	{
		if ( _settings == null )
			return Vector3.zero;

		Transform parent = transform.parent;
		if ( parent == null || _sourceMeshFilter == null || _sourceMeshFilter.sharedMesh == null )
			return new Vector3( 0f, _settings.AnchorOffsetY, 0f );

		Bounds bounds = _sourceMeshFilter.sharedMesh.bounds;
		float y;
		switch ( _settings.Anchor )
		{
			case CompleteGlowAnchor.BoundsBottom:
				y = bounds.center.y - bounds.extents.y;
				break;
			case CompleteGlowAnchor.BoundsCenter:
				y = bounds.center.y;
				break;
			default:
				y = bounds.center.y + bounds.extents.y;
				break;
		}

		Vector3 localOnSource = new Vector3( bounds.center.x, y + _settings.AnchorOffsetY, bounds.center.z );
		Vector3 world = _sourceMeshFilter.transform.TransformPoint( localOnSource );
		return parent.InverseTransformPoint( world );
	}

	public Vector3 ResolveCompensatedLocalScale()
	{
		if ( _settings == null || !_settings.CompensateParentScale )
			return Vector3.one;

		Transform parent = transform.parent;
		if ( parent == null )
			return Vector3.one;

		Vector3 lossy = parent.lossyScale;
		return new Vector3(
			Mathf.Approximately( lossy.x, 0f ) ? 1f : 1f / lossy.x,
			Mathf.Approximately( lossy.y, 0f ) ? 1f : 1f / lossy.y,
			Mathf.Approximately( lossy.z, 0f ) ? 1f : 1f / lossy.z );
	}

	void OnEnable()
	{
		ApplyAppearance();
	}

	void OnValidate()
	{
		ApplyAppearance();
	}
}
