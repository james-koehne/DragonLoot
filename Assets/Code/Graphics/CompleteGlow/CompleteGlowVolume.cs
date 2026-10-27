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
	float _displayExposure;
	float _displayAlpha = 1f;
	float _flashElapsed;
	float _flashPeak;
	float _flashRest;
	float _flashDuration;
	bool _flashing;
	bool _revealFading;
	bool _holdRevealAppearance;
	float _revealElapsed;
	float _revealFadeInDuration;
	float _revealFadeOutDuration;

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

	/// <summary>
	/// Updates SoftShaft / point-light tint and reapplies the property block.
	/// Use this from display complete FX so runtime tints survive ApplyAppearance / exposure flash.
	/// </summary>
	public void SetColor( Color color )
	{
		if ( _settings == null )
			return;

		_settings.Color = color;
		ApplyAppearance();
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

		bool useRevealAppearance = _revealFading || _holdRevealAppearance;
		float exposure = ( _flashing || useRevealAppearance ) ? _displayExposure : _settings.Exposure;
		float alpha = useRevealAppearance ? _displayAlpha : _settings.AlphaMultiplier;

		_meshRenderer.GetPropertyBlock( _propertyBlock );
		_propertyBlock.SetColor( ColorId, _settings.Color );
		_propertyBlock.SetFloat( ExposureId, exposure );
		_propertyBlock.SetFloat( AlphaId, alpha );
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
			float lightMult = useRevealAppearance ? _displayAlpha : 1f;
			_pointLight.enabled = _settings.AddPointLight && lightMult > 0.001f;
			if ( _settings.AddPointLight )
			{
				_pointLight.intensity = _settings.LightIntensity * lightMult;
				_pointLight.range = _settings.LightRange;
				if ( _settings.LightColorFromGlow )
					_pointLight.color = _settings.Color;
			}
		}
	}

	/// <summary>
	/// Snap SoftShaft exposure to FlashExposure, then ease down to resting Exposure.
	/// Play-mode only; no-op when disabled in settings.
	/// </summary>
	public void PlayExposureFlash()
	{
		if ( !Application.isPlaying || _settings == null || !_settings.PlayExposureFlashOnShow )
			return;

		CancelRevealFadeOut();
		_flashPeak = Mathf.Max( 0f, _settings.FlashExposure );
		_flashRest = Mathf.Max( 0f, _settings.Exposure );
		_flashDuration = Mathf.Max( 0f, _settings.FlashDuration );
		_flashElapsed = 0f;
		_flashing = true;
		_displayExposure = _flashPeak;
		_displayAlpha = _settings.AlphaMultiplier;
		ApplyAppearance();

		if ( _flashDuration <= 0f )
			CancelExposureFlash();
	}

	/// <summary>
	/// Alpha 0→1 quickly at peak glow, then slowly fade alpha (and exposure) to 0.
	/// Used for one-shot complete FX such as door unlock.
	/// </summary>
	public void PlayRevealFadeOut( float fadeInDuration, float fadeOutDuration )
	{
		if ( !Application.isPlaying || _settings == null )
			return;

		CancelExposureFlash();
		_holdRevealAppearance = false;
		_revealFadeInDuration = Mathf.Max( 0.01f, fadeInDuration );
		_revealFadeOutDuration = Mathf.Max( 0.01f, fadeOutDuration );
		_revealElapsed = 0f;
		_revealFading = true;
		_flashPeak = Mathf.Max( _settings.FlashExposure, _settings.Exposure );
		_flashRest = 0f;
		_displayAlpha = 0f;
		_displayExposure = _flashPeak;
		ApplyAppearance();
	}

	public bool IsRevealFadePlaying => _revealFading;

	public float ResolveRevealFadeTotalDuration( float fadeInDuration, float fadeOutDuration )
	{
		return Mathf.Max( 0.01f, fadeInDuration ) + Mathf.Max( 0.01f, fadeOutDuration );
	}

	/// <summary>Stops any active flash and restores resting exposure.</summary>
	public void CancelExposureFlash()
	{
		_flashing = false;
		_flashElapsed = 0f;
		if ( _settings != null )
			_displayExposure = _settings.Exposure;
		ApplyAppearance();
	}

	/// <summary>
	/// Stops reveal fade-out. When <paramref name="restoreResting"/> is false, keeps alpha at 0
	/// so disabling the FX root does not flash full brightness.
	/// </summary>
	public void CancelRevealFadeOut( bool restoreResting = true )
	{
		_revealFading = false;
		_revealElapsed = 0f;
		if ( restoreResting )
		{
			_holdRevealAppearance = false;
			_displayAlpha = _settings != null ? _settings.AlphaMultiplier : 1f;
			if ( _settings != null )
				_displayExposure = _settings.Exposure;
		}
		else
		{
			_holdRevealAppearance = true;
			_displayAlpha = 0f;
			_displayExposure = 0f;
		}

		ApplyAppearance();
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
		else if ( _settings.FootprintSource == CompleteGlowFootprintSource.SourceMesh )
		{
			Vector2[] ring = ResolveSourceMeshFootprintRing( applyPadding: false );
			if ( ring != null && ring.Length >= 3 )
				footprint = ResolveRingExtents( ring );
			else
			{
				Bounds bounds = _sourceMeshFilter.sharedMesh.bounds;
				Vector3 lossy = _sourceMeshFilter.transform.lossyScale;
				footprint = new Vector2( Mathf.Abs( bounds.size.x * lossy.x ), Mathf.Abs( bounds.size.y * lossy.y ) );
			}
		}
		else
		{
			Bounds bounds = _sourceMeshFilter.sharedMesh.bounds;
			Vector3 lossy = _sourceMeshFilter.transform.lossyScale;
			if ( _settings.FootprintSource == CompleteGlowFootprintSource.MeshBoundsXY )
				footprint = new Vector2( Mathf.Abs( bounds.size.x * lossy.x ), Mathf.Abs( bounds.size.y * lossy.y ) );
			else
				footprint = new Vector2( Mathf.Abs( bounds.size.x * lossy.x ), Mathf.Abs( bounds.size.z * lossy.z ) );
		}

		footprint.x = Mathf.Max( 0.01f, footprint.x + _settings.FootprintPadding.x );
		footprint.y = Mathf.Max( 0.01f, footprint.y + _settings.FootprintPadding.y );
		return footprint;
	}

	/// <summary>
	/// Projects source mesh vertices into this volume's local XZ plane and returns a convex-hull silhouette.
	/// Requires transform (position/rotation/scale) to already match the intended glow placement.
	/// </summary>
	public Vector2[] ResolveSourceMeshFootprintRing( int maxPoints = 64, bool applyPadding = true )
	{
		if ( _settings == null || _sourceMeshFilter == null || _sourceMeshFilter.sharedMesh == null )
			return null;

		Vector3[] verts = _sourceMeshFilter.sharedMesh.vertices;
		if ( verts == null || verts.Length < 3 )
			return null;

		Transform source = _sourceMeshFilter.transform;
		System.Collections.Generic.List<Vector2> projected = new System.Collections.Generic.List<Vector2>( verts.Length );
		for ( int i = 0; i < verts.Length; i++ )
		{
			Vector3 world = source.TransformPoint( verts[ i ] );
			Vector3 local = transform.InverseTransformPoint( world );
			projected.Add( new Vector2( local.x, local.z ) );
		}

		Vector2[] hull = BuildConvexHull( projected );
		if ( hull == null || hull.Length < 3 )
			return null;

		if ( applyPadding )
		{
			float pad = Mathf.Max( _settings.FootprintPadding.x, _settings.FootprintPadding.y );
			if ( pad > 0.0001f )
				hull = ExpandHull( hull, pad );
		}

		return DecimateRing( hull, Mathf.Clamp( maxPoints, 8, 128 ) );
	}

	static Vector2 ResolveRingExtents( Vector2[] ring )
	{
		float minX = float.PositiveInfinity;
		float maxX = float.NegativeInfinity;
		float minZ = float.PositiveInfinity;
		float maxZ = float.NegativeInfinity;
		for ( int i = 0; i < ring.Length; i++ )
		{
			Vector2 p = ring[ i ];
			if ( p.x < minX )
				minX = p.x;
			if ( p.x > maxX )
				maxX = p.x;
			if ( p.y < minZ )
				minZ = p.y;
			if ( p.y > maxZ )
				maxZ = p.y;
		}

		return new Vector2( Mathf.Max( 0.01f, maxX - minX ), Mathf.Max( 0.01f, maxZ - minZ ) );
	}

	static Vector2[] ExpandHull( Vector2[] hull, float outward )
	{
		Vector2 centroid = Vector2.zero;
		for ( int i = 0; i < hull.Length; i++ )
			centroid += hull[ i ];
		centroid /= hull.Length;

		Vector2[] expanded = new Vector2[ hull.Length ];
		for ( int i = 0; i < hull.Length; i++ )
		{
			Vector2 fromCenter = hull[ i ] - centroid;
			float len = fromCenter.magnitude;
			Vector2 dir = len > 0.0001f ? fromCenter / len : Vector2.right;
			expanded[ i ] = centroid + dir * ( len + outward );
		}

		return expanded;
	}

	static Vector2[] DecimateRing( Vector2[] ring, int maxPoints )
	{
		if ( ring == null || ring.Length <= maxPoints )
			return ring;

		Vector2[] result = new Vector2[ maxPoints ];
		for ( int i = 0; i < maxPoints; i++ )
		{
			float t = (float)i / maxPoints;
			int index = Mathf.Clamp( Mathf.FloorToInt( t * ring.Length ), 0, ring.Length - 1 );
			result[ i ] = ring[ index ];
		}

		return result;
	}

	static Vector2[] BuildConvexHull( System.Collections.Generic.List<Vector2> points )
	{
		if ( points == null || points.Count < 3 )
			return null;

		points.Sort( ( a, b ) =>
		{
			int cmp = a.x.CompareTo( b.x );
			return cmp != 0 ? cmp : a.y.CompareTo( b.y );
		} );

		// Deduplicate nearly-identical points.
		System.Collections.Generic.List<Vector2> unique = new System.Collections.Generic.List<Vector2>( points.Count );
		for ( int i = 0; i < points.Count; i++ )
		{
			if ( unique.Count == 0 || ( points[ i ] - unique[ unique.Count - 1 ] ).sqrMagnitude > 1e-8f )
				unique.Add( points[ i ] );
		}

		if ( unique.Count < 3 )
			return null;

		System.Collections.Generic.List<Vector2> lower = new System.Collections.Generic.List<Vector2>();
		for ( int i = 0; i < unique.Count; i++ )
		{
			while ( lower.Count >= 2 && Cross( lower[ lower.Count - 2 ], lower[ lower.Count - 1 ], unique[ i ] ) <= 0f )
				lower.RemoveAt( lower.Count - 1 );
			lower.Add( unique[ i ] );
		}

		System.Collections.Generic.List<Vector2> upper = new System.Collections.Generic.List<Vector2>();
		for ( int i = unique.Count - 1; i >= 0; i-- )
		{
			while ( upper.Count >= 2 && Cross( upper[ upper.Count - 2 ], upper[ upper.Count - 1 ], unique[ i ] ) <= 0f )
				upper.RemoveAt( upper.Count - 1 );
			upper.Add( unique[ i ] );
		}

		lower.RemoveAt( lower.Count - 1 );
		upper.RemoveAt( upper.Count - 1 );
		lower.AddRange( upper );
		return lower.Count >= 3 ? lower.ToArray() : null;
	}

	static float Cross( Vector2 o, Vector2 a, Vector2 b )
	{
		return ( a.x - o.x ) * ( b.y - o.y ) - ( a.y - o.y ) * ( b.x - o.x );
	}

	public Quaternion ResolveAuthoredLocalRotation()
	{
		if ( _settings == null || !_settings.PreserveAuthoredRotation )
			return Quaternion.identity;
		return Quaternion.Euler( _settings.AuthoredLocalEuler );
	}

	public Vector3 ResolveLocalAnchorPosition()
	{
		if ( _settings == null )
			return Vector3.zero;

		Transform parent = transform.parent;
		bool doorOriented = _settings.FootprintSource == CompleteGlowFootprintSource.MeshBoundsXY
			|| _settings.FootprintSource == CompleteGlowFootprintSource.SourceMesh;

		if ( parent == null || _sourceMeshFilter == null || _sourceMeshFilter.sharedMesh == null )
		{
			if ( doorOriented )
				return Vector3.zero;
			return new Vector3( 0f, _settings.AnchorOffsetY, 0f );
		}

		Bounds bounds = _sourceMeshFilter.sharedMesh.bounds;
		Vector3 localOnSource;
		if ( doorOriented )
		{
			float z;
			switch ( _settings.Anchor )
			{
				case CompleteGlowAnchor.BoundsBottom:
					z = bounds.center.z - bounds.extents.z;
					break;
				case CompleteGlowAnchor.BoundsCenter:
					z = bounds.center.z;
					break;
				default:
					z = bounds.center.z + bounds.extents.z;
					break;
			}

			localOnSource = new Vector3( bounds.center.x, bounds.center.y, z + _settings.AnchorOffsetY );
		}
		else
		{
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

			localOnSource = new Vector3( bounds.center.x, y + _settings.AnchorOffsetY, bounds.center.z );
		}

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

	void Update()
	{
		if ( !Application.isPlaying )
			return;

		if ( _revealFading )
		{
			TickRevealFadeOut();
			return;
		}

		if ( !_flashing )
			return;

		_flashElapsed += Time.deltaTime;
		float t = _flashDuration > 0f ? Mathf.Clamp01( _flashElapsed / _flashDuration ) : 1f;
		float curve = 1f;
		if ( _settings != null && _settings.FlashCurve != null && _settings.FlashCurve.length > 0 )
			curve = Mathf.Clamp01( _settings.FlashCurve.Evaluate( t ) );
		else
			curve = 1f - t;

		_displayExposure = Mathf.LerpUnclamped( _flashRest, _flashPeak, curve );
		ApplyAppearance();

		if ( t >= 1f )
		{
			_flashing = false;
			_displayExposure = _flashRest;
			ApplyAppearance();
		}
	}

	void TickRevealFadeOut()
	{
		_revealElapsed += Time.deltaTime;
		float fadeIn = Mathf.Max( 0.01f, _revealFadeInDuration );
		float fadeOut = Mathf.Max( 0.01f, _revealFadeOutDuration );

		if ( _revealElapsed <= fadeIn )
		{
			float tin = Mathf.Clamp01( _revealElapsed / fadeIn );
			tin = tin * tin * ( 3f - 2f * tin );
			_displayAlpha = tin;
			_displayExposure = _flashPeak;
		}
		else
		{
			// Ease-in fade-out: linger near full, then ease gently to 0.
			float tout = Mathf.Clamp01( ( _revealElapsed - fadeIn ) / fadeOut );
			float eased = tout * tout;
			_displayAlpha = 1f - eased;
			_displayExposure = Mathf.Lerp( _flashPeak, _flashRest, eased );
		}

		ApplyAppearance();

		if ( _revealElapsed >= fadeIn + fadeOut )
		{
			_displayAlpha = 0f;
			_displayExposure = 0f;
			_holdRevealAppearance = true;
			ApplyAppearance();
			_revealFading = false;
		}
	}

	void OnEnable()
	{
		if ( !_flashing && !_revealFading && !_holdRevealAppearance && _settings != null )
		{
			_displayExposure = _settings.Exposure;
			_displayAlpha = _settings.AlphaMultiplier;
		}
		ApplyAppearance();
	}

	void OnDisable()
	{
		if ( !Application.isPlaying )
			return;

		CancelExposureFlash();
		CancelRevealFadeOut( false );
	}

	void OnValidate()
	{
		if ( !_flashing && !_revealFading && !_holdRevealAppearance )
			ApplyAppearance();
	}
}
