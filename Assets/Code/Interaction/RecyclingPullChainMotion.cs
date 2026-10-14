using System.Collections;

using UnityEngine;

/// <summary>
/// Ratcheting vertical pull-chain motion: drop on pulse, hang, creep up, snap retract.
/// </summary>
public class RecyclingPullChainMotion : MonoBehaviour
{
	[SerializeField]
	RecyclingMachine station;

	[SerializeField]
	Transform visual;

	Vector3 _restLocalPos;
	Coroutine _strokeRoutine;
	Coroutine _retractRoutine;

	public Transform Visual => visual != null ? visual : transform;

	public void BindStation( RecyclingMachine owner )
	{
		station = owner;
	}

	void Awake()
	{
		if ( station == null )
			station = GetComponentInParent<RecyclingMachine>();
		if ( visual == null )
			visual = transform;
		_restLocalPos = visual.localPosition;
	}

	public void PlayPullStroke( float chargeNormalized )
	{
		if ( visual == null )
			return;

		if ( _retractRoutine != null )
		{
			StopCoroutine( _retractRoutine );
			_retractRoutine = null;
		}

		if ( _strokeRoutine != null )
			StopCoroutine( _strokeRoutine );

		_strokeRoutine = StartCoroutine( StrokeRoutine( chargeNormalized ) );
	}

	public void SnapRetract()
	{
		if ( visual == null )
			return;

		if ( _strokeRoutine != null )
		{
			StopCoroutine( _strokeRoutine );
			_strokeRoutine = null;
		}

		if ( _retractRoutine != null )
			StopCoroutine( _retractRoutine );

		_retractRoutine = StartCoroutine( RetractRoutine() );
	}

	public void TwitchDenied()
	{
		if ( visual == null )
			return;

		if ( _strokeRoutine != null || _retractRoutine != null )
			return;

		_strokeRoutine = StartCoroutine( TwitchRoutine() );
	}

	IEnumerator StrokeRoutine( float chargeNormalized )
	{
		RecyclingMachineDefinition def = station != null ? station.Definition : null;
		float stroke = def != null ? def.pullStrokeLength : 0.45f;
		float minScale = def != null ? def.pullStrokeMinScale : 0.55f;
		float hang = def != null ? def.pullHangSeconds : 0.08f;
		float creep = def != null ? def.pullCreepSeconds : 0.18f;
		float recover = def != null ? def.pullCreepRecover : 0.35f;

		float scale = Mathf.Lerp( 1f, minScale, Mathf.Clamp01( chargeNormalized ) );
		float drop = stroke * scale;
		Vector3 bottom = _restLocalPos + Vector3.down * drop;
		Vector3 start = visual.localPosition;
		const float dropSeconds = 0.1f;
		float elapsed = 0f;

		while ( elapsed < dropSeconds )
		{
			elapsed += Time.deltaTime;
			float u = Mathf.Clamp01( elapsed / dropSeconds );
			float ease = u * u;
			visual.localPosition = Vector3.Lerp( start, bottom, ease );
			yield return null;
		}

		visual.localPosition = bottom;
		if ( station != null )
			station.NotifyPullCatch();

		if ( hang > 0f )
			yield return new WaitForSeconds( hang );

		Vector3 creepTarget = Vector3.Lerp( bottom, _restLocalPos, Mathf.Clamp01( recover ) );
		elapsed = 0f;
		while ( elapsed < creep )
		{
			elapsed += Time.deltaTime;
			float u = Mathf.Clamp01( elapsed / Mathf.Max( 0.01f, creep ) );
			visual.localPosition = Vector3.Lerp( bottom, creepTarget, u );
			yield return null;
		}

		visual.localPosition = creepTarget;
		_strokeRoutine = null;
	}

	IEnumerator RetractRoutine()
	{
		RecyclingMachineDefinition def = station != null ? station.Definition : null;
		float duration = def != null ? def.chainRetractSeconds : 0.22f;
		Vector3 start = visual.localPosition;
		float elapsed = 0f;

		while ( elapsed < duration )
		{
			elapsed += Time.deltaTime;
			float u = Mathf.Clamp01( elapsed / Mathf.Max( 0.01f, duration ) );
			float ease = 1f - ( 1f - u ) * ( 1f - u );
			visual.localPosition = Vector3.Lerp( start, _restLocalPos, ease );
			yield return null;
		}

		visual.localPosition = _restLocalPos;
		_retractRoutine = null;
	}

	IEnumerator TwitchRoutine()
	{
		Vector3 start = visual.localPosition;
		Vector3 twitch = start + Vector3.down * 0.06f;
		const float half = 0.05f;
		float elapsed = 0f;
		while ( elapsed < half )
		{
			elapsed += Time.deltaTime;
			visual.localPosition = Vector3.Lerp( start, twitch, Mathf.Clamp01( elapsed / half ) );
			yield return null;
		}

		elapsed = 0f;
		while ( elapsed < half )
		{
			elapsed += Time.deltaTime;
			visual.localPosition = Vector3.Lerp( twitch, start, Mathf.Clamp01( elapsed / half ) );
			yield return null;
		}

		visual.localPosition = start;
		_strokeRoutine = null;
	}

#if UNITY_EDITOR
	public void EditorSetVisual( Transform value )
	{
		visual = value;
		if ( visual != null )
			_restLocalPos = visual.localPosition;
	}
#endif
}
