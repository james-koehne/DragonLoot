using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Keeps quest / tutorial target meshes registered for the quest outline channel while TutorialHud provides roots.
/// Tutorial override roots use tutorialOutline colour; otherwise quest cyan.
/// Objective outlines stay visible at any distance (including buildable ghosts outside build mode).
/// </summary>
public class QuestObjectiveOutline : MonoBehaviour
{
	static QuestObjectiveOutline _instance;
	static readonly List<Renderer> RendererScratch = new List<Renderer>( 32 );
	static readonly List<Renderer> ActiveRenderers = new List<Renderer>( 32 );
	static readonly List<Transform> ActiveRoots = new List<Transform>( 8 );
	static readonly HashSet<EntityId> SeenRoots = new HashSet<EntityId>();

	bool _subscribed;
	bool _hadTutorialOverride;
	HoverOutlineVisualSettings _baseSettings;

	public static QuestObjectiveOutline EnsureExists()
	{
		if ( _instance != null )
			return _instance;

		GameObject go = new GameObject( "QuestObjectiveOutline" );
		_instance = go.AddComponent<QuestObjectiveOutline>();
		DontDestroyOnLoad( go );
		return _instance;
	}

	void Awake()
	{
		if ( _instance != null && _instance != this )
		{
			Destroy( gameObject );
			return;
		}

		_instance = this;
	}

	void OnEnable()
	{
		Subscribe();
		Refresh();
	}

	void OnDisable()
	{
		Unsubscribe();
		ClearCaches();
		QuestOutlineRegistrar.Clear();
	}

	void OnDestroy()
	{
		if ( _instance == this )
			_instance = null;

		Unsubscribe();
		ClearCaches();
		QuestOutlineRegistrar.Clear();
	}

	void Subscribe()
	{
		if ( _subscribed )
			return;
		EventBus.Subscribe<TutorialHudChangedEvent>( OnTutorialHud );
		EventBus.Subscribe<PouchChangedEvent>( OnPouchChanged );
		_subscribed = true;
	}

	void Unsubscribe()
	{
		if ( !_subscribed )
			return;
		EventBus.Unsubscribe<TutorialHudChangedEvent>( OnTutorialHud );
		EventBus.Unsubscribe<PouchChangedEvent>( OnPouchChanged );
		_subscribed = false;
	}

	void OnTutorialHud( TutorialHudChangedEvent evt )
	{
		Refresh();
	}

	void OnPouchChanged( PouchChangedEvent evt )
	{
		Refresh();
	}

	void LateUpdate()
	{
		bool tutorialOverride = TutorialHud.HasTutorialOutlineOverride;
		if ( tutorialOverride != _hadTutorialOverride || Time.frameCount % 15 == 0 )
			Refresh();
		_hadTutorialOverride = tutorialOverride;
	}

	void Refresh()
	{
		RendererScratch.Clear();
		ActiveRoots.Clear();
		SeenRoots.Clear();

		if ( !TutorialHud.HasOutlineRoots )
		{
			ClearCaches();
			QuestOutlineRegistrar.Clear();
			return;
		}

		TutorialHud.CollectOutlineRoots( AppendRootRenderers );

		ActiveRenderers.Clear();
		ActiveRenderers.AddRange( RendererScratch );

		_baseSettings = ResolveQuestSettings();
		ApplyOutline();
	}

	void ApplyOutline()
	{
		if ( _baseSettings == null || ActiveRenderers.Count == 0 || ActiveRoots.Count == 0 )
		{
			QuestOutlineRegistrar.Clear();
			return;
		}

		HoverOutlineVisualSettings settings = _baseSettings.Clone();
		settings.Validate();
		QuestOutlineRegistrar.SetTargets( ActiveRenderers, settings );
	}

	void AppendRootRenderers( Transform root )
	{
		if ( root == null )
			return;

		EntityId id = root.GetEntityId();
		if ( !SeenRoots.Add( id ) )
			return;

		ActiveRoots.Add( root );

		BuildableObject buildable = root.GetComponentInParent<BuildableObject>();
		if ( buildable != null && buildable.ObjectiveOutlineRequested && !buildable.IsBuilt )
		{
			IReadOnlyList<Renderer> ghostRenderers = buildable.GetGhostOutlineRenderers();
			for ( int i = 0; i < ghostRenderers.Count; i++ )
			{
				Renderer renderer = ghostRenderers[ i ];
				if ( renderer != null && !RendererScratch.Contains( renderer ) )
					RendererScratch.Add( renderer );
			}

			if ( ghostRenderers.Count > 0 )
				return;
		}

		HoverOutlineTargetUtility.AppendQuestOutlineRenderers( root.gameObject, RendererScratch );
	}

	void ClearCaches()
	{
		ActiveRenderers.Clear();
		ActiveRoots.Clear();
		_baseSettings = null;
	}

	static HoverOutlineVisualSettings ResolveQuestSettings()
	{
		PlayerInteractionDefinition def = null;
		def = RuntimeDefinition.Resolve( ref def );
		if ( TutorialHud.HasTutorialOutlineOverride )
		{
			if ( def != null && def.tutorialOutline != null )
			{
				HoverOutlineVisualSettings tutorialClone = def.tutorialOutline.Clone();
				tutorialClone.Validate();
				return tutorialClone;
			}

			return HoverOutlineVisualSettings.DefaultTutorial();
		}

		if ( def != null && def.questOutline != null )
		{
			HoverOutlineVisualSettings clone = def.questOutline.Clone();
			clone.Validate();
			return clone;
		}

		return HoverOutlineVisualSettings.DefaultQuest();
	}
}
