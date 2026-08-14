using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Fallback quest catalog used when the Addressables <see cref="QuestCatalogDefinition"/> asset is missing.
/// </summary>
public static class QuestCatalogFallback
{
	static QuestCatalogDefinition _runtime;

	public static QuestCatalogDefinition GetOrCreate()
	{
		if ( _runtime != null )
			return _runtime;

		_runtime = ScriptableObject.CreateInstance<QuestCatalogDefinition>();
		_runtime.name = "QuestCatalogDefinition_Runtime";
		_runtime.quests = new List<QuestDefinition>
		{
			BuildStarting(),
			BuildCoinSorting(),
			BuildConstellation(),
			BuildMuseum(),
			BuildFinal()
		};
		return _runtime;
	}

	static QuestDefinition BuildStarting()
	{
		QuestDefinition quest = ScriptableObject.CreateInstance<QuestDefinition>();
		quest.id = "quest_starting";
		quest.displayTitle = "Clear the Way";
		quest.onStartDialogue = new[]
		{
			Line( "Ah. There you are, little Hoardkeeper." ),
			Line( "I have been... busy.", 0.6f )
		};
		quest.onCompleteDialogue = new[]
		{
			Line( "Now, Hoardkeeper..." ),
			Line( "I believe we have rather a lot of work to do.", 0.4f )
		};
		quest.steps = new[]
		{
			new QuestStep
			{
				id = "view_doorway_gold",
				objectiveText = "Look toward the gold blocking the doorway",
				markerTargetId = QuestSceneAutoWire.IdVolumeDoorwayGold,
				onCompleteDialogue = new[]
				{
					Line( "Oh." ),
					Line( "My hoard has grown somewhat... unwieldy." ),
					Line( "You'll need to dig your way out...", 0.5f )
				},
				conditions = new[]
				{
					new QuestCondition { type = QuestConditionType.EnterVolume, targetId = QuestSceneAutoWire.IdVolumeDoorwayGold }
				}
			},
			new QuestStep
			{
				id = "clear_and_sort",
				objectiveText = "Clear the doorway pile and place the loot on the sorting plinth",
				markerTargetId = QuestSceneAutoWire.IdStartingDoorPile,
				conditions = new[]
				{
					new QuestCondition { type = QuestConditionType.ClearPile, targetId = QuestSceneAutoWire.IdStartingDoorPile },
					new QuestCondition
					{
						type = QuestConditionType.PlaceOnOwner,
						targetId = QuestSceneAutoWire.IdStartingSortingPlinth,
						requiredCount = 5
					}
				}
			}
		};
		return quest;
	}

	static QuestDefinition BuildCoinSorting()
	{
		QuestDefinition quest = ScriptableObject.CreateInstance<QuestDefinition>();
		quest.id = "quest_coin_sorting";
		quest.displayTitle = "Sort the Gold";
		quest.onStartDialogue = new[]
		{
			Line( "Gold is rather easier to appreciate when it isn't scattered across the floor.", 0.4f )
		};
		quest.onCompleteDialogue = new[]
		{
			Line( "Much better. A respectable hoard should be sorted.", 0.4f )
		};
		quest.steps = new[]
		{
			new QuestStep
			{
				id = "find_sorter",
				objectiveText = "Find the coin sorter",
				markerTargetId = QuestSceneAutoWire.IdCoinSorter,
				conditions = new[]
				{
					new QuestCondition { type = QuestConditionType.EnterVolume, targetId = QuestSceneAutoWire.IdVolumeCoinSorter }
				}
			},
			new QuestStep
			{
				id = "use_sorter",
				objectiveText = "Use the coin sorter",
				markerTargetId = QuestSceneAutoWire.IdCoinSorter,
				onCompleteDialogue = new[]
				{
					Line( "That is considerably faster than your manual sorting.", 0.4f )
				},
				conditions = new[]
				{
					new QuestCondition { type = QuestConditionType.UseCoinSorter, targetId = QuestSceneAutoWire.IdCoinSorter }
				}
			},
			new QuestStep
			{
				id = "fill_coin_plinth",
				objectiveText = "Bring sorted stacks to the coin plinths",
				markerTargetId = QuestSceneAutoWire.IdCoinPlinth,
				conditions = new[]
				{
					new QuestCondition { type = QuestConditionType.CompleteCoinDisplay, targetId = QuestSceneAutoWire.IdCoinPlinth }
				}
			}
		};
		return quest;
	}

	static QuestDefinition BuildConstellation()
	{
		QuestDefinition quest = ScriptableObject.CreateInstance<QuestDefinition>();
		quest.id = "quest_constellation";
		quest.displayTitle = "Admire the Gems";
		quest.onStartDialogue = new[]
		{
			Line( "Gold is for counting." ),
			Line( "Gems, however..." ),
			Line( "...are for admiring.", 0.5f )
		};
		quest.onCompleteDialogue = new[]
		{
			Line( "I haven't seen that in a very long time.", 1.2f ),
			Line( "Beautiful.", 0.6f )
		};
		quest.steps = new[]
		{
			new QuestStep
			{
				id = "find_constellation",
				objectiveText = "Find the constellation wall",
				markerTargetId = QuestSceneAutoWire.IdConstellation,
				conditions = new[]
				{
					new QuestCondition { type = QuestConditionType.EnterVolume, targetId = QuestSceneAutoWire.IdVolumeConstellation }
				}
			},
			new QuestStep
			{
				id = "fill_constellation",
				objectiveText = "Fill the constellation with the required gems",
				markerTargetId = QuestSceneAutoWire.IdConstellation,
				conditions = new[]
				{
					new QuestCondition { type = QuestConditionType.CompleteConstellation, targetId = QuestSceneAutoWire.IdConstellation }
				}
			}
		};
		return quest;
	}

	static QuestDefinition BuildMuseum()
	{
		QuestDefinition quest = ScriptableObject.CreateInstance<QuestDefinition>();
		quest.id = "quest_museum";
		quest.displayTitle = "Remember the Treasures";
		quest.onStartDialogue = new[]
		{
			Line( "And these..." ),
			Line( "These are the things worth remembering.", 0.5f )
		};
		quest.onCompleteDialogue = new[]
		{
			Line( "Hmm." ),
			Line( "I remember stealing that.", 1.1f ),
			Line( "Good times.", 0.5f )
		};
		quest.steps = new[]
		{
			new QuestStep
			{
				id = "find_museum",
				objectiveText = "Find an artifact and bring it to the museum",
				markerTargetId = QuestSceneAutoWire.IdMuseumTable,
				conditions = new[]
				{
					new QuestCondition { type = QuestConditionType.EnterVolume, targetId = QuestSceneAutoWire.IdVolumeMuseum }
				}
			},
			new QuestStep
			{
				id = "place_artifact",
				objectiveText = "Place the artifact in its correct museum spot",
				markerTargetId = QuestSceneAutoWire.IdMuseumTable,
				conditions = new[]
				{
					new QuestCondition { type = QuestConditionType.CompleteArtifactTable, targetId = QuestSceneAutoWire.IdMuseumTable }
				}
			}
		};
		return quest;
	}

	static QuestDefinition BuildFinal()
	{
		QuestDefinition quest = ScriptableObject.CreateInstance<QuestDefinition>();
		quest.id = "quest_final";
		quest.displayTitle = "Organise the Hoard";
		quest.onStartDialogue = new[]
		{
			Line( "You have cleared the way." ),
			Line( "You have counted the gold." ),
			Line( "You have arranged the gems." ),
			Line( "And you have given my treasures somewhere worthy of them.", 1.2f ),
			Line( "I believe you understand your duties.", 0.5f )
		};
		quest.steps = new[]
		{
			new QuestStep
			{
				id = "organise_hoard",
				objectiveText = "Organise the dragon's hoard — clear and sort all gold piles",
				conditions = new[]
				{
					new QuestCondition { type = QuestConditionType.SectionSorted, sectionId = "Section 1" }
				}
			}
		};
		return quest;
	}

	static QuestDialogueLine Line( string text, float pauseAfter = 0.25f )
	{
		return new QuestDialogueLine
		{
			speaker = "Dragon",
			text = text,
			pauseAfter = pauseAfter
		};
	}
}
