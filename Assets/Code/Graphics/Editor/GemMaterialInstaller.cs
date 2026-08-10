#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Creates DragonLoot stylized gem materials and assigns them to Addressable gem visuals.
/// Six cut-style presets share DragonLoot/Gem; differences are material parameters only.
/// </summary>
public static class GemMaterialInstaller
{
	const string ShaderName = "DragonLoot/Gem";
	const string MaterialFolder = "Assets/Materials/Shaders/Gem";

	const string RoundMatPath = MaterialFolder + "/M_Gem_Round.mat";
	const string EmeraldMatPath = MaterialFolder + "/M_Gem_Emerald.mat";
	const string RubyMatPath = MaterialFolder + "/M_Gem_Ruby.mat";
	const string SapphireMatPath = MaterialFolder + "/M_Gem_Sapphire.mat";
	const string DiamondMatPath = MaterialFolder + "/M_Gem_Diamond.mat";
	const string CrystalMatPath = MaterialFolder + "/M_Gem_Crystal.mat";

	const string EmeraldVisual = "Assets/Addressables/Treasure/EmeraldVisual.prefab";
	const string SapphireVisual = "Assets/Addressables/Treasure/SapphireVisual.prefab";
	const string DiamondVisual = "Assets/Addressables/Treasure/DiamondVisual.prefab";

	struct GemStyleDefaults
	{
		public Color baseColor;
		public Color internalColor;
		public Color fresnelColor;
		public float transparency;
		public float smoothness;
		public float ior;
		public float thickness;
		public float absorption;
		public float fresnelIntensity;
		public float fresnelPower;
		public float refractionStrength;
		public float distortionAmount;
		public float sparkleIntensity;
		public float sparkleDensity;
		public float sparkleSharpness;
		public float inclusionStrength;
		public float reflectionFloor;
	}

	[InitializeOnLoadMethod]
	static void AutoInstallIfMissing()
	{
		EditorApplication.delayCall += () =>
		{
			if (EditorApplication.isPlayingOrWillChangePlaymode)
				return;
			if (AssetDatabase.LoadAssetAtPath<Material>(EmeraldMatPath) != null)
				return;
			if (Shader.Find(ShaderName) == null)
				return;

			TryInstall(forceAssignPrefabs: true, applyDefaults: true);
		};
	}

	[MenuItem(DragonLootMenus.GraphicsGemsInstall)]
	public static void MenuInstall()
	{
		TryInstall(forceAssignPrefabs: true, applyDefaults: false);
	}

	[MenuItem(DragonLootMenus.GraphicsGemsReset)]
	public static void MenuResetDefaults()
	{
		TryInstall(forceAssignPrefabs: true, applyDefaults: true);
		Debug.Log("Gem material defaults reset and Addressable visuals wired.");
	}

	public static void TryInstall(bool forceAssignPrefabs, bool applyDefaults)
	{
		Shader shader = Shader.Find(ShaderName);
		if (shader == null)
		{
			Debug.LogWarning("Gem shader not found: " + ShaderName + " (wait for Unity import, then re-run).");
			return;
		}

		EnsureMaterialFolder();

		Material round = CreateOrUpdate(RoundMatPath, shader, RoundDefaults(), applyDefaults);
		Material emerald = CreateOrUpdate(EmeraldMatPath, shader, EmeraldDefaults(), applyDefaults);
		Material ruby = CreateOrUpdate(RubyMatPath, shader, RubyDefaults(), applyDefaults);
		Material sapphire = CreateOrUpdate(SapphireMatPath, shader, SapphireDefaults(), applyDefaults);
		Material diamond = CreateOrUpdate(DiamondMatPath, shader, DiamondDefaults(), applyDefaults);
		Material crystal = CreateOrUpdate(CrystalMatPath, shader, CrystalDefaults(), applyDefaults);

		if (forceAssignPrefabs)
		{
			AssignToVisual(EmeraldVisual, emerald);
			AssignToVisual(SapphireVisual, sapphire);
			AssignToVisual(DiamondVisual, diamond);
		}

		AssetDatabase.SaveAssets();
		Debug.Log(
			"Stylized gem materials installed (Round / Emerald / Ruby / Sapphire / Diamond / Crystal): "
			+ round.name + ", " + emerald.name + ", " + ruby.name + ", "
			+ sapphire.name + ", " + diamond.name + ", " + crystal.name + ".");
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
			AssetDatabase.CreateFolder("Assets/Materials/Shaders", "Gem");
	}

	static Material CreateOrUpdate(string path, Shader shader, GemStyleDefaults defaults, bool applyDefaults)
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

		material.enableInstancing = true;

		if (applyDefaults || created)
			SetDefaults(material, defaults);

		EditorUtility.SetDirty(material);
		return material;
	}

	static void SetDefaults(Material material, GemStyleDefaults d)
	{
		material.SetColor("_BaseColor", d.baseColor);
		material.SetFloat("_Transparency", d.transparency);
		material.SetFloat("_Metallic", 0.05f);
		material.SetFloat("_Smoothness", d.smoothness);
		material.SetFloat("_BumpScale", 1f);
		material.SetFloat("_OcclusionStrength", 1f);
		material.SetFloat("_IOR", d.ior);
		material.SetFloat("_Thickness", d.thickness);
		material.SetFloat("_Absorption", d.absorption);
		material.SetColor("_InternalColor", d.internalColor);
		material.SetFloat("_ReflectionFloor", d.reflectionFloor);
		material.SetColor("_FresnelColor", d.fresnelColor);
		material.SetFloat("_FresnelIntensity", d.fresnelIntensity);
		material.SetFloat("_FresnelPower", d.fresnelPower);
		material.SetFloat("_RefractionStrength", d.refractionStrength);
		material.SetFloat("_DistortionAmount", d.distortionAmount);
		material.SetFloat("_SparkleIntensity", 0f);
		material.SetFloat("_SparkleDensity", d.sparkleDensity);
		material.SetFloat("_SparkleSharpness", d.sparkleSharpness);
		material.SetFloat("_BrightnessVariation", 0.1f);
		material.SetFloat("_SaturationVariation", 0.1f);
		material.SetFloat("_HueVariation", 0.05f);
		material.SetFloat("_RefractionVariation", 0.1f);
		material.SetFloat("_SparkleVariation", 0.15f);
		material.SetFloat("_RoughnessUvScale", 48f);
		material.SetFloat("_RoughnessUvAmount", 0.06f);
		material.SetFloat("_ScratchesEnabled", 0f);
		material.SetFloat("_ScratchStrength", 0.25f);
		material.SetFloat("_EdgeWearEnabled", 0f);
		material.SetFloat("_EdgeWearStrength", 0.35f);
		material.SetFloat("_InclusionStrength", d.inclusionStrength);
		material.SetFloat("_SparkleMapEnabled", 0f);
		material.SetFloat("_Surface", 1f);
		material.SetFloat("_Cull", 2f);
		material.DisableKeyword("_SPARKLEMAP_ON");
		material.DisableKeyword("_SCRATCHES_ON");
		material.DisableKeyword("_EDGEWEAR_ON");
		material.enableInstancing = true;
		material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
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

	// Soft cabochon — mid-high smooth, medium fresnel/absorption/sparkle.
	static GemStyleDefaults RoundDefaults()
	{
		return new GemStyleDefaults
		{
			baseColor = new Color(0.55f, 0.78f, 0.95f, 0.82f),
			internalColor = new Color(0.25f, 0.45f, 0.7f, 1f),
			fresnelColor = new Color(0.7f, 0.9f, 1f, 1f),
			transparency = 0.72f,
			smoothness = 0.88f,
			ior = 1.52f,
			thickness = 1f,
			absorption = 1.1f,
			fresnelIntensity = 0.95f,
			fresnelPower = 3.8f,
			refractionStrength = 0.55f,
			distortionAmount = 0.9f,
			sparkleIntensity = 0.85f,
			sparkleDensity = 12f,
			sparkleSharpness = 20f,
			inclusionStrength = 0.12f,
			reflectionFloor = 0.08f
		};
	}

	// Deep green centre, cooler fresnel, stronger absorption, low sparkle.
	static GemStyleDefaults EmeraldDefaults()
	{
		return new GemStyleDefaults
		{
			baseColor = new Color(0.12f, 0.72f, 0.38f, 0.88f),
			internalColor = new Color(0.02f, 0.28f, 0.12f, 1f),
			fresnelColor = new Color(0.45f, 1f, 0.7f, 1f),
			transparency = 0.7f,
			smoothness = 0.86f,
			ior = 1.58f,
			thickness = 1.15f,
			absorption = 1.85f,
			fresnelIntensity = 1.05f,
			fresnelPower = 3.6f,
			refractionStrength = 0.5f,
			distortionAmount = 0.85f,
			sparkleIntensity = 0.45f,
			sparkleDensity = 10f,
			sparkleSharpness = 18f,
			inclusionStrength = 0.22f,
			reflectionFloor = 0.07f
		};
	}

	// Saturated warm red, strong absorption, medium sparkle.
	static GemStyleDefaults RubyDefaults()
	{
		return new GemStyleDefaults
		{
			baseColor = new Color(0.92f, 0.12f, 0.22f, 0.88f),
			internalColor = new Color(0.35f, 0.02f, 0.06f, 1f),
			fresnelColor = new Color(1f, 0.45f, 0.55f, 1f),
			transparency = 0.72f,
			smoothness = 0.93f,
			ior = 1.77f,
			thickness = 1.1f,
			absorption = 1.7f,
			fresnelIntensity = 1.15f,
			fresnelPower = 3.4f,
			refractionStrength = 0.6f,
			distortionAmount = 1f,
			sparkleIntensity = 0.95f,
			sparkleDensity = 13f,
			sparkleSharpness = 26f,
			inclusionStrength = 0.14f,
			reflectionFloor = 0.08f
		};
	}

	// Cool blue, medium-strong absorption, medium sparkle.
	static GemStyleDefaults SapphireDefaults()
	{
		return new GemStyleDefaults
		{
			baseColor = new Color(0.15f, 0.35f, 0.95f, 0.86f),
			internalColor = new Color(0.04f, 0.12f, 0.45f, 1f),
			fresnelColor = new Color(0.45f, 0.7f, 1f, 1f),
			transparency = 0.74f,
			smoothness = 0.94f,
			ior = 1.77f,
			thickness = 1.05f,
			absorption = 1.45f,
			fresnelIntensity = 1.2f,
			fresnelPower = 3.3f,
			refractionStrength = 0.62f,
			distortionAmount = 1f,
			sparkleIntensity = 1f,
			sparkleDensity = 14f,
			sparkleSharpness = 28f,
			inclusionStrength = 0.1f,
			reflectionFloor = 0.08f
		};
	}

	// Brilliant — max smooth, strong cool fresnel, low absorption, high sparkle.
	static GemStyleDefaults DiamondDefaults()
	{
		return new GemStyleDefaults
		{
			baseColor = new Color(0.92f, 0.95f, 1f, 0.65f),
			internalColor = new Color(0.75f, 0.82f, 0.95f, 1f),
			fresnelColor = new Color(0.85f, 0.95f, 1f, 1f),
			transparency = 0.55f,
			smoothness = 0.98f,
			ior = 2.42f,
			thickness = 0.85f,
			absorption = 0.45f,
			fresnelIntensity = 1.55f,
			fresnelPower = 2.8f,
			refractionStrength = 1.1f,
			distortionAmount = 1.25f,
			sparkleIntensity = 1.75f,
			sparkleDensity = 18f,
			sparkleSharpness = 36f,
			inclusionStrength = 0.05f,
			reflectionFloor = 0.1f
		};
	}

	// Clearest — high alpha, strong fresnel, low absorption, high sparkle.
	static GemStyleDefaults CrystalDefaults()
	{
		return new GemStyleDefaults
		{
			baseColor = new Color(0.85f, 0.95f, 1f, 0.45f),
			internalColor = new Color(0.7f, 0.88f, 1f, 1f),
			fresnelColor = new Color(0.9f, 1f, 1f, 1f),
			transparency = 0.4f,
			smoothness = 0.96f,
			ior = 1.54f,
			thickness = 0.75f,
			absorption = 0.35f,
			fresnelIntensity = 1.4f,
			fresnelPower = 3f,
			refractionStrength = 0.95f,
			distortionAmount = 1.1f,
			sparkleIntensity = 1.5f,
			sparkleDensity = 16f,
			sparkleSharpness = 30f,
			inclusionStrength = 0.08f,
			reflectionFloor = 0.09f
		};
	}
}
#endif
