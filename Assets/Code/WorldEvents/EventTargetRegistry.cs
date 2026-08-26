using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Runtime lookup for quest/event targets, volumes, and spawn points by string id.
/// </summary>
public static class EventTargetRegistry
{
	static readonly Dictionary<string, QuestTarget> Targets = new Dictionary<string, QuestTarget>();
	static readonly List<QuestTarget> AllTargets = new List<QuestTarget>();
	static readonly Dictionary<string, QuestVolume> Volumes = new Dictionary<string, QuestVolume>();
	static readonly Dictionary<string, EventSpawnPoint> SpawnPoints = new Dictionary<string, EventSpawnPoint>();

#if UNITY_EDITOR
	[RuntimeInitializeOnLoadMethod( RuntimeInitializeLoadType.SubsystemRegistration )]
	static void ResetStatics()
	{
		Targets.Clear();
		AllTargets.Clear();
		Volumes.Clear();
		SpawnPoints.Clear();
	}
#endif

	public static void Register( QuestTarget target )
	{
		if ( target == null || string.IsNullOrEmpty( target.Id ) )
			return;

		Targets[ target.Id ] = target;
		if ( !AllTargets.Contains( target ) )
			AllTargets.Add( target );
	}

	public static void Unregister( QuestTarget target )
	{
		if ( target == null )
			return;

		if ( !string.IsNullOrEmpty( target.Id ) &&
		     Targets.TryGetValue( target.Id, out QuestTarget existing ) &&
		     existing == target )
			Targets.Remove( target.Id );

		AllTargets.Remove( target );
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

	public static void Register( EventSpawnPoint spawnPoint )
	{
		if ( spawnPoint == null || string.IsNullOrEmpty( spawnPoint.Id ) )
			return;

		SpawnPoints[ spawnPoint.Id ] = spawnPoint;
	}

	public static void Unregister( EventSpawnPoint spawnPoint )
	{
		if ( spawnPoint == null || string.IsNullOrEmpty( spawnPoint.Id ) )
			return;

		if ( SpawnPoints.TryGetValue( spawnPoint.Id, out EventSpawnPoint existing ) && existing == spawnPoint )
			SpawnPoints.Remove( spawnPoint.Id );
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

	public static bool TryGetSpawnPoint( string id, out EventSpawnPoint spawnPoint )
	{
		if ( string.IsNullOrEmpty( id ) )
		{
			spawnPoint = null;
			return false;
		}

		return SpawnPoints.TryGetValue( id, out spawnPoint ) && spawnPoint != null;
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

		if ( TryGetSpawnPoint( id, out EventSpawnPoint spawn ) && spawn != null )
		{
			marker = spawn.SpawnTransform;
			return marker != null;
		}

		return false;
	}

	public static string ResolveTargetId( Component component )
	{
		QuestTarget target = ResolveTarget( component );
		if ( target == null )
			return null;
		return target.Id;
	}

	public static string ResolveAreaId( Component component )
	{
		QuestTarget target = ResolveTarget( component );
		if ( target == null )
			return null;
		return target.AreaId;
	}

	public static QuestTarget ResolveTarget( Component component )
	{
		if ( component == null )
			return null;

		QuestTarget target = component.GetComponent<QuestTarget>();
		if ( target == null )
			target = component.GetComponentInParent<QuestTarget>();
		if ( target == null )
			target = component.GetComponentInChildren<QuestTarget>( true );
		return target;
	}

	public static string GetAreaIdByTargetId( string targetId )
	{
		if ( !TryGetTarget( targetId, out QuestTarget target ) || target == null )
			return null;
		return target.AreaId;
	}

	public static void CollectInArea<T>( string areaId, List<T> into ) where T : Component
	{
		if ( into == null || string.IsNullOrEmpty( areaId ) )
			return;

		for ( int i = 0; i < AllTargets.Count; i++ )
		{
			QuestTarget target = AllTargets[ i ];
			if ( target == null || target.AreaId != areaId )
				continue;

			T match = target.GetComponent<T>();
			if ( match == null )
				match = target.GetComponentInParent<T>();
			if ( match == null )
				match = target.GetComponentInChildren<T>( true );
			if ( match == null )
				continue;
			if ( into.Contains( match ) )
				continue;
			into.Add( match );
		}
	}
}

/// <summary>Back-compat alias for <see cref="EventTargetRegistry"/>.</summary>
public static class QuestTargetRegistry
{
	public static void Register( QuestTarget target ) => EventTargetRegistry.Register( target );
	public static void Unregister( QuestTarget target ) => EventTargetRegistry.Unregister( target );
	public static void Register( QuestVolume volume ) => EventTargetRegistry.Register( volume );
	public static void Unregister( QuestVolume volume ) => EventTargetRegistry.Unregister( volume );
	public static bool TryGetTarget( string id, out QuestTarget target ) => EventTargetRegistry.TryGetTarget( id, out target );
	public static bool TryGetVolume( string id, out QuestVolume volume ) => EventTargetRegistry.TryGetVolume( id, out volume );
	public static bool TryGetMarkerTransform( string id, out Transform marker ) => EventTargetRegistry.TryGetMarkerTransform( id, out marker );
	public static string ResolveTargetId( Component component ) => EventTargetRegistry.ResolveTargetId( component );
	public static string ResolveAreaId( Component component ) => EventTargetRegistry.ResolveAreaId( component );
	public static QuestTarget ResolveTarget( Component component ) => EventTargetRegistry.ResolveTarget( component );
	public static string GetAreaIdByTargetId( string targetId ) => EventTargetRegistry.GetAreaIdByTargetId( targetId );
	public static void CollectInArea<T>( string areaId, List<T> into ) where T : Component => EventTargetRegistry.CollectInArea( areaId, into );
}
