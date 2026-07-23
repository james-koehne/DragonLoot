/// <summary>
/// Exactly one owner for a treasure item at any time (World may be null).
/// </summary>
public interface ITreasureOwner
{
	TreasureOwnerKind OwnerKind { get; }

	/// <summary>Detaches the item from this owner so it can transfer (does not destroy).</summary>
	void ReleaseTreasure( TreasureItem item );
}
