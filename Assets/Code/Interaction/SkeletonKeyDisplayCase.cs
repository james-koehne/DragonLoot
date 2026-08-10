using UnityEngine;

/// <summary>
/// Small display case that becomes interactable after enough chests have been opened.
/// Grants one non-consuming skeleton key.
/// </summary>
public class SkeletonKeyDisplayCase : InteractableBase
{
	[SerializeField]
	TreasureDefinition skeletonKeyDefinition;

	[SerializeField]
	[Min( 1 )]
	int chestsRequired = 3;

	[SerializeField]
	Transform spawnPoint;

	[SerializeField]
	bool claimed;

	public int ChestsRequired => Mathf.Max( 1, chestsRequired );
	public bool IsUnlocked => ChestProgress.ChestsOpened >= ChestsRequired;
	public bool IsClaimed => claimed || ChestProgress.SkeletonKeyClaimed;

	void Awake()
	{
		RefreshInteractionName();
	}

	void OnEnable()
	{
		if ( ChestProgress.SkeletonKeyClaimed )
			claimed = true;
		RefreshInteractionName();
	}

	void Update()
	{
		RefreshInteractionName();
	}

	public void Configure( TreasureDefinition keyDefinition, int required )
	{
		skeletonKeyDefinition = keyDefinition;
		chestsRequired = Mathf.Max( 1, required );
		RefreshInteractionName();
	}

	public override bool CanInteract( PlayerController player )
	{
		if ( !base.CanInteract( player ) || player == null )
			return false;

		if ( skeletonKeyDefinition == null )
			return false;

		if ( IsClaimed || !IsUnlocked )
			return false;

		PlayerCarry carry = player.Carry;
		return carry != null && carry.CanAdd( skeletonKeyDefinition );
	}

	public override void Interact( PlayerController player )
	{
		if ( !CanInteract( player ) )
			return;

		GrantKeyAsync( player );
	}

	async void GrantKeyAsync( PlayerController player )
	{
		if ( player == null || skeletonKeyDefinition == null )
			return;

		PlayerCarry carry = player.Carry;
		if ( carry == null || !carry.CanAdd( skeletonKeyDefinition ) )
			return;

		Vector3 pos = spawnPoint != null ? spawnPoint.position : transform.position + Vector3.up * 0.35f;
		Quaternion rot = spawnPoint != null ? spawnPoint.rotation : Quaternion.identity;

		TreasureItem spawned = await TreasureItemFactory.SpawnAsync( skeletonKeyDefinition, pos, rot, null );
		if ( spawned == null )
			return;

		if ( !player.TryReceiveTreasureItem( spawned ) )
		{
			TreasureItemFactory.Despawn( spawned );
			return;
		}

		claimed = true;
		ChestProgress.MarkSkeletonKeyClaimed();
		RefreshInteractionName();
	}

	void RefreshInteractionName()
	{
		if ( IsClaimed )
		{
			SetInteractionName( "Skeleton Key (Taken)" );
			return;
		}

		if ( !IsUnlocked )
		{
			int remaining = Mathf.Max( 0, ChestsRequired - ChestProgress.ChestsOpened );
			SetInteractionName( $"Skeleton Key ({remaining} chests left)" );
			return;
		}

		SetInteractionName( "Take Skeleton Key" );
	}
}
