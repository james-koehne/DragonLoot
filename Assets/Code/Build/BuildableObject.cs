using System.Collections.Generic;

using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// World-placed structure that starts unbuilt: built content disabled, ghost meshes shown in build mode
/// when nearby and prerequisites are met. Hold ContextualInteract on the ghost to complete.
/// </summary>
public class BuildableObject : MonoBehaviour
{
	public const string BuiltChildName = "Built";
	public const string GhostChildName = "Ghost";

	static readonly List<BuildableObject> ActiveBuildables = new List<BuildableObject>( 64 );
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
	static readonly int PlayerWorldPosId = Shader.PropertyToID( "_PlayerWorldPos" );
	static readonly int FadeStartId = Shader.PropertyToID( "_FadeStart" );
	static readonly int FadeEndId = Shader.PropertyToID( "_FadeEnd" );
	static readonly int CompleteGlowId = Shader.PropertyToID( "_CompleteGlow" );
	static readonly int CompleteColorId = Shader.PropertyToID( "_CompleteColor" );
	static readonly int AlphaMulId = Shader.PropertyToID( "_AlphaMul" );

	[SerializeField]
	Transform _builtRoot;

	[SerializeField]
	Transform _ghostRoot;

	[SerializeField]
	BuildableObject[] _requiredBuildables;

	[SerializeField]
	BuildableDefinition _definition;

	[SerializeField]
	bool _startBuilt;

	readonly List<MeshRenderer> _ghostRenderers = new List<MeshRenderer>( 8 );
	readonly List<Renderer> _outlineRendererScratch = new List<Renderer>( 8 );
	readonly List<MeshCollider> _aimMeshColliders = new List<MeshCollider>( 8 );
	MaterialPropertyBlock _propertyBlock;
	Material _ghostMaterial;
	bool _isBuilt;
	bool _ghostVisible;
	bool _loggedCircularPrereq;
	bool _setupComplete;
	bool _completing;
	bool _builtActivatedDuringReveal;
	float _completeT;
	float _completeGlow;
	float _completeAlphaMul = 1f;
	BuildModeDefinition _completeDef;
	Bounds _cachedWorldBounds;
	bool _hasCachedWorldBounds;

	public bool IsBuilt => _isBuilt;
	public bool IsCompleting => _completing;
	public bool IsGhostVisible => _ghostVisible;
	public Transform BuiltRoot => _builtRoot;
	public Transform GhostRoot => _ghostRoot;
	public BuildableObject[] RequiredBuildables => _requiredBuildables;
	public BuildableDefinition Definition => _definition;

	public static IReadOnlyList<BuildableObject> Active => ActiveBuildables;

	public string ResolveDisplayName()
	{
		if ( _definition != null )
			return _definition.ResolveDisplayName();
		return name;
	}

	public string ResolveDescription()
	{
		if ( _definition != null )
			return _definition.ResolveDescription();
		return string.Empty;
	}

	public int ResolveCost()
	{
		if ( _definition != null )
			return _definition.ResolveCost();
		return 0;
	}

	void Awake()
	{
		EnsurePropertyBlock();
		EnsureHierarchy();
		EnsureGhostMeshesExist();
		ApplyInitialState();
		_setupComplete = true;
	}

	/// <summary>
	/// If Ghost has no mesh parts but Built does, clone ghosts at runtime (e.g. wrap failed earlier).
	/// </summary>
	void EnsureGhostMeshesExist()
	{
		if ( _isBuilt || _startBuilt )
			return;
		if ( _ghostRoot == null || _builtRoot == null )
			return;

		CacheGhostRenderers();
		if ( _ghostRenderers.Count > 0 )
			return;

		MeshFilter[] builtFilters = _builtRoot.GetComponentsInChildren<MeshFilter>( true );
		bool hasMesh = false;
		for ( int i = 0; i < builtFilters.Length; i++ )
		{
			if ( builtFilters[ i ] != null && builtFilters[ i ].sharedMesh != null )
			{
				hasMesh = true;
				break;
			}
		}

		if ( !hasMesh )
			return;

		BuildModeDefinition def = null;
		def = RuntimeDefinition.Resolve( ref def );
		RebuildGhostsFromBuilt( def );
	}

	void EnsurePropertyBlock()
	{
		if ( _propertyBlock == null )
			_propertyBlock = new MaterialPropertyBlock();
	}

	void OnEnable()
	{
		if ( !ActiveBuildables.Contains( this ) )
			ActiveBuildables.Add( this );
	}

	void OnDisable()
	{
		ActiveBuildables.Remove( this );
		SetGhostVisible( false );
	}

	void OnDestroy()
	{
		ActiveBuildables.Remove( this );
		if ( _ghostMaterial != null && _ghostMaterial.name == "BuildGhostRuntime" )
		{
			Destroy( _ghostMaterial );
			_ghostMaterial = null;
		}
	}

	public bool ArePrerequisitesMet()
	{
		if ( HasCircularPrerequisite() )
			return false;

		if ( _requiredBuildables == null || _requiredBuildables.Length == 0 )
			return true;

		for ( int i = 0; i < _requiredBuildables.Length; i++ )
		{
			BuildableObject required = _requiredBuildables[ i ];
			if ( required == null )
				continue;
			if ( !required.IsBuilt )
				return false;
		}

		return true;
	}

	public bool IsEligibleToShow( Vector3 playerPosition, float fadeEnd )
	{
		if ( _isBuilt || _completing || !_setupComplete )
			return false;
		if ( !ArePrerequisitesMet() )
			return false;

		if ( !TryGetWorldBounds( out Bounds bounds ) )
			bounds = new Bounds( transform.position, Vector3.one );

		// Distance to the nearest point on the AABB — long tracks activate when near any end,
		// not only when near the bounds center.
		float radius = Mathf.Max( 0.1f, fadeEnd );
		Vector3 nearest = bounds.ClosestPoint( playerPosition );
		return ( nearest - playerPosition ).sqrMagnitude <= radius * radius;
	}

	public Vector3 GetWorldCenter()
	{
		if ( TryGetWorldBounds( out Bounds bounds ) )
			return bounds.center;

		if ( _ghostRoot != null )
			return _ghostRoot.position;

		return transform.position;
	}

	public bool TryGetWorldBounds( out Bounds bounds )
	{
		if ( _hasCachedWorldBounds )
		{
			bounds = _cachedWorldBounds;
			return true;
		}

		return TryComputeWorldBounds( out bounds );
	}

	public bool TryGetAimBounds( out Bounds bounds )
	{
		return TryGetWorldBounds( out bounds );
	}

	public bool TryIntersectAimRay( Ray ray, out float distance )
	{
		distance = 0f;
		if ( !TryGetAimBounds( out Bounds bounds ) )
			bounds = new Bounds( transform.position, Vector3.one * 1.5f );

		return bounds.IntersectRay( ray, out distance );
	}

	public void SetGhostVisible( bool visible )
	{
		// Complete reveal owns ghost visibility until the fade finishes.
		if ( _completing )
			return;

		if ( _isBuilt )
			visible = false;

		bool wasVisible = _ghostVisible;
		_ghostVisible = visible;
		if ( _ghostRoot != null && _ghostRoot.gameObject.activeSelf != visible )
			_ghostRoot.gameObject.SetActive( visible );

		// Refresh aim colliders when first shown (inactive mesh cooks are unreliable).
		if ( visible && ( !wasVisible || _aimMeshColliders.Count == 0 ) )
			EnsureAimColliders();
	}

	public void UpdateGhostFade( Vector3 playerWorldPos, BuildModeDefinition def )
	{
		if ( !_ghostVisible || _completing )
			return;

		if ( _ghostMaterial == null )
			EnsureGhostMaterial( def );

		float fadeStart = def != null ? def.fadeStart : 12f;
		float fadeEnd = def != null ? def.fadeEnd : 18f;
		ApplyGhostMaterialParams( def, playerWorldPos, completeGlow: 0f, alphaMul: 1f, fadeStart, fadeEnd );
	}

	/// <summary>
	/// Ghost mesh renderers for hover-outline masking while aimed in build mode.
	/// </summary>
	public IReadOnlyList<Renderer> GetGhostOutlineRenderers()
	{
		_outlineRendererScratch.Clear();
		if ( !_ghostVisible || _completing )
			return _outlineRendererScratch;

		for ( int i = 0; i < _ghostRenderers.Count; i++ )
		{
			MeshRenderer renderer = _ghostRenderers[ i ];
			if ( renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy )
				continue;
			_outlineRendererScratch.Add( renderer );
		}

		return _outlineRendererScratch;
	}

	public void Complete()
	{
		if ( _isBuilt || _completing )
			return;

		_isBuilt = true;
		_completing = true;
		_builtActivatedDuringReveal = false;
		_completeT = 0f;
		_completeGlow = 0f;
		_completeAlphaMul = 1f;
		_completeDef = null;
		_completeDef = RuntimeDefinition.Resolve( ref _completeDef );

		_ghostVisible = true;
		if ( _ghostRoot != null && !_ghostRoot.gameObject.activeSelf )
			_ghostRoot.gameObject.SetActive( true );

		if ( _ghostMaterial == null )
			EnsureGhostMaterial( _completeDef );

		ApplyGhostMaterialParams( _completeDef, ResolvePlayerPos(), completeGlow: 0f, alphaMul: 1f, fadeStart: 9999f, fadeEnd: 10000f );
		EventBus.Publish( new BuildableCompletedEvent { Buildable = this } );
	}

	void Update()
	{
		if ( !_completing )
			return;

		TickCompleteReveal();
	}

	void TickCompleteReveal()
	{
		BuildModeDefinition def = _completeDef;
		float flash = def != null ? Mathf.Max( 0.05f, def.completeFlashSeconds ) : 0.18f;
		float hold = def != null ? Mathf.Max( 0f, def.completeHoldSeconds ) : 0.08f;
		float fade = def != null ? Mathf.Max( 0.05f, def.completeFadeSeconds ) : 0.4f;
		float activateAt = flash * 0.65f;
		float fadeStart = flash + hold;
		float total = fadeStart + fade;

		_completeT += Time.deltaTime;

		if ( _completeT < flash )
			_completeGlow = Mathf.SmoothStep( 0f, 1f, Mathf.Clamp01( _completeT / flash ) );
		else
			_completeGlow = 1f;

		if ( !_builtActivatedDuringReveal && _completeT >= activateAt )
		{
			_builtActivatedDuringReveal = true;
			if ( _builtRoot != null )
				_builtRoot.gameObject.SetActive( true );
		}

		if ( _completeT <= fadeStart )
			_completeAlphaMul = 1f;
		else
			_completeAlphaMul = 1f - Mathf.SmoothStep( 0f, 1f, Mathf.Clamp01( ( _completeT - fadeStart ) / fade ) );

		ApplyGhostMaterialParams( def, ResolvePlayerPos(), _completeGlow, _completeAlphaMul, fadeStart: 9999f, fadeEnd: 10000f );

		if ( _completeT < total )
			return;

		FinishCompleteReveal();
	}

	void FinishCompleteReveal()
	{
		_completing = false;
		_completeGlow = 0f;
		_completeAlphaMul = 1f;
		_ghostVisible = false;
		if ( _ghostRoot != null )
			_ghostRoot.gameObject.SetActive( false );

		if ( !_builtActivatedDuringReveal && _builtRoot != null )
			_builtRoot.gameObject.SetActive( true );

		_builtActivatedDuringReveal = false;
		_completeDef = null;
	}

	static Vector3 ResolvePlayerPos()
	{
		if ( GameMode.Instance != null && GameMode.Instance.Player != null )
			return GameMode.Instance.Player.transform.position;
		return Vector3.zero;
	}

	/// <summary>
	/// Ensures Built/Ghost hierarchy and clones mesh ghosts from Built when Ghost is empty.
	/// Safe to call from editor wrap tools and at runtime.
	/// </summary>
	public void EnsureHierarchy()
	{
		if ( _builtRoot == null )
		{
			Transform existing = transform.Find( BuiltChildName );
			if ( existing != null )
				_builtRoot = existing;
			else
			{
				GameObject builtGo = new GameObject( BuiltChildName );
				_builtRoot = builtGo.transform;
				_builtRoot.SetParent( transform, false );
			}
		}

		if ( _ghostRoot == null )
		{
			Transform existing = transform.Find( GhostChildName );
			if ( existing != null )
				_ghostRoot = existing;
			else
			{
				GameObject ghostGo = new GameObject( GhostChildName );
				_ghostRoot = ghostGo.transform;
				_ghostRoot.SetParent( transform, false );
			}
		}

		CacheGhostRenderers();
		EnsureAimColliders();
	}

	/// <summary>
	/// Rebuilds ghost mesh children from Built MeshFilters using the build-ghost material.
	/// </summary>
	public void RebuildGhostsFromBuilt( BuildModeDefinition def )
	{
		EnsureHierarchy();
		ClearGhostChildren();

		if ( _builtRoot == null )
			return;

		EnsureGhostMaterial( def );

		MeshFilter[] filters = _builtRoot.GetComponentsInChildren<MeshFilter>( true );
		for ( int i = 0; i < filters.Length; i++ )
		{
			MeshFilter filter = filters[ i ];
			if ( filter == null || filter.sharedMesh == null )
				continue;

			MeshRenderer sourceRenderer = filter.GetComponent<MeshRenderer>();
			int materialSlots = sourceRenderer != null ? Mathf.Max( 1, sourceRenderer.sharedMaterials.Length ) : 1;

			Transform source = filter.transform;
			GameObject child = new GameObject( "GhostPart_" + i );
			child.transform.SetParent( _ghostRoot, false );
			child.transform.position = source.position;
			child.transform.rotation = source.rotation;
			child.transform.localScale = source.lossyScale;

			MeshFilter ghostFilter = child.AddComponent<MeshFilter>();
			ghostFilter.sharedMesh = filter.sharedMesh;

			MeshRenderer ghostRenderer = child.AddComponent<MeshRenderer>();
			ghostRenderer.shadowCastingMode = ShadowCastingMode.Off;
			ghostRenderer.receiveShadows = false;

			Material[] mats = new Material[ materialSlots ];
			for ( int m = 0; m < materialSlots; m++ )
				mats[ m ] = _ghostMaterial;
			ghostRenderer.sharedMaterials = mats;
		}

		CacheGhostRenderers();
		EnsureAimColliders();
		float fadeStart = def != null ? def.fadeStart : 12f;
		float fadeEnd = def != null ? def.fadeEnd : 18f;
		ApplyGhostMaterialParams( def, Vector3.zero, completeGlow: 0f, alphaMul: 1f, fadeStart, fadeEnd );
	}

	public void AssignRoots( Transform builtRoot, Transform ghostRoot )
	{
		_builtRoot = builtRoot;
		_ghostRoot = ghostRoot;
	}

	public void SetRequiredBuildables( BuildableObject[] required )
	{
		_requiredBuildables = required;
	}

	void ApplyInitialState()
	{
		_isBuilt = _startBuilt;
		if ( _isBuilt )
		{
			if ( _builtRoot != null )
				_builtRoot.gameObject.SetActive( true );
			SetGhostVisible( false );
			return;
		}

		if ( _builtRoot != null )
			_builtRoot.gameObject.SetActive( false );
		SetGhostVisible( false );

		BuildModeDefinition def = null;
		def = RuntimeDefinition.Resolve( ref def );
		if ( _ghostRenderers.Count > 0 )
			EnsureGhostMaterial( def );
	}

	void ClearGhostChildren()
	{
		if ( _ghostRoot == null )
			return;

		for ( int i = _ghostRoot.childCount - 1; i >= 0; i-- )
		{
			Transform child = _ghostRoot.GetChild( i );
			if ( child == null )
				continue;
#if UNITY_EDITOR
			if ( !Application.isPlaying )
				DestroyImmediate( child.gameObject );
			else
#endif
				Destroy( child.gameObject );
		}

		_ghostRenderers.Clear();
		_aimMeshColliders.Clear();
		_hasCachedWorldBounds = false;
	}

	void CacheGhostRenderers()
	{
		_ghostRenderers.Clear();
		if ( _ghostRoot == null )
			return;

		_ghostRoot.GetComponentsInChildren( true, _ghostRenderers );
		_hasCachedWorldBounds = false;
	}

	void EnsureAimColliders()
	{
		if ( _ghostRoot == null )
			return;

		// Non-convex mesh colliders match the ghost shape. Unity forbids non-convex triggers,
		// so use Collectable (player does not physically collide; aim rays still hit).
		BoxCollider legacyBox = _ghostRoot.GetComponent<BoxCollider>();
		if ( legacyBox != null )
		{
#if UNITY_EDITOR
			if ( !Application.isPlaying )
				DestroyImmediate( legacyBox );
			else
#endif
				Destroy( legacyBox );
		}

		int collectableLayer = PhysicsLayers.CollectableLayer;
		if ( collectableLayer < 0 )
			collectableLayer = 0;

		_aimMeshColliders.Clear();
		MeshFilter[] filters = _ghostRoot.GetComponentsInChildren<MeshFilter>( true );
		for ( int i = 0; i < filters.Length; i++ )
		{
			MeshFilter filter = filters[ i ];
			if ( filter == null || filter.sharedMesh == null )
				continue;

			MeshCollider meshCollider = filter.GetComponent<MeshCollider>();
			if ( meshCollider == null )
				meshCollider = filter.gameObject.AddComponent<MeshCollider>();

			meshCollider.sharedMesh = filter.sharedMesh;
			meshCollider.convex = false;
			meshCollider.isTrigger = false;
			meshCollider.enabled = true;
			filter.gameObject.layer = collectableLayer;
			_aimMeshColliders.Add( meshCollider );
		}

		_ghostRoot.gameObject.layer = collectableLayer;
		TryComputeWorldBounds( out _ );
	}

	/// <summary>
	/// True when <paramref name="collider"/> is one of this buildable's ghost mesh aim volumes.
	/// </summary>
	public bool IsGhostAimCollider( Collider collider )
	{
		if ( collider == null || _ghostRoot == null )
			return false;

		for ( int i = 0; i < _aimMeshColliders.Count; i++ )
		{
			if ( _aimMeshColliders[ i ] == collider )
				return true;
		}

		// Mesh under Ghost that may have been added after the last Ensure — still counts as direct mesh aim.
		if ( collider is MeshCollider
			&& collider.transform != null
			&& ( collider.transform == _ghostRoot || collider.transform.IsChildOf( _ghostRoot ) ) )
			return true;

		return false;
	}

	bool TryComputeWorldBounds( out Bounds bounds )
	{
		bounds = default;
		bool has = false;

		Transform meshRoot = null;
		if ( _ghostRoot != null && _ghostRoot.childCount > 0 )
			meshRoot = _ghostRoot;
		else if ( _builtRoot != null )
			meshRoot = _builtRoot;

		if ( meshRoot != null )
		{
			MeshFilter[] filters = meshRoot.GetComponentsInChildren<MeshFilter>( true );
			for ( int i = 0; i < filters.Length; i++ )
			{
				MeshFilter filter = filters[ i ];
				if ( filter == null || filter.sharedMesh == null )
					continue;

				Bounds meshBounds = filter.sharedMesh.bounds;
				Matrix4x4 localToWorld = filter.transform.localToWorldMatrix;
				EncapsulateTransformedWorldBounds( ref bounds, ref has, meshBounds, localToWorld );
			}
		}

		if ( !has && _aimMeshColliders.Count > 0 )
		{
			for ( int i = 0; i < _aimMeshColliders.Count; i++ )
			{
				MeshCollider collider = _aimMeshColliders[ i ];
				if ( collider == null || !collider.enabled )
					continue;

				if ( !has )
				{
					bounds = collider.bounds;
					has = true;
				}
				else
					bounds.Encapsulate( collider.bounds );
			}
		}

		if ( !has )
		{
			bounds = new Bounds( transform.position, Vector3.one );
			_cachedWorldBounds = bounds;
			_hasCachedWorldBounds = true;
			return false;
		}

		_cachedWorldBounds = bounds;
		_hasCachedWorldBounds = true;
		return true;
	}

	static void EncapsulateTransformedWorldBounds( ref Bounds worldBounds, ref bool hasBounds, Bounds meshBounds, Matrix4x4 localToWorld )
	{
		Vector3 extents = meshBounds.extents;
		Vector3 center = meshBounds.center;
		for ( int x = -1; x <= 1; x += 2 )
		{
			for ( int y = -1; y <= 1; y += 2 )
			{
				for ( int z = -1; z <= 1; z += 2 )
				{
					Vector3 corner = center + new Vector3( extents.x * x, extents.y * y, extents.z * z );
					Vector3 world = localToWorld.MultiplyPoint3x4( corner );
					if ( !hasBounds )
					{
						worldBounds = new Bounds( world, Vector3.zero );
						hasBounds = true;
					}
					else
						worldBounds.Encapsulate( world );
				}
			}
		}
	}

	void EnsureGhostMaterial( BuildModeDefinition def )
	{
		if ( _ghostMaterial != null )
			return;

		Material shared = null;
#if UNITY_EDITOR
		if ( !Application.isPlaying )
		{
			shared = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(
				"Assets/Materials/Shaders/Placement/M_BuildGhost.mat" );
			if ( shared != null )
			{
				_ghostMaterial = shared;
				return;
			}
		}
#endif

		Shader shader = def != null ? def.ghostShader : null;
		if ( shader == null && shared != null )
			shader = shared.shader;
		if ( shader == null )
			shader = Shader.Find( "DragonLoot/Build Ghost" );
		if ( shader == null )
			shader = Shader.Find( "DragonLoot/Placement Ghost" );
		if ( shader == null )
			shader = Shader.Find( "Universal Render Pipeline/Unlit" );

		_ghostMaterial = new Material( shader );
		_ghostMaterial.name = "BuildGhostRuntime";

		if ( Application.isPlaying && _ghostRenderers.Count > 0 )
		{
			for ( int i = 0; i < _ghostRenderers.Count; i++ )
			{
				MeshRenderer renderer = _ghostRenderers[ i ];
				if ( renderer == null )
					continue;
				Material[] mats = renderer.sharedMaterials;
				for ( int m = 0; m < mats.Length; m++ )
					mats[ m ] = _ghostMaterial;
				renderer.sharedMaterials = mats;
			}
		}
	}

	void ApplyGhostMaterialParams(
		BuildModeDefinition def,
		Vector3 playerWorldPos,
		float completeGlow,
		float alphaMul,
		float fadeStart,
		float fadeEnd )
	{
		if ( _ghostMaterial == null )
			EnsureGhostMaterial( def );
		if ( _ghostMaterial == null )
			return;

		EnsurePropertyBlock();

		Color tint = def != null ? def.ghostColor : new Color( 0.35f, 0.75f, 1f, 0.4f );
		Color rim = Color.Lerp( tint, Color.white, 0.35f );
		rim.a = 1f;
		Color core = tint * 0.35f;
		core.a = 1f;
		Color completeColor = def != null ? def.completeGlowColor : new Color( 1.4f, 0.95f, 0.28f, 1f );

		float fresnelPower = def != null ? def.ghostFresnelPower : 2.4f;
		float fresnelBoost = def != null ? def.ghostFresnelBoost : 0.7f;
		float pulseAmount = def != null ? def.ghostPulseAmount : 0.12f;
		float pulseSpeed = def != null ? def.ghostPulseSpeed : 0.85f;
		float rimIntensity = def != null ? def.ghostRimIntensity : 1.15f;
		float coreIntensity = def != null ? def.ghostCoreIntensity : 0.28f;

		float glow = Mathf.Clamp01( completeGlow );
		float alpha = Mathf.Clamp01( alphaMul );
		// Amp pulse during the yellow flash for a lively build punch.
		float pulseAmt = Mathf.Lerp( pulseAmount, Mathf.Max( pulseAmount, 0.28f ), glow );
		float pulseSpd = Mathf.Lerp( pulseSpeed, Mathf.Max( pulseSpeed, 2.4f ), glow );

		_ghostMaterial.SetColor( BaseColorId, tint );
		if ( _ghostMaterial.HasProperty( ColorId ) )
			_ghostMaterial.SetColor( ColorId, tint );
		_ghostMaterial.SetColor( RimColorId, rim );
		_ghostMaterial.SetColor( CoreColorId, core );
		_ghostMaterial.SetFloat( FresnelPowerId, fresnelPower );
		_ghostMaterial.SetFloat( FresnelBoostId, fresnelBoost );
		_ghostMaterial.SetFloat( RimIntensityId, rimIntensity );
		_ghostMaterial.SetFloat( CoreIntensityId, coreIntensity );
		_ghostMaterial.SetFloat( PulseSpeedId, pulseSpd );
		_ghostMaterial.SetFloat( PulseAmountId, pulseAmt );
		_ghostMaterial.SetVector( PlayerWorldPosId, playerWorldPos );
		_ghostMaterial.SetFloat( FadeStartId, fadeStart );
		_ghostMaterial.SetFloat( FadeEndId, fadeEnd );
		_ghostMaterial.SetFloat( CompleteGlowId, glow );
		_ghostMaterial.SetColor( CompleteColorId, completeColor );
		_ghostMaterial.SetFloat( AlphaMulId, alpha );

		_propertyBlock.Clear();
		_propertyBlock.SetColor( BaseColorId, tint );
		_propertyBlock.SetColor( ColorId, tint );
		_propertyBlock.SetColor( RimColorId, rim );
		_propertyBlock.SetColor( CoreColorId, core );
		_propertyBlock.SetFloat( FresnelPowerId, fresnelPower );
		_propertyBlock.SetFloat( FresnelBoostId, fresnelBoost );
		_propertyBlock.SetFloat( RimIntensityId, rimIntensity );
		_propertyBlock.SetFloat( CoreIntensityId, coreIntensity );
		_propertyBlock.SetFloat( PulseSpeedId, pulseSpd );
		_propertyBlock.SetFloat( PulseAmountId, pulseAmt );
		_propertyBlock.SetVector( PlayerWorldPosId, playerWorldPos );
		_propertyBlock.SetFloat( FadeStartId, fadeStart );
		_propertyBlock.SetFloat( FadeEndId, fadeEnd );
		_propertyBlock.SetFloat( CompleteGlowId, glow );
		_propertyBlock.SetColor( CompleteColorId, completeColor );
		_propertyBlock.SetFloat( AlphaMulId, alpha );

		for ( int i = 0; i < _ghostRenderers.Count; i++ )
		{
			MeshRenderer renderer = _ghostRenderers[ i ];
			if ( renderer != null )
				renderer.SetPropertyBlock( _propertyBlock );
		}
	}

	bool HasCircularPrerequisite()
	{
		if ( _requiredBuildables == null || _requiredBuildables.Length == 0 )
			return false;

		HashSet<BuildableObject> visiting = new HashSet<BuildableObject>();
		HashSet<BuildableObject> visited = new HashSet<BuildableObject>();
		bool circular = HasCircularPrerequisiteRecursive( this, visiting, visited );
		if ( circular && !_loggedCircularPrereq )
		{
			_loggedCircularPrereq = true;
			Debug.LogWarning( "BuildableObject has a circular prerequisite chain: " + name, this );
		}

		return circular;
	}

	static bool HasCircularPrerequisiteRecursive(
		BuildableObject current,
		HashSet<BuildableObject> visiting,
		HashSet<BuildableObject> visited )
	{
		if ( current == null )
			return false;
		if ( visited.Contains( current ) )
			return false;
		if ( !visiting.Add( current ) )
			return true;

		BuildableObject[] required = current._requiredBuildables;
		if ( required != null )
		{
			for ( int i = 0; i < required.Length; i++ )
			{
				if ( HasCircularPrerequisiteRecursive( required[ i ], visiting, visited ) )
					return true;
			}
		}

		visiting.Remove( current );
		visited.Add( current );
		return false;
	}
}
