#if UNITY_EDITOR
using System.Collections.Generic;

using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Creates quest definition assets and wires Level scene QuestTarget / QuestVolume components.
/// </summary>
[InitializeOnLoad]
static class QuestCatalogInstaller
{
	const string QuestsFolder = "Assets/Definitions/Quests";
	const string CatalogPath = "Assets/Definitions/QuestCatalogDefinition.asset";
	const string LevelScenePath = "Assets/Scenes/Level.unity";

	static QuestCatalogInstaller()
	{
		EditorApplication.delayCall += () => EnsureCatalog( forceRebuildContent: false );
	}

	[MenuItem( DragonLootMenus.QuestsInstallCatalog, priority = 300 )]
	static void InstallCatalogFromMenu()
	{
		QuestCatalogDefinition catalog = EnsureCatalog( forceRebuildContent: true );
		if ( catalog != null )
		{
			Selection.activeObject = catalog;
			EditorGUIUtility.PingObject( catalog );
		}
	}

	[MenuItem( DragonLootMenus.QuestsWireLevel, priority = 301 )]
	static void WireLevelFromMenu()
	{
		EnsureCatalog( forceRebuildContent: false );
		CreateStandaloneQuestSetupInLevel();
	}

	/// <summary>Batch/CI entry: DragonLoot/Quests install + Level standalone setup.</summary>
	public static void BatchInstall()
	{
		EnsureCatalog( forceRebuildContent: true );
		CreateStandaloneQuestSetupInLevel();
		AssetDatabase.SaveAssets();
		EditorSceneManager.SaveOpenScenes();
	}

	/// <summary>
	/// Creates a <c>QuestSetup</c> root with standalone QuestTarget / QuestVolume objects
	/// the designer can move / parent onto interactables.
	/// </summary>
	public static void CreateStandaloneQuestSetupInLevel()
	{
		Scene scene = EditorSceneManager.OpenScene( LevelScenePath, OpenSceneMode.Single );
		CreateStandaloneQuestSetup( scene );
		EditorSceneManager.MarkSceneDirty( scene );
		EditorSceneManager.SaveScene( scene );
		Debug.Log( "Created standalone QuestSetup objects in Level scene." );
	}

	static void CreateStandaloneQuestSetup( Scene scene )
	{
		GameObject existingRoot = GameObject.Find( "QuestSetup" );
		if ( existingRoot != null )
			Undo.DestroyObjectImmediate( existingRoot );

		GameObject root = new GameObject( "QuestSetup" );
		Undo.RegisterCreatedObjectUndo( root, "Create QuestSetup" );
		SceneManager.MoveGameObjectToScene( root, scene );

		GameObject targetsRoot = new GameObject( "QuestTargets" );
		Undo.RegisterCreatedObjectUndo( targetsRoot, "Create QuestTargets" );
		targetsRoot.transform.SetParent( root.transform, false );

		GameObject volumesRoot = new GameObject( "QuestVolumes" );
		Undo.RegisterCreatedObjectUndo( volumesRoot, "Create QuestVolumes" );
		volumesRoot.transform.SetParent( root.transform, false );

		string[] targetIds =
		{
			QuestSceneAutoWire.IdStartingDoorPile,
			QuestSceneAutoWire.IdStartingSortingPlinth,
			QuestSceneAutoWire.IdCoinSorter,
			QuestSceneAutoWire.IdCoinPlinth,
			QuestSceneAutoWire.IdConstellation,
			QuestSceneAutoWire.IdMuseumTable
		};

		for ( int i = 0; i < targetIds.Length; i++ )
		{
			string id = targetIds[ i ];
			GameObject go = new GameObject( "QuestTarget_" + id );
			Undo.RegisterCreatedObjectUndo( go, "Create QuestTarget" );
			go.transform.SetParent( targetsRoot.transform, false );
			go.transform.localPosition = new Vector3( i * 3f, 1f, 0f );

			QuestMarkerAnchor anchor = go.AddComponent<QuestMarkerAnchor>();
			QuestTarget target = go.AddComponent<QuestTarget>();
			target.SetId( id );
			SerializedObject so = new SerializedObject( target );
			so.FindProperty( "markerAnchor" ).objectReferenceValue = anchor;
			so.ApplyModifiedPropertiesWithoutUndo();
		}

		CreateVolume( volumesRoot.transform, QuestSceneAutoWire.IdVolumeDoorwayGold, new Vector3( 4f, 3f, 4f ), new Vector3( 0f, 1.5f, 6f ) );
		CreateVolume( volumesRoot.transform, QuestSceneAutoWire.IdVolumeCoinSorter, new Vector3( 5f, 3f, 5f ), new Vector3( 3f, 1.5f, 6f ) );
		CreateVolume( volumesRoot.transform, QuestSceneAutoWire.IdVolumeConstellation, new Vector3( 5f, 3f, 5f ), new Vector3( 6f, 1.5f, 6f ) );
		CreateVolume( volumesRoot.transform, QuestSceneAutoWire.IdVolumeMuseum, new Vector3( 5f, 3f, 5f ), new Vector3( 9f, 1.5f, 6f ) );

		Selection.activeGameObject = root;
	}

	static void CreateVolume( Transform parent, string id, Vector3 size, Vector3 localPosition )
	{
		GameObject go = new GameObject( "QuestVolume_" + id );
		Undo.RegisterCreatedObjectUndo( go, "Create QuestVolume" );
		go.transform.SetParent( parent, false );
		go.transform.localPosition = localPosition;

		BoxCollider box = go.AddComponent<BoxCollider>();
		box.isTrigger = true;
		box.size = size;

		QuestVolume volume = go.AddComponent<QuestVolume>();
		volume.SetId( id );
	}

	static QuestCatalogDefinition EnsureCatalog( bool forceRebuildContent )
	{
		EnsureFolder( "Assets/Definitions" );
		EnsureFolder( QuestsFolder );

		QuestDefinition starting = EnsureQuestAsset( QuestsFolder + "/Quest_Starting.asset", "quest_starting", BuildStartingQuest, forceRebuildContent );
		QuestDefinition coin = EnsureQuestAsset( QuestsFolder + "/Quest_CoinSorting.asset", "quest_coin_sorting", BuildCoinSortingQuest, forceRebuildContent );
		QuestDefinition constellation = EnsureQuestAsset( QuestsFolder + "/Quest_Constellation.asset", "quest_constellation", BuildConstellationQuest, forceRebuildContent );
		QuestDefinition museum = EnsureQuestAsset( QuestsFolder + "/Quest_Museum.asset", "quest_museum", BuildMuseumQuest, forceRebuildContent );
		QuestDefinition finalQuest = EnsureQuestAsset( QuestsFolder + "/Quest_Final.asset", "quest_final", BuildFinalQuest, forceRebuildContent );

		QuestCatalogDefinition catalog = AssetDatabase.LoadAssetAtPath<QuestCatalogDefinition>( CatalogPath );
		if ( catalog == null )
		{
			catalog = ScriptableObject.CreateInstance<QuestCatalogDefinition>();
			catalog.name = "QuestCatalogDefinition";
			AssetDatabase.CreateAsset( catalog, CatalogPath );
		}

		catalog.quests = new List<QuestDefinition>
		{
			starting,
			coin,
			constellation,
			museum,
			finalQuest
		};
		EditorUtility.SetDirty( catalog );
		AssetDatabase.SaveAssets();

		RegisterDefinitionAddressable( CatalogPath, "QuestCatalogDefinition" );
		return catalog;
	}

	delegate void QuestBuilder( QuestDefinition quest );

	static QuestDefinition EnsureQuestAsset( string path, string id, QuestBuilder builder, bool forceRebuild )
	{
		QuestDefinition quest = AssetDatabase.LoadAssetAtPath<QuestDefinition>( path );
		if ( quest == null )
		{
			quest = ScriptableObject.CreateInstance<QuestDefinition>();
			quest.name = System.IO.Path.GetFileNameWithoutExtension( path );
			AssetDatabase.CreateAsset( quest, path );
			forceRebuild = true;
		}

		if ( forceRebuild || string.IsNullOrEmpty( quest.id ) || quest.steps == null || quest.steps.Length == 0 )
		{
			Undo.RecordObject( quest, "Build Quest Content" );
			quest.id = id;
			builder( quest );
			EditorUtility.SetDirty( quest );
		}

		return quest;
	}

	static void BuildStartingQuest( QuestDefinition quest )
	{
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
	}

	static void BuildCoinSortingQuest( QuestDefinition quest )
	{
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
	}

	static void BuildConstellationQuest( QuestDefinition quest )
	{
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
	}

	static void BuildMuseumQuest( QuestDefinition quest )
	{
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
	}

	static void BuildFinalQuest( QuestDefinition quest )
	{
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
				markerTargetId = null,
				conditions = new[]
				{
					new QuestCondition { type = QuestConditionType.SectionSorted, sectionId = "Section 1" }
				}
			}
		};
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

	static void EnsureFolder( string path )
	{
		if ( AssetDatabase.IsValidFolder( path ) )
			return;

		string parent = System.IO.Path.GetDirectoryName( path ).Replace( '\\', '/' );
		string name = System.IO.Path.GetFileName( path );
		if ( !AssetDatabase.IsValidFolder( parent ) )
			EnsureFolder( parent );
		AssetDatabase.CreateFolder( parent, name );
	}

	static void RegisterDefinitionAddressable( string assetPath, string address )
	{
		AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
		if ( settings == null )
			return;

		string guid = AssetDatabase.AssetPathToGUID( assetPath );
		if ( string.IsNullOrEmpty( guid ) )
			return;

		AddressableAssetEntry entry = settings.FindAssetEntry( guid );
		if ( entry == null )
			entry = settings.CreateOrMoveEntry( guid, settings.DefaultGroup );

		entry.SetAddress( address );
		if ( !entry.labels.Contains( "Definition" ) )
			entry.SetLabel( "Definition", true, true );
		settings.SetDirty( AddressableAssetSettings.ModificationEvent.EntryModified, entry, true );
	}
}
#endif
