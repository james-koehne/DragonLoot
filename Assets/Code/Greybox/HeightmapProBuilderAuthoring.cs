using UnityEngine;

/// <summary>
/// Stores heightmap → ProBuilder bake settings on a greybox mesh so it can be rebuilt from the editor tool.
/// </summary>
[DisallowMultipleComponent]
public sealed class HeightmapProBuilderAuthoring : MonoBehaviour
{
	public HeightmapProBuilderSettings settings = new HeightmapProBuilderSettings();
}
