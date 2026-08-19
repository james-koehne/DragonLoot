using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Fallback quest catalog used when the Addressables <see cref="QuestCatalogDefinition"/> asset is missing.
/// Also used by the editor installer to populate quest assets.
/// </summary>
public static class QuestCatalogFallback
{
	static QuestCatalogDefinition _runtime;

	public static QuestCatalogDefinition GetOrCreate()
	{
		if ( _runtime != null )
			return _runtime;

		QuestDefinition coins = CreateRuntime( "quest_coin_sorting" );
		PopulateCoinsSubquest( coins );
		QuestDefinition constellation = CreateRuntime( "quest_constellation" );
		PopulateConstellationSubquest( constellation );
		QuestDefinition artifacts = CreateRuntime( "quest_museum" );
		PopulateArtifactsSubquest( artifacts );
		QuestDefinition starting = CreateRuntime( "quest_starting" );
		PopulateStartingQuest( starting, coins, constellation, artifacts );
		QuestDefinition main = CreateRuntime( "quest_final" );
		PopulateMainQuest( main );

		_runtime = ScriptableObject.CreateInstance<QuestCatalogDefinition>();
		_runtime.name = "QuestCatalogDefinition_Runtime";
		_runtime.quests = new List<QuestDefinition> { starting, main };
		return _runtime;
	}

	static QuestDefinition CreateRuntime( string id )
	{
		QuestDefinition quest = ScriptableObject.CreateInstance<QuestDefinition>();
		quest.id = id;
		return quest;
	}

	public static void PopulateStartingQuest(
		QuestDefinition quest,
		QuestDefinition coins,
		QuestDefinition constellation,
		QuestDefinition artifacts )
	{
		quest.displayTitle = "Clear the Way";
		quest.onStartDialogue = new[]
		{
			Line( "Ah. Welcome, little Hoardkeeper." ),
			Line( "My hoard has grown somewhat... unwieldy.", 0.6f )
		};
		quest.onCompleteDialogue = new[]
		{
			Line( "That is much better." ),
			Line( "I will now unlock the door.", 0.4f )
		};
		quest.events = new[]
		{
			new QuestEvent
			{
				id = "volume_doorway_gold",
				dialogue = new[]
				{
					Line( "Oh." ),
					Line( "You'll need to clear that pile too." )
				},
				conditions = new[]
				{
					new QuestCondition { type = QuestConditionType.EnterVolume, targetId = QuestSceneAutoWire.IdVolumeDoorwayGold }
				}
			}
		};
		quest.objectives = new[]
		{
			new QuestObjective
			{
				id = "clear_and_sort",
				objectiveText = "Clear and sort all treasure",
				markerTargetId = QuestSceneAutoWire.IdStartingDoorPile,
				subObjectives = new[]
				{
					SubObjective(
						"gems_in_constellation",
						"Gems in constellation",
						QuestSceneAutoWire.IdConstellation,
						new QuestCondition
						{
							type = QuestConditionType.CompleteConstellation,
							targetId = QuestSceneAutoWire.IdConstellation,
							areaId = QuestSceneAutoWire.AreaStarting
						} ),
					SubObjective(
						"coins_stacked",
						"Coins stacked",
						QuestSceneAutoWire.IdCoinSorter,
						new QuestCondition
						{
							type = QuestConditionType.CompleteCoinDisplay,
							areaId = QuestSceneAutoWire.AreaStarting
						} ),
					SubObjective(
						"gold_bars_stacked",
						"Gold bars stacked",
						QuestSceneAutoWire.IdGoldBarTable,
						new QuestCondition
						{
							type = QuestConditionType.CompleteGoldBarDisplay,
							areaId = QuestSceneAutoWire.AreaStarting
						} ),
					SubObjective(
						"artifacts_sorted",
						"Artifacts sorted",
						QuestSceneAutoWire.IdMuseumTable,
						new QuestCondition
						{
							type = QuestConditionType.CompleteArtifactTable,
							areaId = QuestSceneAutoWire.AreaStarting
						} )
				}
			}
		};
		quest.subquests = new[] { coins, constellation, artifacts };
	}

	public static void PopulateCoinsSubquest( QuestDefinition quest )
	{
		quest.displayTitle = "Coins";
		quest.revealWhenParentObjectiveId = "clear_and_sort";
		quest.linkedParentObjectiveId = "coins_stacked";
		quest.onStartDialogue = System.Array.Empty<QuestDialogueLine>();
		quest.onCompleteDialogue = new[]
		{
			Line( "Much better. A respectable hoard should be sorted.", 0.4f )
		};
		quest.events = new[]
		{
			new QuestEvent
			{
				id = "first_coins_on_table",
				dialogue = new[]
				{
					Line( "Gold is rather easier to appreciate when it isn't scattered across the floor.", 0.4f )
				},
				conditions = new[]
				{
					new QuestCondition
					{
						type = QuestConditionType.PlaceOnOwner,
						areaId = QuestSceneAutoWire.AreaStarting,
						requiredCount = 1,
						filterByCategory = true,
						requiredCategory = TreasureCategory.Coin
					}
				}
			}
		};
		quest.objectives = new[]
		{
			new QuestObjective
			{
				id = "use_coin_sorter",
				objectiveText = "Use the coin sorter",
				markerTargetId = QuestSceneAutoWire.IdCoinSorter,
				optional = true,
				onCompleteDialogue = new[]
				{
					Line( "That is considerably faster than your manual sorting.", 0.4f )
				},
				conditions = new[]
				{
					new QuestCondition { type = QuestConditionType.UseCoinSorter, targetId = QuestSceneAutoWire.IdCoinSorter }
				}
			}
		};
	}

	public static void PopulateConstellationSubquest( QuestDefinition quest )
	{
		quest.displayTitle = "Constellation";
		quest.revealWhenParentObjectiveId = "clear_and_sort";
		quest.linkedParentObjectiveId = "gems_in_constellation";
		quest.onStartDialogue = System.Array.Empty<QuestDialogueLine>();
		quest.onCompleteDialogue = new[]
		{
			Line( "I haven't seen that in a very long time.", 1.2f ),
			Line( "Beautiful.", 0.6f )
		};
		quest.events = new[]
		{
			new QuestEvent
			{
				id = "near_constellation",
				dialogue = new[]
				{
					Line( "Gold is for stacking." ),
					Line( "Gems, however..." ),
					Line( "...are for admiring.", 0.5f )
				},
				conditions = new[]
				{
					new QuestCondition { type = QuestConditionType.EnterVolume, targetId = QuestSceneAutoWire.IdVolumeConstellation }
				}
			}
		};
		quest.objectives = System.Array.Empty<QuestObjective>();
	}

	public static void PopulateArtifactsSubquest( QuestDefinition quest )
	{
		quest.displayTitle = "Artifacts";
		quest.revealWhenParentObjectiveId = "clear_and_sort";
		quest.linkedParentObjectiveId = "artifacts_sorted";
		quest.onStartDialogue = System.Array.Empty<QuestDialogueLine>();
		quest.onCompleteDialogue = System.Array.Empty<QuestDialogueLine>();
		quest.events = new[]
		{
			new QuestEvent
			{
				id = "find_artifact",
				dialogue = new[]
				{
					Line( "These are the things worth remembering.", 0.5f )
				},
				conditions = new[]
				{
					new QuestCondition
					{
						type = QuestConditionType.PickupTreasure,
						filterByCategory = true,
						requiredCategory = TreasureCategory.Artifact,
						excludeGoldBars = true
					}
				}
			},
			new QuestEvent
			{
				id = "place_artifact",
				dialogue = new[]
				{
					Line( "I remember stealing that.", 1.1f ),
					Line( "Good times." )
				},
				conditions = new[]
				{
					new QuestCondition
					{
						type = QuestConditionType.PlaceOnOwner,
						areaId = QuestSceneAutoWire.AreaStarting,
						requiredCount = 1,
						filterByCategory = true,
						requiredCategory = TreasureCategory.Artifact,
						excludeGoldBars = true
					}
				}
			}
		};
		quest.objectives = System.Array.Empty<QuestObjective>();
	}

	public static void PopulateMainQuest( QuestDefinition quest )
	{
		quest.displayTitle = "The Hoard";
		quest.onStartDialogue = System.Array.Empty<QuestDialogueLine>();
		quest.onCompleteDialogue = System.Array.Empty<QuestDialogueLine>();
		quest.events = new[]
		{
			new QuestEvent
			{
				id = "enter_hallway",
				dialogue = new[]
				{
					Line( "Now, Hoardkeeper..." )
				},
				conditions = new[]
				{
					new QuestCondition { type = QuestConditionType.EnterVolume, targetId = QuestSceneAutoWire.IdVolumeHallwayEnter }
				}
			},
			new QuestEvent
			{
				id = "see_hoard",
				dialogue = new[]
				{
					Line( "I believe we have rather a lot of work to do.", 0.4f )
				},
				conditions = new[]
				{
					new QuestCondition { type = QuestConditionType.EnterVolume, targetId = QuestSceneAutoWire.IdVolumeHallwayEnd }
				}
			}
		};
		quest.objectives = new[]
		{
			new QuestObjective
			{
				id = "walk_hallway",
				objectiveText = "Walk to the end of the hallway",
				markerTargetId = QuestSceneAutoWire.IdVolumeHallwayEnd,
				conditions = new[]
				{
					new QuestCondition { type = QuestConditionType.EnterVolume, targetId = QuestSceneAutoWire.IdVolumeHallwayEnd }
				}
			},
			new QuestObjective
			{
				id = "organise_hoard",
				objectiveText = "Clear and sort the whole hoard",
				conditions = new[]
				{
					new QuestCondition { type = QuestConditionType.SectionSorted, sectionId = "Section 1" }
				}
			}
		};
	}

	public static void AssignMainQuestStinger( QuestDefinition quest, AudioClip stinger )
	{
		if ( quest == null || quest.events == null )
			return;
		for ( int i = 0; i < quest.events.Length; i++ )
		{
			if ( quest.events[ i ] != null && quest.events[ i ].id == "see_hoard" )
			{
				quest.events[ i ].stinger = stinger;
				quest.events[ i ].stingerVol = 0.85f;
			}
		}
	}

	static QuestObjective SubObjective( string id, string text, string markerId, QuestCondition condition )
	{
		return new QuestObjective
		{
			id = id,
			objectiveText = text,
			markerTargetId = markerId,
			conditions = new[] { condition }
		};
	}

	public static QuestDialogueLine Line( string text, float pauseAfter = 0.25f )
	{
		return new QuestDialogueLine
		{
			speaker = "Dragon",
			text = text,
			pauseAfter = pauseAfter
		};
	}
}
