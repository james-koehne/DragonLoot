#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// One-shot create / soft-repair for M_GoldPile_Procedural.mat and dynamic heightfield pile wiring.
/// Does not overwrite authored float/color/keyword values (those were being reset on Play Mode).
/// </summary>
public static class GoldPileMaterialInstaller
{
	const string ProceduralShaderName = "DragonLoot/Gold Pile Procedural";
	const string MaterialPath = "Assets/Materials/Shaders/GoldPile/M_GoldPile_Procedural.mat";
	const string ProcCoinAlbedo =
		"Assets/ThirdParty/LiquidFire Package 4 - BSH games/Devtoid - Gold Coins/3D Assets/Gold Coins/Textures/GoldCoins_AlbedoTransparency.png";
	const string ProcCoinNormal =
		"Assets/ThirdParty/LiquidFire Package 4 - BSH games/Devtoid - Gold Coins/3D Assets/Gold Coins/Textures/GoldCoins_Normal.png";
	const string ProcCoinMetallic =
		"Assets/ThirdParty/LiquidFire Package 4 - BSH games/Devtoid - Gold Coins/3D Assets/Gold Coins/Textures/GoldCoins_MetallicSmoothness.png";
	const string Occlusion = "Assets/ThirdParty/REAL_DEDICATED/BasicTreasureCoins/Textures/T_BasicTreasureCoins_Occlusion.tga";
	const string ProcCopperAlbedo =
		"Assets/ThirdParty/LiquidFire Package 4 - BSH games/Devtoid - Gold Coins/3D Assets/Copper Coins/Textures/CopperCoins_AlbedoTransparency.png";
	const string ProcSilverAlbedo =
		"Assets/ThirdParty/LiquidFire Package 4 - BSH games/Devtoid - Gold Coins/3D Assets/Silver Coins/Textures/SilverCoins_AlbedoTransparency.png";
	const string GoldPilePrefab = "Assets/ThirdParty/REAL_DEDICATED/BasicTreasureCoins/Prefabs/CoinPile_Gold.prefab";
	const string GoldCoinVisual = "Assets/Addressables/Treasure/Coins/GoldCoinVisual.prefab";
	const string GoldCoinDef = "Assets/Definitions/Treasure/GoldCoin.asset";
	const string LootStreamSettingsPath = GoldPileLootStreamSettings.AssetPath;
	const string LegacyLootStreamSettingsPath = GoldPileLootStreamSettings.LegacyAssetPath;

	[MenuItem(DragonLootMenus.GraphicsGoldPileInstall)]
	public static void MenuInstall()
	{
		TryInstall(forceAssignTargets: true);
	}

	[MenuItem(DragonLootMenus.GraphicsGoldPileApply)]
	public static void MenuApplyToSelection()
	{
		Material material = EnsureMaterial(forceDefaults: false);
		if (material == null)
			return;

		int assigned = ApplyGoldPileMaterialToSelection(material, "Apply Gold Pile Material");
		AssetDatabase.SaveAssets();
		Debug.Log("Applied M_GoldPile_Procedural to " + assigned + " target(s).");
	}

	[MenuItem(DragonLootMenus.GraphicsGoldPileWireDynamic)]
	public static void MenuWireDynamicPile()
	{
		Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
		if (material == null)
			TryInstall(forceAssignTargets: false);
		material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
		EnsureLootStreamSettings();
		WireDynamicPilePrefab(material);
		AssetDatabase.SaveAssets();
		Debug.Log("Dynamic gold pile components wired onto CoinPile_Gold.");
	}

	/// <summary>
	/// Ensures CoinPile_Gold loot instances use lean DragonLoot/Coin Pile materials.
	/// </summary>
	public static void RefreshLootInstanceCoinPileMaterials()
	{
		Material surface = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
		EnsureLootStreamSettings();
		WireDynamicPilePrefab(surface);
	}

	[MenuItem(DragonLootMenus.GraphicsGoldPileLootStream)]
	public static void MenuInstallLootStreamSettings()
	{
		GoldPileLootStreamSettings settings = EnsureLootStreamSettings();
		Material pileMat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
		if (pileMat != null)
			WireDynamicPilePrefab(pileMat);
		AssetDatabase.SaveAssets();
		Debug.Log(
			"Loot stream settings ready: " + (settings != null ? settings.name : "null")
			+ " at " + LootStreamSettingsPath);
	}

	[MenuItem(DragonLootMenus.GraphicsGoldPileReset)]
	public static void MenuResetDefaults()
	{
		Material material = EnsureMaterial(forceDefaults: true);
		if (material == null)
			return;

		Debug.Log("M_GoldPile_Procedural defaults reset.");
	}

	/// <summary>
	/// Soft repair: fix shader ref, fill null textures only, do not stomp floats.
	/// </summary>
	public static void TryInstall(bool forceAssignTargets = false)
	{
		Material material = EnsureMaterial(forceDefaults: false);
		if (material == null)
			return;

		if (forceAssignTargets)
		{
			EnsureLootStreamSettings();
			AssignToPrefab(material);
			AssignToOpenScenes(material);
			WireDynamicPilePrefab(material);
		}

		AssetDatabase.SaveAssets();
	}

	static Material EnsureMaterial(bool forceDefaults)
	{
		Shader shader = Shader.Find(ProceduralShaderName);
		if (shader == null)
		{
			Debug.LogWarning("Gold Pile Procedural shader not found: " + ProceduralShaderName
				+ ". Wait for Unity to import DragonLoot_GoldPileProcedural.shader.");
			return null;
		}

		Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
		bool created = false;
		if (material == null)
		{
			material = new Material(shader);
			material.name = "M_GoldPile_Procedural";
			AssetDatabase.CreateAsset(material, MaterialPath);
			created = true;
		}
		else if (material.shader != shader)
		{
			material.shader = shader;
		}

		AssignProceduralMapsIfMissing(material, forceOverwrite: created || forceDefaults);
		if (created || forceDefaults)
			SetProceduralDefaults(material);
		GoldPileQuality.Apply(material);
		EditorUtility.SetDirty(material);
		AssetDatabase.SaveAssets();
		return material;
	}

	static int ApplyGoldPileMaterialToSelection(Material material, string undoLabel)
	{
		int assigned = 0;

		Object[] selectedAssets = Selection.objects;
		for (int i = 0; i < selectedAssets.Length; i++)
		{
			TreasurePileDefinition definition = selectedAssets[i] as TreasurePileDefinition;
			if (definition == null)
				continue;

			Undo.RecordObject(definition, undoLabel);
			definition.pileMaterial = material;
			EditorUtility.SetDirty(definition);
			assigned++;
		}

		GameObject[] selection = Selection.gameObjects;
		for (int i = 0; i < selection.Length; i++)
		{
			TreasurePileVisual visual = selection[i].GetComponentInParent<TreasurePileVisual>();
			if (visual == null)
				visual = selection[i].GetComponentInChildren<TreasurePileVisual>(true);

			if (visual != null)
			{
				Undo.RecordObject(visual, undoLabel);
				SerializedObject so = new SerializedObject(visual);
				SerializedProperty prop = so.FindProperty("pileMaterial");
				if (prop != null)
				{
					prop.objectReferenceValue = material;
					so.ApplyModifiedProperties();
				}
			}

			MeshRenderer[] renderers = selection[i].GetComponentsInChildren<MeshRenderer>(true);
			for (int r = 0; r < renderers.Length; r++)
			{
				MeshRenderer renderer = renderers[r];
				bool isGoldPile = renderer.sharedMaterial != null
					&& GoldPileQuality.IsGoldPileMaterial(renderer.sharedMaterial);
				if (!isGoldPile && visual == null)
					continue;

				Undo.RecordObject(renderer, undoLabel);
				renderer.sharedMaterial = material;
				if (renderer.GetComponent<GoldPileQualityBinder>() == null)
					Undo.AddComponent<GoldPileQualityBinder>(renderer.gameObject);
				assigned++;
			}
		}

		return assigned;
	}

	static void AssignProceduralMapsIfMissing(Material material, bool forceOverwrite)
	{
		Texture2D albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(ProcCoinAlbedo);
		Texture2D bump = AssetDatabase.LoadAssetAtPath<Texture2D>(ProcCoinNormal);
		Texture2D mask = AssetDatabase.LoadAssetAtPath<Texture2D>(ProcCoinMetallic);
		Texture2D dirt = AssetDatabase.LoadAssetAtPath<Texture2D>(Occlusion);
		Texture2D copperAlbedo = AssetDatabase.LoadAssetAtPath<Texture2D>(ProcCopperAlbedo);
		Texture2D silverAlbedo = AssetDatabase.LoadAssetAtPath<Texture2D>(ProcSilverAlbedo);

		if (albedo != null && (forceOverwrite || material.GetTexture("_BaseMap") == null))
			material.SetTexture("_BaseMap", albedo);
		if (bump != null && (forceOverwrite || material.GetTexture("_BumpMap") == null))
			material.SetTexture("_BumpMap", bump);
		if (mask != null && (forceOverwrite || material.GetTexture("_MetallicGlossMap") == null))
			material.SetTexture("_MetallicGlossMap", mask);
		if (dirt != null && material.HasProperty("_DirtMap")
			&& (forceOverwrite || material.GetTexture("_DirtMap") == null))
			material.SetTexture("_DirtMap", dirt);
		if (copperAlbedo != null && material.HasProperty("_CopperBaseMap")
			&& (forceOverwrite || material.GetTexture("_CopperBaseMap") == null))
			material.SetTexture("_CopperBaseMap", copperAlbedo);
		if (silverAlbedo != null && material.HasProperty("_SilverBaseMap")
			&& (forceOverwrite || material.GetTexture("_SilverBaseMap") == null))
			material.SetTexture("_SilverBaseMap", silverAlbedo);
	}

	static void SetProceduralDefaults(Material material)
	{
		// Coin face defaults match M_GoldCoinPile / DragonLoot/Coin Pile.
		material.SetColor("_BaseColor", new Color(1f, 0.9f, 0.45f, 1f));
		material.SetColor("_GapColor", new Color(0.55f, 0.32f, 0.08f, 1f));
		material.SetFloat("_Metallic", 0.92f);
		material.SetFloat("_Smoothness", 0.75f);
		material.SetFloat("_GapMetallic", 0.85f);
		material.SetFloat("_GapSmoothness", 0.35f);
		material.SetFloat("_MetallicVariation", 0f);
		material.SetFloat("_BumpScale", 1f);
		material.SetFloat("_CoinNormalStrength", 1f);
		material.SetFloat("_DeformEnabled", 0f);
		material.SetFloat("_DeformScale", 1.5f);
		material.SetFloat("_DeformWorldSize", 4f);
		material.SetFloat("_DeformResolution", 64f);
		material.SetFloat("_DeformNormalSoften", 0f);
		material.SetFloat("_DeformSampleBlur", 0f);
		material.SetColor("_FresnelColor", new Color(1f, 0.82f, 0.45f, 1f));
		material.SetFloat("_FresnelIntensity", 0.25f);
		material.SetFloat("_FresnelPower", 4f);

		material.SetFloat("_CoinDiameter", 0.05f);
		material.SetFloat("_CoinDensity", 400f);
		material.SetFloat("_CellJitter", 0.65f);
		material.SetFloat("_RotationRandomness", 1f);
		material.SetFloat("_CoinTilt", 0.2f);
		material.SetFloat("_RimBevelStrength", 0.55f);
		material.SetFloat("_RimBevelWidth", 0.14f);
		material.SetFloat("_RimAoStrength", 0.35f);
		material.SetFloat("_BurialAmount", 0.08f);
		material.SetFloat("_TintVariation", 0.04f);
		material.SetFloat("_SmoothnessVariation", 0f);
		material.SetFloat("_SpecularVariation", 0f);
		material.SetFloat("_HueVariation", 0f);
		material.SetFloat("_ValueVariation", 0.04f);
		material.SetFloat("_EdgeHighlightStrength", 0f);
		material.SetFloat("_EdgeWidth", 0.12f);
		// Devtoid coin atlas sits high in the square with baked rim — crop to the face.
		material.SetVector("_CoinUVCenter", new Vector4(0.5f, 0.58f, 0f, 0f));
		material.SetFloat("_CoinUVScale", 1.35f);
		material.SetFloat("_PomEnabled", 1f);
		material.SetFloat("_PomHeight", 0.08f);
		material.SetFloat("_PomSteps", 8f);
		material.SetFloat("_CopperAmount", 0.2f);
		material.SetFloat("_SilverAmount", 0.1f);
		material.SetColor("_CopperColor", new Color(1f, 0.62f, 0.38f, 1f));
		material.SetColor("_SilverColor", new Color(0.92f, 0.94f, 0.98f, 1f));
		material.SetFloat("_DirtStrength", 0f);
		material.SetFloat("_AOStrength", 0f);

		material.SetFloat("_DistantCoinEnabled", 1f);
		material.SetColor("_DistantCoinColor0", new Color(1f, 0.78f, 0.28f, 1f));
		material.SetColor("_DistantCoinColor1", new Color(0.82f, 0.86f, 0.92f, 1f));
		material.SetColor("_DistantCoinColor2", new Color(0.95f, 0.45f, 0.2f, 1f));
		material.SetColor("_DistantCoinColor3", new Color(0.55f, 0.32f, 0.08f, 1f));
		material.SetFloat("_DistantCoinAmount0", 0.4f);
		material.SetFloat("_DistantCoinAmount1", 0.25f);
		material.SetFloat("_DistantCoinAmount2", 0.2f);
		material.SetFloat("_DistantCoinAmount3", 0.15f);
		material.SetFloat("_DistantCoinDensity", 4f);
		material.SetFloat("_DistantCoinCoverage", 0.35f);
		material.SetFloat("_DistantCoinIntensity", 1.25f);
		material.SetFloat("_DistantCoinMaxPixels", 4f);
		material.SetFloat("_DistantCoinGrowFar", 80f);
		material.SetFloat("_DistantCoinGrowNear", 28f);
		material.SetFloat("_DistantCoinFadeStart", 22f);
		material.SetFloat("_DistantCoinFadeEnd", 10f);
		material.SetFloat("_DistantCoinTopMask", 0.65f);
		material.SetFloat("_DistantCoinMetalFocusEnabled", 0f);
		material.SetFloat("_DistantCoinMetalFocus", 0.75f);
		material.SetFloat("_DistantCoinMetalFocusPower", 16f);

		material.SetFloat("_DisableLod", 0f);
		material.SetFloat("_LodNear", 8f);
		material.SetFloat("_LodMid", 18f);
		material.SetFloat("_LodFar", 40f);
		material.SetFloat("_LodNoiseFade", 1f);

		material.EnableKeyword("_POM_ON");
		GoldPileQuality.Apply(material);
	}

	static void AssignToPrefab(Material material)
	{
		GameObject prefabRoot = PrefabUtility.LoadPrefabContents(GoldPilePrefab);
		if (prefabRoot == null)
			return;

		try
		{
			MeshRenderer[] renderers = prefabRoot.GetComponentsInChildren<MeshRenderer>(true);
			bool dirty = false;
			for (int i = 0; i < renderers.Length; i++)
			{
				MeshRenderer renderer = renderers[i];
				if (renderer.sharedMaterial == material)
					continue;

				renderer.sharedMaterial = material;
				dirty = true;

				if (renderer.GetComponent<GoldPileQualityBinder>() == null)
					renderer.gameObject.AddComponent<GoldPileQualityBinder>();
			}

			if (dirty)
				PrefabUtility.SaveAsPrefabAsset(prefabRoot, GoldPilePrefab);
		}
		finally
		{
			PrefabUtility.UnloadPrefabContents(prefabRoot);
		}
	}

	const string GoldHoardDef = "Assets/Definitions/Treasure/GoldHoard.asset";

	static void WireDynamicPilePrefab(Material pileMaterial)
	{
		GameObject prefabRoot = PrefabUtility.LoadPrefabContents(GoldPilePrefab);
		if (prefabRoot == null)
			return;

		try
		{
			EnsureRootColliderRemoved(prefabRoot);

			TreasurePileInteractable interactable = prefabRoot.GetComponent<TreasurePileInteractable>();
			if (interactable == null)
				interactable = prefabRoot.AddComponent<TreasurePileInteractable>();

			TreasurePileVisual visual = prefabRoot.GetComponent<TreasurePileVisual>();
			if (visual == null)
				visual = prefabRoot.AddComponent<TreasurePileVisual>();

			if (interactable == null || visual == null)
			{
				Debug.LogError("Failed to add TreasurePileInteractable / TreasurePileVisual to CoinPile_Gold.");
				return;
			}

			if (prefabRoot.GetComponent<GoldPileTerrainMesh>() == null)
				prefabRoot.AddComponent<GoldPileTerrainMesh>();

			GoldPileLootInstances loot = prefabRoot.GetComponent<GoldPileLootInstances>();
			if (loot == null)
				loot = prefabRoot.AddComponent<GoldPileLootInstances>();

			MonoBehaviour[] behaviours = prefabRoot.GetComponents<MonoBehaviour>();
			for (int i = behaviours.Length - 1; i >= 0; i--)
			{
				MonoBehaviour behaviour = behaviours[i];
				if (behaviour == null)
					continue;
				string typeName = behaviour.GetType().Name;
				if (typeName == "GoldPileCoinShell" || typeName == "GoldPileCoinShellDebug")
					Object.DestroyImmediate(behaviour);
			}

			if (prefabRoot.GetComponent<GoldPileLootStreamDebug>() == null)
				prefabRoot.AddComponent<GoldPileLootStreamDebug>();

			GoldPileLootStreamSettings streamSettings = EnsureLootStreamSettings();

			SerializedObject so = new SerializedObject(visual);
			if (pileMaterial != null)
				so.FindProperty("pileMaterial").objectReferenceValue = pileMaterial;
			so.FindProperty("lootInstances").objectReferenceValue = loot;

			GameObject coinVisual = AssetDatabase.LoadAssetAtPath<GameObject>(GoldCoinVisual);
			SerializedObject lootSo = new SerializedObject(loot);
			if (coinVisual != null)
			{
				MeshFilter mf = coinVisual.GetComponentInChildren<MeshFilter>();
				if (mf != null)
					lootSo.FindProperty("fallbackMesh").objectReferenceValue = mf.sharedMesh;
			}

			Material goldCoinPile = CoinMaterialInstaller.EnsureGoldPileMaterial();
			Material silverCoinPile = AssetDatabase.LoadAssetAtPath<Material>(
				"Assets/Materials/Shaders/Coin/M_SilverCoinPile.mat");
			Material copperCoinPile = AssetDatabase.LoadAssetAtPath<Material>(
				"Assets/Materials/Shaders/Coin/M_CopperCoinPile.mat");

			if (goldCoinPile != null)
			{
				goldCoinPile.enableInstancing = true;
				EditorUtility.SetDirty(goldCoinPile);
				lootSo.FindProperty("pileMaterial").objectReferenceValue = goldCoinPile;
				lootSo.FindProperty("fallbackMaterial").objectReferenceValue = goldCoinPile;
			}

			SerializedProperty silverPileProp = lootSo.FindProperty("silverPileMaterial");
			if (silverPileProp != null && silverCoinPile != null)
			{
				silverCoinPile.enableInstancing = true;
				EditorUtility.SetDirty(silverCoinPile);
				silverPileProp.objectReferenceValue = silverCoinPile;
			}

			SerializedProperty copperPileProp = lootSo.FindProperty("copperPileMaterial");
			if (copperPileProp != null && copperCoinPile != null)
			{
				copperCoinPile.enableInstancing = true;
				EditorUtility.SetDirty(copperCoinPile);
				copperPileProp.objectReferenceValue = copperCoinPile;
			}

			SerializedProperty streamProp = lootSo.FindProperty("streamSettings");
			if (streamProp != null)
				streamProp.objectReferenceValue = streamSettings;

			TreasurePileDefinition hoard = AssetDatabase.LoadAssetAtPath<TreasurePileDefinition>(GoldHoardDef);
			SerializedObject pileSo = new SerializedObject(interactable);
			SerializedProperty defProp = pileSo.FindProperty("pileDefinition");
			if (defProp != null && hoard != null)
				defProp.objectReferenceValue = hoard;

			TreasureDefinition goldCoin = AssetDatabase.LoadAssetAtPath<TreasureDefinition>(GoldCoinDef);
			SerializedProperty treasureProp = pileSo.FindProperty("treasure");
			if (treasureProp != null && goldCoin != null)
				treasureProp.objectReferenceValue = goldCoin;

			SerializedProperty remaining = pileSo.FindProperty("remainingCount");
			if (remaining != null && hoard != null)
				remaining.intValue = Mathf.Max(1, hoard.TotalUnits());

			SerializedProperty visualProp = pileSo.FindProperty("pileVisual");
			if (visualProp != null)
				visualProp.objectReferenceValue = visual;

			so.ApplyModifiedPropertiesWithoutUndo();
			lootSo.ApplyModifiedPropertiesWithoutUndo();
			pileSo.ApplyModifiedPropertiesWithoutUndo();
			PrefabUtility.SaveAsPrefabAsset(prefabRoot, GoldPilePrefab);
		}
		finally
		{
			PrefabUtility.UnloadPrefabContents(prefabRoot);
		}
	}

	static GoldPileLootStreamSettings EnsureLootStreamSettings()
	{
		GoldPileLootStreamSettings settings = AssetDatabase.LoadAssetAtPath<GoldPileLootStreamSettings>(LootStreamSettingsPath);
		if (settings != null)
			return settings;

		// Prefer migrating the legacy Materials path into Definitions if it still exists.
		GoldPileLootStreamSettings legacy = AssetDatabase.LoadAssetAtPath<GoldPileLootStreamSettings>(LegacyLootStreamSettingsPath);
		if (legacy != null)
		{
			string moveError = AssetDatabase.MoveAsset(LegacyLootStreamSettingsPath, LootStreamSettingsPath);
			if (string.IsNullOrEmpty(moveError))
			{
				settings = AssetDatabase.LoadAssetAtPath<GoldPileLootStreamSettings>(LootStreamSettingsPath);
				if (settings != null)
					return settings;
			}

			settings = legacy;
			return settings;
		}

		// Migrate / replace obsolete shell settings asset if present.
		const string ObsoleteShellSettings = "Assets/Materials/Shaders/GoldPile/GoldPileCoinShellSettings.asset";
		if (AssetDatabase.LoadAssetAtPath<Object>(ObsoleteShellSettings) != null)
			AssetDatabase.DeleteAsset(ObsoleteShellSettings);

		string folder = System.IO.Path.GetDirectoryName(LootStreamSettingsPath)?.Replace('\\', '/');
		if (!string.IsNullOrEmpty(folder) && !AssetDatabase.IsValidFolder(folder))
		{
			// Definitions/Treasure/Pile should already exist; create only if missing.
			EnsureFolder(folder);
		}

		settings = ScriptableObject.CreateInstance<GoldPileLootStreamSettings>();
		settings.chunkSize = 8f;
		settings.lod0End = 14f;
		settings.lod1End = 28f;
		settings.lod2End = 48f;
		settings.lodHysteresisFrames = 8;
		settings.lod0Density = 1f;
		settings.lod1Density = 0.6f;
		settings.lod2Density = 0.25f;
		AssetDatabase.CreateAsset(settings, LootStreamSettingsPath);
		EditorUtility.SetDirty(settings);
		return settings;
	}

	static void EnsureFolder(string folderPath)
	{
		if (AssetDatabase.IsValidFolder(folderPath))
			return;

		string[] parts = folderPath.Split('/');
		if (parts.Length < 2 || parts[0] != "Assets")
			return;

		string current = "Assets";
		for (int i = 1; i < parts.Length; i++)
		{
			string next = current + "/" + parts[i];
			if (!AssetDatabase.IsValidFolder(next))
				AssetDatabase.CreateFolder(current, parts[i]);
			current = next;
		}
	}

	static void EnsureRootColliderRemoved(GameObject root)
	{
		if (root == null)
			return;

		// Heightfield piles use chunked GoldPileColliderTiles — never keep a root MeshCollider.
		MeshCollider[] colliders = root.GetComponentsInChildren<MeshCollider>(true);
		for (int i = 0; i < colliders.Length; i++)
		{
			MeshCollider col = colliders[i];
			if (col == null)
				continue;

			// Keep runtime collider tiles if somehow present while wiring.
			Transform t = col.transform;
			if (t.name.StartsWith("ColliderTile_") || t.name == "GoldPileColliders" || t.name == "~GoldPileColliders")
				continue;

			Object.DestroyImmediate(col, true);
		}
	}

	static void AssignToOpenScenes(Material material)
	{
		MeshRenderer[] renderers = Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
		for (int i = 0; i < renderers.Length; i++)
		{
			MeshRenderer renderer = renderers[i];
			if (renderer == null || renderer.gameObject.name != "CoinPile")
				continue;

			Undo.RecordObject(renderer, "Assign Gold Pile Material");
			renderer.sharedMaterial = material;
			if (renderer.GetComponent<GoldPileQualityBinder>() == null)
				Undo.AddComponent<GoldPileQualityBinder>(renderer.gameObject);
			EditorUtility.SetDirty(renderer);
			EditorUtility.SetDirty(renderer.gameObject);
			UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(renderer.gameObject.scene);
		}
	}
}
#endif
