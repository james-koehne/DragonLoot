using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

using FeedbackSystem;

/// <summary>
/// Runtime demo that builds targets, feedback chains, and UI buttons.
/// Open via FeedbackSystem &gt; Open Demo Scene, or add this component to an empty scene and press Play.
/// </summary>
public class FeedbackDemoController : MonoBehaviour
{
	Canvas _canvas;
	RectTransform _buttonRoot;
	int _buttonIndex;
	GameObject _spawnPrefab;
	Light _eventLight;

	void Awake()
	{
		EnsureCamera();
		EnsureLight();
		BuildUi();
		BuildSpawnPrefab();
		BuildDemos();
	}

	void EnsureCamera()
	{
		if ( Camera.main != null )
		{
			Camera.main.transform.SetPositionAndRotation( new Vector3( 0f, 3.5f, -8f ), Quaternion.Euler( 18f, 0f, 0f ) );
			return;
		}

		GameObject cameraObject = new GameObject( "Main Camera" );
		cameraObject.tag = "MainCamera";
		Camera camera = cameraObject.AddComponent<Camera>();
		camera.clearFlags = CameraClearFlags.SolidColor;
		camera.backgroundColor = new Color( 0.12f, 0.13f, 0.16f );
		cameraObject.AddComponent<AudioListener>();
		cameraObject.transform.SetPositionAndRotation( new Vector3( 0f, 3.5f, -8f ), Quaternion.Euler( 18f, 0f, 0f ) );
	}

	void EnsureLight()
	{
		_eventLight = GetComponentInChildren<Light>();
		if ( _eventLight != null )
			return;

		GameObject lightObject = new GameObject( "Demo Light" );
		lightObject.transform.SetParent( transform, false );
		lightObject.transform.rotation = Quaternion.Euler( 50f, -30f, 0f );
		_eventLight = lightObject.AddComponent<Light>();
		_eventLight.type = LightType.Directional;
		_eventLight.intensity = 1.1f;
	}

	void BuildUi()
	{
		GameObject canvasObject = new GameObject( "Demo Canvas" );
		canvasObject.transform.SetParent( transform, false );
		_canvas = canvasObject.AddComponent<Canvas>();
		_canvas.renderMode = RenderMode.ScreenSpaceOverlay;
		canvasObject.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
		canvasObject.AddComponent<GraphicRaycaster>();

		EnsureEventSystem();

		GameObject root = new GameObject( "Buttons", typeof( RectTransform ) );
		root.transform.SetParent( canvasObject.transform, false );
		_buttonRoot = root.GetComponent<RectTransform>();
		_buttonRoot.anchorMin = new Vector2( 0f, 1f );
		_buttonRoot.anchorMax = new Vector2( 0f, 1f );
		_buttonRoot.pivot = new Vector2( 0f, 1f );
		_buttonRoot.anchoredPosition = new Vector2( 16f, -16f );
		_buttonRoot.sizeDelta = new Vector2( 280f, 800f );
	}

	static void EnsureEventSystem()
	{
		EventSystem existing = FindEventSystem();
		if ( existing != null )
		{
			StandaloneInputModule legacy = existing.GetComponent<StandaloneInputModule>();
			if ( legacy != null )
				Object.DestroyImmediate( legacy );

			if ( existing.GetComponent<InputSystemUIInputModule>() == null )
				existing.gameObject.AddComponent<InputSystemUIInputModule>();
			return;
		}

		GameObject eventSystem = new GameObject( "EventSystem" );
		eventSystem.AddComponent<EventSystem>();
		eventSystem.AddComponent<InputSystemUIInputModule>();
	}

	static EventSystem FindEventSystem()
	{
		EventSystem[] systems = Resources.FindObjectsOfTypeAll<EventSystem>();
		for ( int i = 0; i < systems.Length; i++ )
		{
			if ( systems[i] != null && systems[i].gameObject.scene.IsValid() )
				return systems[i];
		}

		return null;
	}

	void BuildSpawnPrefab()
	{
		_spawnPrefab = GameObject.CreatePrimitive( PrimitiveType.Sphere );
		_spawnPrefab.name = "Spawn Template";
		_spawnPrefab.transform.SetParent( transform, false );
		_spawnPrefab.transform.position = new Vector3( 0f, -50f, 0f );
		_spawnPrefab.transform.localScale = Vector3.one * 0.35f;
	}

	void BuildDemos()
	{
		CreateFloor();

		GameObject punchTarget = CreateCube( "Punch Target", new Vector3( -3f, 0.5f, 0f ), new Color( 0.85f, 0.55f, 0.2f ) );
		Feedbacks punch = punchTarget.AddComponent<Feedbacks>();
		punch.AddFeedback( new PunchScaleFeedback
		{
			Target = punchTarget.transform,
			Punch = new Vector3( 0.35f, 0.35f, 0.35f ),
			Duration = 0.25f
		} );
		AddButton( "Punch Scale", punch );

		GameObject shakeTarget = CreateCube( "Shake Target", new Vector3( -1.5f, 0.5f, 0f ), new Color( 0.3f, 0.7f, 0.85f ) );
		Feedbacks shake = shakeTarget.AddComponent<Feedbacks>();
		shake.AddFeedback( new ShakeTransformFeedback
		{
			Target = shakeTarget.transform,
			Duration = 0.4f,
			Strength = 0.12f
		} );
		AddButton( "Shake Transform", shake );

		GameObject moveTarget = CreateCube( "Move Target", new Vector3( 0f, 0.5f, 0f ), new Color( 0.55f, 0.8f, 0.35f ) );
		Feedbacks move = moveTarget.AddComponent<Feedbacks>();
		move.AddFeedback( new MoveTransformFeedback
		{
			Target = moveTarget.transform,
			TargetPosition = new Vector3( 0f, 1.5f, 0f ),
			Duration = 0.35f,
			WorldSpace = true
		} );
		move.AddFeedback( new DelayFeedback { Duration = 0.15f } );
		move.AddFeedback( new MoveTransformFeedback
		{
			Target = moveTarget.transform,
			TargetPosition = new Vector3( 0f, 0.5f, 0f ),
			Duration = 0.35f,
			WorldSpace = true
		} );
		AddButton( "Move + Delay", move );

		GameObject colorTarget = CreateCube( "Color Target", new Vector3( 1.5f, 0.5f, 0f ), Color.white );
		Renderer colorRenderer = colorTarget.GetComponent<Renderer>();
		Feedbacks color = colorTarget.AddComponent<Feedbacks>();
		color.AddFeedback( new SetColorFeedback
		{
			Renderer = colorRenderer,
			Color = new Color( 1f, 0.25f, 0.45f ),
			PropertyName = "_BaseColor"
		} );
		color.AddFeedback( new DelayFeedback { Duration = 0.25f } );
		color.AddFeedback( new SetColorFeedback
		{
			Renderer = colorRenderer,
			Color = Color.white,
			PropertyName = "_BaseColor"
		} );
		AddButton( "Set Color", color );

		GameObject activeTarget = CreateCube( "Active Target", new Vector3( 3f, 0.5f, 0f ), new Color( 0.8f, 0.35f, 0.7f ) );
		GameObject activeHost = new GameObject( "Set Active Host" );
		activeHost.transform.SetParent( transform, false );
		Feedbacks active = activeHost.AddComponent<Feedbacks>();
		active.AddFeedback( new SetActiveFeedback { Target = activeTarget, Active = false } );
		active.AddFeedback( new DelayFeedback { Duration = 0.4f } );
		active.AddFeedback( new SetActiveFeedback { Target = activeTarget, Active = true } );
		AddButton( "Set Active", active );

		ParticleSystem particles = CreateParticles( new Vector3( -3f, 1.2f, 1.5f ) );
		Feedbacks particlePlay = particles.gameObject.AddComponent<Feedbacks>();
		particlePlay.AddFeedback( new PlayParticlesFeedback { ParticleSystem = particles } );
		AddButton( "Play Particles", particlePlay );

		GameObject burstHost = new GameObject( "Burst Host" );
		burstHost.transform.SetParent( particles.transform, false );
		Feedbacks burst = burstHost.AddComponent<Feedbacks>();
		burst.AddFeedback( new ParticleBurstFeedback { ParticleSystem = particles, Count = 24 } );
		AddButton( "Particle Burst", burst );

		AudioClip beepA = CreateBeep( "BeepA", 660f );
		AudioClip beepB = CreateBeep( "BeepB", 880f );
		AudioClip beepC = CreateBeep( "BeepC", 990f );
		GameObject audioHost = new GameObject( "Audio Host" );
		audioHost.transform.SetParent( transform, false );
		Feedbacks sfx = audioHost.AddComponent<Feedbacks>();
		sfx.AddFeedback( new PlaySFXFeedback { Clip = beepA, Volume = 0.8f, Pitch = 1f } );
		AddButton( "Play SFX", sfx );

		GameObject randomAudioHost = new GameObject( "Random Audio Host" );
		randomAudioHost.transform.SetParent( transform, false );
		Feedbacks randomSfx = randomAudioHost.AddComponent<Feedbacks>();
		randomSfx.AddFeedback( new PlayRandomSFXFeedback
		{
			Clips = new[] { beepA, beepB, beepC },
			Volume = 0.8f,
			Pitch = 1f
		} );
		AddButton( "Play Random SFX", randomSfx );

		GameObject combo = CreateCube( "Combo Target", new Vector3( 0f, 0.5f, 2f ), new Color( 0.95f, 0.85f, 0.35f ) );
		ParticleSystem comboParticles = CreateParticles( combo.transform.position + Vector3.up * 0.6f );
		comboParticles.transform.SetParent( combo.transform, true );
		Feedbacks comboFeedbacks = combo.AddComponent<Feedbacks>();
		comboFeedbacks.AddFeedback( new PlayRandomSFXFeedback
		{
			Clips = new[] { beepA, beepB, beepC },
			Volume = 0.7f,
			Pitch = 1f
		} );
		ParallelFeedback parallel = new ParallelFeedback();
		parallel.Feedbacks.Add( new PlayParticlesFeedback { ParticleSystem = comboParticles } );
		parallel.Feedbacks.Add( new PunchScaleFeedback
		{
			Target = combo.transform,
			Punch = new Vector3( 0.3f, 0.3f, 0.3f ),
			Duration = 0.28f
		} );
		parallel.Feedbacks.Add( new ShakeTransformFeedback
		{
			Target = combo.transform,
			Duration = 0.28f,
			Strength = 0.08f
		} );
		comboFeedbacks.AddFeedback( parallel );
		UnityEventFeedback unityEvent = new UnityEventFeedback();
		unityEvent.Event.AddListener( ToggleEventLight );
		comboFeedbacks.AddFeedback( unityEvent );
		AddButton( "Sequence Combo", comboFeedbacks );

		GameObject sequenceTarget = CreateCube( "Sequence Target", new Vector3( 2.2f, 0.5f, 2f ), new Color( 0.4f, 0.45f, 0.9f ) );
		Feedbacks nested = sequenceTarget.AddComponent<Feedbacks>();
		SequenceFeedback sequence = new SequenceFeedback();
		sequence.Feedbacks.Add( new SetScaleFeedback { Target = sequenceTarget.transform, Scale = Vector3.one * 1.25f } );
		sequence.Feedbacks.Add( new DelayFeedback { Duration = 0.2f } );
		sequence.Feedbacks.Add( new SetRotationFeedback { Target = sequenceTarget.transform, Rotation = new Vector3( 0f, 45f, 0f ), WorldSpace = false } );
		sequence.Feedbacks.Add( new DelayFeedback { Duration = 0.2f } );
		sequence.Feedbacks.Add( new SetScaleFeedback { Target = sequenceTarget.transform, Scale = Vector3.one } );
		sequence.Feedbacks.Add( new SetRotationFeedback { Target = sequenceTarget.transform, Rotation = Vector3.zero, WorldSpace = false } );
		nested.AddFeedback( sequence );
		AddButton( "Nested Sequence", nested );

		GameObject instantiateHost = new GameObject( "Instantiate Host" );
		instantiateHost.transform.SetParent( transform, false );
		Feedbacks instantiate = instantiateHost.AddComponent<Feedbacks>();
		instantiate.AddFeedback( new InstantiateFeedback
		{
			Prefab = _spawnPrefab,
			Position = new Vector3( 3.5f, 0.4f, 1.5f ),
			Rotation = Vector3.zero
		} );
		AddButton( "Instantiate", instantiate );

		GameObject destroyTarget = CreateCube( "Destroy Target", new Vector3( -2.2f, 0.5f, 2f ), new Color( 0.7f, 0.2f, 0.2f ) );
		GameObject destroyHost = new GameObject( "Destroy Host" );
		destroyHost.transform.SetParent( transform, false );
		Feedbacks destroy = destroyHost.AddComponent<Feedbacks>();
		destroy.AddFeedback( new PunchScaleFeedback { Target = destroyTarget.transform, Punch = new Vector3( 0.2f, 0.2f, 0.2f ), Duration = 0.15f } );
		destroy.AddFeedback( new DelayFeedback { Duration = 0.15f } );
		destroy.AddFeedback( new DestroyGameObjectFeedback { Target = destroyTarget, Delay = 0.05f } );
		AddButton( "Destroy", destroy );

		GameObject rendererTarget = CreateCube( "Renderer Target", new Vector3( 1.1f, 0.5f, -1.6f ), new Color( 0.7f, 0.7f, 0.75f ) );
		Feedbacks rendererToggle = rendererTarget.AddComponent<Feedbacks>();
		rendererToggle.AddFeedback( new SetRendererEnabledFeedback
		{
			Renderer = rendererTarget.GetComponent<Renderer>(),
			RendererEnabled = false
		} );
		rendererToggle.AddFeedback( new DelayFeedback { Duration = 0.35f } );
		rendererToggle.AddFeedback( new SetRendererEnabledFeedback
		{
			Renderer = rendererTarget.GetComponent<Renderer>(),
			RendererEnabled = true
		} );
		AddButton( "Renderer Enabled", rendererToggle );

		GameObject poseTarget = CreateCube( "Pose Target", new Vector3( -1.1f, 0.5f, -1.6f ), new Color( 0.2f, 0.55f, 0.45f ) );
		Feedbacks pose = poseTarget.AddComponent<Feedbacks>();
		pose.AddFeedback( new SetPositionFeedback
		{
			Target = poseTarget.transform,
			Position = new Vector3( -1.1f, 1.25f, -1.6f ),
			WorldSpace = true
		} );
		pose.AddFeedback( new DelayFeedback { Duration = 0.25f } );
		pose.AddFeedback( new SetPositionFeedback
		{
			Target = poseTarget.transform,
			Position = new Vector3( -1.1f, 0.5f, -1.6f ),
			WorldSpace = true
		} );
		AddButton( "Set Position", pose );
	}

	void ToggleEventLight()
	{
		if ( _eventLight == null )
			return;

		_eventLight.color = _eventLight.color == Color.white ? new Color( 1f, 0.85f, 0.4f ) : Color.white;
	}

	void CreateFloor()
	{
		GameObject floor = GameObject.CreatePrimitive( PrimitiveType.Plane );
		floor.name = "Floor";
		floor.transform.SetParent( transform, false );
		floor.transform.localScale = new Vector3( 1.4f, 1f, 1.4f );
		Renderer renderer = floor.GetComponent<Renderer>();
		if ( renderer != null )
		{
			Material material = new Material( renderer.sharedMaterial );
			material.color = new Color( 0.18f, 0.19f, 0.22f );
			if ( material.HasProperty( "_BaseColor" ) )
				material.SetColor( "_BaseColor", new Color( 0.18f, 0.19f, 0.22f ) );
			renderer.sharedMaterial = material;
		}
	}

	GameObject CreateCube( string name, Vector3 position, Color color )
	{
		GameObject cube = GameObject.CreatePrimitive( PrimitiveType.Cube );
		cube.name = name;
		cube.transform.SetParent( transform, false );
		cube.transform.position = position;
		Renderer renderer = cube.GetComponent<Renderer>();
		if ( renderer != null )
		{
			renderer.material.color = color;
			if ( renderer.material.HasProperty( "_BaseColor" ) )
				renderer.material.SetColor( "_BaseColor", color );
		}

		return cube;
	}

	ParticleSystem CreateParticles( Vector3 position )
	{
		GameObject particleObject = new GameObject( "Particles" );
		particleObject.transform.SetParent( transform, false );
		particleObject.transform.position = position;
		ParticleSystem system = particleObject.AddComponent<ParticleSystem>();
		system.Stop( true, ParticleSystemStopBehavior.StopEmittingAndClear );
		ParticleSystem.MainModule main = system.main;
		main.playOnAwake = false;
		main.loop = false;
		main.duration = 0.4f;
		main.startLifetime = 0.45f;
		main.startSpeed = 2.5f;
		main.startSize = 0.12f;
		main.maxParticles = 64;
		main.simulationSpace = ParticleSystemSimulationSpace.World;
		ParticleSystem.EmissionModule emission = system.emission;
		emission.rateOverTime = 0f;
		emission.SetBursts( new[] { new ParticleSystem.Burst( 0f, 12 ) } );
		ParticleSystem.ShapeModule shape = system.shape;
		shape.shapeType = ParticleSystemShapeType.Sphere;
		shape.radius = 0.1f;
		return system;
	}

	AudioClip CreateBeep( string name, float frequency )
	{
		const int sampleRate = 44100;
		const float duration = 0.12f;
		int sampleCount = Mathf.CeilToInt( sampleRate * duration );
		AudioClip clip = AudioClip.Create( name, sampleCount, 1, sampleRate, false );
		float[] data = new float[sampleCount];
		for ( int i = 0; i < sampleCount; i++ )
		{
			float t = i / (float)sampleRate;
			float envelope = 1f - ( t / duration );
			data[i] = Mathf.Sin( 2f * Mathf.PI * frequency * t ) * 0.28f * envelope;
		}

		clip.SetData( data, 0 );
		return clip;
	}

	void AddButton( string label, Feedbacks feedbacks )
	{
		GameObject buttonObject = new GameObject( label + " Button", typeof( RectTransform ) );
		buttonObject.transform.SetParent( _buttonRoot, false );
		RectTransform rect = buttonObject.GetComponent<RectTransform>();
		rect.anchorMin = new Vector2( 0f, 1f );
		rect.anchorMax = new Vector2( 0f, 1f );
		rect.pivot = new Vector2( 0f, 1f );
		rect.sizeDelta = new Vector2( 260f, 34f );
		rect.anchoredPosition = new Vector2( 0f, -_buttonIndex * 40f );

		Image image = buttonObject.AddComponent<Image>();
		image.color = new Color( 0.18f, 0.2f, 0.26f, 0.92f );
		Button button = buttonObject.AddComponent<Button>();
		button.targetGraphic = image;
		Feedbacks captured = feedbacks;
		button.onClick.AddListener( () =>
		{
			if ( captured != null )
				captured.Play();
		} );

		GameObject textObject = new GameObject( "Label", typeof( RectTransform ) );
		textObject.transform.SetParent( buttonObject.transform, false );
		RectTransform textRect = textObject.GetComponent<RectTransform>();
		textRect.anchorMin = Vector2.zero;
		textRect.anchorMax = Vector2.one;
		textRect.offsetMin = new Vector2( 10f, 0f );
		textRect.offsetMax = new Vector2( -10f, 0f );
		Text text = textObject.AddComponent<Text>();
		text.font = Resources.GetBuiltinResource<Font>( "LegacyRuntime.ttf" );
		if ( text.font == null )
			text.font = Resources.GetBuiltinResource<Font>( "Arial.ttf" );
		text.text = label;
		text.alignment = TextAnchor.MiddleLeft;
		text.color = Color.white;
		text.fontSize = 15;

		_buttonIndex++;
	}
}
