#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// One-shot create / soft-repair for M_GoldPile.mat and dynamic heightfield pile wiring.
/// Does not overwrite authored float/color/keyword values (those were being reset on Play Mode).
/// </summary>
public static class GoldPileMaterialInstaller
{
	const string ShaderName = "DragonLoot/Gold Pile";
	const string StylizedShaderName = "DragonLoot/Gold Pile Stylized";
	const string ProceduralShaderName = "DragonLoot/Gold Pile Procedural";
	const string MaterialPath = "Assets/Materials/Shaders/GoldPile/M_GoldPile.mat";
	const string StylizedMaterialPath = "Assets/Materials/Shaders/GoldPile/M_GoldPile_Stylized.mat";
	const string ProceduralMaterialPath = "Assets/Materials/Shaders/GoldPile/M_GoldPile_Procedural.mat";
	const string GoldBc = "Assets/ThirdParty/REAL_DEDICATED/BasicTreasureCoins/Textures/T_BasicTreasureCoins_Gold_BC.tga";
	const string Normal = "Assets/ThirdParty/REAL_DEDICATED/BasicTreasureCoins/Textures/T_BasicTreasureCoins_N.tga";
	const string MetallicSmooth = "Assets/ThirdParty/REAL_DEDICATED/BasicTreasureCoins/Textures/T_BasicTreasureCoins_MetallicSmooth.tga";
	const string Occlusion = "Assets/ThirdParty/REAL_DEDICATED/BasicTreasureCoins/Textures/T_BasicTreasureCoins_Occlusion.tga";
	const string ProcCoinAlbedo =
		"Assets/ThirdParty/LiquidFire Package 4 - BSH games/Devtoid - Gold Coins/3D Assets/Gold Coins/Textures/GoldCoins_AlbedoTransparency.png";
	const string ProcCoinNormal =
		"Assets/ThirdParty/LiquidFire Package 4 - BSH games/Devtoid - Gold Coins/3D Assets/Gold Coins/Textures/GoldCoins_Normal.png";
	const string ProcCoinMetallic =
		"Assets/ThirdParty/LiquidFire Package 4 - BSH games/Devtoid - Gold Coins/3D Assets/Gold Coins/Textures/GoldCoins_MetallicSmoothness.png";
	const string ProcCopperAlbedo =
		"Assets/ThirdParty/LiquidFire Package 4 - BSH games/Devtoid - Gold Coins/3D Assets/Copper Coins/Textures/CopperCoins_AlbedoTransparency.png";
	const string ProcSilverAlbedo =
		"Assets/ThirdParty/LiquidFire Package 4 - BSH games/Devtoid - Gold Coins/3D Assets/Silver Coins/Textures/SilverCoins_AlbedoTransparency.png";
	const string GoldPilePrefab = "Assets/ThirdParty/REAL_DEDICATED/BasicTreasureCoins/Prefabs/CoinPile_Gold.prefab";
	const string GoldCoinVisual = "Assets/Addressables/Treasure/Coins/GoldCoinVisual.prefab";
	const string GoldCoinDef = "Assets/Definitions/Treasure/GoldCoin.asset";
	const string LootStreamSettingsPath = "Assets/Materials/Shaders/GoldPile/GoldPileLootStreamSettings.asset";

	[MenuItem("DragonLoot/Graphics/Install Gold Pile Material")]
	public static void MenuInstall()
	{
		TryInstall(forceAssignTargets: true);
	}

	[MenuItem("DragonLoot/Graphics/Install Gold Pile Stylized Material (AB)")]
	public static void MenuInstallStylized()
	{
		Material material = EnsureStylizedMaterial(forceDefaults: true);
		if (material == null)
			return;

		Debug.Log(
			"M_GoldPile_Stylized ready at " + StylizedMaterialPath
			+ ". Assign it on TreasurePileVisual / pile renderer to A/B against M_GoldPile. "
			+ "Does not overwrite the production material assignment.");
	}

	[MenuItem("DragonLoot/Graphics/Apply Gold Pile Stylized To Selection")]
	public static void MenuApplyStylizedToSelection()
	{
		Material material = EnsureStylizedMaterial(forceDefaults: false);
		if (material == null)
			return;

		int assigned = 0;

		Object[] selectedAssets = Selection.objects;
		for (int i = 0; i < selectedAssets.Length; i++)
		{
			TreasurePileDefinition definition = selectedAssets[i] as TreasurePileDefinition;
			if (definition == null)
				continue;

			Undo.RecordObject(definition, "Apply Gold Pile Stylized");
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
				Undo.RecordObject(visual, "Apply Gold Pile Stylized");
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

				Undo.RecordObject(renderer, "Apply Gold Pile Stylized");
				renderer.sharedMaterial = material;
				if (renderer.GetComponent<GoldPileQualityBinder>() == null)
					Undo.AddComponent<GoldPileQualityBinder>(renderer.gameObject);
				assigned++;
			}
		}

		AssetDatabase.SaveAssets();
		Debug.Log("Applied M_GoldPile_Stylized to " + assigned + " target(s).");
	}

	[MenuItem("DragonLoot/Graphics/Install Gold Pile Procedural Material (AB)")]
	public static void MenuInstallProcedural()
	{
		Material material = EnsureProceduralMaterial(forceDefaults: true);
		if (material == null)
			return;

		Debug.Log(
			"M_GoldPile_Procedural ready at " + ProceduralMaterialPath
			+ ". Assign it on TreasurePileVisual / pile renderer to A/B against M_GoldPile. "
			+ "Does not overwrite the production material assignment.");
	}

	[MenuItem("DragonLoot/Graphics/Apply Gold Pile Procedural To Selection")]
	public static void MenuApplyProceduralToSelection()
	{
		Material material = EnsureProceduralMaterial(forceDefaults: false);
		if (material == null)
			return;

		int assigned = ApplyGoldPileMaterialToSelection(material, "Apply Gold Pile Procedural");
		AssetDatabase.SaveAssets();
		Debug.Log("Applied M_GoldPile_Procedural to " + assigned + " target(s).");
	}

	[MenuItem("DragonLoot/Graphics/Wire Dynamic Gold Pile Prefab")]
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
		Material surface = AssetDatabase.LoadAssetAtPath<Material>(StylizedMaterialPath);
		if (surface == null)
			surface = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
		EnsureLootStreamSettings();
		WireDynamicPilePrefab(surface);
	}

	[MenuItem("DragonLoot/Graphics/Install Loot Stream Settings")]
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

	[MenuItem("DragonLoot/Graphics/Reset Gold Pile Material Defaults")]
	public static void MenuResetDefaults()
	{
		Shader shader = Shader.Find(ShaderName);
		if (shader == null)
		{
			Debug.LogWarning("Gold Pile shader not found: " + ShaderName);
			return;
		}

		Material material = LoadOrCreateMaterial(shader, applyDefaults: true);
		AssignMapsIfMissing(material, forceOverwrite: true);
		SetDefaults(material);
		GoldPileQuality.Apply(material);
		EditorUtility.SetDirty(material);
		AssetDatabase.SaveAssets();
		Debug.Log("M_GoldPile defaults reset.");
	}

	/// <summary>
	/// Soft repair: fix shader ref, fill null textures only, do not stomp floats.
	/// </summary>
	public static void TryInstall(bool forceAssignTargets = false)
	{
		Shader shader = Shader.Find(ShaderName);
		if (shader == null)
			return;

		Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
		bool created = false;
		if (material == null)
		{
			material = LoadOrCreateMaterial(shader, applyDefaults: true);
			created = true;
		}
		else if (material.shader != shader)
		{
			material.shader = shader;
			EditorUtility.SetDirty(material);
		}

		AssignMapsIfMissing(material, forceOverwrite: created);
		GoldPileQuality.Apply(material);
		EditorUtility.SetDirty(material);

		if (forceAssignTargets || created)
		{
			EnsureLootStreamSettings();
			AssignToPrefab(material);
			AssignToOpenScenes(material);
			WireDynamicPilePrefab(material);
		}

		AssetDatabase.SaveAssets();
	}

	static Material LoadOrCreateMaterial(Shader shader, bool applyDefaults)
	{
		Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
		if (material != null)
			return material;

		material = new Material(shader);
		material.name = "M_GoldPile";
		AssetDatabase.CreateAsset(material, MaterialPath);
		AssignMapsIfMissing(material, forceOverwrite: true);
		if (applyDefaults)
			SetDefaults(material);
		GoldPileQuality.Apply(material);
		EditorUtility.SetDirty(material);
		return material;
	}

	static Material EnsureStylizedMaterial(bool forceDefaults)
	{
		Shader shader = Shader.Find(StylizedShaderName);
		if (shader == null)
		{
			Debug.LogWarning("Gold Pile Stylized shader not found: " + StylizedShaderName
				+ ". Wait for Unity to import DragonLoot_GoldPileStylized.shader.");
			return null;
		}

		Material material = AssetDatabase.LoadAssetAtPath<Material>(StylizedMaterialPath);
		bool created = false;
		if (material == null)
		{
			material = new Material(shader);
			material.name = "M_GoldPile_Stylized";
			AssetDatabase.CreateAsset(material, StylizedMaterialPath);
			created = true;
		}
		else if (material.shader != shader)
		{
			material.shader = shader;
		}

		AssignMapsIfMissing(material, forceOverwrite: created || forceDefaults);
		if (created || forceDefaults)
			SetStylizedDefaults(material);
		GoldPileQuality.Apply(material);
		EditorUtility.SetDirty(material);
		AssetDatabase.SaveAssets();
		return material;
	}

	static Material EnsureProceduralMaterial(bool forceDefaults)
	{
		Shader shader = Shader.Find(ProceduralShaderName);
		if (shader == null)
		{
			Debug.LogWarning("Gold Pile Procedural shader not found: " + ProceduralShaderName
				+ ". Wait for Unity to import DragonLoot_GoldPileProcedural.shader.");
			return null;
		}

		Material material = AssetDatabase.LoadAssetAtPath<Material>(ProceduralMaterialPath);
		bool created = false;
		if (material == null)
		{
			material = new Material(shader);
			material.name = "M_GoldPile_Procedural";
			AssetDatabase.CreateAsset(material, ProceduralMaterialPath);
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

	static void AssignMapsIfMissing(Material material, bool forceOverwrite)
	{
		Texture2D albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(GoldBc);
		Texture2D bump = AssetDatabase.LoadAssetAtPath<Texture2D>(Normal);
		Texture2D mask = AssetDatabase.LoadAssetAtPath<Texture2D>(MetallicSmooth);
		Texture2D ao = AssetDatabase.LoadAssetAtPath<Texture2D>(Occlusion);

		if (albedo != null && (forceOverwrite || material.GetTexture("_BaseMap") == null))
			material.SetTexture("_BaseMap", albedo);
		if (bump != null)
		{
			if (forceOverwrite || material.GetTexture("_BumpMap") == null)
				material.SetTexture("_BumpMap", bump);
			if (forceOverwrite || material.GetTexture("_PileNormalMap") == null)
				material.SetTexture("_PileNormalMap", bump);
		}
		if (mask != null && (forceOverwrite || material.GetTexture("_MetallicGlossMap") == null))
			material.SetTexture("_MetallicGlossMap", mask);
		if (ao != null)
		{
			if (forceOverwrite || material.GetTexture("_OcclusionMap") == null)
				material.SetTexture("_OcclusionMap", ao);
			if (material.HasProperty("_HeightMap")
				&& (forceOverwrite || material.GetTexture("_HeightMap") == null))
				material.SetTexture("_HeightMap", ao);
		}
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

	static void SetDefaults(Material material)
	{
		material.SetColor("_BaseColor", new Color(1f, 0.84f, 0.3f, 1f));
		material.SetFloat("_Metallic", 1f);
		material.SetFloat("_Smoothness", 0.85f);
		material.SetFloat("_BumpScale", 1f);
		material.SetFloat("_CoinNormalStrength", 1f);
		material.SetFloat("_PileNormalStrength", 0.35f);
		material.SetFloat("_OcclusionStrength", 1f);
		material.SetFloat("_HeightScale", 0f);
		material.SetFloat("_HeightAmount", 0f);
		material.SetFloat("_DeformEnabled", 0f);
		material.SetFloat("_DeformScale", 1.5f);
		material.SetFloat("_DeformWorldSize", 4f);
		material.SetFloat("_DeformResolution", 64f);
		material.SetFloat("_Parallax", 0.03f);
		material.SetFloat("_POMSteps", 12f);
		material.SetFloat("_WorldTiling", 0.35f);
		material.SetFloat("_TriplanarSharpness", 4f);
		material.SetFloat("_LodNear", 12f);
		material.SetFloat("_LodFar", 35f);
		material.SetFloat("_PomEnabled", 1f);
		material.SetFloat("_TriplanarEnabled", 1f);
		material.SetFloat("_SparkleEnabled", 0f);
		material.SetFloat("_DetailEnabled", 0f);

		material.EnableKeyword("_POM_ON");
		material.EnableKeyword("_TRIPLANAR");
		material.DisableKeyword("_SPARKLE_ON");
		material.DisableKeyword("_DETAIL_ON");
		GoldPileQuality.Apply(material);
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

		material.DisableKeyword("_POM_ON");
		material.DisableKeyword("_HEIGHTMAP_ON");
		GoldPileQuality.Apply(material);
	}

	static void SetStylizedDefaults(Material material)
	{
		material.SetColor("_BaseColor", new Color(1f, 0.78f, 0.28f, 1f));
		material.SetFloat("_Metallic", 0.72f);
		material.SetFloat("_Smoothness", 0.45f);
		material.SetFloat("_BumpScale", 1f);
		material.SetFloat("_CoinNormalStrength", 1f);
		material.SetFloat("_PileNormalStrength", 0.3f);
		material.SetFloat("_OcclusionStrength", 1f);
		material.SetFloat("_DeformEnabled", 0f);
		material.SetFloat("_DeformScale", 1.5f);
		material.SetFloat("_DeformWorldSize", 4f);
		material.SetFloat("_DeformResolution", 64f);
		material.SetFloat("_WorldTiling", 0.42f);
		material.SetFloat("_HueVariation", 0.04f);
		material.SetFloat("_BrightnessVariation", 0.12f);
		material.SetFloat("_RoughnessVariation", 0.15f);
		material.SetFloat("_EdgeDirtStrength", 1.1f);
		material.SetFloat("_DetailEnabled", 0f);

		material.SetFloat("_StylizedMetalSoftness", 0.55f);
		material.SetFloat("_StylizedContrast", 1.15f);
		material.SetFloat("_StylizedSaturation", 1.25f);
		material.SetColor("_StylizedWarmTint", new Color(1f, 0.82f, 0.4f, 1f));
		material.SetFloat("_StylizedWarmStrength", 0.45f);
		material.SetFloat("_AlbedoMix", 0.35f);
		material.SetColor("_RimColor", new Color(1.4f, 1.05f, 0.45f, 1f));
		material.SetFloat("_RimPower", 2.5f);
		material.SetFloat("_RimIntensity", 0.85f);
		material.SetColor("_SpecRampColor", new Color(1.6f, 1.35f, 0.7f, 1f));
		material.SetFloat("_SpecRampThreshold", 0.68f);
		material.SetFloat("_SpecRampSoftness", 0.1f);
		material.SetFloat("_SpecRampIntensity", 1.1f);
		material.SetColor("_CreviceColor", new Color(0.42f, 0.18f, 0.06f, 1f));
		material.SetFloat("_CreviceStrength", 0.9f);

		material.SetFloat("_DistantCoinShineEnabled", 0f);
		material.SetFloat("_DistantCoinShineIntensity", 1f);
		material.SetFloat("_DistantCoinShineMetallic", 0.9f);
		material.SetFloat("_DistantCoinShineSmoothness", 0.75f);
		material.SetFloat("_DistantCoinShineNormalStrength", 1f);
		material.DisableKeyword("_DETAIL_ON");
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
			EnsureRootCollider(prefabRoot);

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

			GoldPilePhysicsPool physics = prefabRoot.GetComponent<GoldPilePhysicsPool>();
			if (physics != null)
				Object.DestroyImmediate(physics);
			GoldPileEdgeProps edges = prefabRoot.GetComponent<GoldPileEdgeProps>();
			if (edges != null)
				Object.DestroyImmediate(edges);
			GoldPileInstanceScatter scatter = prefabRoot.GetComponent<GoldPileInstanceScatter>();
			if (scatter != null)
				Object.DestroyImmediate(scatter);

			GoldPileLootStreamSettings streamSettings = EnsureLootStreamSettings();

			SerializedObject so = new SerializedObject(visual);
			if (pileMaterial != null)
				so.FindProperty("pileMaterial").objectReferenceValue = pileMaterial;
			so.FindProperty("lootInstances").objectReferenceValue = loot;
			SerializedProperty carveRadiusProp = so.FindProperty("carveRadius");
			if (carveRadiusProp != null)
				carveRadiusProp.floatValue = 0.55f;
			SerializedProperty carveScale = so.FindProperty("carveVolumeScale");
			if (carveScale != null)
				carveScale.floatValue = 0.02f;

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

		// Migrate / replace obsolete shell settings asset if present.
		const string ObsoleteShellSettings = "Assets/Materials/Shaders/GoldPile/GoldPileCoinShellSettings.asset";
		if (AssetDatabase.LoadAssetAtPath<Object>(ObsoleteShellSettings) != null)
			AssetDatabase.DeleteAsset(ObsoleteShellSettings);

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

	static void EnsureRootCollider(GameObject root)
	{
		if (root.GetComponent<Collider>() != null)
			return;

		MeshCollider meshCollider = root.AddComponent<MeshCollider>();
		meshCollider.convex = false;

		MeshFilter ownFilter = root.GetComponent<MeshFilter>();
		if (ownFilter != null && ownFilter.sharedMesh != null)
		{
			meshCollider.sharedMesh = ownFilter.sharedMesh;
			return;
		}

		MeshFilter[] childFilters = root.GetComponentsInChildren<MeshFilter>(true);
		for (int i = 0; i < childFilters.Length; i++)
		{
			MeshFilter filter = childFilters[i];
			if (filter == null || filter.sharedMesh == null)
				continue;

			meshCollider.sharedMesh = filter.sharedMesh;
			return;
		}

		// Placeholder until GoldPileTerrainMesh builds the runtime collider mesh.
		meshCollider.sharedMesh = null;
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
