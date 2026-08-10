using UnityEngine;

/// <summary>
/// Quality gate for Gold Pile shaders. Forces POM off on Mobile quality.
/// Does not force POM on for PC — respects material author toggles.
/// </summary>
public static class GoldPileQuality
{
	public const string ProceduralShaderName = "DragonLoot/Gold Pile Procedural";
	public const string PomKeyword = "_POM_ON";

	/// <summary>
	/// True when the active quality tier may use POM (PC). Mobile disables it.
	/// </summary>
	public static bool AllowPom
	{
		get
		{
			string name = QualitySettings.names[QualitySettings.GetQualityLevel()];
			return !string.Equals(name, "Mobile", System.StringComparison.OrdinalIgnoreCase);
		}
	}

	public static bool IsGoldPileMaterial(Material material)
	{
		if (material == null || material.shader == null)
			return false;

		return material.shader.name == ProceduralShaderName;
	}

	/// <summary>
	/// Apply quality keyword state to a single material. Only disables POM on low tiers.
	/// </summary>
	public static void Apply(Material material)
	{
		if (!IsGoldPileMaterial(material))
			return;

		if (!AllowPom)
			material.DisableKeyword(PomKeyword);
	}

	public static void Apply(Renderer renderer)
	{
		if (renderer == null)
			return;

		Material[] materials = renderer.sharedMaterials;
		for (int i = 0; i < materials.Length; i++)
			Apply(materials[i]);
	}

	public static void ApplyAllLoaded()
	{
		Material[] materials = Resources.FindObjectsOfTypeAll<Material>();
		for (int i = 0; i < materials.Length; i++)
			Apply(materials[i]);
	}
}
