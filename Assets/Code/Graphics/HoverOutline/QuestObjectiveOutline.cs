using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Keeps quest / tutorial target meshes registered for the quest outline channel while TutorialHud provides roots.
/// Tutorial override roots use tutorialOutline colour; otherwise quest cyan.
/// Fades out between <see cref="FadeStartMeters"/> and <see cref="FadeOutMeters"/>; once fully out,
/// stays hidden until the player remains at least <see cref="FadeOutMeters"/> away for <see cref="ShowAfterAwaySeconds"/>.
/// </summary>
public class QuestObjectiveOutline : MonoBehaviour
{
	const float FadeStartMeters = 15f;
	const float FadeOutMeters = 10f;
	const float ShowAfterAwaySeconds = 5f;

	static QuestObjectiveOutline _instance;
	static readonly List<Renderer> RendererScratch = new List<Renderer>( 32 );
	static readonly List<Renderer> ActiveRenderers = new List<Renderer>( 32 );
	static readonly List<Transform> ActiveRoots = new List<Transform>( 8 );
	static readonly HashSet<int> SeenRoots = new HashSet<int>();

	bool _subscribed;
	bool _hadTutorialOverride;
	bool _proximityHidden;
	float _awayTimer;
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
		else
			ApplyProximityFade();
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
		ApplyProximityFade();
	}

	void ApplyProximityFade()
	{
		if ( _baseSettings == null || ActiveRenderers.Count == 0 || ActiveRoots.Count == 0 )
		{
			QuestOutlineRegistrar.Clear();
			return;
		}

		float nearest = ResolveNearestDistance();
		UpdateProximityLatch( nearest );
		float fade = EvaluateProximityFade( nearest );
		if ( fade <= 0.001f )
		{
			QuestOutlineRegistrar.Clear();
			return;
		}

		HoverOutlineVisualSettings faded = _baseSettings.Clone();
		faded.outlineIntensity *= fade;
		Color c = faded.outlineColor;
		faded.outlineColor = new Color( c.r, c.g, c.b, c.a * fade );
		faded.Validate();
		QuestOutlineRegistrar.SetTargets( ActiveRenderers, faded );
	}

	float ResolveNearestDistance()
	{
		float nearest = float.MaxValue;
		for ( int i = 0; i < ActiveRoots.Count; i++ )
		{
			Transform root = ActiveRoots[ i ];
			if ( root == null )
				continue;

			float distance = TutorialHudDistance.HorizontalTo( root.position );
			if ( distance < nearest )
				nearest = distance;
		}

		return nearest;
	}

	void UpdateProximityLatch( float nearest )
	{
		if ( nearest == float.MaxValue )
		{
			_proximityHidden = false;
			_awayTimer = 0f;
			return;
		}

		if ( nearest <= FadeOutMeters )
		{
			_proximityHidden = true;
			_awayTimer = 0f;
			return;
		}

		if ( !_proximityHidden )
			return;

		_awayTimer += Time.deltaTime;
		if ( _awayTimer >= ShowAfterAwaySeconds )
		{
			_proximityHidden = false;
			_awayTimer = 0f;
		}
	}

	float EvaluateProximityFade( float nearest )
	{
		if ( _proximityHidden || nearest == float.MaxValue )
			return 0f;

		if ( nearest >= FadeStartMeters )
			return 1f;
		if ( nearest <= FadeOutMeters )
			return 0f;

		return Mathf.SmoothStep( 0f, 1f, Mathf.InverseLerp( FadeOutMeters, FadeStartMeters, nearest ) );
	}

	void AppendRootRenderers( Transform root )
	{
		if ( root == null )
			return;

		int id = root.GetInstanceID();
		if ( !SeenRoots.Add( id ) )
			return;

		ActiveRoots.Add( root );
		HoverOutlineTargetUtility.AppendQuestOutlineRenderers( root.gameObject, RendererScratch );
	}

	void ClearCaches()
	{
		ActiveRenderers.Clear();
		ActiveRoots.Clear();
		_baseSettings = null;
		_proximityHidden = false;
		_awayTimer = 0f;
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
