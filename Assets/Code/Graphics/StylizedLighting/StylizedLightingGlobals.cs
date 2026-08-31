using UnityEngine;

/// <summary>
/// Keeps stylized lighting shader globals in sync with <see cref="StylizedLightingDefinition"/>.
/// Bootstraps defaults early, then resolves the Addressable definition when GameInstance is ready.
/// </summary>
[DefaultExecutionOrder( -100 )]
public class StylizedLightingGlobals : MonoBehaviour
{
	static StylizedLightingGlobals _instance;
	StylizedLightingDefinition _definition;
	int _lastAppliedFrame = -1;
	static float _specularIntensityMultiplier = 1f;

	public static void SetSpecularIntensityMultiplier( float multiplier )
	{
		_specularIntensityMultiplier = Mathf.Max( 0f, multiplier );
		if ( _instance != null )
			_instance.PushGlobals( force: true );
	}

	public static void ResetSpecularIntensityMultiplier()
	{
		SetSpecularIntensityMultiplier( 1f );
	}

	[RuntimeInitializeOnLoadMethod( RuntimeInitializeLoadType.SubsystemRegistration )]
	static void ResetStatics()
	{
		_instance = null;
		_specularIntensityMultiplier = 1f;
	}

	[RuntimeInitializeOnLoadMethod( RuntimeInitializeLoadType.BeforeSceneLoad )]
	static void Bootstrap()
	{
		StylizedLightingDefinition.ApplyDefaultGlobals();
		EnsureInstance();
	}

	static void EnsureInstance()
	{
		if ( _instance != null )
			return;

		GameObject go = new GameObject( "[StylizedLightingGlobals]" );
		Object.DontDestroyOnLoad( go );
		go.hideFlags = HideFlags.HideAndDontSave;
		_instance = go.AddComponent<StylizedLightingGlobals>();
	}

	void OnEnable()
	{
		PushGlobals( force: true );
	}

	void LateUpdate()
	{
		// LateUpdate so Addressables-loaded definitions are more likely available.
		PushGlobals( force: false );
	}

	void PushGlobals( bool force )
	{
		if ( !force && _lastAppliedFrame == Time.frameCount )
			return;

		StylizedLightingDefinition definition = ResolveDefinition();
		if ( definition != null )
			definition.ApplyToGlobals();
		else
			StylizedLightingDefinition.ApplyDefaultGlobals();

		ApplySpecularMultiplier();

		_lastAppliedFrame = Time.frameCount;
	}

	void ApplySpecularMultiplier()
	{
		if ( Mathf.Approximately( _specularIntensityMultiplier, 1f ) )
			return;

		float current = Shader.GetGlobalFloat( StylizedLightingDefinition.SpecularIntensityId );
		Shader.SetGlobalFloat( StylizedLightingDefinition.SpecularIntensityId, current * _specularIntensityMultiplier );
	}

	StylizedLightingDefinition ResolveDefinition()
	{
		if ( _definition != null )
			return _definition;

		RuntimeDefinition.Resolve( ref _definition );
		return _definition;
	}

#if UNITY_EDITOR
	void OnValidate()
	{
		PushGlobals( force: true );
	}
#endif
}
