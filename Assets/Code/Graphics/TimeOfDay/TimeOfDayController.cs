using UnityEngine;

/// <summary>
/// Advances normalized day time and pushes Sky Portal celestial shader globals.
/// Place one in the level; assign an optional <see cref="TimeOfDayDefinition"/>.
/// </summary>
[DefaultExecutionOrder( -90 )]
[ExecuteAlways]
public class TimeOfDayController : MonoBehaviour
{
	[SerializeField]
	TimeOfDayDefinition _definition;

	[SerializeField]
	[Tooltip( "When enabled, normalized time advances automatically." )]
	bool _autoLoop = true;

	[SerializeField]
	bool _paused;

	[SerializeField]
	[Range( 0f, 1f )]
	[Tooltip( "0 = midnight, 0.25 = sunrise, 0.5 = noon, 0.75 = sunset." )]
	float _normalizedTime = 0.3f;

	[SerializeField]
	[Tooltip( "Overrides definition day length when > 0. Useful for playtest scrubbing." )]
	[Min( 0f )]
	float _dayLengthOverrideSeconds;

	public float NormalizedTime
	{
		get => _normalizedTime;
		set
		{
			_normalizedTime = Mathf.Repeat( value, 1f );
			PushGlobals();
		}
	}

	public bool Paused
	{
		get => _paused;
		set => _paused = value;
	}

	public bool AutoLoop
	{
		get => _autoLoop;
		set => _autoLoop = value;
	}

	[RuntimeInitializeOnLoadMethod( RuntimeInitializeLoadType.BeforeSceneLoad )]
	static void BootstrapDefaults()
	{
		TimeOfDayDefinition.ApplyDefaultGlobals();
	}

	void OnEnable()
	{
		PushGlobals();
	}

	void Start()
	{
		if ( Application.isPlaying && _definition != null )
			_normalizedTime = _definition.startNormalizedTime;

		PushGlobals();
	}

	void Update()
	{
		if ( !Application.isPlaying )
		{
			PushGlobals();
			return;
		}

		if ( _autoLoop && !_paused )
		{
			float dayLength = ResolveDayLengthSeconds();
			_normalizedTime = Mathf.Repeat( _normalizedTime + Time.deltaTime / dayLength, 1f );
		}

		PushGlobals();
	}

	float ResolveDayLengthSeconds()
	{
		if ( _dayLengthOverrideSeconds > 0.01f )
			return _dayLengthOverrideSeconds;

		if ( _definition != null )
			return Mathf.Max( 1f, _definition.dayLengthSeconds );

		return 600f;
	}

	void PushGlobals()
	{
		if ( _definition != null )
		{
			_definition.Validate();
			_definition.ApplyToGlobals( _normalizedTime );
			return;
		}

		TimeOfDayDefinition.ApplyGlobals( _normalizedTime, 25f, 0.5f );
	}

#if UNITY_EDITOR
	void OnValidate()
	{
		_normalizedTime = Mathf.Repeat( _normalizedTime, 1f );
		_dayLengthOverrideSeconds = Mathf.Max( 0f, _dayLengthOverrideSeconds );
		if ( isActiveAndEnabled )
			PushGlobals();
	}
#endif
}
