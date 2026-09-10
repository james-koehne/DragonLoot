using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Serialization;

[CreateAssetMenu( fileName = "TreasureDefinition", menuName = "Definitions/TreasureDefinition" )]
public class TreasureDefinition : ScriptableObject
{
	[Header( "Identity" )]
	public string id;

	public string displayName;

	public TreasureCategory category;

	[Tooltip( "Variant within the category, e.g. Gold or Sapphire." )]
	public string variant;

	[Tooltip( "When true, first pickup still counts as discovered but skips the New Discovery toast." )]
	public bool suppressDiscoveryPopup;

	[Header( "Stats" )]
	[Min( 0 )]
	public int value = 1;

	[Tooltip( "Carry weight. Movement slows linearly with total carried weight; coins/gems typically use 1." )]
	[Min( 1 )]
	public int weight = 1;

	[Tooltip( "When true, this treasure cannot be carried with any other items." )]
	public bool exclusiveCarry = false;

	[Tooltip( "When true, uses heavyThrowForce instead of throwForce." )]
	public bool usesHeavyThrow = false;

	[Tooltip( "When true, this treasure cannot be thrown into empty space. Place it on a valid surface instead." )]
	public bool cannotThrow = false;

	[Tooltip( "When true, this treasure can only be placed on walkable ground or in an artifact presentation slot. Also prevents throwing." )]
	public bool placeOnGroundOrArtifactSlotOnly = false;

	[Tooltip( "Multiplier on global throw force for this treasure." )]
	[Min( 0f )]
	public float throwForceScale = 1f;

	[Tooltip( "Multiplier on global throw up bias for this treasure." )]
	[Min( 0f )]
	public float throwUpBiasScale = 1f;

	[Header( "Physics" )]
	[Min( 0.01f )]
	public float rigidbodyMass = 0.1f;

	[Min( 0f )]
	public float drag = 0.5f;

	[Min( 0f )]
	public float angularDrag = 0.5f;

	[Tooltip( "Rigidbody sleep threshold while in free physics." )]
	[Min( 0f )]
	public float sleepThreshold = 0.01f;

	[Tooltip( "Torque strength that tips upright coins toward lying flat." )]
	[Min( 0f )]
	public float autoToppleStrength = 2.5f;

	[Tooltip( "Degrees from upright above which auto-topple applies." )]
	[Range( 0f, 90f )]
	public float maxUprightAngle = 35f;

	[Tooltip( "Seconds of low motion before forcing sleep / stabilization." )]
	[Min( 0f )]
	public float physicsStabilizationDelay = 0.35f;

	[Tooltip( "World radius used for proximity / pile pick helpers." )]
	[Min( 0.01f )]
	public float pickupRadius = 0.35f;

	[Tooltip( "When seated on a gold pile, solid-collide with the player. Leave off for small props you can walk through; enable for large obstacles." )]
	public bool collideWithPlayerOnPile = false;

	[Header( "Key" )]
	[Tooltip( "Used when category is Key. Ignored for skeleton keys." )]
	public KeyType keyType = KeyType.Iron;

	[Tooltip( "When true, this key opens any chest key type and is not consumed on use." )]
	public bool isSkeletonKey = false;

	[Header( "Chest / Container" )]
	[Tooltip( "Used when category is Chest or Container. Owns lock type, duration, and placeholder contents." )]
	public ChestDefinition chestDefinition;

	[Header( "Visuals" )]
	public Sprite icon;

	[Tooltip( "Optional mesh override applied at bind when set." )]
	public Mesh meshOverride;

	[Tooltip( "Optional material override applied at bind when set." )]
	public Material materialOverride;

	[Tooltip( "Addressable prefab used for all states (pile, hand, table, floor)." )]
	public AssetReferenceGameObject prefab;

	[Tooltip( "Scale while in pile / free in the world (ground stacks, loose coins, instanced coins)." )]
	public Vector3 worldScale = Vector3.one * 0.35f;

	[Tooltip( "Scale while held in the hand, including held coin stacks. Coin thickness scales by heldScale.y / worldScale.y." )]
	public Vector3 heldScale = Vector3.one * 0.08f;

	[Tooltip( "Local euler degrees for the Active Item (screen-center held treasure). Identity when zero." )]
	[FormerlySerializedAs( "heldLocalEuler" )]
	public Vector3 activeHeldLocalEuler = Vector3.zero;

	[Tooltip( "Local euler degrees for items in the Held Stack (hand pile). Identity when zero." )]
	public Vector3 heldStackLocalEuler = Vector3.zero;

	[Header( "Cleaning" )]
	[Tooltip( "Auto: Artifacts require cleaning. Required / NotRequired override the category default." )]
	public TreasureCleaningRequirement cleaningRequirement = TreasureCleaningRequirement.Auto;

	[Header( "Stacking" )]
	[Tooltip( "If false, this treasure cannot be stacked onto other loose treasure, coin stacks, or mixed table slots. Gems never floor-stack regardless." )]
	public bool canStack = true;

	[Tooltip( "When true, floor and gold-bar tables form interleaved pair stacks (two side by side, next pair on top rotated 90). Do not use generic canStack." )]
	public bool usesInterleavedBarStack;

	[Tooltip( "World-space height each stacked item adds (ground, tables, world coin stacks). Held stacks scale this by heldScale.y / worldScale.y. 0 uses a category fallback." )]
	[Min( 0f )]
	public float coinThickness = 0.04f;

	[Tooltip( "Cells this treasure occupies on a minecart cargo grid (X = columns, Y = rows). Minimum 1×1." )]
	public Vector2Int cartGridSize = Vector2Int.one;

	[Header( "Audio" )]
	[Tooltip( "Random one-shots when this treasure is picked up into the hand." )]
	public AudioClip[] pickupClips;

	[Range( 0f, 1f )]
	public float pickupVolumeMin = 1f;

	[Range( 0f, 1f )]
	public float pickupVolumeMax = 1f;

	[Range( -3f, 3f )]
	public float pickupPitchMin = 1f;

	[Range( -3f, 3f )]
	public float pickupPitchMax = 1f;

	[Tooltip( "Random one-shots when this treasure is placed." )]
	public AudioClip[] placeClips;

	[Range( 0f, 1f )]
	public float placeVolumeMin = 1f;

	[Range( 0f, 1f )]
	public float placeVolumeMax = 1f;

	[Range( -3f, 3f )]
	public float placePitchMin = 1f;

	[Range( -3f, 3f )]
	public float placePitchMax = 1f;

	/// <summary>
	/// True when this treasure opens or bashes like a chest (has a <see cref="ChestDefinition"/>).
	/// </summary>
	public bool UsesChestInteract()
	{
		if ( chestDefinition != null )
			return true;

		return category == TreasureCategory.Chest || category == TreasureCategory.Container;
	}

	/// <summary>
	/// False when this treasure cannot be thrown (explicit flag or ground/slot-only placement).
	/// </summary>
	public bool GetCanThrow()
	{
		return !cannotThrow && !placeOnGroundOrArtifactSlotOnly;
	}

	/// <summary>
	/// False when <see cref="placeOnGroundOrArtifactSlotOnly"/> is set and
	/// <paramref name="target"/> is not walkable ground or an artifact presentation slot.
	/// </summary>
	public bool AllowsPlacementTarget( ITreasurePlacementTarget target )
	{
		if ( !placeOnGroundOrArtifactSlotOnly )
			return true;

		if ( target is FloorPlacementTarget )
			return true;

		if ( target is ArtifactPresentationTableInteractable )
			return true;

		return false;
	}

	public float GetStackThickness()
	{
		if ( coinThickness > 0.0001f )
			return coinThickness;

		float fallback = worldScale.y * 0.12f;
		return fallback > 0.0001f ? fallback : 0.04f;
	}

	public Vector2Int GetCartGridSize()
	{
		return new Vector2Int( Mathf.Max( 1, cartGridSize.x ), Mathf.Max( 1, cartGridSize.y ) );
	}

	/// <summary>
	/// True when this treasure must be cleaned before artifact presentation display.
	/// Auto resolves to true for <see cref="TreasureCategory.Artifact"/> only.
	/// </summary>
	public bool GetRequiresCleaning()
	{
		switch ( cleaningRequirement )
		{
			case TreasureCleaningRequirement.Required:
				return true;
			case TreasureCleaningRequirement.NotRequired:
				return false;
			default:
				return category == TreasureCategory.Artifact;
		}
	}

	public PrimitiveType GetFallbackPrimitive()
	{
		switch ( category )
		{
			case TreasureCategory.Gem:
				return PrimitiveType.Capsule;
			case TreasureCategory.Coin:
				return PrimitiveType.Sphere;
			default:
				return PrimitiveType.Cube;
		}
	}

	public Vector3 GetFallbackHeldScale()
	{
		return heldScale;
	}

	public Quaternion GetHeldLocalRotation( bool active )
	{
		Vector3 euler = active ? activeHeldLocalEuler : heldStackLocalEuler;
		if ( euler.sqrMagnitude < 0.0001f )
			return Quaternion.identity;

		return Quaternion.Euler( euler );
	}

	public Vector3 GetFallbackPlacedScale()
	{
		return worldScale;
	}

	public float GetDefaultMass()
	{
		switch ( category )
		{
			case TreasureCategory.Gem:
				return 0.25f;
			case TreasureCategory.Coin:
				return 0.08f;
			default:
				return 0.15f;
		}
	}

	public void EnsurePhysicsDefaults()
	{
		if ( worldScale.sqrMagnitude < 0.0001f )
			worldScale = GetCategoryWorldScale();
		if ( heldScale.sqrMagnitude < 0.0001f )
			heldScale = GetCategoryHeldScale();
		if ( rigidbodyMass <= 0.01f )
			rigidbodyMass = GetDefaultMass();
		if ( coinThickness <= 0.0001f )
			coinThickness = GetStackThickness();
	}

	void OnValidate()
	{
		if ( string.IsNullOrEmpty( id ) && !string.IsNullOrEmpty( name ) )
			id = name;

		if ( string.IsNullOrEmpty( displayName ) && !string.IsNullOrEmpty( name ) )
			displayName = name;

		value = Mathf.Max( 0, value );
		weight = Mathf.Max( 1, weight );
		throwForceScale = Mathf.Max( 0f, throwForceScale );
		throwUpBiasScale = Mathf.Max( 0f, throwUpBiasScale );
		EnsurePhysicsDefaults();
		rigidbodyMass = Mathf.Max( 0.01f, rigidbodyMass );
		drag = Mathf.Max( 0f, drag );
		angularDrag = Mathf.Max( 0f, angularDrag );
		sleepThreshold = Mathf.Max( 0f, sleepThreshold );
		autoToppleStrength = Mathf.Max( 0f, autoToppleStrength );
		maxUprightAngle = Mathf.Clamp( maxUprightAngle, 0f, 90f );
		physicsStabilizationDelay = Mathf.Max( 0f, physicsStabilizationDelay );
		pickupRadius = Mathf.Max( 0.01f, pickupRadius );
		coinThickness = Mathf.Max( 0f, coinThickness );
		cartGridSize = new Vector2Int( Mathf.Max( 1, cartGridSize.x ), Mathf.Max( 1, cartGridSize.y ) );
	}

	Vector3 GetCategoryWorldScale()
	{
		switch ( category )
		{
			case TreasureCategory.Gem:
				return new Vector3( 0.25f, 0.2f, 0.25f );
			case TreasureCategory.Coin:
				return Vector3.one * 0.3f;
			default:
				return Vector3.one * 0.3f;
		}
	}

	Vector3 GetCategoryHeldScale()
	{
		switch ( category )
		{
			case TreasureCategory.Gem:
				return new Vector3( 0.08f, 0.06f, 0.08f );
			case TreasureCategory.Coin:
				return Vector3.one * 0.2f;
			default:
				return Vector3.one * 0.07f;
		}
	}
}
