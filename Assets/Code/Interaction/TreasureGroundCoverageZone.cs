using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Ground treasure coverage: assigns a <see cref="TreasureGroupDefinition"/>,
/// samples traversable TreasureSurface cells inside a box footprint, previews layout
/// in the Scene view, and spawns real collectible loot at runtime.
/// </summary>
[RequireComponent( typeof( BoxCollider ) )]
public class TreasureGroundCoverageZone : MonoBehaviour
{
	[SerializeField]
	TreasureGroupDefinition group;

	[SerializeField]
	[Tooltip( "0 = derive from this component instance id." )]
	int layoutSeed;

	[Header( "Perlin Density" )]
	[SerializeField]
	[Min( 0.01f )]
	float densityFrequency = 0.15f;

	[SerializeField]
	[Range( 0f, 1f )]
	[Tooltip( "Soft bias for prop placement patches. Coin stacks always spread across the full zone." )]
	float densityThreshold = 0.35f;

	[Header( "Coin Stacks" )]
	[SerializeField]
	[Min( 1 )]
	int minCoinsPerStack = 3;

	[SerializeField]
	[Min( 1 )]
	int maxCoinsPerStack = 28;

	[SerializeField]
	[Min( 0.01f )]
	float stackHeightFrequency = 0.31f;

	[SerializeField]
	[Range( 0f, 1f )]
	[Tooltip( "0 = homogeneous stacks when counts allow. 1 = every stack mixed. Leftover coins that cannot fill a min-height homogeneous stack still mix." )]
	float coinMixFraction = 0.45f;

	[Header( "Spacing" )]
	[SerializeField]
	[Tooltip( "0 = GroundCoinStack.DefaultJoinRadius * 2.2." )]
	[Min( 0f )]
	float minSpacing;

	[SerializeField]
	[Range( 0f, 1f )]
	[Tooltip( "Breaks the cell grid. 0 = sit on cell centers. 1 = jitter up to min spacing. Overlap is still rejected." )]
	float placementJitter = 0.85f;

	[SerializeField]
	TreasureGroundCategoryRules categoryRules = TreasureGroundCategoryRules.Default();

	[Header( "Surface" )]
	[SerializeField]
	[Min( 1 )]
	int surfaceNeighborhoodCells = 1;

	[Header( "Preview / Runtime" )]
	[SerializeField]
	bool showPreview = true;

	[SerializeField]
	bool spawnAtRuntime = true;

	public TreasureGroupDefinition Group => group;
	public int LayoutSeed => layoutSeed;
	public float DensityFrequency => densityFrequency;
	public float DensityThreshold => densityThreshold;
	public int MinCoinsPerStack => minCoinsPerStack;
	public int MaxCoinsPerStack => maxCoinsPerStack;
	public float StackHeightFrequency => stackHeightFrequency;
	public float CoinMixFraction => coinMixFraction;
	public float MinSpacing => minSpacing;
	public float PlacementJitter => placementJitter;
	public TreasureGroundCategoryRules CategoryRules => categoryRules;
	public int SurfaceNeighborhoodCells => surfaceNeighborhoodCells;
	public bool ShowPreview => showPreview;
	public bool SpawnAtRuntime => spawnAtRuntime;

	public int ExpectedGoldBarCount
	{
		get
		{
			if ( group == null || group.entries == null )
				return 0;

			int total = 0;
			for ( int i = 0; i < group.entries.Length; i++ )
			{
				TreasureGroupEntry entry = group.entries[ i ];
				if ( entry.treasure == null || entry.count <= 0 )
					continue;
				if ( GoldBarStack.IsStackable( entry.treasure ) )
					total += entry.count;
			}

			return total;
		}
	}

	public BoxCollider Box => _box != null ? _box : ( _box = GetComponent<BoxCollider>() );

	BoxCollider _box;
	bool _spawned;

	void Reset()
	{
		BoxCollider box = Box;
		box.isTrigger = true;
		box.center = Vector3.zero;
		box.size = new Vector3( 6f, 0.5f, 6f );
		categoryRules = TreasureGroundCategoryRules.Default();
	}

	void OnValidate()
	{
		minCoinsPerStack = Mathf.Max( 1, minCoinsPerStack );
		maxCoinsPerStack = Mathf.Max( minCoinsPerStack, maxCoinsPerStack );
		densityFrequency = Mathf.Max( 0.01f, densityFrequency );
		stackHeightFrequency = Mathf.Max( 0.01f, stackHeightFrequency );
		coinMixFraction = Mathf.Clamp01( coinMixFraction );
		placementJitter = Mathf.Clamp01( placementJitter );
		surfaceNeighborhoodCells = Mathf.Max( 1, surfaceNeighborhoodCells );
#if UNITY_EDITOR
		UnityEditor.SceneView.RepaintAll();
#endif
	}

	void Start()
	{
		if ( !spawnAtRuntime || _spawned )
			return;

		SpawnCoverageLoot();
	}

	public void SetShowPreview( bool enabled )
	{
		showPreview = enabled;
	}

	public Bounds GetWorldBounds()
	{
		BoxCollider box = Box;
		if ( box == null )
			return new Bounds( transform.position, Vector3.one );

		return box.bounds;
	}

	public int CountRemainingGoldBarsInZone()
	{
		Bounds bounds = GetWorldBounds();
		int remaining = 0;

		IReadOnlyList<GroundGoldBarStack> stacks = GroundGoldBarStack.ActiveStacks;
		for ( int i = 0; i < stacks.Count; i++ )
		{
			GroundGoldBarStack stack = stacks[ i ];
			if ( stack == null )
				continue;
			if ( !bounds.Contains( stack.transform.position ) )
				continue;
			remaining += stack.Count;
		}

		remaining += WorldTreasureStreamer.CountHiddenGoldBarsInBounds( bounds );

		TreasureItem[] items = Object.FindObjectsByType<TreasureItem>( FindObjectsInactive.Exclude, FindObjectsSortMode.None );
		for ( int i = 0; i < items.Length; i++ )
		{
			TreasureItem item = items[ i ];
			if ( item == null || !GoldBarStack.IsStackable( item.Definition ) )
				continue;
			if ( item.Owner is GroundGoldBarStack || item.Owner is GoldBarDisplayTableInteractable || item.Owner is PlayerCarry )
				continue;
			if ( !bounds.Contains( item.transform.position ) )
				continue;
			remaining++;
		}

		return remaining;
	}

	public int ComputeSettingsFingerprint()
	{
		unchecked
		{
			int h = 17;
			h = h * 31 + ( group != null ? group.ComputeContentFingerprint() : 0 );
			h = h * 31 + ResolveEffectiveSeed();
			h = h * 31 + densityFrequency.GetHashCode();
			h = h * 31 + densityThreshold.GetHashCode();
			h = h * 31 + minCoinsPerStack;
			h = h * 31 + maxCoinsPerStack;
			h = h * 31 + stackHeightFrequency.GetHashCode();
			h = h * 31 + coinMixFraction.GetHashCode();
			h = h * 31 + minSpacing.GetHashCode();
			h = h * 31 + placementJitter.GetHashCode();
			h = h * 31 + categoryRules.coinSpacingScale.GetHashCode();
			h = h * 31 + categoryRules.gemDensityScale.GetHashCode();
			h = h * 31 + categoryRules.gemSpacingScale.GetHashCode();
			h = h * 31 + categoryRules.artifactDensityScale.GetHashCode();
			h = h * 31 + categoryRules.artifactSpacingScale.GetHashCode();
			h = h * 31 + surfaceNeighborhoodCells;
			Bounds world = GetWorldBounds();
			h = h * 31 + world.min.GetHashCode();
			h = h * 31 + world.max.GetHashCode();
			return h;
		}
	}

	public int ResolveEffectiveSeed()
	{
		if ( layoutSeed != 0 )
			return layoutSeed;
		return Mathf.Abs( GetInstanceID() );
	}

	public TreasureGroundCoveragePlacement.Request BuildPlacementRequest()
	{
		return new TreasureGroundCoveragePlacement.Request
		{
			worldBounds = GetWorldBounds(),
			group = group,
			layoutSeed = ResolveEffectiveSeed(),
			densityFrequency = densityFrequency,
			densityThreshold = densityThreshold,
			minCoinsPerStack = minCoinsPerStack,
			maxCoinsPerStack = maxCoinsPerStack,
			stackHeightFrequency = stackHeightFrequency,
			coinMixFraction = coinMixFraction,
			minSpacing = minSpacing,
			placementJitter = placementJitter,
			categoryRules = categoryRules,
			surfaceNeighborhoodCells = surfaceNeighborhoodCells
		};
	}

	public void SpawnCoverageLoot()
	{
		if ( _spawned )
			return;

		if ( group == null )
		{
			Debug.LogWarning( $"Ground coverage on '{name}': no TreasureGroupDefinition assigned.", this );
			return;
		}

		TreasureGroundCoveragePlacement.Result result = TreasureGroundCoveragePlacement.Compute( BuildPlacementRequest() );
		if ( !result.success )
		{
			Debug.LogWarning( $"Ground coverage on '{name}': {result.error}", this );
			return;
		}

		LooseTreasureManager.EnsureExists();
		int createdStacks = 0;
		int createdProps = 0;

		if ( result.coinStacks != null )
		{
			for ( int i = 0; i < result.coinStacks.Length; i++ )
			{
				TreasureGroundCoveragePlacement.CoinStackPlacement stackPlacement = result.coinStacks[ i ];
				if ( stackPlacement.slots == null || stackPlacement.Count <= 0 )
					continue;

				WorldTreasureStreamer.SpawnOrRecordCoinStack(
					stackPlacement.worldPosition,
					Quaternion.identity,
					stackPlacement.slots,
					BuildStackName( stackPlacement, createdStacks ) );
				createdStacks++;
			}
		}

		if ( result.props != null )
		{
			for ( int i = 0; i < result.props.Length; i++ )
			{
				TreasureGroundCoveragePlacement.PropPlacement propPlacement = result.props[ i ];
				if ( propPlacement.definition == null )
					continue;

				TreasureItem item = TreasureItemFactory.SpawnSync(
					propPlacement.definition,
					propPlacement.worldPosition,
					propPlacement.rotation );
				if ( item == null )
					continue;

				item.EnterSettledPhysics( propPlacement.worldPosition, propPlacement.rotation );
				item.name = BuildPropName( propPlacement.definition, createdProps );
				createdProps++;
			}
		}

		_spawned = true;
		int minStackCoins = int.MaxValue;
		int maxStackCoins = 0;
		if ( result.coinStacks != null )
		{
			for ( int i = 0; i < result.coinStacks.Length; i++ )
			{
				int count = result.coinStacks[ i ].Count;
				if ( count <= 0 )
					continue;
				if ( count < minStackCoins )
					minStackCoins = count;
				if ( count > maxStackCoins )
					maxStackCoins = count;
			}
		}

		if ( minStackCoins == int.MaxValue )
			minStackCoins = 0;

		Debug.Log(
			$"Ground coverage spawned on '{name}': {createdStacks} coin stacks, {createdProps} props "
			+ $"({result.placedCoins}/{result.requestedCoins} coins, {result.placedProps}/{result.requestedProps} props). "
			+ $"Stack heights {minStackCoins}-{maxStackCoins}.",
			this );
	}

	static string BuildStackName( TreasureGroundCoveragePlacement.CoinStackPlacement stack, int index )
	{
		if ( stack.IsHomogeneous )
		{
			TreasureDefinition definition = stack.PrimaryDefinition;
			string label = definition != null ? definition.name : "CoinStack";
			return $"{label}_Stack_{index:000}";
		}

		return $"Mixed_Stack_{index:000}";
	}

	static string BuildPropName( TreasureDefinition definition, int index )
	{
		string label = definition != null ? definition.name : "Prop";
		return $"{label}_{index:000}";
	}

	void OnDrawGizmosSelected()
	{
		Bounds bounds = GetWorldBounds();
		Gizmos.color = new Color( 0.95f, 0.75f, 0.2f, 0.35f );
		Gizmos.DrawCube( bounds.center, bounds.size );
		Gizmos.color = new Color( 0.95f, 0.75f, 0.2f, 0.9f );
		Gizmos.DrawWireCube( bounds.center, bounds.size );
	}
}
