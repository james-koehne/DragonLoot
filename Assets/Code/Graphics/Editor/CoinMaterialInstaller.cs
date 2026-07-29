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
	const string StackMultiShaderName = "DragonLoot/Coin Stack Multi";
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
	const string MultiStackMatPath = MaterialFolder + "/M_MultiCoinStack.mat";
	const string AlbedoArrayPath = MaterialFolder + "/TA_CoinStack_Albedo.asset";
	const string NormalArrayPath = MaterialFolder + "/TA_CoinStack_Normal.asset";
	const string MaskArrayPath = MaterialFolder + "/TA_CoinStack_Mask.asset";

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
	public static string MultiStackMaterialPath => MultiStackMatPath;
	public static string StackVisualDefinitionAssetPath => StackVisualDefinitionPath;

	static readonly Color GoldFresnel = new Color(1f, 0.82f, 0.45f, 1f);
	static readonly Color SilverFresnel = new Color(0.85f, 0.9f, 1f, 1f);
	static readonly Color CopperFresnel = new Color(1f, 0.55f, 0.3f, 1f);

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
				&& AssetDatabase.LoadAssetAtPath<Material>(MultiStackMatPath) != null
				&& AssetDatabase.LoadAssetAtPath<CoinStackVisualDefinition>(StackVisualDefinitionPath) != null)
				return;
			if (Shader.Find(HeldShaderName) == null
				|| Shader.Find(PileShaderName) == null
				|| Shader.Find(StackShaderName) == null
				|| Shader.Find(StackMultiShaderName) == null)
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
		Shader stackMultiShader = Shader.Find(StackMultiShaderName);
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

		if (stackMultiShader == null)
		{
			Debug.LogWarning("Coin Stack Multi shader not found: " + StackMultiShaderName + " (wait for Unity import, then re-run).");
			return;
		}

		EnsureMaterialFolder();

		Material gold = CreateOrUpdateHeld(
			GoldMatPath, heldShader, GoldSrcMat,
			new Color(1f, 0.9f, 0.45f, 1f),
			GoldFresnel,
			applyDefaults);
		Material silver = CreateOrUpdateHeld(
			SilverMatPath, heldShader, SilverSrcMat,
			new Color(0.92f, 0.94f, 0.98f, 1f),
			SilverFresnel,
			applyDefaults);
		Material copper = CreateOrUpdateHeld(
			CopperMatPath, heldShader, CopperSrcMat,
			new Color(1f, 0.62f, 0.38f, 1f),
			CopperFresnel,
			applyDefaults);

		CreateOrUpdatePile(
			GoldPileMatPath, pileShader, GoldSrcMat,
			new Color(1f, 0.9f, 0.45f, 1f),
			GoldFresnel,
			applyDefaults);
		CreateOrUpdatePile(
			SilverPileMatPath, pileShader, SilverSrcMat,
			new Color(0.92f, 0.94f, 0.98f, 1f),
			SilverFresnel,
			applyDefaults);
		CreateOrUpdatePile(
			CopperPileMatPath, pileShader, CopperSrcMat,
			new Color(1f, 0.62f, 0.38f, 1f),
			CopperFresnel,
			applyDefaults);

		Material goldStack = CreateOrUpdateStack(
			GoldStackMatPath, stackShader, GoldSrcMat,
			new Color(1f, 0.9f, 0.45f, 1f),
			GoldFresnel,
			applyDefaults);
		Material silverStack = CreateOrUpdateStack(
			SilverStackMatPath, stackShader, SilverSrcMat,
			new Color(0.92f, 0.94f, 0.98f, 1f),
			SilverFresnel,
			applyDefaults);
		Material copperStack = CreateOrUpdateStack(
			CopperStackMatPath, stackShader, CopperSrcMat,
			new Color(1f, 0.62f, 0.38f, 1f),
			CopperFresnel,
			applyDefaults);

		CreateOrUpdateMultiStack(stackMultiShader, goldStack, silverStack, copperStack, applyDefaults);

		WireCoinStackVisualDefinition(applyDefaults);

		if (forceAssignPrefabs)
		{
			AssignToVisual(GoldVisual, gold);
			AssignToVisual(SilverVisual, silver);
			AssignToVisual(CopperVisual, copper);
			WireGoldPileLootPileMaterials();
		}

		AssetDatabase.SaveAssets();
		Debug.Log("Coin materials installed: held + pile + stack + multi stack + CoinStackVisualDefinition.");
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
		Material multiStack = AssetDatabase.LoadAssetAtPath<Material>(MultiStackMatPath);
		Mesh stackMesh = LoadStackCoinMesh();

		SerializedObject so = new SerializedObject(definition);
		so.FindProperty("goldStackMaterial").objectReferenceValue = goldStack;
		so.FindProperty("silverStackMaterial").objectReferenceValue = silverStack;
		so.FindProperty("copperStackMaterial").objectReferenceValue = copperStack;
		SerializedProperty multiProp = so.FindProperty("multiStackMaterial");
		if (multiProp != null)
			multiProp.objectReferenceValue = multiStack;
		if (stackMesh != null)
			so.FindProperty("stackMesh").objectReferenceValue = stackMesh;

		if (applyDefaults)
		{
			so.FindProperty("diameterScale").floatValue = 1f;
			so.FindProperty("heightLerpSpeed").floatValue = 18f;
			so.FindProperty("minCountForCylinder").intValue = 2;
			so.FindProperty("meshReferenceDiameter").floatValue = 0f;
			so.FindProperty("meshReferenceHeight").floatValue = 0f;
			SerializedProperty goldIndex = so.FindProperty("goldTypeIndex");
			if (goldIndex != null)
				goldIndex.intValue = 0;
			SerializedProperty silverIndex = so.FindProperty("silverTypeIndex");
			if (silverIndex != null)
				silverIndex.intValue = 1;
			SerializedProperty copperIndex = so.FindProperty("copperTypeIndex");
			if (copperIndex != null)
				copperIndex.intValue = 2;
			SerializedProperty typeCount = so.FindProperty("typeCount");
			if (typeCount != null)
				typeCount.intValue = 3;
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
		Material goldPile = AssetDatabase.LoadAssetAtPath<Material>(GoldPileMatPath);
		Material silverPile = AssetDatabase.LoadAssetAtPath<Material>(SilverPileMatPath);
		Material copperPile = AssetDatabase.LoadAssetAtPath<Material>(CopperPileMatPath);
		if (goldPile == null && silverPile == null && copperPile == null)
			return;

		if (goldPile != null)
		{
			goldPile.enableInstancing = true;
			EditorUtility.SetDirty(goldPile);
		}
		if (silverPile != null)
		{
			silverPile.enableInstancing = true;
			EditorUtility.SetDirty(silverPile);
		}
		if (copperPile != null)
		{
			copperPile.enableInstancing = true;
			EditorUtility.SetDirty(copperPile);
		}

		// Open scenes (Level CoinPile objects live here, not on the removed ThirdParty prefab).
		GoldPileLootInstances[] sceneLoot = Object.FindObjectsByType<GoldPileLootInstances>(
			FindObjectsInactive.Include,
			FindObjectsSortMode.None );
		for (int i = 0; i < sceneLoot.Length; i++)
			ApplyPileMaterials(sceneLoot[i], goldPile, silverPile, copperPile);

		// Prefabs under Assets that still host GoldPileLootInstances.
		string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" });
		for (int i = 0; i < prefabGuids.Length; i++)
		{
			string path = AssetDatabase.GUIDToAssetPath(prefabGuids[i]);
			if (string.IsNullOrEmpty(path) || path.StartsWith("Assets/ThirdParty", System.StringComparison.OrdinalIgnoreCase))
				continue;

			GameObject prefabRoot = PrefabUtility.LoadPrefabContents(path);
			if (prefabRoot == null)
				continue;

			try
			{
				GoldPileLootInstances[] lootOnPrefab = prefabRoot.GetComponentsInChildren<GoldPileLootInstances>(true);
				if (lootOnPrefab == null || lootOnPrefab.Length == 0)
					continue;

				bool dirty = false;
				for (int l = 0; l < lootOnPrefab.Length; l++)
				{
					if (ApplyPileMaterials(lootOnPrefab[l], goldPile, silverPile, copperPile))
						dirty = true;
				}

				if (dirty)
					PrefabUtility.SaveAsPrefabAsset(prefabRoot, path);
			}
			finally
			{
				PrefabUtility.UnloadPrefabContents(prefabRoot);
			}
		}
	}

	static bool ApplyPileMaterials(
		GoldPileLootInstances loot,
		Material goldPile,
		Material silverPile,
		Material copperPile )
	{
		if (loot == null)
			return false;

		SerializedObject lootSo = new SerializedObject(loot);
		bool changed = false;

		SerializedProperty pileProp = lootSo.FindProperty("pileMaterial");
		SerializedProperty fallbackProp = lootSo.FindProperty("fallbackMaterial");
		SerializedProperty silverProp = lootSo.FindProperty("silverPileMaterial");
		SerializedProperty copperProp = lootSo.FindProperty("copperPileMaterial");

		if (pileProp != null && goldPile != null && pileProp.objectReferenceValue == null)
		{
			pileProp.objectReferenceValue = goldPile;
			changed = true;
		}

		if (fallbackProp != null && goldPile != null && fallbackProp.objectReferenceValue == null)
		{
			fallbackProp.objectReferenceValue = goldPile;
			changed = true;
		}

		if (silverProp != null && silverPile != null && silverProp.objectReferenceValue == null)
		{
			silverProp.objectReferenceValue = silverPile;
			changed = true;
		}

		if (copperProp != null && copperPile != null && copperProp.objectReferenceValue == null)
		{
			copperProp.objectReferenceValue = copperPile;
			changed = true;
		}

		if (!changed)
			return false;

		lootSo.ApplyModifiedPropertiesWithoutUndo();
		EditorUtility.SetDirty(loot);
		return true;
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

	static void CreateOrUpdateMultiStack(
		Shader multiShader,
		Material goldStack,
		Material silverStack,
		Material copperStack,
		bool applyDefaults)
	{
		Texture2D[] albedos =
		{
			GetMaterialTexture(goldStack, "_BaseMap"),
			GetMaterialTexture(silverStack, "_BaseMap"),
			GetMaterialTexture(copperStack, "_BaseMap")
		};
		Texture2D[] normals =
		{
			GetMaterialTexture(goldStack, "_BumpMap"),
			GetMaterialTexture(silverStack, "_BumpMap"),
			GetMaterialTexture(copperStack, "_BumpMap")
		};
		Texture2D[] masks =
		{
			GetMaterialTexture(goldStack, "_MetallicGlossMap"),
			GetMaterialTexture(silverStack, "_MetallicGlossMap"),
			GetMaterialTexture(copperStack, "_MetallicGlossMap")
		};

		Texture2DArray albedoArray = CreateOrUpdateTextureArrayCompatible(AlbedoArrayPath, "TA_CoinStack_Albedo", albedos, linear: false);
		Texture2DArray normalArray = CreateOrUpdateTextureArrayCompatible(NormalArrayPath, "TA_CoinStack_Normal", normals, linear: true);
		Texture2DArray maskArray = CreateOrUpdateTextureArrayCompatible(MaskArrayPath, "TA_CoinStack_Mask", masks, linear: true);

		Material material = AssetDatabase.LoadAssetAtPath<Material>(MultiStackMatPath);
		bool created = false;
		if (material == null)
		{
			material = new Material(multiShader);
			material.name = "M_MultiCoinStack";
			AssetDatabase.CreateAsset(material, MultiStackMatPath);
			created = true;
			applyDefaults = true;
		}
		else if (material.shader != multiShader)
		{
			material.shader = multiShader;
			EditorUtility.SetDirty(material);
		}

		if (albedoArray != null)
			material.SetTexture("_BaseMapArray", albedoArray);
		if (normalArray != null)
			material.SetTexture("_BumpMapArray", normalArray);
		if (maskArray != null)
			material.SetTexture("_MetallicGlossMapArray", maskArray);

		Vector4[] fresnels =
		{
			GoldFresnel,
			SilverFresnel,
			CopperFresnel,
			GoldFresnel,
			GoldFresnel,
			GoldFresnel,
			GoldFresnel,
			GoldFresnel
		};
		material.SetVectorArray("_TypeFresnelColor", fresnels);
		material.SetFloat("_TypeCount", 3f);

		if (applyDefaults || created)
			SetStackDefaults(material, Color.white, GoldFresnel);

		material.enableInstancing = true;
		EditorUtility.SetDirty(material);
	}

	static Texture2D GetMaterialTexture(Material material, string property)
	{
		if (material == null || !material.HasProperty(property))
			return null;
		return material.GetTexture(property) as Texture2D;
	}

	static Texture2DArray CreateOrUpdateTextureArray(string path, string name, Texture2D[] slices, bool linear)
	{
		if (slices == null || slices.Length == 0)
			return null;

		Texture2D first = null;
		for (int i = 0; i < slices.Length; i++)
		{
			if (slices[i] != null)
			{
				first = slices[i];
				break;
			}
		}

		if (first == null)
		{
			Debug.LogWarning("Coin stack texture array '" + name + "' skipped — no source slices.");
			return null;
		}

		int width = first.width;
		int height = first.height;
		int depth = slices.Length;
		TextureFormat format = first.format;
		bool hasMips = first.mipmapCount > 1;

		for (int i = 0; i < slices.Length; i++)
		{
			Texture2D slice = slices[i];
			if (slice == null)
			{
				Debug.LogWarning("Coin stack texture array '" + name + "' missing slice " + i);
				return null;
			}

			if (slice.width != width || slice.height != height)
			{
				Debug.LogWarning(
					"Coin stack texture array '" + name + "' slice size mismatch at " + i
					+ " (" + slice.width + "x" + slice.height + " vs " + width + "x" + height + ").");
				return null;
			}

			if (slice.format != format)
			{
				Debug.LogWarning(
					"Coin stack texture array '" + name + "' slice format mismatch at " + i
					+ " (" + slice.format + " vs " + format + ").");
				return null;
			}
		}

		Texture2DArray array = AssetDatabase.LoadAssetAtPath<Texture2DArray>(path);
		bool recreate = array == null
			|| array.width != width
			|| array.height != height
			|| array.depth != depth
			|| array.format != format;

		if (recreate)
		{
			array = new Texture2DArray(width, height, depth, format, hasMips, linear)
			{
				name = name,
				filterMode = first.filterMode,
				wrapMode = first.wrapMode,
				anisoLevel = first.anisoLevel
			};

			if (AssetDatabase.LoadAssetAtPath<Texture2DArray>(path) != null)
				AssetDatabase.DeleteAsset(path);
			AssetDatabase.CreateAsset(array, path);
		}

		for (int i = 0; i < depth; i++)
		{
			Texture2D slice = slices[i];
			int mipCount = Mathf.Min(array.mipmapCount, slice.mipmapCount);
			for (int mip = 0; mip < mipCount; mip++)
				Graphics.CopyTexture(slice, 0, mip, array, i, mip);
		}

		EditorUtility.SetDirty(array);
		return array;
	}

	static Texture2DArray CreateOrUpdateTextureArrayCompatible(
		string path,
		string name,
		Texture2D[] slices,
		bool linear)
	{
		Texture2DArray array = CreateOrUpdateTextureArray(path, name, slices, linear);
		if (array != null)
			return array;

		// Fallback: blit into a shared RGBA32 array when compressed formats or sizes differ.
		return CreateOrUpdateTextureArrayViaBlit(path, name, slices, linear);
	}

	static Texture2DArray CreateOrUpdateTextureArrayViaBlit(string path, string name, Texture2D[] slices, bool linear)
	{
		if (slices == null || slices.Length == 0)
			return null;

		int width = 0;
		int height = 0;
		for (int i = 0; i < slices.Length; i++)
		{
			if (slices[i] == null)
				return null;
			width = Mathf.Max(width, slices[i].width);
			height = Mathf.Max(height, slices[i].height);
		}

		if (width <= 0 || height <= 0)
			return null;

		int depth = slices.Length;
		Texture2DArray array = new Texture2DArray(width, height, depth, TextureFormat.RGBA32, true, linear)
		{
			name = name,
			filterMode = FilterMode.Bilinear,
			wrapMode = TextureWrapMode.Repeat,
			anisoLevel = 1
		};

		RenderTexture rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, linear
			? RenderTextureReadWrite.Linear
			: RenderTextureReadWrite.sRGB);
		Texture2D readback = new Texture2D(width, height, TextureFormat.RGBA32, false, linear);

		try
		{
			for (int i = 0; i < depth; i++)
			{
				Graphics.Blit(slices[i], rt);
				RenderTexture prev = RenderTexture.active;
				RenderTexture.active = rt;
				readback.ReadPixels(new Rect(0, 0, width, height), 0, 0);
				readback.Apply(false, false);
				RenderTexture.active = prev;
				array.SetPixels(readback.GetPixels(), i);
			}

			array.Apply(true, true);
		}
		finally
		{
			RenderTexture.ReleaseTemporary(rt);
			Object.DestroyImmediate(readback);
		}

		if (AssetDatabase.LoadAssetAtPath<Texture2DArray>(path) != null)
			AssetDatabase.DeleteAsset(path);
		AssetDatabase.CreateAsset(array, path);
		EditorUtility.SetDirty(array);
		return array;
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
		material.SetFloat("_FresnelIntensity", 0.35f);
		material.SetFloat("_FresnelPower", 4f);
		material.SetFloat("_ShineBoost", 0.35f);
		material.SetFloat("_SpecularIntensity", 0.75f);
		material.SetFloat("_SpecularPower", 64f);
		material.SetFloat("_SparkleEnabled", 1f);
		material.SetColor("_SparkleColor", new Color(1f, 0.92f, 0.65f, 1f));
		material.SetFloat("_SparkleIntensity", 1.5f);
		material.SetFloat("_SparkleDensity", 14f);
		material.SetFloat("_SparkleSharpness", 48f);
		material.SetFloat("_SparkleSpeed", 2f);
		material.SetFloat("_SparkleCoverage", 0.3f);
		material.SetFloat("_SparkleFlicker", 0.45f);
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
