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
	[Tooltip( "Stable save id. Falls back to definition id, then GameObject name." )]
	string _persistenceId;

	[SerializeField]
	[Tooltip( "Catalog world event ids fired once when this placement is built (live Complete only)." )]
	string[] _onCompleteWorldEventIds;

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

	bool _objectiveOutlineRequested;
	bool _pileBlockedCached;
	float _pileBlockedCheckedAt = -999f;

	const float PileBlockedCacheSeconds = 0.18f;
	const float PileSampleStep = 0.65f;

	public bool IsBuilt => _isBuilt;
	public bool IsCompleting => _completing;
	public bool IsGhostVisible => _ghostVisible;
	public bool ObjectiveOutlineRequested => _objectiveOutlineRequested;
	public Transform BuiltRoot => _builtRoot;
	public Transform GhostRoot => _ghostRoot;
	public BuildableObject[] RequiredBuildables => _requiredBuildables;
	public BuildableDefinition Definition => _definition;

	/// <summary>
	/// Keeps the ghost visible for quest outline even outside build mode / fade distance.
	/// Ghost still uses the normal build-ghost material (same look as in build mode).
	/// </summary>
	public void SetObjectiveOutlineRequested( bool requested )
	{
		_objectiveOutlineRequested = requested;
		if ( _isBuilt || _completing )
		{
			_objectiveOutlineRequested = false;
			return;
		}

		if ( !requested )
			return;

		SetGhostVisible( true );
		EnsureObjectiveGhostLook();
	}

	void EnsureObjectiveGhostLook()
	{
		BuildModeDefinition def = GameInstance.GetDefinition<BuildModeDefinition>();
		EnsureGhostMaterial( def );

		// Keep objective ghosts fully readable at any distance (outline + ghost mesh).
		PlayerController player = GameMode.Instance != null ? GameMode.Instance.Player : null;
		Vector3 playerPos = player != null ? player.transform.position : transform.position;
		ApplyGhostMaterialParams( def, playerPos, completeGlow: 0f, alphaMul: 1f, fadeStart: 9999f, fadeEnd: 10000f );
	}

	public string ResolvePersistenceId()
	{
		if ( !string.IsNullOrEmpty( _persistenceId ) )
			return _persistenceId;
		if ( _definition != null && !string.IsNullOrEmpty( _definition.id ) )
			return _definition.id;
		return gameObject != null ? gameObject.name : string.Empty;
	}

	/// <summary>Applies built/unbuilt from a save without complete VFX or events.</summary>
	public void ApplySavedBuiltState( bool built )
	{
		_completing = false;
		_builtActivatedDuringReveal = false;
		_completeT = 0f;
		_completeGlow = 0f;
		_completeAlphaMul = 1f;
		_isBuilt = built;

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
	}

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

	/// <summary>
	/// True when a treasure pile's heightfield intersects this buildable's ghost mesh colliders.
	/// Result is cached briefly; aimed buildables always refresh.
	/// </summary>
	public bool IsBlockedByTreasurePile()
	{
		if ( _isBuilt || _completing )
			return false;

		bool aimed = IsCurrentlyAimedBuildable();
		float now = Time.unscaledTime;
		if ( !aimed && now - _pileBlockedCheckedAt < PileBlockedCacheSeconds )
			return _pileBlockedCached;

		_pileBlockedCached = EvaluateBlockedByTreasurePile();
		_pileBlockedCheckedAt = now;
		return _pileBlockedCached;
	}

	bool IsCurrentlyAimedBuildable()
	{
		if ( GameMode.Instance == null || GameMode.Instance.Player == null )
			return false;
		PlayerBuildMode buildMode = GameMode.Instance.Player.BuildMode;
		return buildMode != null && buildMode.AimedBuildable == this;
	}

	bool EvaluateBlockedByTreasurePile()
	{
		EnsureAimCollidersForPileCheck();
		if ( _aimMeshColliders.Count == 0 )
		{
			if ( !TryGetWorldBounds( out Bounds bounds ) )
				bounds = new Bounds( transform.position, Vector3.one );
			return PlacementFloorSurface.WorldBoundsOverlapsHeightfieldPile( bounds );
		}

		IReadOnlyList<TreasurePileInteractable> piles = TreasurePileInteractable.ActivePiles;
		if ( piles == null || piles.Count == 0 )
			return false;

		if ( !TryGetWorldBounds( out Bounds ghostBounds ) )
			ghostBounds = new Bounds( transform.position, Vector3.one );

		for ( int p = 0; p < piles.Count; p++ )
		{
			TreasurePileInteractable pile = piles[ p ];
			if ( pile == null )
				continue;

			TreasurePileVisual visual = pile.PileVisual;
			if ( visual == null )
				continue;

			if ( !TryGetPileWorldBounds( visual, out Bounds pileBounds ) )
				continue;
			if ( !ghostBounds.Intersects( pileBounds ) )
				continue;

			for ( int c = 0; c < _aimMeshColliders.Count; c++ )
			{
				MeshCollider meshCol = _aimMeshColliders[ c ];
				if ( meshCol == null || !meshCol.enabled )
					continue;

				Bounds meshBounds = meshCol.bounds;
				if ( !meshBounds.Intersects( pileBounds ) )
					continue;
				if ( AimMeshIntersectsPileHeightfield( meshCol, meshBounds, pileBounds, visual ) )
					return true;
			}
		}

		return false;
	}

	void EnsureAimCollidersForPileCheck()
	{
		if ( _ghostRoot == null )
			return;
		if ( _aimMeshColliders.Count > 0 )
			return;

		bool wasActive = _ghostRoot.gameObject.activeSelf;
		if ( !wasActive )
			_ghostRoot.gameObject.SetActive( true );

		EnsureAimColliders();

		if ( !wasActive && !_ghostVisible )
			_ghostRoot.gameObject.SetActive( false );
	}

	static bool TryGetPileWorldBounds( TreasurePileVisual pile, out Bounds bounds )
	{
		bounds = default;
		if ( pile == null )
			return false;

		GoldPileHeightfield heightfield = pile.Heightfield;
		float sizeX;
		float sizeZ;
		if ( heightfield != null && heightfield.IsInitialized )
		{
			sizeX = heightfield.WorldSizeX;
			sizeZ = heightfield.WorldSizeZ;
		}
		else
		{
			sizeX = pile.AuthoredWorldSizeX;
			sizeZ = pile.AuthoredWorldSizeZ;
		}

		if ( sizeX < 0.1f )
			sizeX = 4f;
		if ( sizeZ < 0.1f )
			sizeZ = 4f;

		Transform root = pile.transform;
		Vector3 center = root.position + Vector3.up * ( sizeX + sizeZ ) * 0.08f;
		Vector3 size = new Vector3( sizeX, Mathf.Max( 2f, Mathf.Max( sizeX, sizeZ ) * 0.35f ), sizeZ );
		bounds = new Bounds( center, size );
		return true;
	}

	static bool AimMeshIntersectsPileHeightfield(
		MeshCollider meshCol,
		Bounds meshBounds,
		Bounds pileBounds,
		TreasurePileVisual pile )
	{
		if ( meshCol == null || pile == null )
			return false;

		GoldPileHeightfield heightfield = pile.Heightfield;
		if ( heightfield == null || !heightfield.IsInitialized )
			return false;

		float minX = Mathf.Max( meshBounds.min.x, pileBounds.min.x );
		float maxX = Mathf.Min( meshBounds.max.x, pileBounds.max.x );
		float minZ = Mathf.Max( meshBounds.min.z, pileBounds.min.z );
		float maxZ = Mathf.Min( meshBounds.max.z, pileBounds.max.z );
		if ( minX > maxX || minZ > maxZ )
			return false;

		float maxRay = meshBounds.size.y + 2f;
		float step = PileSampleStep;

		// Corners + center of the overlap region first (cheap early outs).
		if ( SampleColumnAgainstPile( meshCol, meshBounds, pile, heightfield, minX, minZ, maxRay )
			|| SampleColumnAgainstPile( meshCol, meshBounds, pile, heightfield, maxX, minZ, maxRay )
			|| SampleColumnAgainstPile( meshCol, meshBounds, pile, heightfield, minX, maxZ, maxRay )
			|| SampleColumnAgainstPile( meshCol, meshBounds, pile, heightfield, maxX, maxZ, maxRay )
			|| SampleColumnAgainstPile( meshCol, meshBounds, pile, heightfield, ( minX + maxX ) * 0.5f, ( minZ + maxZ ) * 0.5f, maxRay ) )
			return true;

		for ( float x = minX; x <= maxX + 0.001f; x += step )
		{
			for ( float z = minZ; z <= maxZ + 0.001f; z += step )
			{
				if ( SampleColumnAgainstPile( meshCol, meshBounds, pile, heightfield, x, z, maxRay ) )
					return true;
			}
		}

		return false;
	}

	static bool SampleColumnAgainstPile(
		MeshCollider meshCol,
		Bounds meshBounds,
		TreasurePileVisual pile,
		GoldPileHeightfield heightfield,
		float x,
		float z,
		float maxRay )
	{
		Ray ray = new Ray( new Vector3( x, meshBounds.max.y + 0.5f, z ), Vector3.down );
		if ( !meshCol.Raycast( ray, out RaycastHit hit, maxRay ) )
			return false;

		Vector3 topHit = hit.point;
		if ( pile.IsPointBuried( topHit ) )
			return true;

		Vector3 mid = new Vector3( x, Mathf.Lerp( meshBounds.min.y, topHit.y, 0.5f ), z );
		if ( pile.IsPointBuried( mid ) )
			return true;

		if ( !pile.ContainsWorldPointXZ( topHit ) )
			return false;

		float surfaceLocalY = heightfield.SampleWorldHeight( topHit, pile.transform );
		Vector3 local = pile.transform.InverseTransformPoint( topHit );
		Vector3 surfaceWorld = pile.transform.TransformPoint( new Vector3( local.x, surfaceLocalY, local.z ) );
		return surfaceWorld.y >= meshBounds.min.y - 0.02f && surfaceWorld.y <= topHit.y + 0.02f;
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

		if ( _objectiveOutlineRequested )
		{
			EnsureObjectiveGhostLook();
			return;
		}

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

	/// <summary>Finds an active buildable by persistence id.</summary>
	public static bool TryFindByPersistenceId( string persistenceId, out BuildableObject buildable )
	{
		buildable = null;
		if ( string.IsNullOrEmpty( persistenceId ) )
			return false;

		for ( int i = 0; i < ActiveBuildables.Count; i++ )
		{
			BuildableObject candidate = ActiveBuildables[ i ];
			if ( candidate == null )
				continue;
			if ( candidate.ResolvePersistenceId() != persistenceId )
				continue;

			buildable = candidate;
			return true;
		}

		return false;
	}

	/// <summary>Finds an active buildable by persistence id and completes it (live Complete VFX/events).</summary>
	public static bool TryCompleteByPersistenceId( string persistenceId )
	{
		if ( !TryFindByPersistenceId( persistenceId, out BuildableObject buildable ) )
			return false;
		if ( buildable.IsBuilt || buildable.IsCompleting )
			return false;

		buildable.Complete();
		return true;
	}

	public void Complete()
	{
		if ( _isBuilt || _completing )
			return;
		if ( IsBlockedByTreasurePile() )
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
		FireOnCompleteWorldEvents();
	}

	void FireOnCompleteWorldEvents()
	{
		if ( _onCompleteWorldEventIds == null || _onCompleteWorldEventIds.Length == 0 )
			return;

		WorldEventSystem worldEvents = WorldEventSystem.Instance;
		if ( worldEvents == null )
			return;

		for ( int i = 0; i < _onCompleteWorldEventIds.Length; i++ )
		{
			string eventId = _onCompleteWorldEventIds[ i ];
			if ( string.IsNullOrEmpty( eventId ) )
				continue;
			worldEvents.TryFireIfUnfired( eventId );
		}
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

	Color ResolveGhostTint( BuildModeDefinition def, float completeGlow )
	{
		// During complete reveal the yellow flash owns the look; keep a neutral base tint.
		if ( _completing || completeGlow > 0.001f )
		{
			if ( def != null )
				return def.ghostColor;
			return new Color( 0.35f, 0.75f, 1f, 0.4f );
		}

		bool blocked = IsBlockedByTreasurePile();
		if ( blocked )
		{
			if ( def != null )
				return def.invalidGhostColor;
			return PlacementFeedbackColors.InvalidGhost;
		}

		if ( def != null )
			return def.validGhostColor;
		return PlacementFeedbackColors.ValidGhost;
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

		Color tint = ResolveGhostTint( def, completeGlow );
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
