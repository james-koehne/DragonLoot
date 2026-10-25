using System;
using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Root blob for named save slots (debug quick-save and future player saves).
/// </summary>
[Serializable]
public class GameSaveData
{
	public const int CurrentSchemaVersion = 2;

	public int schemaVersion = CurrentSchemaVersion;
	public string label;
	public string savedAtUtc;
	public string levelScenePath;

	public ProfileSaveData progress;
	public GameSavePlayerData player = new GameSavePlayerData();
	public GameSaveWorldData world = new GameSaveWorldData();
}

[Serializable]
public class GameSavePlayerData
{
	public GameSaveVec3 position;
	public float yaw;
	public float cameraPitch;
	public int selectedBucket;
	/// <summary>Bitmask of pouches that have ever held an item (CarryBucketKind index).</summary>
	public int pouchEverHeldMask;
	public List<string> coinIds = new List<string>();
	public List<string> gemIds = new List<string>();
	public List<string> artifactIds = new List<string>();
	public List<string> resourceIds = new List<string>();
	public List<string> generalIds = new List<string>();
	public List<string> junkIds = new List<string>();
	public List<GameSaveCooldownEntry> abilityCooldowns = new List<GameSaveCooldownEntry>();
}

[Serializable]
public class GameSaveCooldownEntry
{
	public string abilityId;
	public float remaining;
}

[Serializable]
public class GameSaveIdCount
{
	public string definitionId;
	public int count;
}

[Serializable]
public class GameSaveWorldData
{
	public List<GameSaveBuildableEntry> buildables = new List<GameSaveBuildableEntry>();
	public List<GameSaveCoinStackEntry> coinStacks = new List<GameSaveCoinStackEntry>();
	public List<GameSaveBarStackEntry> barStacks = new List<GameSaveBarStackEntry>();
	public List<GameSaveLooseTreasureEntry> loose = new List<GameSaveLooseTreasureEntry>();
	public List<GameSavePileEntry> piles = new List<GameSavePileEntry>();
	public List<GameSaveStationEntry> stations = new List<GameSaveStationEntry>();
	public List<GameSaveDisplayTableEntry> displays = new List<GameSaveDisplayTableEntry>();
	public List<GameSaveConstellationEntry> constellations = new List<GameSaveConstellationEntry>();
	public List<GameSaveChestEntry> chests = new List<GameSaveChestEntry>();
	public List<GameSavePlatformGroupEntry> platforms = new List<GameSavePlatformGroupEntry>();
	public GameSaveMapFogEntry mapFog;
}

[Serializable]
public class GameSavePileEntry
{
	public string persistenceId;
	public int resolutionX;
	public int resolutionZ;
	public float maxHeight;
	public string heightsU16Base64;
	public bool emptied;
	public List<GameSaveIdCount> remaining = new List<GameSaveIdCount>();
}

[Serializable]
public class GameSaveStationEntry
{
	public string persistenceId;
	public string kind;
	public int state;
	public float charge;
	public float travelNormalized;
	public float cleanProgress;
	public List<string> hopperIds = new List<string>();
	public List<GameSaveIdCount> pendingOutput = new List<GameSaveIdCount>();
	public string occupantId;
}

[Serializable]
public class GameSaveDisplayTableEntry
{
	public string persistenceId;
	public List<GameSaveDisplaySlotEntry> slots = new List<GameSaveDisplaySlotEntry>();
}

[Serializable]
public class GameSaveDisplaySlotEntry
{
	public int slotIndex;
	public List<string> definitionIds = new List<string>();
}

[Serializable]
public class GameSaveConstellationEntry
{
	public string persistenceId;
	public bool complete;
	public List<string> slotDefinitionIds = new List<string>();
}

[Serializable]
public class GameSaveChestEntry
{
	public string persistenceId;
	public int state;
	public bool destroyed;
}

[Serializable]
public class GameSavePlatformGroupEntry
{
	public string groupId;
	public bool activated;
}

[Serializable]
public class GameSaveMapFogEntry
{
	public int resolution;
	public string discoveryBase64;
}

[Serializable]
public class GameSaveBuildableEntry
{
	public string persistenceId;
	public bool built;
}

[Serializable]
public class GameSaveCoinStackEntry
{
	public GameSaveVec3 position;
	public GameSaveQuat rotation;
	public float variationSeed;
	public List<string> slotIds = new List<string>();
}

[Serializable]
public class GameSaveBarStackEntry
{
	public GameSaveVec3 position;
	public GameSaveQuat rotation;
	public string definitionId;
	public int count;
}

[Serializable]
public class GameSaveLooseTreasureEntry
{
	public GameSaveVec3 position;
	public GameSaveQuat rotation;
	public string definitionId;
	public bool surfaceMode;
}

[Serializable]
public struct GameSaveVec3
{
	public float x;
	public float y;
	public float z;

	public static GameSaveVec3 From( Vector3 v )
	{
		return new GameSaveVec3 { x = v.x, y = v.y, z = v.z };
	}

	public Vector3 ToVector3()
	{
		return new Vector3( x, y, z );
	}
}

[Serializable]
public struct GameSaveQuat
{
	public float x;
	public float y;
	public float z;
	public float w;

	public static GameSaveQuat From( Quaternion q )
	{
		return new GameSaveQuat { x = q.x, y = q.y, z = q.z, w = q.w };
	}

	public Quaternion ToQuaternion()
	{
		if ( x == 0f && y == 0f && z == 0f && w == 0f )
			return Quaternion.identity;
		return new Quaternion( x, y, z, w );
	}
}

[Serializable]
public class GameSaveSlotInfo
{
	public string slotId;
	public string label;
	public string savedAtUtc;
	public string filePath;
}
