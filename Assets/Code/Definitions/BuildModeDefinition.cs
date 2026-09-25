using UnityEngine;
using UnityEngine.AddressableAssets;

[CreateAssetMenu( fileName = "BuildModeDefinition", menuName = "Definitions/BuildModeDefinition" )]
public class BuildModeDefinition : ScriptableObject
{
	[Header( "Proximity" )]
	[Tooltip( "World radius from the player within which eligible unbuilt buildables show ghosts." )]
	[Min( 0.5f )]
	public float showRadius = 20f;

	[Header( "Ghost Distance Fade" )]
	[Tooltip( "Fragments closer than this stay fully visible." )]
	[Min( 0.1f )]
	public float fadeStart = 12f;

	[Tooltip( "Fragments farther than this are fully faded out." )]
	[Min( 0.1f )]
	public float fadeEnd = 18f;

	[Header( "Build Hold" )]
	[Tooltip( "Seconds to hold ContextualInteract while aiming a ghost to complete the build." )]
	[Min( 0.1f )]
	public float buildHoldSeconds = 2f;

	[Tooltip( "Max aim ray distance for selecting a build ghost. 0 uses the player's interact range." )]
	[Min( 0f )]
	public float aimMaxDistance = 0f;

	[Header( "Ghost Visual" )]
	[Tooltip( "Must be assigned so the ghost shader is included in player builds (Shader.Find is stripped otherwise)." )]
	public Shader ghostShader;

	public Color ghostColor = new Color( 0.35f, 0.75f, 1f, 0.4f );

	[Range( 0.5f, 8f )]
	public float ghostFresnelPower = 2.4f;

	[Range( 0f, 2f )]
	public float ghostFresnelBoost = 0.7f;

	[Range( 0f, 1f )]
	public float ghostPulseAmount = 0.12f;

	[Min( 0f )]
	public float ghostPulseSpeed = 0.85f;

	[Range( 0f, 2f )]
	public float ghostRimIntensity = 1.15f;

	[Range( 0f, 1f )]
	public float ghostCoreIntensity = 0.28f;

	[Header( "Hammer Tool" )]
	[Tooltip( "Optional Addressable hammer prefab shown in the active hand while in build mode. If empty, a simple procedural hammer is used." )]
	public AssetReferenceGameObject hammerPrefab;

	[Tooltip( "Local offset of the tool hammer under the active carry root." )]
	public Vector3 hammerLocalOffset = new Vector3( 0.05f, -0.05f, 0.08f );

	[Tooltip( "Local euler degrees for the tool hammer." )]
	public Vector3 hammerLocalEuler = new Vector3( 10f, 0f, -20f );

	[Tooltip( "Uniform scale for the tool hammer." )]
	[Min( 0.01f )]
	public float hammerLocalScale = 0.35f;

	void OnValidate()
	{
		showRadius = Mathf.Max( 0.5f, showRadius );
		fadeStart = Mathf.Max( 0.1f, fadeStart );
		fadeEnd = Mathf.Max( fadeStart + 0.01f, fadeEnd );
		buildHoldSeconds = Mathf.Max( 0.1f, buildHoldSeconds );
		aimMaxDistance = Mathf.Max( 0f, aimMaxDistance );
		hammerLocalScale = Mathf.Max( 0.01f, hammerLocalScale );
	}
}
