using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Serialization;

public struct LanternRevealSweepOverrides
{
	public float SkylightFadeDuration;
	public float SweepDuration;
	public float SweepDurationZ;
	public float SweepDurationY;
	public float LanternFadeDuration;
	public float LanternStartDelay;
}

/// <summary>
/// Z (and optional Y) sweep lantern reveal with optional skylight fade running in parallel.
/// Scene setup: add to level, set <see cref="_revealId"/>, assign <see cref="_skylights"/>,
/// align <see cref="_sweepReference"/> +Z toward the hallway sweep direction (and +Y for diagonal sweeps),
/// and set reveal lanterns to <see cref="LanternActivationMode.RevealOnly"/> with matching <see cref="LanternActivator.RevealId"/>.
/// On Roof/Area Light add <see cref="SkylightReveal"/> (two lights, SkyPortal renderer, LightRays renderer).
/// </summary>
public class LanternRevealSweepController : MonoBehaviour
{
	const float SameAxisEpsilon = 0.05f;

	static readonly Dictionary<string, LanternRevealSweepController> Controllers = new Dictionary<string, LanternRevealSweepController>();

	[SerializeField]
	string _revealId = LanternActivator.IntroLedgeRevealId;

	[FormerlySerializedAs( "_skylight" )]
	[SerializeField]
	SkylightReveal[] _skylights;

	[SerializeField]
	float _skylightFadeDuration = 2f;

	[SerializeField]
	Transform _sweepReference;

	[SerializeField]
	[Tooltip( "Local Z on sweep reference where the reveal begins. Can be negative." )]
	float _sweepStartZ;

	[SerializeField]
	[Tooltip( "Local Z on sweep reference where the reveal ends. Can cross zero (e.g. start -5, end 10)." )]
	float _sweepEndZ;

	[SerializeField]
	[Tooltip( "Local Y on sweep reference where the reveal begins. Set end Y to a different value for a diagonal sweep." )]
	float _sweepStartY;

	[SerializeField]
	[Tooltip( "Local Y on sweep reference where the reveal ends. Matches start Y for a horizontal-only sweep." )]
	float _sweepEndY;

	[SerializeField]
	[Tooltip( "When true, start/end Z and Y are computed from reveal lantern positions each run." )]
	bool _autoComputeBounds = true;

	[FormerlySerializedAs( "_sweepDuration" )]
	[SerializeField]
	[Min( 0f )]
	[Tooltip( "Seconds for the sweep front to travel from start Z to end Z." )]
	float _sweepDurationZ = 4f;

	[SerializeField]
	[Min( 0f )]
	[Tooltip( "Seconds for the sweep front to travel from start Y to end Y. Diagonal sweeps use both axis durations independently." )]
	float _sweepDurationY = 4f;

	[SerializeField]
	[Min( 0f )]
	[Tooltip( "Fade duration applied to each lantern when the sweep front reaches it." )]
	float _lanternFadeDuration = 0.75f;

	[SerializeField]
	[Min( 0f )]
	[Tooltip( "Seconds after reveal start before the lantern sweep begins. Skylight and punch effects are unaffected." )]
	float _lanternStartDelay;

	[Header( "Reveal Punch" )]
	[SerializeField]
	RevealPunchChannel _bloomPunch = new RevealPunchChannel
	{
		rise = 0.15f,
		hold = 0.2f,
		fall = 1.2f,
		peak = 8f
	};

	[SerializeField]
	RevealPunchChannel _skylightPunch = new RevealPunchChannel
	{
		rise = 0.1f,
		hold = 0.15f,
		fall = 0.8f,
		peak = 2.5f
	};

	[SerializeField]
	RevealPunchChannel _specularPunch = new RevealPunchChannel
	{
		rise = 0.15f,
		hold = 0.2f,
		fall = 1f,
		peak = 2f
	};

	bool _hasCompleted;
	Coroutine _revealRoutine;
	Volume _bloomVolume;
	VolumeProfile _bloomProfile;
	Bloom _bloomOverride;

#if UNITY_EDITOR
	[RuntimeInitializeOnLoadMethod( RuntimeInitializeLoadType.SubsystemRegistration )]
	static void ResetStatics()
	{
		Controllers.Clear();
	}
#endif

	void Awake()
	{
		if ( _sweepReference == null )
			_sweepReference = transform;
	}

	void OnEnable()
	{
		if ( string.IsNullOrEmpty( _revealId ) )
			return;

		Controllers[ _revealId ] = this;
	}

	void OnDisable()
	{
		if ( !string.IsNullOrEmpty( _revealId ) &&
		     Controllers.TryGetValue( _revealId, out LanternRevealSweepController existing ) &&
		     existing == this )
			Controllers.Remove( _revealId );

		if ( _revealRoutine != null )
		{
			StopCoroutine( _revealRoutine );
			_revealRoutine = null;
		}

		ResetPunchEffects();
	}

	public static bool TryStartReveal( string revealId, LanternRevealSweepOverrides overrides )
	{
		if ( string.IsNullOrEmpty( revealId ) )
			return false;

		if ( !Controllers.TryGetValue( revealId, out LanternRevealSweepController controller ) || controller == null )
		{
			Debug.LogWarning( "LanternRevealSweepController: no controller registered for reveal id '" + revealId + "'." );
			return false;
		}

		controller.StartReveal( overrides );
		return true;
	}

	public void StartReveal( LanternRevealSweepOverrides overrides )
	{
		if ( _revealRoutine != null )
		{
			StopCoroutine( _revealRoutine );
			_revealRoutine = null;
			ResetPunchEffects();
		}

		_hasCompleted = false;
		_revealRoutine = StartCoroutine( RevealRoutine( overrides ) );
	}

	public static void DebugResetAll()
	{
		LanternRevealSweepController[] controllers = UnityEngine.Object.FindObjectsByType<LanternRevealSweepController>( FindObjectsInactive.Exclude, FindObjectsSortMode.None );
		for ( int i = 0; i < controllers.Length; i++ )
		{
			if ( controllers[ i ] != null )
				controllers[ i ].DebugResetReveal();
		}
	}

	public void DebugResetReveal()
	{
		_hasCompleted = false;
		if ( _revealRoutine != null )
		{
			StopCoroutine( _revealRoutine );
			_revealRoutine = null;
		}

		ApplyToSkylights( skylight =>
		{
			skylight.SetReveal( 0f );
			skylight.SetOvershootScale( 1f );
		} );

		ResetPunchEffects();

		LanternActivator[] activators = UnityEngine.Object.FindObjectsByType<LanternActivator>( FindObjectsInactive.Exclude, FindObjectsSortMode.None );
		for ( int i = 0; i < activators.Length; i++ )
		{
			LanternActivator activator = activators[ i ];
			if ( activator == null || activator.ActivationMode != LanternActivationMode.RevealOnly )
				continue;
			if ( activator.RevealId != _revealId )
				continue;

			activator.DebugReset();
		}
	}

	IEnumerator RevealRoutine( LanternRevealSweepOverrides overrides )
	{
		float skylightDuration = ResolveOverride( overrides.SkylightFadeDuration, _skylightFadeDuration );
		float sweepDurationZ = ResolvePerAxisSweepDuration( overrides.SweepDurationZ, overrides.SweepDuration, _sweepDurationZ );
		float sweepDurationY = ResolvePerAxisSweepDuration( overrides.SweepDurationY, overrides.SweepDuration, _sweepDurationY );
		float lanternFadeDuration = ResolveOverride( overrides.LanternFadeDuration, _lanternFadeDuration );
		float lanternStartDelay = ResolveOverride( overrides.LanternStartDelay, _lanternStartDelay );

		List<LanternActivator> lanterns = CollectRevealLanterns();
		if ( lanterns.Count == 0 )
		{
			Debug.LogWarning( "LanternRevealSweepController: no reveal lanterns found for id '" + _revealId + "'." );
			_revealRoutine = null;
			yield break;
		}

		Transform reference = _sweepReference != null ? _sweepReference : transform;
		float sweepStartZ = _sweepStartZ;
		float sweepEndZ = _sweepEndZ;
		float sweepStartY = _sweepStartY;
		float sweepEndY = _sweepEndY;
		if ( _autoComputeBounds )
			ComputeBounds( lanterns, reference, out sweepStartZ, out sweepEndZ, out sweepStartY, out sweepEndY );

		List<LanternSweepEntry> entries = BuildSweepEntries( lanterns, reference, sweepStartZ, sweepEndZ, sweepStartY, sweepEndY, sweepDurationZ, sweepDurationY );
		HashSet<LanternActivator> triggered = new HashSet<LanternActivator>();
		float maxTriggerTime = 0f;
		for ( int i = 0; i < entries.Count; i++ )
		{
			if ( entries[ i ].TriggerTime > maxTriggerTime )
				maxTriggerTime = entries[ i ].TriggerTime;
		}

		bool animateSkylight = HasSkylights() && skylightDuration > 0f;
		float punchDuration = GetMaxPunchDuration();
		float lanternRevealDuration = lanternStartDelay + maxTriggerTime;
		float revealDuration = Mathf.Max( animateSkylight ? skylightDuration : 0f, lanternRevealDuration, punchDuration );
		float elapsed = 0f;

		if ( animateSkylight )
		{
			ApplyToSkylights( skylight =>
			{
				skylight.SetReveal( 0f );
				skylight.SetOvershootScale( 1f );
			} );
		}

		ResetPunchEffects();

		while ( elapsed < revealDuration || triggered.Count < entries.Count )
		{
			elapsed += Time.deltaTime;

			if ( animateSkylight )
			{
				float skylightT = skylightDuration > 0f ? Mathf.Clamp01( elapsed / skylightDuration ) : 1f;
				ApplyToSkylights( skylight => skylight.SetReveal( skylightT ) );
			}

			ApplyPunchEffects( elapsed );
			float sweepElapsed = Mathf.Max( 0f, elapsed - lanternStartDelay );
			TriggerDueLanterns( entries, sweepElapsed, lanternFadeDuration, triggered );
			if ( triggered.Count >= entries.Count && elapsed >= revealDuration )
				break;

			yield return null;
		}

		if ( animateSkylight )
			ApplyToSkylights( skylight => skylight.SetReveal( 1f ) );

		ApplyPunchEffects( revealDuration );
		ResetPunchEffects();

		TriggerDueLanterns( entries, lanternRevealDuration + 1f, lanternFadeDuration, triggered );

		if ( lanternFadeDuration > 0f )
			yield return new WaitForSeconds( lanternFadeDuration );

		_hasCompleted = true;
		_revealRoutine = null;
	}

	struct LanternSweepEntry
	{
		public LanternActivator Lantern;
		public float TriggerTime;
	}

	static List<LanternSweepEntry> BuildSweepEntries(
		List<LanternActivator> lanterns,
		Transform reference,
		float sweepStartZ,
		float sweepEndZ,
		float sweepStartY,
		float sweepEndY,
		float sweepDurationZ,
		float sweepDurationY )
	{
		List<LanternSweepEntry> entries = new List<LanternSweepEntry>( lanterns.Count );
		float zSpan = sweepEndZ - sweepStartZ;
		float ySpan = sweepEndY - sweepStartY;
		bool hasZSpan = Mathf.Abs( zSpan ) > SameAxisEpsilon;
		bool hasYSpan = Mathf.Abs( ySpan ) > SameAxisEpsilon;

		for ( int i = 0; i < lanterns.Count; i++ )
		{
			LanternActivator lantern = lanterns[ i ];
			if ( lantern == null )
				continue;

			Vector2 localPos = GetLocalSweepPosition( lantern, reference );
			float triggerTime = 0f;
			if ( hasZSpan )
			{
				float zNormalized = Mathf.Clamp01( ( localPos.x - sweepStartZ ) / zSpan );
				triggerTime = Mathf.Max( triggerTime, zNormalized * sweepDurationZ );
			}

			if ( hasYSpan )
			{
				float yNormalized = Mathf.Clamp01( ( localPos.y - sweepStartY ) / ySpan );
				triggerTime = Mathf.Max( triggerTime, yNormalized * sweepDurationY );
			}

			entries.Add( new LanternSweepEntry
			{
				Lantern = lantern,
				TriggerTime = triggerTime
			} );
		}

		return entries;
	}

	static void TriggerDueLanterns(
		List<LanternSweepEntry> entries,
		float elapsed,
		float lanternFadeDuration,
		HashSet<LanternActivator> triggered )
	{
		for ( int i = 0; i < entries.Count; i++ )
		{
			LanternActivator lantern = entries[ i ].Lantern;
			if ( lantern == null || triggered.Contains( lantern ) )
				continue;
			if ( elapsed + SameAxisEpsilon < entries[ i ].TriggerTime )
				continue;

			lantern.FadeToLit( true, lanternFadeDuration );
			triggered.Add( lantern );
		}
	}

	List<LanternActivator> CollectRevealLanterns()
	{
		List<LanternActivator> results = new List<LanternActivator>();
		LanternActivator[] activators = UnityEngine.Object.FindObjectsByType<LanternActivator>( FindObjectsInactive.Exclude, FindObjectsSortMode.None );
		for ( int i = 0; i < activators.Length; i++ )
		{
			LanternActivator activator = activators[ i ];
			if ( activator == null || activator.ActivationMode != LanternActivationMode.RevealOnly )
				continue;
			if ( activator.RevealId != _revealId )
				continue;
			if ( !results.Contains( activator ) )
				results.Add( activator );
		}

		return results;
	}

	static void ComputeBounds(
		List<LanternActivator> lanterns,
		Transform reference,
		out float startZ,
		out float endZ,
		out float startY,
		out float endY )
	{
		startZ = float.PositiveInfinity;
		endZ = float.NegativeInfinity;
		startY = float.PositiveInfinity;
		endY = float.NegativeInfinity;
		for ( int i = 0; i < lanterns.Count; i++ )
		{
			Vector2 localPos = GetLocalSweepPosition( lanterns[ i ], reference );
			if ( localPos.x < startZ )
				startZ = localPos.x;
			if ( localPos.x > endZ )
				endZ = localPos.x;
			if ( localPos.y < startY )
				startY = localPos.y;
			if ( localPos.y > endY )
				endY = localPos.y;
		}

		if ( float.IsPositiveInfinity( startZ ) )
		{
			startZ = 0f;
			endZ = 0f;
			startY = 0f;
			endY = 0f;
		}
	}

	static Vector2 GetLocalSweepPosition( LanternActivator activator, Transform reference )
	{
		if ( activator == null || reference == null )
			return Vector2.zero;

		Vector3 localPos = reference.InverseTransformPoint( activator.transform.position );
		return new Vector2( localPos.z, localPos.y );
	}

	static float ResolveOverride( float overrideValue, float defaultValue )
	{
		return overrideValue > 0f ? overrideValue : defaultValue;
	}

	static float ResolvePerAxisSweepDuration( float axisOverride, float legacyOverride, float defaultValue )
	{
		if ( axisOverride > 0f )
			return axisOverride;

		if ( legacyOverride > 0f )
			return legacyOverride;

		return defaultValue;
	}

	float GetMaxPunchDuration()
	{
		float maxDuration = 0f;
		if ( _bloomPunch.IsActive )
			maxDuration = Mathf.Max( maxDuration, _bloomPunch.TotalDuration );
		if ( _skylightPunch.IsScaleActive )
			maxDuration = Mathf.Max( maxDuration, _skylightPunch.TotalDuration );
		if ( _specularPunch.IsScaleActive )
			maxDuration = Mathf.Max( maxDuration, _specularPunch.TotalDuration );
		return maxDuration;
	}

	void ApplyPunchEffects( float elapsed )
	{
		ApplyBloomPunch( elapsed );

		if ( HasSkylights() && _skylightPunch.IsScaleActive )
			ApplyToSkylights( skylight => skylight.SetOvershootScale( _skylightPunch.EvaluateScale( elapsed, 1f ) ) );
		else if ( HasSkylights() )
			ApplyToSkylights( skylight => skylight.SetOvershootScale( 1f ) );

		if ( _specularPunch.IsScaleActive )
			StylizedLightingGlobals.SetSpecularIntensityMultiplier( _specularPunch.EvaluateScale( elapsed, 1f ) );
		else
			StylizedLightingGlobals.ResetSpecularIntensityMultiplier();
	}

	void ApplyBloomPunch( float elapsed )
	{
		if ( !_bloomPunch.IsActive )
		{
			if ( _bloomVolume != null )
				_bloomVolume.weight = 0f;
			return;
		}

		EnsureBloomVolume();
		_bloomOverride.intensity.Override( _bloomPunch.peak );
		_bloomVolume.weight = _bloomPunch.EvaluateWeight( elapsed );
	}

	void EnsureBloomVolume()
	{
		if ( _bloomVolume != null )
			return;

		GameObject go = new GameObject( "[LanternRevealBloom]" );
		go.transform.SetParent( transform, false );
		_bloomVolume = go.AddComponent<Volume>();
		_bloomVolume.isGlobal = true;
		_bloomVolume.priority = 100f;
		_bloomVolume.weight = 0f;

		_bloomProfile = ScriptableObject.CreateInstance<VolumeProfile>();
		_bloomProfile.name = "LanternRevealBloomProfile";
		_bloomOverride = _bloomProfile.Add<Bloom>( true );
		_bloomOverride.intensity.Override( _bloomPunch.peak );
		_bloomOverride.threshold.Override( 0.9f );
		_bloomVolume.profile = _bloomProfile;
	}

	void ResetPunchEffects()
	{
		if ( _bloomVolume != null )
			_bloomVolume.weight = 0f;

		ApplyToSkylights( skylight => skylight.SetOvershootScale( 1f ) );

		StylizedLightingGlobals.ResetSpecularIntensityMultiplier();
	}

	bool HasSkylights()
	{
		if ( _skylights == null )
			return false;

		for ( int i = 0; i < _skylights.Length; i++ )
		{
			if ( _skylights[ i ] != null )
				return true;
		}

		return false;
	}

	void ApplyToSkylights( Action<SkylightReveal> action )
	{
		if ( _skylights == null || action == null )
			return;

		for ( int i = 0; i < _skylights.Length; i++ )
		{
			SkylightReveal skylight = _skylights[ i ];
			if ( skylight != null )
				action( skylight );
		}
	}
}
