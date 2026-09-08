#if UNITY_EDITOR
/// <summary>
/// Canonical Unity menu paths for DragonLoot editor tools.
/// Keep MenuItem attributes pointed here so the top menu stays consistent.
/// </summary>
public static class DragonLootMenus
{
	public const string Root = "DragonLoot";

	// --- Graphics ---
	public const string GraphicsCoinsInstall = Root + "/Graphics/Coins/Install Materials";
	public const string GraphicsCoinsReset = Root + "/Graphics/Coins/Reset Material Defaults";

	public const string GraphicsGemsInstall = Root + "/Graphics/Gems/Install Materials";
	public const string GraphicsGemsReset = Root + "/Graphics/Gems/Reset Material Defaults";

	public const string GraphicsArtifactsInstall = Root + "/Graphics/Artifacts/Install Materials";

	public const string GraphicsGoldPileInstall = Root + "/Graphics/Gold Pile/Install Material";
	public const string GraphicsGoldPileApply = Root + "/Graphics/Gold Pile/Apply Material To Selection";
	public const string GraphicsGoldPileWireDynamic = Root + "/Graphics/Gold Pile/Wire Dynamic Prefab";
	public const string GraphicsGoldPileLootStream = Root + "/Graphics/Gold Pile/Install Loot Stream Settings";
	public const string GraphicsGoldPileReset = Root + "/Graphics/Gold Pile/Reset Material Defaults";
	public const string GraphicsGoldPileCarveInstall = Root + "/Graphics/Gold Pile/Install Carve Definition";
	public const string GraphicsGoldPileStripRuntimeGeometry = Root + "/Graphics/Gold Pile/Strip Persisted Runtime Geometry";

	public const string GraphicsStylizedLightingInstall = Root + "/Graphics/Stylized Lighting/Install Definition";
	public const string GraphicsAreaLightingInstall = Root + "/Graphics/Area Lighting/Install Definition";

	public const string GraphicsLightFlickerInstall = Root + "/Graphics/Light Flicker/Install Definition";
	public const string GraphicsLightFlickerResetPresets = Root + "/Graphics/Light Flicker/Reset Presets To Defaults";
	public const string GraphicsLightFlickerAddToSelection = Root + "/Graphics/Light Flicker/Add To Selection";
	public const string GraphicsLightFlickerApplyPreset = Root + "/Graphics/Light Flicker/Apply Preset";
	public const string GraphicsLightFlickerRecaptureSelection = Root + "/Graphics/Light Flicker/Recapture Bases In Selection";
	public const string GraphicsLightFlickerRefreshPreviews = Root + "/Graphics/Light Flicker/Refresh Edit Previews In Open Scenes";

	// --- Audio ---
	public const string AudioInstallDefinition = Root + "/Audio/Install Definition";
	public const string AudioResetClipLists = Root + "/Audio/Reset Clip Lists To Defaults";

	// --- Treasure content ---
	public const string TreasureCreate = Root + "/Treasure/Create Treasure";
	public const string TreasureInstallArtifacts = Root + "/Treasure/Install Fantasy Pack Artifacts";
	public const string TreasureInstallChests = Root + "/Treasure/Install Chests & Keys";
	public const string TreasureBuildDisplayTables = Root + "/Treasure/Build Display Table Prefabs";
	public const string TreasureInstallGoldBarStackSettings = Root + "/Treasure/Install Gold Bar Stack Settings";
	public const string TreasurePurgeSlotOrphans = Root + "/Treasure/Purge Artifact Slot Indicator Orphans";
	public const string TreasurePileContents = Root + "/Treasure/Pile Contents";
	public const string TreasureSceneInventory = Root + "/Treasure/Scene Inventory";

	// --- Stations ---
	public const string StationsCleaningCreate = Root + "/Stations/Cleaning/Create Setup";
	public const string StationsCleaningPlace = Root + "/Stations/Cleaning/Place In Level";
	public const string StationsCoinSortingCreate = Root + "/Stations/Coin Sorting/Create Setup";
	public const string StationsCoinSortingPlace = Root + "/Stations/Coin Sorting/Place In Level";

	// --- Treasure surface ---
	public const string TreasureSurfaceEditor = Root + "/Treasure Surface/Open Editor";
	public const string TreasureSurfaceCreateDefinition = Root + "/Treasure Surface/Create Definition";
	public const string TreasureSurfaceCreateAuthoring = Root + "/Treasure Surface/Create Authoring In Active Scene";
	public const string TreasureSurfaceMigratePaint = Root + "/Treasure Surface/Migrate Authoring Paint To Asset";

	// --- Minecart ---
	public const string MinecartCreateSetup = Root + "/Minecart/Create Setup";
	public const string MinecartCreateDriveSetup = Root + "/Minecart/Create Drive Cart";
	public const string MinecartCreateTrack = Root + "/Minecart/Create Track";
	public const string MinecartCreateUnloadPoint = Root + "/Minecart/Create Unload Point";
	public const string MinecartCreateCallPost = Root + "/Minecart/Create Call Post";

	public const string GameObjectMinecartSetup = "GameObject/DragonLoot/Minecart Setup";
	public const string GameObjectMinecartDriveSetup = "GameObject/DragonLoot/Minecart Drive Cart";
	public const string GameObjectMinecartTrack = "GameObject/DragonLoot/Minecart Track";
	public const string GameObjectMinecartUnloadPoint = "GameObject/DragonLoot/Minecart Unload Point";
	public const string GameObjectMinecartCallPost = "GameObject/DragonLoot/Minecart Call Post";

	// --- Tools ---
	public const string ToolsMilanoteSettings = Root + "/Tools/Milanote Sync Settings";
	public const string ToolsMilanoteTasks = Root + "/Tools/Milanote Sync Tasks";

	// --- UI ---
	public const string UiInstallPausePouch = Root + "/UI/Install Pause & Pouch Summary On Interface";
	public const string UiInstallMap = Root + "/UI/Install Map On Interface";

	public const string GameObjectMapRegion = "GameObject/DragonLoot/Map Region Volume";
	public const string GameObjectMapLabel = "GameObject/DragonLoot/Map Label Marker";
	public const string GameObjectTutorialMapMarker = "GameObject/DragonLoot/Tutorial Map Marker";

	// --- Tutorials ---
	public const string TutorialsInstallCatalog = Root + "/Tutorials/Install Catalog";
}
#endif
