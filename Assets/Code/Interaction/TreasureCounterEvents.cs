using UnityEngine;

/// <summary>Published when treasure is first discovered (picked up from an undiscovered source).</summary>
public struct TreasureCollectedEvent
{
	public TreasureDefinition Treasure;
	public int Amount;
	public int Collected;
}

/// <summary>Published when Sorted changes for a treasure type (correct display place/remove).</summary>
public struct TreasureSortedEvent
{
	public TreasureDefinition Treasure;
	public int Delta;
	public int Sorted;
}

/// <summary>Published whenever a counter entry's displayed values change.</summary>
public struct TreasureCounterChangedEvent
{
	public TreasureCounterEntry Entry;
}

/// <summary>Published when Sorted reaches Total for a counter entry.</summary>
public struct CategoryCompletedEvent
{
	public string Id;
	public string DisplayName;
	public TreasureDefinition Treasure;
}

/// <summary>Published when a previously completed entry drops below Sorted == Total.</summary>
public struct CategoryReopenedEvent
{
	public string Id;
	public string DisplayName;
	public TreasureDefinition Treasure;
}

/// <summary>Published when every counter entry in the active section is completed.</summary>
public struct SectionCompletedEvent
{
	public string SectionId;
	public int CategoriesCompleted;
	public int TotalCategories;
	public int OverallCompletionPercent;
}

/// <summary>Published when treasure is placed onto the freeform sorting table (does not change Sorted).</summary>
public struct TreasurePlacedOnSortingTableEvent
{
	public TreasureDefinition Treasure;
	public int Amount;
}

/// <summary>Runtime progress for one treasure definition (counter "category").</summary>
public class TreasureCounterEntry
{
	public string Id;
	public string DisplayName;
	public Sprite Icon;
	public TreasureCategory Family;
	public TreasureDefinition Definition;
	public int Total;
	public int Collected;
	public int Sorted;
	public bool Completed;

	public int Remaining => Total - Sorted;
}
