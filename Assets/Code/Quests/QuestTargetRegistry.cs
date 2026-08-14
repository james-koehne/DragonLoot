using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Runtime lookup for <see cref="QuestTarget"/> and <see cref="QuestVolume"/> by string id.
/// </summary>
public static class QuestTargetRegistry
{
	static readonly Dictionary<string, QuestTarget> Targets = new Dictionary<string, QuestTarget>();
	static readonly Dictionary<string, QuestVolume> Volumes = new Dictionary<string, QuestVolume>();

#if UNITY_EDITOR
	[RuntimeInitializeOnLoadMethod( RuntimeInitializeLoadType.SubsystemRegistration )]
	static void ResetStatics()
	{
		Targets.Clear();
		Volumes.Clear();
	}
#endif

	public static void Register( QuestTarget target )
	{
		if ( target == null || string.IsNullOrEmpty( target.Id ) )
			return;

		Targets[ target.Id ] = target;
	}

	public static void Unregister( QuestTarget target )
	{
		if ( target == null || string.IsNullOrEmpty( target.Id ) )
			return;

		if ( Targets.TryGetValue( target.Id, out QuestTarget existing ) && existing == target )
			Targets.Remove( target.Id );
	}

	public static void Register( QuestVolume volume )
	{
		if ( volume == null || string.IsNullOrEmpty( volume.Id ) )
			return;

		Volumes[ volume.Id ] = volume;
	}

	public static void Unregister( QuestVolume volume )
	{
		if ( volume == null || string.IsNullOrEmpty( volume.Id ) )
			return;

		if ( Volumes.TryGetValue( volume.Id, out QuestVolume existing ) && existing == volume )
			Volumes.Remove( volume.Id );
	}

	public static bool TryGetTarget( string id, out QuestTarget target )
	{
		if ( string.IsNullOrEmpty( id ) )
		{
			target = null;
			return false;
		}

		return Targets.TryGetValue( id, out target ) && target != null;
	}

	public static bool TryGetVolume( string id, out QuestVolume volume )
	{
		if ( string.IsNullOrEmpty( id ) )
		{
			volume = null;
			return false;
		}

		return Volumes.TryGetValue( id, out volume ) && volume != null;
	}

	public static bool TryGetMarkerTransform( string id, out Transform marker )
	{
		marker = null;
		if ( TryGetTarget( id, out QuestTarget target ) && target != null )
		{
			marker = target.MarkerTransform;
			return marker != null;
		}

		if ( TryGetVolume( id, out QuestVolume volume ) && volume != null )
		{
			marker = volume.transform;
			return true;
		}

		return false;
	}

	public static string ResolveTargetId( Component component )
	{
		if ( component == null )
			return null;

		QuestTarget target = component.GetComponent<QuestTarget>();
		if ( target == null )
			target = component.GetComponentInParent<QuestTarget>();
		if ( target == null )
			target = component.GetComponentInChildren<QuestTarget>( true );
		if ( target == null )
			return null;

		return target.Id;
	}
}
