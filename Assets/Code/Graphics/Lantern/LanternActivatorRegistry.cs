using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Distance-based lantern activation with optional <see cref="LanternActivator.GroupId"/> unison.
/// </summary>
public static class LanternActivatorRegistry
{
	static readonly List<LanternActivator> Activators = new List<LanternActivator>();
	static readonly Dictionary<string, GroupEntry> Groups = new Dictionary<string, GroupEntry>();
	static LanternActivatorRegistryDriver _driver;

#if UNITY_EDITOR
	[RuntimeInitializeOnLoadMethod( RuntimeInitializeLoadType.SubsystemRegistration )]
	static void ResetStatics()
	{
		Activators.Clear();
		Groups.Clear();
		_driver = null;
	}
#endif

	public static void Register( LanternActivator activator )
	{
		if ( activator == null || Activators.Contains( activator ) )
			return;

		Activators.Add( activator );
		if ( !string.IsNullOrEmpty( activator.GroupId ) )
		{
			if ( !Groups.TryGetValue( activator.GroupId, out GroupEntry group ) )
			{
				group = new GroupEntry();
				Groups[ activator.GroupId ] = group;
			}

			if ( !group.Members.Contains( activator ) )
				group.Members.Add( activator );
		}

		EnsureDriver();
	}

	public static void Unregister( LanternActivator activator )
	{
		if ( activator == null )
			return;

		Activators.Remove( activator );
		if ( !string.IsNullOrEmpty( activator.GroupId ) &&
		     Groups.TryGetValue( activator.GroupId, out GroupEntry group ) )
		{
			group.Members.Remove( activator );
			if ( group.Members.Count == 0 )
				Groups.Remove( activator.GroupId );
		}
	}

	public static IReadOnlyList<LanternActivator> GetAll() => Activators;

	static void EnsureDriver()
	{
		if ( _driver != null )
			return;

		GameObject go = new GameObject( "LanternActivatorRegistry" );
		_driver = go.AddComponent<LanternActivatorRegistryDriver>();
		Object.DontDestroyOnLoad( go );
	}

	internal static void Tick()
	{
		Vector3 playerPosition = ResolvePlayerPosition();
		if ( playerPosition == InvalidPosition )
			return;

		for ( int i = 0; i < Activators.Count; i++ )
		{
			LanternActivator activator = Activators[ i ];
			if ( activator == null || activator.ActivationMode != LanternActivationMode.Distance )
				continue;
			if ( !string.IsNullOrEmpty( activator.GroupId ) )
				continue;

			UpdateSoloActivator( activator, playerPosition );
		}

		foreach ( KeyValuePair<string, GroupEntry> pair in Groups )
			UpdateGroup( pair.Value, playerPosition );
	}

	static void UpdateSoloActivator( LanternActivator activator, Vector3 playerPosition )
	{
		float distance = PlanarDistance( playerPosition, activator.transform.position );
		if ( distance <= activator.ActivateDistance )
			activator.ApplyDistanceLitState( true );
		else if ( activator.AllowDeactivate && distance >= activator.DeactivateDistance )
			activator.ApplyDistanceLitState( false );
	}

	static void UpdateGroup( GroupEntry group, Vector3 playerPosition )
	{
		if ( group == null || group.Members.Count == 0 )
			return;

		Vector3 centroid = Vector3.zero;
		int count = 0;
		for ( int i = 0; i < group.Members.Count; i++ )
		{
			LanternActivator member = group.Members[ i ];
			if ( member == null || member.ActivationMode != LanternActivationMode.Distance )
				continue;

			centroid += member.transform.position;
			count++;
		}

		if ( count == 0 )
			return;

		centroid /= count;
		LanternActivator sample = group.Members[ 0 ];
		float distance = PlanarDistance( playerPosition, centroid );
		bool shouldBeLit = group.WasLit;
		if ( !group.WasLit )
		{
			if ( distance <= sample.ActivateDistance )
				shouldBeLit = true;
		}
		else if ( distance >= sample.DeactivateDistance )
		{
			shouldBeLit = false;
			for ( int i = 0; i < group.Members.Count; i++ )
			{
				LanternActivator member = group.Members[ i ];
				if ( member == null || member.ActivationMode != LanternActivationMode.Distance )
					continue;

				if ( !member.AllowDeactivate )
				{
					shouldBeLit = true;
					break;
				}
			}
		}

		if ( shouldBeLit == group.WasLit )
			return;

		group.WasLit = shouldBeLit;
		for ( int i = 0; i < group.Members.Count; i++ )
		{
			LanternActivator member = group.Members[ i ];
			if ( member == null || member.ActivationMode != LanternActivationMode.Distance )
				continue;

			member.ApplyDistanceLitState( shouldBeLit );
		}
	}

	static readonly Vector3 InvalidPosition = new Vector3( float.NaN, float.NaN, float.NaN );

	static Vector3 ResolvePlayerPosition()
	{
		if ( GameMode.Instance == null || GameMode.Instance.Player == null )
			return InvalidPosition;

		return GameMode.Instance.Player.transform.position;
	}

	static float PlanarDistance( Vector3 a, Vector3 b )
	{
		a.y = 0f;
		b.y = 0f;
		return Vector3.Distance( a, b );
	}

	sealed class GroupEntry
	{
		public readonly List<LanternActivator> Members = new List<LanternActivator>();
		public bool WasLit;
	}

	sealed class LanternActivatorRegistryDriver : MonoBehaviour
	{
		void LateUpdate()
		{
			Tick();
		}
	}
}
