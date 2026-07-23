using UnityEngine;
using UnityEngine.AddressableAssets;

[CreateAssetMenu( fileName = "CoreDefinition", menuName = "Definitions/CoreDefinition" )]
public class CoreDefinition : ScriptableObject
{
	public DebugDefinition DebugDefinition;
	public AssetReference cameraAssetRef;
	public AssetReference playerControllerAssetRef;
	public AssetReference interfaceAssetRef;
	public AssetReference gameControllerAssetRef;
}