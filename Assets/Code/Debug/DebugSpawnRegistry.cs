using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Runtime lookup for <see cref="DebugSpawnPoint"/> markers and fallback volume teleports.
/// </summary>
public static class DebugSpawnRegistry
{
	public readonly struct FallbackVolume
	{
		public readonly string VolumeId;
		public readonly string DisplayName;

		public FallbackVolume( string volumeId, string displayName )
		{
			VolumeId = volumeId;
			DisplayName = displayName;
		}
	}

	public static readonly FallbackVolume[] FallbackVolumes =
	{
		new FallbackVolume( EventSceneAutoWire.IdVolumeMainCave, "Main Cave" ),
		new FallbackVolume( EventSceneAutoWire.IdVolumeWorkshop, "Workshop" ),
		new FallbackVolume( EventSceneAutoWire.IdVolumeArtifactMuseum, "Artifact Museum" ),
		new FallbackVolume( EventSceneAutoWire.IdVolumeCoinHall, "Coin Hall" )
	};

	static readonly string[] IntroEventIds =
	{
		"intro_welcome",
		"intro_hallway",
		"intro_ledge"
	};

	static readonly Dictionary<string, DebugSpawnPoint> Points = new Dictionary<string, DebugSpawnPoint>();
	static readonly List<DebugSpawnPoint> All = new List<DebugSpawnPoint>();

#if UNITY_EDITOR
	[RuntimeInitializeOnLoadMethod( RuntimeInitializeLoadType.SubsystemRegistration )]
	static void ResetStatics()
	{
		Points.Clear();
		All.Clear();
	}
#endif

	public static string SelectedSpawnId
	{
		get
		{
			DebugDefinition def = GetDebugDefinition();
			if ( def == null || string.IsNullOrEmpty( def.debugSpawnId ) )
				return string.Empty;
			return def.debugSpawnId;
		}
		set
		{
			DebugDefinition def = GetDebugDefinition();
			if ( def == null )
				return;

			string id = value;
			if ( id == null )
				id = string.Empty;
			def.debugSpawnId = id;
		}
	}

	static DebugDefinition GetDebugDefinition()
	{
		if ( GameMode.Instance == null )
			return null;
		return GameMode.Instance.DebugDefinition;
	}

	public static IReadOnlyList<DebugSpawnPoint> GetAll() => All;

	public static int Count => All.Count;

	public static void Register( DebugSpawnPoint point )
	{
		if ( point == null || string.IsNullOrEmpty( point.Id ) )
			return;

		Points[ point.Id ] = point;
		if ( !All.Contains( point ) )
			All.Add( point );
	}

	public static void Unregister( DebugSpawnPoint point )
	{
		if ( point == null )
			return;

		if ( !string.IsNullOrEmpty( point.Id ) &&
		     Points.TryGetValue( point.Id, out DebugSpawnPoint existing ) &&
		     existing == point )
			Points.Remove( point.Id );

		All.Remove( point );
	}

	public static bool TryGet( string id, out DebugSpawnPoint point )
	{
		if ( string.IsNullOrEmpty( id ) )
		{
			point = null;
			return false;
		}

		return Points.TryGetValue( id, out point ) && point != null;
	}

	public static bool TryGetSelectedPose( out Vector3 position, out Quaternion rotation )
	{
		return TryGetPose( SelectedSpawnId, out position, out rotation );
	}

	public static bool TryGetPose( string spawnId, out Vector3 position, out Quaternion rotation )
	{
		position = Vector3.zero;
		rotation = Quaternion.identity;

		if ( string.IsNullOrEmpty( spawnId ) )
			return false;

		if ( TryGet( spawnId, out DebugSpawnPoint point ) && point != null && point.SpawnTransform != null )
		{
			position = point.SpawnTransform.position;
			rotation = FlattenYaw( point.SpawnTransform.rotation );
			return true;
		}

		return TryGetFallbackPose( spawnId, out position, out rotation );
	}

	public static bool TryGetFallbackPose( string volumeId, out Vector3 position, out Quaternion rotation )
	{
		position = Vector3.zero;
		rotation = Quaternion.identity;

		if ( !IsFallbackVolumeId( volumeId ) )
			return false;

		if ( !EventTargetRegistry.TryGetVolume( volumeId, out QuestVolume volume ) || volume == null )
			return false;

		Collider col = volume.GetComponent<Collider>();
		position = col != null ? col.bounds.center : volume.transform.position;
		rotation = FlattenYaw( volume.transform.rotation );
		return true;
	}

	public static bool SelectedSkipsIntro()
	{
		return SpawnSkipsIntro( SelectedSpawnId );
	}

	public static bool SpawnSkipsIntro( string spawnId )
	{
		if ( string.IsNullOrEmpty( spawnId ) )
			return false;

		if ( TryGet( spawnId, out DebugSpawnPoint point ) && point != null )
			return point.SkipIntro;

		return IsFallbackVolumeId( spawnId );
	}

	public static bool IsFallbackVolumeId( string volumeId )
	{
		if ( string.IsNullOrEmpty( volumeId ) )
			return false;

		for ( int i = 0; i < FallbackVolumes.Length; i++ )
		{
			if ( FallbackVolumes[ i ].VolumeId == volumeId )
				return true;
		}

		return false;
	}

	public static bool TryTeleportTo( string spawnId )
	{
		if ( !TryGetPose( spawnId, out Vector3 position, out Quaternion rotation ) )
			return false;

		PlayerController player = DebugOverlay.GetPlayer();
		if ( player == null )
			return false;

		player.TeleportTo( position, rotation );
		if ( SpawnSkipsIntro( spawnId ) )
			ApplySkipIntro();
		return true;
	}

	public static void ApplySkipIntroIfSelected()
	{
		if ( !SelectedSkipsIntro() )
			return;
		if ( !TryGetSelectedPose( out _, out _ ) )
			return;

		ApplySkipIntro();
	}

	public static void ApplySkipIntro()
	{
		WorldEventSystem system = WorldEventSystem.Instance;
		if ( system != null )
		{
			for ( int i = 0; i < IntroEventIds.Length; i++ )
				system.MarkFiredThisSessionOnly( IntroEventIds[ i ] );
		}

		HallwayFogBlend fog = HallwayFogBlend.Active;
		if ( fog != null )
			fog.DebugLockAtBase();

		LanternRevealSweepController.DebugSnapComplete();

		IReadOnlyList<LanternActivator> lanterns = LanternActivatorRegistry.GetAll();
		for ( int i = 0; i < lanterns.Count; i++ )
		{
			LanternActivator lantern = lanterns[ i ];
			if ( lantern != null )
				lantern.FadeToLit( true, 0f );
		}
	}

	static Quaternion FlattenYaw( Quaternion rotation )
	{
		Vector3 forward = rotation * Vector3.forward;
		forward.y = 0f;
		if ( forward.sqrMagnitude <= 0.0001f )
			return Quaternion.Euler( 0f, rotation.eulerAngles.y, 0f );
		return Quaternion.LookRotation( forward.normalized, Vector3.up );
	}
}
