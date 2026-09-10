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
/// Diagonal (or axis-aligned) sweep lantern reveal with optional skylight fade running in parallel.
/// Scene setup: add to level, set <see cref="_revealId"/>, assign <see cref="_skylights"/>,
/// align <see cref="_sweepReference"/> so local Z/Y match the hallway, set start/end Z+Y as the
/// sweep endpoints (or enable auto bounds), and set reveal lanterns to
/// <see cref="LanternActivationMode.RevealOnly"/> with matching <see cref="LanternActivator.RevealId"/>.
/// The reveal front travels from (startZ, startY) to (endZ, endY); lanterns trigger by projection onto that segment.
/// On Roof/Area Light add <see cref="SkylightReveal"/> (two lights, SkyPortal renderer, LightRays renderer);
/// configure each skylight's start delay, fade duration, and punch on that component.
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
	[Min( 0f )]
	[Tooltip( "Default fade duration for skylights whose Fade Duration is 0. World-event override replaces this for all skylights when set." )]
	float _skylightFadeDuration = 2f;

	[SerializeField]
	Transform _sweepReference;

	[SerializeField]
	[Tooltip( "Local Z on sweep reference for the sweep start point (with Start Y). Can be negative." )]
	float _sweepStartZ;

	[SerializeField]
	[Tooltip( "Local Z on sweep reference for the sweep end point (with End Y). Can cross zero (e.g. start -5, end 10)." )]
	float _sweepEndZ;

	[SerializeField]
	[Tooltip( "Local Y on sweep reference for the sweep start point (with Start Z). Differ from End Y for a diagonal." )]
	float _sweepStartY;

	[SerializeField]
	[Tooltip( "Local Y on sweep reference for the sweep end point (with End Z). Match Start Y for a Z-only sweep." )]
	float _sweepEndY;

	[SerializeField]
	[Tooltip( "When true, start/end Z and Y are the axis-aligned bounds of reveal lantern positions each run (diagonal of that box)." )]
	bool _autoComputeBounds = true;

	[FormerlySerializedAs( "_sweepDuration" )]
	[SerializeField]
	[Min( 0f )]
	[Tooltip( "Seconds for the sweep front to travel from start (Z,Y) to end (Z,Y). Used when the path has a Z component (and as fallback)." )]
	float _sweepDurationZ = 4f;

	[SerializeField]
	[Min( 0f )]
	[Tooltip( "Seconds used when the sweep path is Y-only. For a diagonal (both Z and Y change), the longer of Z/Y duration is used." )]
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

	public static void DebugSnapComplete()
	{
		foreach ( KeyValuePair<string, LanternRevealSweepController> pair in Controllers )
		{
			if ( pair.Value != null )
				pair.Value.SnapComplete();
		}
	}

	void SnapComplete()
	{
		if ( _revealRoutine != null )
		{
			StopCoroutine( _revealRoutine );
			_revealRoutine = null;
		}

		_hasCompleted = true;
		FinishSkylightReveal();
		ResetPunchEffects();

		IReadOnlyList<LanternActivator> activators = LanternActivatorRegistry.GetAll();
		for ( int i = 0; i < activators.Count; i++ )
		{
			LanternActivator activator = activators[ i ];
			if ( activator == null || activator.ActivationMode != LanternActivationMode.RevealOnly )
				continue;
			if ( activator.RevealId != _revealId )
				continue;

			activator.FadeToLit( true, 0f );
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
		float skylightFadeOverride = overrides.SkylightFadeDuration;
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

		bool animateSkylight = HasSkylights();
		float punchDuration = GetMaxPunchDuration();
		float skylightRevealDuration = GetSkylightRevealDuration( skylightFadeOverride );
		float lanternRevealDuration = lanternStartDelay + maxTriggerTime;
		float revealDuration = Mathf.Max( animateSkylight ? skylightRevealDuration : 0f, lanternRevealDuration, punchDuration );
		float elapsed = 0f;

		if ( animateSkylight )
			ResetSkylights();

		ResetPunchEffects();

		while ( elapsed < revealDuration || triggered.Count < entries.Count )
		{
			elapsed += Time.deltaTime;

			if ( animateSkylight )
				ApplySkylightReveal( elapsed, skylightFadeOverride );

			ApplyPunchEffects( elapsed );
			float sweepElapsed = Mathf.Max( 0f, elapsed - lanternStartDelay );
			TriggerDueLanterns( entries, sweepElapsed, lanternFadeDuration, triggered );
			if ( triggered.Count >= entries.Count && elapsed >= revealDuration )
				break;

			yield return null;
		}

		if ( animateSkylight )
			FinishSkylightReveal();

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
		// Local sweep space is (Z, Y) — see GetLocalSweepPosition.
		Vector2 sweepStart = new Vector2( sweepStartZ, sweepStartY );
		Vector2 sweepDelta = new Vector2( sweepEndZ - sweepStartZ, sweepEndY - sweepStartY );
		float sweepDeltaSqr = sweepDelta.sqrMagnitude;
		float sweepDuration = ResolveDiagonalSweepDuration( sweepDelta, sweepDurationZ, sweepDurationY );

		for ( int i = 0; i < lanterns.Count; i++ )
		{
			LanternActivator lantern = lanterns[ i ];
			if ( lantern == null )
				continue;

			Vector2 localPos = GetLocalSweepPosition( lantern, reference );
			float triggerTime = 0f;
			if ( sweepDeltaSqr > SameAxisEpsilon * SameAxisEpsilon )
			{
				float normalized = Vector2.Dot( localPos - sweepStart, sweepDelta ) / sweepDeltaSqr;
				triggerTime = Mathf.Clamp01( normalized ) * sweepDuration;
			}

			entries.Add( new LanternSweepEntry
			{
				Lantern = lantern,
				TriggerTime = triggerTime
			} );
		}

		return entries;
	}

	static float ResolveDiagonalSweepDuration( Vector2 sweepDelta, float durationZ, float durationY )
	{
		bool hasZSpan = Mathf.Abs( sweepDelta.x ) > SameAxisEpsilon;
		bool hasYSpan = Mathf.Abs( sweepDelta.y ) > SameAxisEpsilon;
		if ( hasZSpan && hasYSpan )
			return Mathf.Max( durationZ, durationY );
		if ( hasYSpan )
			return durationY;
		return durationZ;
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
		if ( _specularPunch.IsScaleActive )
			maxDuration = Mathf.Max( maxDuration, _specularPunch.TotalDuration );

		if ( _skylights != null )
		{
			for ( int i = 0; i < _skylights.Length; i++ )
			{
				SkylightReveal skylight = _skylights[ i ];
				if ( skylight == null )
					continue;

				RevealPunchChannel punch = skylight.Punch;
				if ( punch.IsScaleActive )
					maxDuration = Mathf.Max( maxDuration, skylight.StartDelay + punch.TotalDuration );
			}
		}

		return maxDuration;
	}

	float GetSkylightRevealDuration( float fadeOverride )
	{
		float maxDuration = 0f;
		if ( _skylights == null )
			return maxDuration;

		for ( int i = 0; i < _skylights.Length; i++ )
		{
			SkylightReveal skylight = _skylights[ i ];
			if ( skylight == null )
				continue;

			float fadeDuration = ResolveSkylightFadeDuration( skylight, fadeOverride );
			float fadeEnd = skylight.StartDelay + fadeDuration;
			float punchEnd = skylight.Punch.IsScaleActive ? skylight.StartDelay + skylight.Punch.TotalDuration : 0f;
			maxDuration = Mathf.Max( maxDuration, fadeEnd, punchEnd );
		}

		return maxDuration;
	}

	float ResolveSkylightFadeDuration( SkylightReveal skylight, float fadeOverride )
	{
		if ( fadeOverride > 0f )
			return fadeOverride;

		if ( skylight != null && skylight.FadeDuration > 0f )
			return skylight.FadeDuration;

		return _skylightFadeDuration;
	}

	void ResetSkylights()
	{
		ApplyToSkylights( skylight =>
		{
			skylight.SetReveal( 0f );
			skylight.SetOvershootScale( 1f );
		} );
	}

	void ApplySkylightReveal( float elapsed, float fadeOverride )
	{
		if ( _skylights == null )
			return;

		for ( int i = 0; i < _skylights.Length; i++ )
		{
			SkylightReveal skylight = _skylights[ i ];
			if ( skylight == null )
				continue;

			float localElapsed = elapsed - skylight.StartDelay;
			float fadeDuration = ResolveSkylightFadeDuration( skylight, fadeOverride );
			float revealT = localElapsed < 0f
				? 0f
				: ( fadeDuration > 0f ? Mathf.Clamp01( localElapsed / fadeDuration ) : 1f );
			skylight.SetReveal( revealT );

			RevealPunchChannel punch = skylight.Punch;
			if ( punch.IsScaleActive )
				skylight.SetOvershootScale( punch.EvaluateScale( Mathf.Max( 0f, localElapsed ), 1f ) );
			else
				skylight.SetOvershootScale( 1f );
		}
	}

	void FinishSkylightReveal()
	{
		ApplyToSkylights( skylight => skylight.SetReveal( 1f ) );
	}

	void ApplyPunchEffects( float elapsed )
	{
		ApplyBloomPunch( elapsed );

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
