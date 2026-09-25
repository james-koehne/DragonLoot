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

	[SerializeField]
	Transform _builtRoot;

	[SerializeField]
	Transform _ghostRoot;

	[SerializeField]
	BuildableObject[] _requiredBuildables;

	[SerializeField]
	bool _startBuilt;

	readonly List<MeshRenderer> _ghostRenderers = new List<MeshRenderer>( 8 );
	readonly MaterialPropertyBlock _propertyBlock = new MaterialPropertyBlock();
	Material _ghostMaterial;
	BoxCollider _aimCollider;
	bool _isBuilt;
	bool _ghostVisible;
	bool _loggedCircularPrereq;
	bool _setupComplete;

	public bool IsBuilt => _isBuilt;
	public Transform BuiltRoot => _builtRoot;
	public Transform GhostRoot => _ghostRoot;
	public BuildableObject[] RequiredBuildables => _requiredBuildables;

	public static IReadOnlyList<BuildableObject> Active => ActiveBuildables;

	void Awake()
	{
		EnsureHierarchy();
		ApplyInitialState();
		_setupComplete = true;
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

	public bool IsEligibleToShow( Vector3 playerPosition, float showRadius )
	{
		if ( _isBuilt || !_setupComplete )
			return false;
		if ( !ArePrerequisitesMet() )
			return false;

		float radius = Mathf.Max( 0.5f, showRadius );
		Vector3 center = GetWorldCenter();
		return ( center - playerPosition ).sqrMagnitude <= radius * radius;
	}

	public Vector3 GetWorldCenter()
	{
		if ( _aimCollider != null )
			return _aimCollider.bounds.center;

		if ( _ghostRoot != null )
			return _ghostRoot.position;

		return transform.position;
	}

	public void SetGhostVisible( bool visible )
	{
		if ( _isBuilt )
			visible = false;

		_ghostVisible = visible;
		if ( _ghostRoot != null && _ghostRoot.gameObject.activeSelf != visible )
			_ghostRoot.gameObject.SetActive( visible );
	}

	public void UpdateGhostFade( Vector3 playerWorldPos, BuildModeDefinition def )
	{
		if ( !_ghostVisible || _ghostMaterial == null || def == null )
			return;

		ApplyGhostMaterialParams( def, playerWorldPos );
	}

	public void Complete()
	{
		if ( _isBuilt )
			return;

		_isBuilt = true;
		SetGhostVisible( false );

		if ( _builtRoot != null )
			_builtRoot.gameObject.SetActive( true );

		EventBus.Publish( new BuildableCompletedEvent { Buildable = this } );
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
		EnsureAimCollider();
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
		EnsureAimCollider();
		ApplyGhostMaterialParams( def, Vector3.zero );
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
		_aimCollider = null;
	}

	void CacheGhostRenderers()
	{
		_ghostRenderers.Clear();
		if ( _ghostRoot == null )
			return;

		_ghostRoot.GetComponentsInChildren( true, _ghostRenderers );
	}

	void EnsureAimCollider()
	{
		if ( _ghostRoot == null )
			return;

		_aimCollider = _ghostRoot.GetComponent<BoxCollider>();
		if ( _aimCollider == null )
			_aimCollider = _ghostRoot.gameObject.AddComponent<BoxCollider>();

		Bounds bounds = new Bounds( _ghostRoot.position, Vector3.zero );
		bool hasBounds = false;
		for ( int i = 0; i < _ghostRenderers.Count; i++ )
		{
			MeshRenderer renderer = _ghostRenderers[ i ];
			if ( renderer == null )
				continue;
			if ( !hasBounds )
			{
				bounds = renderer.bounds;
				hasBounds = true;
			}
			else
				bounds.Encapsulate( renderer.bounds );
		}

		if ( !hasBounds )
			bounds = new Bounds( _ghostRoot.position, Vector3.one * 0.5f );

		Vector3 localCenter = _ghostRoot.InverseTransformPoint( bounds.center );
		Vector3 lossy = _ghostRoot.lossyScale;
		Vector3 localSize = new Vector3(
			SafeDivide( bounds.size.x, Mathf.Abs( lossy.x ) ),
			SafeDivide( bounds.size.y, Mathf.Abs( lossy.y ) ),
			SafeDivide( bounds.size.z, Mathf.Abs( lossy.z ) ) );

		_aimCollider.isTrigger = false;
		_aimCollider.center = localCenter;
		_aimCollider.size = Vector3.Max( localSize, Vector3.one * 0.05f );

		int collectable = PhysicsLayers.CollectableLayer;
		if ( collectable >= 0 )
			_ghostRoot.gameObject.layer = collectable;
	}

	static float SafeDivide( float numerator, float denominator )
	{
		if ( denominator < 0.0001f )
			return numerator;
		return numerator / denominator;
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

	void ApplyGhostMaterialParams( BuildModeDefinition def, Vector3 playerWorldPos )
	{
		if ( _ghostMaterial == null )
			EnsureGhostMaterial( def );
		if ( _ghostMaterial == null )
			return;

		Color tint = def != null ? def.ghostColor : new Color( 0.35f, 0.75f, 1f, 0.4f );
		Color rim = Color.Lerp( tint, Color.white, 0.35f );
		rim.a = 1f;
		Color core = tint * 0.35f;
		core.a = 1f;

		float fresnelPower = def != null ? def.ghostFresnelPower : 2.4f;
		float fresnelBoost = def != null ? def.ghostFresnelBoost : 0.7f;
		float pulseAmount = def != null ? def.ghostPulseAmount : 0.12f;
		float pulseSpeed = def != null ? def.ghostPulseSpeed : 0.85f;
		float rimIntensity = def != null ? def.ghostRimIntensity : 1.15f;
		float coreIntensity = def != null ? def.ghostCoreIntensity : 0.28f;
		float fadeStart = def != null ? def.fadeStart : 12f;
		float fadeEnd = def != null ? def.fadeEnd : 18f;

		_ghostMaterial.SetColor( BaseColorId, tint );
		if ( _ghostMaterial.HasProperty( ColorId ) )
			_ghostMaterial.SetColor( ColorId, tint );
		_ghostMaterial.SetColor( RimColorId, rim );
		_ghostMaterial.SetColor( CoreColorId, core );
		_ghostMaterial.SetFloat( FresnelPowerId, fresnelPower );
		_ghostMaterial.SetFloat( FresnelBoostId, fresnelBoost );
		_ghostMaterial.SetFloat( RimIntensityId, rimIntensity );
		_ghostMaterial.SetFloat( CoreIntensityId, coreIntensity );
		_ghostMaterial.SetFloat( PulseSpeedId, pulseSpeed );
		_ghostMaterial.SetFloat( PulseAmountId, pulseAmount );
		_ghostMaterial.SetVector( PlayerWorldPosId, playerWorldPos );
		_ghostMaterial.SetFloat( FadeStartId, fadeStart );
		_ghostMaterial.SetFloat( FadeEndId, fadeEnd );

		_propertyBlock.Clear();
		_propertyBlock.SetColor( BaseColorId, tint );
		_propertyBlock.SetColor( ColorId, tint );
		_propertyBlock.SetColor( RimColorId, rim );
		_propertyBlock.SetColor( CoreColorId, core );
		_propertyBlock.SetFloat( FresnelPowerId, fresnelPower );
		_propertyBlock.SetFloat( FresnelBoostId, fresnelBoost );
		_propertyBlock.SetFloat( RimIntensityId, rimIntensity );
		_propertyBlock.SetFloat( CoreIntensityId, coreIntensity );
		_propertyBlock.SetFloat( PulseSpeedId, pulseSpeed );
		_propertyBlock.SetFloat( PulseAmountId, pulseAmount );
		_propertyBlock.SetVector( PlayerWorldPosId, playerWorldPos );
		_propertyBlock.SetFloat( FadeStartId, fadeStart );
		_propertyBlock.SetFloat( FadeEndId, fadeEnd );

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
