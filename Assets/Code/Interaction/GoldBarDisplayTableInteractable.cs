using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Typed gold-bar display: accepts one gold-bar <see cref="TreasureDefinition"/> and snaps
/// matching carried bars into a generated slot grid. Each slot is an interleaved stack
/// (two side by side, next pair on top rotated 90°).
/// Setup: collider on root, child DisplayArea, assign accepted treasure + grid settings.
/// </summary>
public class GoldBarDisplayTableInteractable : TypedDisplayTableInteractable
{
	public override TreasureOwnerKind OwnerKind => TreasureOwnerKind.GoldBarTable;

	protected override TreasureCategory RequiredCategory => TreasureCategory.Artifact;

	protected override string DefaultInteractionName => "Gold Bar Display";

	public TreasureDefinition AcceptedBar => AcceptedTreasure;

	public IReadOnlyList<TreasureItem> DisplayedBars => DisplayedItems;

	protected override void Reset()
	{
		base.Reset();
		slotSpacing = 0.45f;
		rows = 2;
		columns = 3;
	}

	public bool CanAcceptWholeCarriedGoldBarStack( IReadOnlyList<TreasureDefinition> definitions )
	{
		if ( definitions == null || definitions.Count == 0 || AcceptedBar == null )
			return false;

		for ( int i = 0; i < definitions.Count; i++ )
		{
			TreasureDefinition def = definitions[ i ];
			if ( def == null || def != AcceptedBar || !GoldBarStack.IsStackable( def ) )
				return false;
		}

		return true;
	}

	protected override void PublishChanged()
	{
		EventBus.Publish( new GoldBarDisplayTableChangedEvent
		{
			Table = this,
			Count = CurrentCount,
			Capacity = Capacity
		} );
	}

	protected override void PublishCompleted()
	{
		EventBus.Publish( new GoldBarDisplayTableCompletedEvent
		{
			Table = this,
			AcceptedBar = AcceptedTreasure
		} );
	}
}
