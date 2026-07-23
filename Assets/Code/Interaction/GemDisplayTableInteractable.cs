using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Typed gem display: accepts one gem <see cref="TreasureDefinition"/> and snaps matching
/// carried gems into a generated horizontal slot grid (Displayed state).
/// Gems are not stackable, so each slot holds one gem. Pickup leaves gaps.
/// Setup: collider on root, child DisplayArea, assign accepted treasure + grid settings.
/// </summary>
public class GemDisplayTableInteractable : TypedDisplayTableInteractable
{
	public override TreasureOwnerKind OwnerKind => TreasureOwnerKind.GemTable;

	protected override TreasureCategory RequiredCategory => TreasureCategory.Gem;

	protected override string DefaultInteractionName => "Gem Display";

	public TreasureDefinition AcceptedGem => AcceptedTreasure;

	public IReadOnlyList<TreasureItem> DisplayedGems => DisplayedItems;

	protected override void PublishChanged()
	{
		EventBus.Publish( new GemDisplayTableChangedEvent
		{
			Table = this,
			Count = CurrentCount,
			Capacity = Capacity
		} );
	}

	protected override void PublishCompleted()
	{
		EventBus.Publish( new GemDisplayTableCompletedEvent
		{
			Table = this,
			AcceptedGem = AcceptedTreasure
		} );
	}
}
