using System;

using UnityEngine;

[Serializable]
public struct ChestContentEntry
{
	public TreasureDefinition treasure;

	[Min( 0 )]
	public int count;
}

[CreateAssetMenu( fileName = "ChestDefinition", menuName = "Definitions/ChestDefinition" )]
public class ChestDefinition : ScriptableObject
{
	[Header( "Identity" )]
	public string id;

	public string displayName;

	[Tooltip( "Designer-facing chest family label (wood, ornate, etc.)." )]
	public string chestType;

	[Header( "Lock" )]
	public KeyType keyType = KeyType.Iron;

	public bool startsLocked = true;

	[Tooltip( "Seconds to finish lockpicking this chest. 0 uses the Lockpick ability default." )]
	[Min( 0f )]
	public float lockpickDuration = 8f;

	[Header( "Contents" )]
	public ChestContentEntry[] contents;

	public string ResolveDisplayName()
	{
		if ( !string.IsNullOrEmpty( displayName ) )
			return displayName;
		if ( !string.IsNullOrEmpty( id ) )
			return id;
		return name;
	}

	void OnValidate()
	{
		if ( string.IsNullOrEmpty( id ) && !string.IsNullOrEmpty( name ) )
			id = name;

		if ( string.IsNullOrEmpty( displayName ) && !string.IsNullOrEmpty( name ) )
			displayName = name;

		lockpickDuration = Mathf.Max( 0f, lockpickDuration );
	}
}
