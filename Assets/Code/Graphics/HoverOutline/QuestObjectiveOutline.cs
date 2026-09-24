using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Keeps quest / tutorial target meshes registered for the quest outline channel while TutorialHud provides roots.
/// Tutorial override roots use tutorialOutline colour; otherwise quest cyan.
/// </summary>
public class QuestObjectiveOutline : MonoBehaviour
{
	static QuestObjectiveOutline _instance;
	static readonly List<Renderer> RendererScratch = new List<Renderer>( 32 );
	static readonly HashSet<int> SeenRoots = new HashSet<int>();

	bool _subscribed;
	bool _hadTutorialOverride;

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
		QuestOutlineRegistrar.Clear();
	}

	void OnDestroy()
	{
		if ( _instance == this )
			_instance = null;

		Unsubscribe();
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
		SeenRoots.Clear();

		if ( !TutorialHud.HasOutlineRoots )
		{
			QuestOutlineRegistrar.Clear();
			return;
		}

		TutorialHud.CollectOutlineRoots( AppendRootRenderers );

		HoverOutlineVisualSettings settings = ResolveQuestSettings();
		QuestOutlineRegistrar.SetTargets( RendererScratch, settings );
	}

	void AppendRootRenderers( Transform root )
	{
		if ( root == null )
			return;

		int id = root.GetInstanceID();
		if ( !SeenRoots.Add( id ) )
			return;

		HoverOutlineTargetUtility.AppendQuestOutlineRenderers( root.gameObject, RendererScratch );
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
