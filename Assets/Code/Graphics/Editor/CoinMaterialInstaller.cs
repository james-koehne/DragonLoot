#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

/// <summary>
/// Creates DragonLoot held coin materials (DragonLoot/Coin), lean pile materials (DragonLoot/Coin Pile),
/// and coin-stack materials (DragonLoot/Coin Stack), plus <see cref="CoinStackVisualDefinition"/>.
/// </summary>
public static class CoinMaterialInstaller
{
	const string HeldShaderName = "DragonLoot/Coin";
	const string PileShaderName = "DragonLoot/Coin Pile";
	const string StackShaderName = "DragonLoot/Coin Stack";
	const string MaterialFolder = "Assets/Materials/Shaders/Coin";
	const string DefinitionsFolder = "Assets/Definitions";
	const string StackVisualDefinitionPath = DefinitionsFolder + "/CoinStackVisualDefinition.asset";
	const string StackCoinMeshPath =
		"Assets/ThirdParty/LiquidFire Package 4 - BSH games/Devtoid - Gold Coins/3D Assets/Copper Coin - Single/Mesh/CopperCoin.fbx";

	const string GoldMatPath = MaterialFolder + "/M_GoldCoin.mat";
	const string SilverMatPath = MaterialFolder + "/M_SilverCoin.mat";
	const string CopperMatPath = MaterialFolder + "/M_CopperCoin.mat";

	const string GoldPileMatPath = MaterialFolder + "/M_GoldCoinPile.mat";
	const string SilverPileMatPath = MaterialFolder + "/M_SilverCoinPile.mat";
	const string CopperPileMatPath = MaterialFolder + "/M_CopperCoinPile.mat";

	const string GoldStackMatPath = MaterialFolder + "/M_GoldCoinStack.mat";
	const string SilverStackMatPath = MaterialFolder + "/M_SilverCoinStack.mat";
	const string CopperStackMatPath = MaterialFolder + "/M_CopperCoinStack.mat";

	const string GoldVisual = "Assets/Addressables/Treasure/Coins/GoldCoinVisual.prefab";
	const string SilverVisual = "Assets/Addressables/Treasure/Coins/SilverCoinVisual.prefab";
	const string CopperVisual = "Assets/Addressables/Treasure/Coins/CopperCoinVisual.prefab";

	const string GoldSrcMat = "Assets/ThirdParty/LiquidFire Package 4 - BSH games/Devtoid - Gold Coins/3D Assets/Gold Coin - Single/Materials/Gold Coin - Single.mat";
	const string SilverSrcMat = "Assets/ThirdParty/LiquidFire Package 4 - BSH games/Devtoid - Gold Coins/3D Assets/Silver Coin - Single/Material/Silver Coin - Single.mat";
	const string CopperSrcMat = "Assets/ThirdParty/LiquidFire Package 4 - BSH games/Devtoid - Gold Coins/3D Assets/Copper Coin - Single/Material/Copper Coin - Single.mat";

	public static string GoldPileMaterialPath => GoldPileMatPath;
	public static string GoldStackMaterialPath => GoldStackMatPath;
	public static string SilverStackMaterialPath => SilverStackMatPath;
	public static string CopperStackMaterialPath => CopperStackMatPath;
	public static string StackVisualDefinitionAssetPath => StackVisualDefinitionPath;

	[InitializeOnLoadMethod]
	static void AutoInstallIfMissing()
	{
		EditorApplication.delayCall += () =>
		{
			if (EditorApplication.isPlayingOrWillChangePlaymode)
				return;
			if (AssetDatabase.LoadAssetAtPath<Material>(GoldMatPath) != null
				&& AssetDatabase.LoadAssetAtPath<Material>(GoldPileMatPath) != null
				&& AssetDatabase.LoadAssetAtPath<Material>(GoldStackMatPath) != null
				&& AssetDatabase.LoadAssetAtPath<CoinStackVisualDefinition>(StackVisualDefinitionPath) != null)
				return;
			if (Shader.Find(HeldShaderName) == null
				|| Shader.Find(PileShaderName) == null
				|| Shader.Find(StackShaderName) == null)
				return;

			TryInstall(forceAssignPrefabs: true, applyDefaults: true);
		};
	}

	[MenuItem("DragonLoot/Graphics/Install Coin Materials")]
	public static void MenuInstall()
	{
		TryInstall(forceAssignPrefabs: true, applyDefaults: false);
	}

	[MenuItem("DragonLoot/Graphics/Reset Coin Material Defaults")]
	public static void MenuResetDefaults()
	{
		TryInstall(forceAssignPrefabs: true, applyDefaults: true);
		Debug.Log("Coin material defaults reset (held + pile + stack definition) and Addressable visuals wired.");
	}

	public static Material EnsureGoldPileMaterial()
	{
		TryInstall(forceAssignPrefabs: false, applyDefaults: false);
		return AssetDatabase.LoadAssetAtPath<Material>(GoldPileMatPath);
	}

	public static void TryInstall(bool forceAssignPrefabs, bool applyDefaults)
	{
		Shader heldShader = Shader.Find(HeldShaderName);
		Shader pileShader = Shader.Find(PileShaderName);
		Shader stackShader = Shader.Find(StackShaderName);
		if (heldShader == null)
		{
			Debug.LogWarning("Coin shader not found: " + HeldShaderName + " (wait for Unity import, then re-run).");
			return;
		}

		if (pileShader == null)
		{
			Debug.LogWarning("Coin Pile shader not found: " + PileShaderName + " (wait for Unity import, then re-run).");
			return;
		}

		if (stackShader == null)
		{
			Debug.LogWarning("Coin Stack shader not found: " + StackShaderName + " (wait for Unity import, then re-run).");
			return;
		}

		EnsureMaterialFolder();

		Material gold = CreateOrUpdateHeld(
			GoldMatPath, heldShader, GoldSrcMat,
			new Color(1f, 0.9f, 0.45f, 1f),
			new Color(1f, 0.82f, 0.45f, 1f),
			applyDefaults);
		Material silver = CreateOrUpdateHeld(
			SilverMatPath, heldShader, SilverSrcMat,
			new Color(0.92f, 0.94f, 0.98f, 1f),
			new Color(0.85f, 0.9f, 1f, 1f),
			applyDefaults);
		Material copper = CreateOrUpdateHeld(
			CopperMatPath, heldShader, CopperSrcMat,
			new Color(1f, 0.62f, 0.38f, 1f),
			new Color(1f, 0.55f, 0.3f, 1f),
			applyDefaults);

		CreateOrUpdatePile(
			GoldPileMatPath, pileShader, GoldSrcMat,
			new Color(1f, 0.9f, 0.45f, 1f),
			new Color(1f, 0.82f, 0.45f, 1f),
			applyDefaults);
		CreateOrUpdatePile(
			SilverPileMatPath, pileShader, SilverSrcMat,
			new Color(0.92f, 0.94f, 0.98f, 1f),
			new Color(0.85f, 0.9f, 1f, 1f),
			applyDefaults);
		CreateOrUpdatePile(
			CopperPileMatPath, pileShader, CopperSrcMat,
			new Color(1f, 0.62f, 0.38f, 1f),
			new Color(1f, 0.55f, 0.3f, 1f),
			applyDefaults);

		CreateOrUpdateStack(
			GoldStackMatPath, stackShader, GoldSrcMat,
			new Color(1f, 0.9f, 0.45f, 1f),
			new Color(1f, 0.82f, 0.45f, 1f),
			applyDefaults);
		CreateOrUpdateStack(
			SilverStackMatPath, stackShader, SilverSrcMat,
			new Color(0.92f, 0.94f, 0.98f, 1f),
			new Color(0.85f, 0.9f, 1f, 1f),
			applyDefaults);
		CreateOrUpdateStack(
			CopperStackMatPath, stackShader, CopperSrcMat,
			new Color(1f, 0.62f, 0.38f, 1f),
			new Color(1f, 0.55f, 0.3f, 1f),
			applyDefaults);

		WireCoinStackVisualDefinition(applyDefaults);

		if (forceAssignPrefabs)
		{
			AssignToVisual(GoldVisual, gold);
			AssignToVisual(SilverVisual, silver);
			AssignToVisual(CopperVisual, copper);
			WireGoldPileLootPileMaterials();
		}

		AssetDatabase.SaveAssets();
		Debug.Log("Coin materials installed: held + pile + stack + CoinStackVisualDefinition.");
	}

	static void WireCoinStackVisualDefinition(bool applyDefaults)
	{
		EnsureDefinitionsFolder();

		CoinStackVisualDefinition definition =
			AssetDatabase.LoadAssetAtPath<CoinStackVisualDefinition>(StackVisualDefinitionPath);
		if (definition == null)
		{
			definition = ScriptableObject.CreateInstance<CoinStackVisualDefinition>();
			definition.name = "CoinStackVisualDefinition";
			AssetDatabase.CreateAsset(definition, StackVisualDefinitionPath);
			applyDefaults = true;
		}

		Material goldStack = AssetDatabase.LoadAssetAtPath<Material>(GoldStackMatPath);
		Material silverStack = AssetDatabase.LoadAssetAtPath<Material>(SilverStackMatPath);
		Material copperStack = AssetDatabase.LoadAssetAtPath<Material>(CopperStackMatPath);
		Mesh stackMesh = LoadStackCoinMesh();

		SerializedObject so = new SerializedObject(definition);
		so.FindProperty("goldStackMaterial").objectReferenceValue = goldStack;
		so.FindProperty("silverStackMaterial").objectReferenceValue = silverStack;
		so.FindProperty("copperStackMaterial").objectReferenceValue = copperStack;
		if (stackMesh != null)
			so.FindProperty("stackMesh").objectReferenceValue = stackMesh;

		if (applyDefaults)
		{
			so.FindProperty("diameterScale").floatValue = 1f;
			so.FindProperty("heightLerpSpeed").floatValue = 18f;
			so.FindProperty("minCountForCylinder").intValue = 2;
			so.FindProperty("meshReferenceDiameter").floatValue = 0f;
			so.FindProperty("meshReferenceHeight").floatValue = 0f;
		}

		so.ApplyModifiedPropertiesWithoutUndo();
		EditorUtility.SetDirty(definition);
		RegisterDefinitionAddressable(StackVisualDefinitionPath);
	}

	static Mesh LoadStackCoinMesh()
	{
		Object[] assets = AssetDatabase.LoadAllAssetsAtPath(StackCoinMeshPath);
		if (assets == null || assets.Length == 0)
		{
			Debug.LogWarning("Coin stack mesh not found at " + StackCoinMeshPath);
			return null;
		}

		Mesh named = null;
		Mesh first = null;
		for (int i = 0; i < assets.Length; i++)
		{
			Mesh mesh = assets[i] as Mesh;
			if (mesh == null)
				continue;
			if (first == null)
				first = mesh;
			if (mesh.name == "GoldCoin" || mesh.name.IndexOf("Coin", System.StringComparison.OrdinalIgnoreCase) >= 0)
				named = mesh;
		}

		return named != null ? named : first;
	}

	static void RegisterDefinitionAddressable(string assetPath)
	{
		AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
		if (settings == null)
		{
			Debug.LogWarning("AddressableAssetSettings missing; skipped registering " + assetPath);
			return;
		}

		string guid = AssetDatabase.AssetPathToGUID(assetPath);
		if (string.IsNullOrEmpty(guid))
			return;

		AddressableAssetGroup group = settings.DefaultGroup;
		AddressableAssetEntry entry = settings.FindAssetEntry(guid);
		if (entry == null)
			entry = settings.CreateOrMoveEntry(guid, group, readOnly: false, postEvent: false);

		entry.SetAddress(assetPath);
		entry.SetLabel("Definition", true, true);
		settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryMoved, entry, true);
	}

	static void EnsureDefinitionsFolder()
	{
		if (AssetDatabase.IsValidFolder(DefinitionsFolder))
			return;

		if (!AssetDatabase.IsValidFolder("Assets/Definitions"))
			AssetDatabase.CreateFolder("Assets", "Definitions");
	}

	static void WireGoldPileLootPileMaterials()
	{
		const string goldPilePrefab = "Assets/ThirdParty/REAL_DEDICATED/BasicTreasureCoins/Prefabs/CoinPile_Gold.prefab";
		GameObject prefabRoot = PrefabUtility.LoadPrefabContents(goldPilePrefab);
		if (prefabRoot == null)
			return;

		try
		{
			GoldPileLootInstances loot = prefabRoot.GetComponent<GoldPileLootInstances>();
			if (loot == null)
				return;

			Material goldPile = AssetDatabase.LoadAssetAtPath<Material>(GoldPileMatPath);
			Material silverPile = AssetDatabase.LoadAssetAtPath<Material>(SilverPileMatPath);
			Material copperPile = AssetDatabase.LoadAssetAtPath<Material>(CopperPileMatPath);

			SerializedObject lootSo = new SerializedObject(loot);
			if (goldPile != null)
			{
				goldPile.enableInstancing = true;
				EditorUtility.SetDirty(goldPile);
				lootSo.FindProperty("pileMaterial").objectReferenceValue = goldPile;
				lootSo.FindProperty("fallbackMaterial").objectReferenceValue = goldPile;
			}

			SerializedProperty silverProp = lootSo.FindProperty("silverPileMaterial");
			if (silverProp != null && silverPile != null)
			{
				silverPile.enableInstancing = true;
				EditorUtility.SetDirty(silverPile);
				silverProp.objectReferenceValue = silverPile;
			}

			SerializedProperty copperProp = lootSo.FindProperty("copperPileMaterial");
			if (copperProp != null && copperPile != null)
			{
				copperPile.enableInstancing = true;
				EditorUtility.SetDirty(copperPile);
				copperProp.objectReferenceValue = copperPile;
			}

			lootSo.ApplyModifiedPropertiesWithoutUndo();
			PrefabUtility.SaveAsPrefabAsset(prefabRoot, goldPilePrefab);
		}
		finally
		{
			PrefabUtility.UnloadPrefabContents(prefabRoot);
		}
	}

	static void EnsureMaterialFolder()
	{
		if (AssetDatabase.IsValidFolder(MaterialFolder))
			return;

		if (!AssetDatabase.IsValidFolder("Assets/Materials"))
			AssetDatabase.CreateFolder("Assets", "Materials");
		if (!AssetDatabase.IsValidFolder("Assets/Materials/Shaders"))
			AssetDatabase.CreateFolder("Assets/Materials", "Shaders");
		if (!AssetDatabase.IsValidFolder(MaterialFolder))
			AssetDatabase.CreateFolder("Assets/Materials/Shaders", "Coin");
	}

	static Material CreateOrUpdateHeld(
		string path,
		Shader shader,
		string sourceMatPath,
		Color tint,
		Color fresnel,
		bool applyDefaults)
	{
		Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
		bool created = false;
		if (material == null)
		{
			material = new Material(shader);
			material.name = System.IO.Path.GetFileNameWithoutExtension(path);
			AssetDatabase.CreateAsset(material, path);
			created = true;
			applyDefaults = true;
		}
		else if (material.shader != shader)
		{
			material.shader = shader;
			EditorUtility.SetDirty(material);
		}

		CopyMapsFromSource(material, sourceMatPath, forceOverwrite: created);
		material.enableInstancing = true;

		if (applyDefaults)
			SetHeldDefaults(material, tint, fresnel);

		EditorUtility.SetDirty(material);
		return material;
	}

	static Material CreateOrUpdatePile(
		string path,
		Shader shader,
		string sourceMatPath,
		Color tint,
		Color fresnel,
		bool applyDefaults)
	{
		Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
		bool created = false;
		if (material == null)
		{
			material = new Material(shader);
			material.name = System.IO.Path.GetFileNameWithoutExtension(path);
			AssetDatabase.CreateAsset(material, path);
			created = true;
			applyDefaults = true;
		}
		else if (material.shader != shader)
		{
			material.shader = shader;
			EditorUtility.SetDirty(material);
		}

		CopyMapsFromSource(material, sourceMatPath, forceOverwrite: created);
		material.enableInstancing = true;

		if (applyDefaults)
			SetPileDefaults(material, tint, fresnel);

		EditorUtility.SetDirty(material);
		return material;
	}

	static Material CreateOrUpdateStack(
		string path,
		Shader shader,
		string sourceMatPath,
		Color tint,
		Color fresnel,
		bool applyDefaults)
	{
		Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
		bool created = false;
		if (material == null)
		{
			material = new Material(shader);
			material.name = System.IO.Path.GetFileNameWithoutExtension(path);
			AssetDatabase.CreateAsset(material, path);
			created = true;
			applyDefaults = true;
		}
		else if (material.shader != shader)
		{
			material.shader = shader;
			EditorUtility.SetDirty(material);
		}

		CopyMapsFromSource(material, sourceMatPath, forceOverwrite: created);
		material.enableInstancing = true;

		if (applyDefaults)
			SetStackDefaults(material, tint, fresnel);

		EditorUtility.SetDirty(material);
		return material;
	}

	static void CopyMapsFromSource(Material material, string sourceMatPath, bool forceOverwrite)
	{
		Material source = AssetDatabase.LoadAssetAtPath<Material>(sourceMatPath);
		if (source == null)
			return;

		CopyTex(material, source, "_BaseMap", forceOverwrite);
		CopyTex(material, source, "_BumpMap", forceOverwrite);
		CopyTex(material, source, "_MetallicGlossMap", forceOverwrite);
		if (material.HasProperty("_OcclusionMap"))
			CopyTex(material, source, "_OcclusionMap", forceOverwrite);
	}

	static void CopyTex(Material dst, Material src, string prop, bool forceOverwrite)
	{
		if (!src.HasProperty(prop) || !dst.HasProperty(prop))
			return;

		Texture tex = src.GetTexture(prop);
		if (tex == null)
			return;

		if (forceOverwrite || dst.GetTexture(prop) == null)
			dst.SetTexture(prop, tex);
	}

	static void SetHeldDefaults(Material material, Color tint, Color fresnel)
	{
		material.SetColor("_BaseColor", tint);
		material.SetFloat("_Metallic", 0.92f);
		material.SetFloat("_Smoothness", 0.75f);
		material.SetFloat("_BumpScale", 1f);
		material.SetFloat("_OcclusionStrength", 0f);
		material.SetFloat("_ReflectionFloor", 0.12f);
		material.SetColor("_FresnelColor", fresnel);
		material.SetFloat("_FresnelIntensity", 0.35f);
		material.SetFloat("_FresnelPower", 4f);
		material.SetFloat("_TintVariation", 0.05f);
		material.SetFloat("_SmoothnessVariation", 0.1f);
		material.SetFloat("_SpecularVariation", 0.1f);
		material.SetFloat("_HueVariation", 0.04f);
		material.SetFloat("_ValueVariation", 0.05f);
		material.SetFloat("_RoughnessUvScale", 32f);
		material.SetFloat("_RoughnessUvAmount", 0.08f);
		material.SetFloat("_EdgeWearEnabled", 0f);
		material.SetFloat("_EdgeWearStrength", 0.5f);
		material.SetFloat("_DirtEnabled", 0f);
		material.SetFloat("_DirtStrength", 0.4f);
		material.DisableKeyword("_EDGEWEAR_ON");
		material.DisableKeyword("_DIRT_ON");
		// Per-coin _VariationSeed MPB must not be batched away by GPU instancing.
		material.enableInstancing = false;
	}

	static void SetPileDefaults(Material material, Color tint, Color fresnel)
	{
		material.SetColor("_BaseColor", tint);
		material.SetFloat("_Metallic", 0.92f);
		material.SetFloat("_Smoothness", 0.75f);
		material.SetFloat("_BumpScale", 1f);
		material.SetFloat("_ReflectionFloor", 0.12f);
		material.SetColor("_FresnelColor", fresnel);
		material.SetFloat("_FresnelIntensity", 0.25f);
		material.SetFloat("_FresnelPower", 4f);
		material.SetFloat("_TintVariation", 0.04f);
		material.SetFloat("_ValueVariation", 0.04f);
		material.enableInstancing = true;
	}

	static void SetStackDefaults(Material material, Color tint, Color fresnel)
	{
		// Keep BaseColor near-white so the Devtoid albedo drives metal color on the coin mesh.
		material.SetColor("_BaseColor", Color.white);
		material.SetFloat("_Metallic", 0.92f);
		material.SetFloat("_Smoothness", 0.75f);
		material.SetFloat("_BumpScale", 1f);
		material.SetFloat("_ReflectionFloor", 0.12f);
		material.SetColor("_FresnelColor", fresnel);
		material.SetFloat("_FresnelIntensity", 0.3f);
		material.SetFloat("_FresnelPower", 4f);
		material.SetFloat("_TintVariation", 0.03f);
		material.SetFloat("_ValueVariation", 0.03f);
		material.SetFloat("_CoinCount", 1f);
		material.SetFloat("_BandContrast", 1.1f);
		material.SetFloat("_GrooveDarkness", 0.35f);
		material.SetFloat("_GrooveWidth", 0.08f);
		material.SetFloat("_RidgeSoftness", 0.02f);
		material.SetFloat("_CapNormalThreshold", 0.55f);
		material.SetFloat("_RidgeAlbedoBoost", 0.02f);
		material.SetFloat("_GrooveSmoothnessScale", 0.82f);
		material.SetFloat("_RidgeSmoothnessScale", 1f);
		material.SetFloat("_GrooveMetallicScale", 0.92f);
		material.SetFloat("_SideBumpScale", 0.35f);
		material.SetFloat("_GrooveNormalStrength", 0.65f);
		material.SetFloat("_GrooveNormalBias", 1f);
		material.SetFloat("_CoinEdgeBevelWidth", 0.12f);
		material.SetFloat("_CoinEdgeBevelStrength", 0.45f);
		material.SetFloat("_SideFacetStrength", 0.04f);
		material.SetFloat("_SideFacetFrequency", 12f);
		material.SetFloat("_SeamSideClip", 0f);
		material.SetFloat("_SeamClipWidth", 0.12f);
		material.SetFloat("_SeamClipGrooveOnly", 0f);
		material.SetFloat("_SeamWorldOffsetMax", 0.012f);
		material.SetFloat("_SeamViewAlignStart", 0.7f);
		material.SetFloat("_SeamViewAlignEnd", 0.92f);
		material.SetFloat("_SeamNormalStrength", 0.55f);
		material.SetFloat("_SeamSoftAO", 0.4f);
		material.SetFloat("_MeshBoundsMinY", -0.05f);
		material.SetFloat("_MeshBoundsSizeY", 0.1f);
		material.enableInstancing = true;
	}

	static void AssignToVisual(string prefabPath, Material material)
	{
		if (material == null)
			return;

		GameObject prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);
		if (prefabRoot == null)
			return;

		try
		{
			MeshRenderer[] renderers = prefabRoot.GetComponentsInChildren<MeshRenderer>(true);
			bool dirty = false;
			for (int i = 0; i < renderers.Length; i++)
			{
				MeshRenderer renderer = renderers[i];
				if (renderer == null || renderer.sharedMaterial == material)
					continue;

				renderer.sharedMaterial = material;
				dirty = true;
			}

			if (dirty)
				PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
		}
		finally
		{
			PrefabUtility.UnloadPrefabContents(prefabRoot);
		}
	}
}
#endif
