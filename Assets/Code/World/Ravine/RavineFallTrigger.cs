using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Pit trigger that starts ravine fall recovery when the player enters.
/// Rearms when recovery finishes (CharacterController is disabled during the arc, so OnTriggerExit is unreliable).
/// </summary>
[DisallowMultipleComponent]
[RequireComponent( typeof( Collider ) )]
public class RavineFallTrigger : MonoBehaviour
{
	static readonly List<RavineFallTrigger> ActiveTriggers = new List<RavineFallTrigger>();

	[SerializeField]
	RavineRecoveryController _controller;

	[SerializeField]
	[Tooltip( "When true, only fires once until recovery finishes or the player cleanly exits." )]
	bool _rearmOnExit = true;

	bool _armed = true;

	void Reset()
	{
		Collider col = GetComponent<Collider>();
		if ( col != null )
			col.isTrigger = true;
	}

	void OnEnable()
	{
		if ( !ActiveTriggers.Contains( this ) )
			ActiveTriggers.Add( this );
	}

	void OnDisable()
	{
		ActiveTriggers.Remove( this );
	}

	void OnTriggerEnter( Collider other )
	{
		if ( !_armed )
			return;

		PlayerController player = ResolvePlayer( other );
		if ( player == null )
			return;

		RavineRecoveryController controller = ResolveController();
		if ( controller == null )
			return;

		if ( !controller.BeginRecovery( player ) )
			return;

		_armed = false;
	}

	void OnTriggerExit( Collider other )
	{
		if ( !_rearmOnExit )
			return;

		if ( ResolvePlayer( other ) == null )
			return;

		// Only rearm from exit when not mid-recovery; recovery completion also rearms.
		RavineRecoveryController controller = ResolveController();
		if ( controller != null && controller.IsRecovering )
			return;

		_armed = true;
	}

	public void Rearm()
	{
		_armed = true;
	}

	public static void RearmAll()
	{
		for ( int i = 0; i < ActiveTriggers.Count; i++ )
		{
			RavineFallTrigger trigger = ActiveTriggers[ i ];
			if ( trigger != null )
				trigger.Rearm();
		}
	}

	RavineRecoveryController ResolveController()
	{
		if ( _controller != null )
			return _controller;

		RavineRecoveryController parent = GetComponentInParent<RavineRecoveryController>();
		if ( parent != null )
			return parent;

		return RavineRecoveryController.Active;
	}

	static PlayerController ResolvePlayer( Collider other )
	{
		if ( other == null )
			return null;

		return other.GetComponentInParent<PlayerController>();
	}
}
