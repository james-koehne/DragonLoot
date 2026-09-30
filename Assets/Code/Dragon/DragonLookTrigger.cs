using UnityEngine;

/// <summary>
/// Trigger volume that tells <see cref="DragonController"/> the player is in a look zone.
/// Add as many as needed (any trigger collider). Overlapping zones are ref-counted.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent( typeof( Collider ) )]
public class DragonLookTrigger : MonoBehaviour
{
	[SerializeField] DragonController _dragon;

	bool _playerInside;

	void Reset()
	{
		Collider col = GetComponent<Collider>();
		if ( col != null )
			col.isTrigger = true;

		SphereCollider sphere = col as SphereCollider;
		if ( sphere != null && sphere.radius < 0.01f )
			sphere.radius = 8f;
	}

	void Awake()
	{
		if ( _dragon == null )
			_dragon = GetComponentInParent<DragonController>();

		Collider col = GetComponent<Collider>();
		if ( col != null )
			col.isTrigger = true;
	}

	void OnDisable()
	{
		if ( !_playerInside || _dragon == null )
			return;

		_playerInside = false;
		_dragon.NotifyLookTriggerExit( this );
	}

	void OnTriggerEnter( Collider other )
	{
		if ( _dragon == null || other == null )
			return;

		PlayerController player = other.GetComponentInParent<PlayerController>();
		if ( player == null )
			return;

		if ( !IsTrackedPlayer( player ) )
			return;

		if ( _playerInside )
			return;

		_playerInside = true;
		_dragon.NotifyLookTriggerEnter( this );
	}

	void OnTriggerExit( Collider other )
	{
		if ( !_playerInside || _dragon == null || other == null )
			return;

		PlayerController player = other.GetComponentInParent<PlayerController>();
		if ( player == null )
			return;

		if ( !IsTrackedPlayer( player ) )
			return;

		_playerInside = false;
		_dragon.NotifyLookTriggerExit( this );
	}

	static bool IsTrackedPlayer( PlayerController player )
	{
		if ( GameMode.Instance == null )
			return true;

		PlayerController tracked = GameMode.Instance.Player;
		return tracked == null || tracked == player;
	}

#if UNITY_EDITOR
	void OnDrawGizmosSelected()
	{
		SphereCollider sphere = GetComponent<SphereCollider>();
		if ( sphere == null )
			return;

		Gizmos.color = new Color( 0.35f, 0.75f, 1f, 0.25f );
		Matrix4x4 matrix = Matrix4x4.TRS( transform.TransformPoint( sphere.center ), transform.rotation, transform.lossyScale );
		Gizmos.matrix = matrix;
		Gizmos.DrawWireSphere( Vector3.zero, sphere.radius );
	}

	public void EditorAssign( DragonController dragon )
	{
		_dragon = dragon;
	}
#endif
}
