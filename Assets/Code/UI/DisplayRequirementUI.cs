using System.Collections.Generic;
using System.Text;

using UnityEngine;

/// <summary>
/// Builds display / constellation / artifact fill requirement text for the look-at HUD.
/// </summary>
public class DisplayRequirementUI
{
	readonly List<TreasureDefinition> _reqDefs = new List<TreasureDefinition>( 8 );
	readonly List<int> _reqNeeded = new List<int>( 8 );
	readonly List<int> _reqFilled = new List<int>( 8 );

	public bool TryAppendSummary( PlayerController player, StringBuilder builder )
	{
		if ( builder == null || !TryResolveDisplay( player, out Component display ) )
			return false;

		int start = builder.Length;
		if ( !TryAppendSummary( display, builder ) )
			return false;

		if ( start > 0 )
			builder.Insert( start, '\n' );
		return true;
	}

	static bool TryResolveDisplay( PlayerController player, out Component display )
	{
		display = null;
		if ( player == null )
			return false;

		PlayerPlacement placement = player.Placement;
		if ( placement != null && TryAsDisplay( placement.ActiveTarget as Component, out display ) )
			return true;

		PlayerInteraction interaction = player.Interaction;
		if ( interaction != null )
		{
			IInteractable focus = interaction.Current;
			TreasureItemInteractable itemFocus = focus as TreasureItemInteractable;
			if ( itemFocus != null && itemFocus.Item != null
				&& TryAsDisplay( itemFocus.Item.Owner as Component, out display ) )
				return true;

			if ( TryAsDisplay( focus as Component, out display ) )
				return true;

			if ( interaction.TryGetPlacementAimHit( out RaycastHit aimHit )
				&& TryResolveFromCollider( aimHit.collider, out display ) )
				return true;

			if ( interaction.TryGetLastHit( out RaycastHit lastHit )
				&& TryResolveFromCollider( lastHit.collider, out display ) )
				return true;
		}

		return false;
	}

	static bool TryResolveFromCollider( Collider collider, out Component display )
	{
		display = null;
		if ( collider == null )
			return false;

		TypedDisplayTableInteractable typed = collider.GetComponentInParent<TypedDisplayTableInteractable>();
		if ( typed != null )
		{
			display = typed;
			return true;
		}

		GemConstellationInteractable constellation = collider.GetComponentInParent<GemConstellationInteractable>();
		if ( constellation != null )
		{
			display = constellation;
			return true;
		}

		ArtifactPresentationTableInteractable artifact = collider.GetComponentInParent<ArtifactPresentationTableInteractable>();
		if ( artifact != null )
		{
			display = artifact;
			return true;
		}

		return false;
	}

	static bool TryAsDisplay( Component component, out Component display )
	{
		display = null;
		if ( component == null )
			return false;

		if ( component is TypedDisplayTableInteractable
			|| component is GemConstellationInteractable
			|| component is ArtifactPresentationTableInteractable )
		{
			display = component;
			return true;
		}

		return false;
	}

	bool TryAppendSummary( Component display, StringBuilder builder )
	{
		if ( display is TypedDisplayTableInteractable typed )
			return AppendTypedTable( typed, builder );

		if ( display is GemConstellationInteractable constellation )
			return AppendConstellation( constellation, builder );

		if ( display is ArtifactPresentationTableInteractable artifact )
			return AppendArtifactTable( artifact, builder );

		return false;
	}

	/// <summary>
	/// Builds fill progress text for a known display / constellation / artifact table.
	/// </summary>
	public bool TryAppendDisplaySummary( Component display, StringBuilder builder )
	{
		if ( builder == null || display == null )
			return false;
		return TryAppendSummary( display, builder );
	}

	bool AppendTypedTable( TypedDisplayTableInteractable table, StringBuilder builder )
	{
		if ( table == null )
			return false;

		if ( table.UsesPerSlotRequirements )
			return AppendPerSlotTypedTable( table, builder );

		TreasureDefinition accepted = table.AcceptedTreasure;
		string label = accepted != null && !string.IsNullOrEmpty( accepted.displayName )
			? accepted.displayName
			: table.InteractionName;

		AppendFillProgress( builder, table.CurrentCount, table.Capacity, label );
		return true;
	}

	bool AppendPerSlotTypedTable( TypedDisplayTableInteractable table, StringBuilder builder )
	{
		int slotCount = table.SlotCount;
		ClearRequirementBuckets();

		int configuredSlots = 0;
		for ( int i = 0; i < slotCount; i++ )
		{
			if ( table.GetRequiredTreasure( i ) != null )
				configuredSlots++;
		}

		int perSlotTarget = 1;
		if ( table.AllowsVerticalStack && configuredSlots > 0 )
		{
			int capacity = table.Capacity;
			if ( capacity > configuredSlots )
				perSlotTarget = capacity / configuredSlots;
		}

		for ( int i = 0; i < slotCount; i++ )
		{
			TreasureDefinition required = table.GetRequiredTreasure( i );
			if ( required == null )
				continue;

			int filled = table.GetSlotCount( i );
			int needed = perSlotTarget;
			int index = _reqDefs.IndexOf( required );
			if ( index < 0 )
			{
				_reqDefs.Add( required );
				_reqNeeded.Add( needed );
				_reqFilled.Add( Mathf.Min( filled, needed ) );
				continue;
			}

			_reqNeeded[ index ] += needed;
			_reqFilled[ index ] += Mathf.Min( filled, needed );
		}

		if ( _reqDefs.Count == 0 )
		{
			AppendFillProgress( builder, table.CurrentCount, table.Capacity, table.InteractionName );
			return true;
		}

		for ( int i = 0; i < _reqDefs.Count; i++ )
		{
			if ( i > 0 )
				builder.Append( '\n' );
			AppendRequirementLine( builder, _reqFilled[ i ], _reqNeeded[ i ], RequirementLabel( _reqDefs[ i ] ) );
		}

		return true;
	}

	bool AppendConstellation( GemConstellationInteractable constellation, StringBuilder builder )
	{
		if ( constellation == null )
			return false;

		int slotCount = constellation.SlotCount;
		ClearRequirementBuckets();

		int anyNeeded = 0;
		int anyFilled = 0;

		for ( int i = 0; i < slotCount; i++ )
		{
			TreasureDefinition required = constellation.GetRequiredGem( i );
			bool occupied = constellation.IsSlotOccupied( i );
			if ( required == null )
			{
				anyNeeded++;
				if ( occupied )
					anyFilled++;
				continue;
			}

			AddRequirement( required, occupied );
		}

		if ( _reqDefs.Count == 0 && anyNeeded > 0 )
		{
			AppendFillProgress( builder, anyFilled, anyNeeded, "Gems" );
			return true;
		}

		if ( _reqDefs.Count == 1 && anyNeeded == 0 )
		{
			AppendRequirementLine( builder, _reqFilled[ 0 ], _reqNeeded[ 0 ], RequirementLabel( _reqDefs[ 0 ] ) );
			return true;
		}

		if ( _reqDefs.Count == 0 && anyNeeded == 0 )
		{
			AppendFillProgress( builder, constellation.CurrentCount, constellation.Capacity, "Gems" );
			return true;
		}

		bool anyLine = false;
		for ( int i = 0; i < _reqDefs.Count; i++ )
		{
			if ( anyLine )
				builder.Append( '\n' );
			AppendRequirementLine( builder, _reqFilled[ i ], _reqNeeded[ i ], RequirementLabel( _reqDefs[ i ] ) );
			anyLine = true;
		}

		if ( anyNeeded > 0 )
		{
			if ( anyLine )
				builder.Append( '\n' );
			AppendRequirementLine( builder, anyFilled, anyNeeded, "Gems" );
		}

		return anyLine || anyNeeded > 0;
	}

	bool AppendArtifactTable( ArtifactPresentationTableInteractable table, StringBuilder builder )
	{
		if ( table == null || table.IsComplete )
			return false;

		int aimed = table.AimedSlotIndex;
		if ( table.IsAimFeedbackFresh && aimed >= 0 && !table.IsSlotPrerequisiteMet( aimed ) )
		{
			TreasureDefinition blocker = table.GetBlockingPrerequisiteArtifact( aimed );
			string name = blocker != null ? RequirementLabel( blocker ) : "required artifact";
			builder.Append( "Place " ).Append( name ).Append( " first" );
			return true;
		}

		int slotCount = table.SlotCount;
		ClearRequirementBuckets();

		for ( int i = 0; i < slotCount; i++ )
		{
			TreasureDefinition required = table.GetRequiredArtifact( i );
			if ( required == null )
				continue;

			AddRequirement( required, table.IsSlotOccupied( i ) );
		}

		if ( _reqDefs.Count == 0 )
		{
			AppendFillProgress( builder, table.CurrentCount, table.Capacity, "Artifacts" );
			return true;
		}

		if ( _reqDefs.Count == 1 )
		{
			AppendRequirementLine( builder, _reqFilled[ 0 ], _reqNeeded[ 0 ], RequirementLabel( _reqDefs[ 0 ] ) );
			return true;
		}

		for ( int i = 0; i < _reqDefs.Count; i++ )
		{
			if ( i > 0 )
				builder.Append( '\n' );
			AppendRequirementLine( builder, _reqFilled[ i ], _reqNeeded[ i ], RequirementLabel( _reqDefs[ i ] ) );
		}

		return true;
	}

	void ClearRequirementBuckets()
	{
		_reqDefs.Clear();
		_reqNeeded.Clear();
		_reqFilled.Clear();
	}

	void AddRequirement( TreasureDefinition required, bool occupied )
	{
		int index = _reqDefs.IndexOf( required );
		if ( index < 0 )
		{
			_reqDefs.Add( required );
			_reqNeeded.Add( 1 );
			_reqFilled.Add( occupied ? 1 : 0 );
			return;
		}

		_reqNeeded[ index ]++;
		if ( occupied )
			_reqFilled[ index ]++;
	}

	static void AppendRequirementLine( StringBuilder builder, int filled, int needed, string label )
	{
		AppendFillProgress( builder, filled, needed, label );
	}

	static void AppendFillProgress( StringBuilder builder, int filled, int needed, string label )
	{
		int pct = needed > 0
			? Mathf.Clamp( Mathf.RoundToInt( 100f * filled / needed ), 0, 100 )
			: ( filled > 0 ? 100 : 0 );
		builder.Append( pct );
		builder.Append( "% - " );
		builder.Append( filled );
		builder.Append( '/' );
		builder.Append( needed );
		builder.Append( ' ' );
		builder.Append( label );
	}

	static string RequirementLabel( TreasureDefinition definition )
	{
		if ( definition == null )
			return "Item";
		if ( !string.IsNullOrEmpty( definition.displayName ) )
			return definition.displayName;
		return definition.name;
	}
}
