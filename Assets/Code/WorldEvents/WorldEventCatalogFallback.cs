using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Fallback intro catalog when the Addressables <see cref="WorldEventCatalogDefinition"/> is missing or empty.
/// </summary>
public static class WorldEventCatalogFallback
{
	static WorldEventCatalogDefinition _runtime;

	public static WorldEventCatalogDefinition GetOrCreate()
	{
		if ( _runtime != null )
			return _runtime;

		_runtime = ScriptableObject.CreateInstance<WorldEventCatalogDefinition>();
		_runtime.name = "WorldEventCatalogDefinition_Runtime";
		_runtime.events = new List<WorldEventDefinition>
		{
			CreateIntroWelcome(),
			CreateIntroHallway(),
			CreateIntroLedge()
		};
		return _runtime;
	}

	static WorldEventDefinition CreateIntroWelcome()
	{
		WorldEventDefinition evt = Create( "intro_welcome" );
		evt.tags = new[] { "intro" };
		evt.conditions = new[]
		{
			new WorldEventCondition { type = WorldEventConditionType.PlayerGameplayInput },
			new WorldEventCondition { type = WorldEventConditionType.ElapsedUnscaledSeconds, delaySeconds = 2f }
		};
		evt.actions = new[]
		{
			DialogueAction( Line( "Ah. Welcome, little Hoardkeeper." ) )
		};
		return evt;
	}

	static WorldEventDefinition CreateIntroHallway()
	{
		WorldEventDefinition evt = Create( "intro_hallway" );
		evt.tags = new[] { "intro" };
		evt.conditions = new[]
		{
			new WorldEventCondition
			{
				type = WorldEventConditionType.EnterVolume,
				targetId = EventSceneAutoWire.IdVolumeHallwayEnter
			}
		};
		evt.actions = new[]
		{
			DialogueAction( Line( "My hoard has grown somewhat... unwieldy." ) )
		};
		return evt;
	}

	static WorldEventDefinition CreateIntroLedge()
	{
		WorldEventDefinition evt = Create( "intro_ledge" );
		evt.tags = new[] { "intro" };
		evt.conditions = new[]
		{
			new WorldEventCondition
			{
				type = WorldEventConditionType.EnterVolume,
				targetId = EventSceneAutoWire.IdVolumeHallwayEnd
			}
		};
		evt.actions = new[]
		{
			BrakePlayerMovementAction( 1f ),
			DialogueAction( Line( "I believe we have rather a lot of work to do." ) ),
			AudioAction( ResolveEpicRiserClip() ),
			LanternRevealSweepAction( LanternActivator.IntroLedgeRevealId ),
			CinematicPresentationAction( CinematicPresentationController.IntroLedgePresentationId, 10f )
		};
		return evt;
	}

	static AudioClip ResolveEpicRiserClip()
	{
#if UNITY_EDITOR
		return UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>( "Assets/Audio/SFX/Stingers/EpicRiser_TEMP_DELETE.wav" );
#else
		return null;
#endif
	}

	static WorldEventAction LanternRevealSweepAction( string revealId )
	{
		return new WorldEventAction
		{
			type = WorldEventActionType.LanternRevealSweep,
			lanternRevealId = revealId
		};
	}

	static WorldEventAction BrakePlayerMovementAction( float duration )
	{
		return new WorldEventAction
		{
			type = WorldEventActionType.BrakePlayerMovement,
			playerBrakeDuration = duration
		};
	}

	static WorldEventAction CinematicPresentationAction( string presentationId, float playerMovementLockDuration = 0f )
	{
		return new WorldEventAction
		{
			type = WorldEventActionType.CinematicPresentation,
			cinematicPresentationId = presentationId,
			cinematicPlayerMovementLockDuration = playerMovementLockDuration
		};
	}

	static WorldEventAction AudioAction( AudioClip clip )
	{
		return new WorldEventAction
		{
			type = WorldEventActionType.PlayAudio,
			audioClip = clip,
			audioVolumeMin = 1f,
			audioVolumeMax = 1f,
			audioPitchMin = 1f,
			audioPitchMax = 1f,
			audioSpatialBlend = 0f
		};
	}

	static WorldEventDefinition Create( string id )
	{
		WorldEventDefinition evt = ScriptableObject.CreateInstance<WorldEventDefinition>();
		evt.id = id;
		evt.name = id;
		return evt;
	}

	static WorldEventAction DialogueAction( params DragonDialogueLine[] lines )
	{
		return new WorldEventAction
		{
			type = WorldEventActionType.Dialogue,
			dialogue = lines
		};
	}

	static DragonDialogueLine Line( string text, float pauseAfter = 0f )
	{
		return new DragonDialogueLine
		{
			speaker = "Dragon",
			text = text,
			pauseAfter = pauseAfter
		};
	}
}
