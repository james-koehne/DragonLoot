using UnityEngine;

/// <summary>
/// Wires game Addressables definitions into the stylized-rendering package providers.
/// </summary>
public static class StylizedRenderingGameBootstrap
{
	[RuntimeInitializeOnLoadMethod( RuntimeInitializeLoadType.SubsystemRegistration )]
	static void ResetProviders()
	{
		StylizedLightingDefinitionProvider.Resolve = null;
		AreaLightingDefinitionProvider.Resolve = null;
	}

	[RuntimeInitializeOnLoadMethod( RuntimeInitializeLoadType.BeforeSceneLoad )]
	static void WireProviders()
	{
		StylizedLightingDefinitionProvider.Resolve = () => GameInstance.GetDefinition<StylizedLightingDefinition>();
		AreaLightingDefinitionProvider.Resolve = () => GameInstance.GetDefinition<AreaLightingDefinition>();
	}
}
