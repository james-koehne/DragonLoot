using UnityEngine;
using UnityEngine.AddressableAssets;

[CreateAssetMenu( fileName = "GameInstanceDefinition", menuName = "Definitions/GameInstanceDefinition", order = 0 )]
public class GameInstanceDefinition : ScriptableObject
{
	public string gameDefName = "GameDefinition";
	public AssetReference funkClientRef;
	public AssetReference gameModeRef;
	public string GameLocalDataName = "LocalGameData";
}