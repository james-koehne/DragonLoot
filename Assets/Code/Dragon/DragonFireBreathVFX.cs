using FeedbackSystem;

using UnityEngine;

/// <summary>
/// Aims a particle stream from the dragon mouth toward a burn target.
/// Authored under the dragon's FireBreathRoot; play/stop driven by <see cref="IncineratorPlatform"/>.
/// </summary>
[DisallowMultipleComponent]
public class DragonFireBreathVFX : MonoBehaviour
{
	[SerializeField] ParticleSystem _particles;
	[SerializeField] Transform _aimOrigin;
	[SerializeField] Feedbacks _onBreathStart;

	Vector3 _targetPoint;
	bool _active;
	bool _hasTarget;

	public bool IsPlaying => _active;

	void Awake()
	{
		if ( _particles == null )
			_particles = GetComponent<ParticleSystem>();
		if ( _aimOrigin == null )
			_aimOrigin = transform;
	}

	void OnDisable()
	{
		StopBreath();
	}

	void LateUpdate()
	{
		if ( !_active || !_hasTarget )
			return;

		AlignToTarget();
	}

	public void PlayToward( Vector3 worldTarget )
	{
		_targetPoint = worldTarget;
		_hasTarget = true;
		_active = true;
		gameObject.SetActive( true );
		AlignToTarget();

		if ( _particles != null )
		{
			_particles.Clear( true );
			_particles.Play( true );
		}

		if ( _onBreathStart != null )
			_onBreathStart.Play();
	}

	public void UpdateTarget( Vector3 worldTarget )
	{
		_targetPoint = worldTarget;
		_hasTarget = true;
	}

	public void StopBreath()
	{
		_active = false;
		_hasTarget = false;
		if ( _particles != null )
			_particles.Stop( true, ParticleSystemStopBehavior.StopEmitting );
		if ( _onBreathStart != null )
			_onBreathStart.Stop();
	}

	void AlignToTarget()
	{
		Transform origin = _aimOrigin != null ? _aimOrigin : transform;
		Vector3 from = origin.position;
		Vector3 toTarget = _targetPoint - from;
		if ( toTarget.sqrMagnitude < 0.0001f )
			return;

		transform.rotation = Quaternion.LookRotation( toTarget.normalized, Vector3.up );

		if ( _particles == null )
			return;

		float distance = toTarget.magnitude;
		ParticleSystem.MainModule main = _particles.main;
		main.startSpeed = Mathf.Max( 1f, distance * 1.15f );
	}

#if UNITY_EDITOR
	public void EditorAssignBreathStartFeedback( Feedbacks onBreathStart )
	{
		_onBreathStart = onBreathStart;
	}
#endif
}
