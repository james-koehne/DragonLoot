#if UNITY_EDITOR
using UnityEditor;

using UnityEngine;

[CustomEditor( typeof( CinematicPresentationController ) )]
public class CinematicPresentationControllerEditor : Editor
{
	SerializedProperty _presentationId;
	SerializedProperty _baseFov;
	SerializedProperty _gnomeLedgeTarget;
	SerializedProperty _returnLookTarget;
	SerializedProperty _envelopeRise;
	SerializedProperty _envelopeHold;
	SerializedProperty _envelopeFall;
	SerializedProperty _fovPeak;
	SerializedProperty _letterboxPeak;
	SerializedProperty _gnomeWalkSpeed;
	SerializedProperty _cameraWalkSpeed;
	SerializedProperty _arriveRadius;
	SerializedProperty _walkCameraHeight;
	SerializedProperty _cameraPath;
	SerializedProperty _lookPath;
	SerializedProperty _splineEnterDuration;
	SerializedProperty _tourDuration;
	SerializedProperty _tourProgressCurve;
	SerializedProperty _lookAheadNormalized;
	SerializedProperty _reattachDuration;
	SerializedProperty _lanternReveal;
	SerializedProperty _cues;

	bool _walkFoldout = true;
	bool _tourFoldout = true;
	bool _returnFoldout = true;
	bool _cuesFoldout = true;
	bool _lanternFoldout = true;
	bool _skylightsFoldout = true;

	void OnEnable()
	{
		_presentationId = serializedObject.FindProperty( "_presentationId" );
		_baseFov = serializedObject.FindProperty( "_baseFov" );
		_gnomeLedgeTarget = serializedObject.FindProperty( "_gnomeLedgeTarget" );
		_returnLookTarget = serializedObject.FindProperty( "_returnLookTarget" );
		_envelopeRise = serializedObject.FindProperty( "_envelopeRise" );
		_envelopeHold = serializedObject.FindProperty( "_envelopeHold" );
		_envelopeFall = serializedObject.FindProperty( "_envelopeFall" );
		_fovPeak = serializedObject.FindProperty( "_fovPeak" );
		_letterboxPeak = serializedObject.FindProperty( "_letterboxPeak" );
		_gnomeWalkSpeed = serializedObject.FindProperty( "_gnomeWalkSpeed" );
		_cameraWalkSpeed = serializedObject.FindProperty( "_cameraWalkSpeed" );
		_arriveRadius = serializedObject.FindProperty( "_arriveRadius" );
		_walkCameraHeight = serializedObject.FindProperty( "_walkCameraHeight" );
		_cameraPath = serializedObject.FindProperty( "_cameraPath" );
		_lookPath = serializedObject.FindProperty( "_lookPath" );
		_splineEnterDuration = serializedObject.FindProperty( "_splineEnterDuration" );
		_tourDuration = serializedObject.FindProperty( "_tourDuration" );
		_tourProgressCurve = serializedObject.FindProperty( "_tourProgressCurve" );
		_lookAheadNormalized = serializedObject.FindProperty( "_lookAheadNormalized" );
		_reattachDuration = serializedObject.FindProperty( "_reattachDuration" );
		_lanternReveal = serializedObject.FindProperty( "_lanternReveal" );
		_cues = serializedObject.FindProperty( "_cues" );
	}

	public override void OnInspectorGUI()
	{
		serializedObject.Update();
		CinematicPresentationController controller = (CinematicPresentationController)target;

		EditorGUILayout.LabelField( "Setup", EditorStyles.boldLabel );
		EditorGUILayout.PropertyField( _presentationId );
		EditorGUILayout.PropertyField( _baseFov );
		EditorGUILayout.PropertyField( _gnomeLedgeTarget, new GUIContent( "Gnome Ledge Target" ) );
		EditorGUILayout.PropertyField( _returnLookTarget, new GUIContent( "Return Look Target" ) );
		EditorGUILayout.PropertyField( _cameraPath );
		EditorGUILayout.PropertyField( _lookPath );

		if ( GUILayout.Button( "Create Intro Splines" ) )
			EditorApplication.ExecuteMenuItem( DragonLootMenus.CinematicCreateIntroSplines );

		EditorGUILayout.Space( 8f );
		EditorGUILayout.LabelField( "UI Envelope", EditorStyles.boldLabel );
		EditorGUILayout.PropertyField( _envelopeRise, new GUIContent( "Rise" ) );
		EditorGUILayout.PropertyField( _envelopeHold, new GUIContent( "Hold" ) );
		EditorGUILayout.PropertyField( _envelopeFall, new GUIContent( "Fall" ) );
		EditorGUILayout.PropertyField( _fovPeak );
		EditorGUILayout.PropertyField( _letterboxPeak );

		EditorGUILayout.Space( 8f );
		LanternRevealSweepController lantern = controller.ResolveLanternReveal();
		DrawTimeline( controller, lantern );

		if ( Application.isPlaying && controller.IsPlaying )
		{
			EditorGUILayout.HelpBox(
				"Playing: " + controller.Phase + " @ " + controller.Elapsed.ToString( "0.00" ) + "s",
				MessageType.Info );
		}

		EditorGUILayout.Space( 8f );
		_walkFoldout = EditorGUILayout.Foldout( _walkFoldout, "Walk Phase", true );
		if ( _walkFoldout )
		{
			EditorGUI.indentLevel++;
			EditorGUILayout.PropertyField( _gnomeWalkSpeed );
			EditorGUILayout.PropertyField( _cameraWalkSpeed );
			EditorGUILayout.PropertyField( _arriveRadius );
			EditorGUILayout.PropertyField( _walkCameraHeight );
			EditorGUI.indentLevel--;
		}

		_tourFoldout = EditorGUILayout.Foldout( _tourFoldout, "Spline Tour", true );
		if ( _tourFoldout )
		{
			EditorGUI.indentLevel++;
			EditorGUILayout.PropertyField( _splineEnterDuration );
			EditorGUILayout.PropertyField( _tourDuration );
			EditorGUILayout.PropertyField( _tourProgressCurve );
			EditorGUILayout.PropertyField( _lookAheadNormalized );
			EditorGUI.indentLevel--;
		}

		_returnFoldout = EditorGUILayout.Foldout( _returnFoldout, "Return To Player", true );
		if ( _returnFoldout )
		{
			EditorGUI.indentLevel++;
			EditorGUILayout.PropertyField( _reattachDuration );
			EditorGUI.indentLevel--;
		}

		EditorGUILayout.Space( 8f );
		DrawLanternSection( controller, lantern );

		_cuesFoldout = EditorGUILayout.Foldout( _cuesFoldout, "Cues", true );
		if ( _cuesFoldout )
		{
			EditorGUI.indentLevel++;
			EditorGUILayout.PropertyField( _cues, includeChildren: true );
			EditorGUI.indentLevel--;
		}

		serializedObject.ApplyModifiedProperties();
	}

	void DrawLanternSection( CinematicPresentationController controller, LanternRevealSweepController lantern )
	{
		_lanternFoldout = EditorGUILayout.Foldout( _lanternFoldout, "Lantern Reveal Sweep", true );
		if ( !_lanternFoldout )
			return;

		EditorGUI.indentLevel++;
		EditorGUILayout.PropertyField( _lanternReveal, new GUIContent( "Controller" ) );

		if ( _lanternReveal.objectReferenceValue == null && lantern != null )
		{
			EditorGUILayout.BeginHorizontal();
			EditorGUILayout.HelpBox( "Found '" + lantern.RevealId + "' in open scenes — assign it to edit here.", MessageType.Info );
			if ( GUILayout.Button( "Assign", GUILayout.Width( 70f ) ) )
			{
				_lanternReveal.objectReferenceValue = lantern;
				serializedObject.ApplyModifiedProperties();
				controller.EditorAssignLanternReveal( lantern );
			}
			EditorGUILayout.EndHorizontal();
		}

		if ( lantern == null )
		{
			EditorGUILayout.HelpBox( "Assign a LanternRevealSweepController to author sweep, skylight, and punches in this inspector.", MessageType.Warning );
			EditorGUI.indentLevel--;
			return;
		}

		SerializedObject lanternSo = new SerializedObject( lantern );
		lanternSo.Update();

		EditorGUILayout.PropertyField( lanternSo.FindProperty( "_revealId" ) );
		EditorGUILayout.PropertyField( lanternSo.FindProperty( "_skylights" ), includeChildren: true );
		EditorGUILayout.PropertyField( lanternSo.FindProperty( "_skylightFadeDuration" ) );
		EditorGUILayout.PropertyField( lanternSo.FindProperty( "_skylightFadeDelay" ) );
		EditorGUILayout.PropertyField( lanternSo.FindProperty( "_lanternStartDelay" ) );
		EditorGUILayout.PropertyField( lanternSo.FindProperty( "_sweepDurationZ" ) );
		EditorGUILayout.PropertyField( lanternSo.FindProperty( "_sweepDurationY" ) );
		EditorGUILayout.PropertyField( lanternSo.FindProperty( "_lanternFadeDuration" ) );
		EditorGUILayout.PropertyField( lanternSo.FindProperty( "_sweepReference" ) );
		EditorGUILayout.PropertyField( lanternSo.FindProperty( "_autoComputeBounds" ) );
		if ( !lantern.AutoComputeBounds )
		{
			EditorGUILayout.PropertyField( lanternSo.FindProperty( "_sweepStartZ" ) );
			EditorGUILayout.PropertyField( lanternSo.FindProperty( "_sweepEndZ" ) );
			EditorGUILayout.PropertyField( lanternSo.FindProperty( "_sweepStartY" ) );
			EditorGUILayout.PropertyField( lanternSo.FindProperty( "_sweepEndY" ) );
		}

		EditorGUILayout.Space( 4f );
		EditorGUILayout.LabelField( "Reveal Punches", EditorStyles.boldLabel );
		EditorGUILayout.PropertyField( lanternSo.FindProperty( "_bloomPunch" ), includeChildren: true );
		EditorGUILayout.PropertyField( lanternSo.FindProperty( "_specularPunch" ), includeChildren: true );

		_skylightsFoldout = EditorGUILayout.Foldout( _skylightsFoldout, "Per-Skylight Timing / Punch", true );
		if ( _skylightsFoldout )
			DrawSkylightEditors( lantern );

		lanternSo.ApplyModifiedProperties();
		EditorGUI.indentLevel--;
	}

	static void DrawSkylightEditors( LanternRevealSweepController lantern )
	{
		SkylightReveal[] skylights = lantern.Skylights;
		if ( skylights == null || skylights.Length == 0 )
		{
			EditorGUILayout.LabelField( "No skylights assigned.", EditorStyles.miniLabel );
			return;
		}

		for ( int i = 0; i < skylights.Length; i++ )
		{
			SkylightReveal skylight = skylights[ i ];
			if ( skylight == null )
			{
				EditorGUILayout.LabelField( "Skylight " + i + ": (missing)", EditorStyles.miniLabel );
				continue;
			}

			SerializedObject so = new SerializedObject( skylight );
			so.Update();
			EditorGUILayout.LabelField( skylight.name, EditorStyles.boldLabel );
			EditorGUI.indentLevel++;
			EditorGUILayout.PropertyField( so.FindProperty( "_startDelay" ) );
			EditorGUILayout.PropertyField( so.FindProperty( "_fadeDuration" ) );
			EditorGUILayout.PropertyField( so.FindProperty( "_punch" ), includeChildren: true );
			EditorGUI.indentLevel--;
			so.ApplyModifiedProperties();
		}
	}

	static void DrawTimeline( CinematicPresentationController controller, LanternRevealSweepController lantern )
	{
		EditorGUILayout.LabelField( "Master Timeline", EditorStyles.boldLabel );

		float walk = Mathf.Max( 0f, controller.EstimateWalkDurationForEditor() );
		float enter = Mathf.Max( 0f, controller.SplineEnterDuration );
		float tour = Mathf.Max( 0f, controller.TourDuration );
		float reattach = Mathf.Max( 0f, controller.ReattachDuration );
		float cameraTotal = walk + enter + tour + reattach;

		float lanternCueDelay = controller.ResolveFirstLanternCueDelay();
		float lanternReveal = lantern != null ? lantern.EstimateRevealDuration() : 0f;
		float total = Mathf.Max( cameraTotal, lanternCueDelay + lanternReveal );
		if ( total < 0.01f )
			total = Mathf.Max( 0.01f, controller.EnvelopeRise + controller.EnvelopeHold + controller.EnvelopeFall );

		DrawLabeledTrack( "Camera", total, () =>
		{
			Rect rect = BeginTrackRect( 22f );
			float x = rect.x;
			DrawSegment( ref x, rect.y, rect.height, walk, total, rect.width, new Color( 0.25f, 0.55f, 0.85f, 0.9f ), "Walk" );
			DrawSegment( ref x, rect.y, rect.height, enter, total, rect.width, new Color( 0.35f, 0.7f, 0.55f, 0.9f ), "Enter" );
			DrawSegment( ref x, rect.y, rect.height, tour, total, rect.width, new Color( 0.85f, 0.65f, 0.25f, 0.9f ), "Tour" );
			DrawSegment( ref x, rect.y, rect.height, reattach, total, rect.width, new Color( 0.75f, 0.35f, 0.55f, 0.9f ), "Return" );
		} );

		float rise = Mathf.Max( 0f, controller.EnvelopeRise );
		float hold = Mathf.Max( 0f, controller.EnvelopeHold );
		float fall = Mathf.Max( 0f, controller.EnvelopeFall );
		float contentHold = Mathf.Max( hold, cameraTotal - rise - fall );
		DrawPunchTrack( "FOV/Letterbox", 0f, rise, contentHold, fall, total, new Color( 0.85f, 0.85f, 0.9f, 0.85f ) );

		DrawCueTrack( controller.Cues, total );

		if ( lantern != null )
		{
			float sweepStart = lanternCueDelay + lantern.LanternStartDelay;
			float sweepDur = Mathf.Max( lantern.SweepDurationZ, lantern.SweepDurationY );
			DrawWindowTrack( "Lantern sweep", sweepStart, sweepDur, total, new Color( 1f, 0.82f, 0.2f, 0.85f ) );
			DrawWindowTrack( "Lantern fade", sweepStart + sweepDur, lantern.LanternFadeDuration, total, new Color( 1f, 0.65f, 0.15f, 0.7f ) );

			float skyStart = lanternCueDelay + lantern.SkylightFadeDelay;
			float skyDur = Mathf.Max( 0f, lantern.EstimateSkylightWindow() - lantern.SkylightFadeDelay );
			DrawWindowTrack( "Skylight fade", skyStart, skyDur, total, new Color( 0.55f, 0.75f, 1f, 0.85f ) );

			RevealPunchChannel bloom = lantern.BloomPunch;
			if ( bloom.IsActive )
				DrawPunchTrack( "Bloom punch", lanternCueDelay + bloom.delay, bloom.rise, bloom.hold, bloom.fall, total, new Color( 1f, 0.45f, 0.85f, 0.9f ) );

			RevealPunchChannel specular = lantern.SpecularPunch;
			if ( specular.IsScaleActive )
				DrawPunchTrack( "Specular punch", lanternCueDelay + specular.delay, specular.rise, specular.hold, specular.fall, total, new Color( 0.55f, 1f, 0.7f, 0.9f ) );

			SkylightReveal[] skylights = lantern.Skylights;
			if ( skylights != null )
			{
				for ( int i = 0; i < skylights.Length; i++ )
				{
					SkylightReveal skylight = skylights[ i ];
					if ( skylight == null )
						continue;

					float fadeDur = skylight.FadeDuration > 0f ? skylight.FadeDuration : lantern.SkylightFadeDuration;
					float localStart = lanternCueDelay + lantern.SkylightFadeDelay + skylight.StartDelay;
					string name = string.IsNullOrEmpty( skylight.name ) ? "Skylight " + i : skylight.name;
					DrawWindowTrack( name + " fade", localStart, fadeDur, total, new Color( 0.4f, 0.65f, 1f, 0.65f ) );

					RevealPunchChannel punch = skylight.Punch;
					if ( punch.IsScaleActive )
						DrawPunchTrack( name + " punch", localStart + punch.delay, punch.rise, punch.hold, punch.fall, total, new Color( 0.7f, 0.9f, 1f, 0.85f ) );
				}
			}
		}
		else
		{
			EditorGUILayout.HelpBox( "Assign Lantern Reveal to show sweep / skylight / punch tracks.", MessageType.None );
		}

		if ( Application.isPlaying && controller.IsPlaying )
			DrawPlayhead( controller.Elapsed, total );

		EditorGUILayout.LabelField(
			"Camera ~" + cameraTotal.ToString( "0.##" ) + "s · Lantern cue @" + lanternCueDelay.ToString( "0.##" ) +
			"s · Total ~" + total.ToString( "0.##" ) + "s",
			EditorStyles.miniLabel );
	}

	static void DrawCueTrack( CinematicCue[] cues, float total )
	{
		EditorGUILayout.BeginHorizontal();
		GUILayout.Label( "Cues", EditorStyles.miniLabel, GUILayout.Width( 110f ) );
		Rect rect = GUILayoutUtility.GetRect( 12f, 14f, GUILayout.ExpandWidth( true ) );
		EditorGUI.DrawRect( rect, new Color( 0.1f, 0.1f, 0.1f, 1f ) );
		if ( cues != null )
		{
			for ( int i = 0; i < cues.Length; i++ )
			{
				CinematicCue cue = cues[ i ];
				if ( cue == null )
					continue;

				float t = Mathf.Clamp01( cue.delay / Mathf.Max( 0.01f, total ) );
				float tickX = rect.x + t * rect.width;
				Color tickColor = cue.type == CinematicCueType.LanternReveal
					? new Color( 1f, 0.85f, 0.2f, 1f )
					: new Color( 0.4f, 0.9f, 1f, 1f );
				EditorGUI.DrawRect( new Rect( tickX - 1f, rect.y, 2f, rect.height ), tickColor );
			}
		}
		EditorGUILayout.EndHorizontal();

		if ( cues == null )
			return;

		EditorGUILayout.BeginHorizontal();
		GUILayout.Space( 114f );
		for ( int i = 0; i < cues.Length; i++ )
		{
			CinematicCue cue = cues[ i ];
			if ( cue == null )
				continue;
			string label = cue.type == CinematicCueType.LanternReveal ? "Lantern" : "Audio";
			GUILayout.Label( label + " @" + cue.delay.ToString( "0.##" ) + "s", EditorStyles.miniLabel );
		}
		EditorGUILayout.EndHorizontal();
	}

	static void DrawPunchTrack( string label, float start, float rise, float hold, float fall, float total, Color color )
	{
		DrawLabeledTrack( label, total, () =>
		{
			Rect rect = BeginTrackRect( 12f );
			float x = rect.x + rect.width * ( start / Mathf.Max( 0.01f, total ) );
			DrawSegment( ref x, rect.y, rect.height, rise, total, rect.width, color * new Color( 1f, 1f, 1f, 0.55f ), "R" );
			DrawSegment( ref x, rect.y, rect.height, hold, total, rect.width, color, "H" );
			DrawSegment( ref x, rect.y, rect.height, fall, total, rect.width, color * new Color( 1f, 1f, 1f, 0.55f ), "F" );
		} );
	}

	static void DrawWindowTrack( string label, float start, float duration, float total, Color color )
	{
		if ( duration <= 0.0001f )
			return;

		DrawLabeledTrack( label, total, () =>
		{
			Rect rect = BeginTrackRect( 12f );
			float x = rect.x + rect.width * ( Mathf.Max( 0f, start ) / Mathf.Max( 0.01f, total ) );
			DrawSegment( ref x, rect.y, rect.height, duration, total, rect.width, color, null );
		} );
	}

	static void DrawLabeledTrack( string label, float total, System.Action drawBody )
	{
		EditorGUILayout.BeginHorizontal();
		GUILayout.Label( label, EditorStyles.miniLabel, GUILayout.Width( 110f ) );
		drawBody();
		EditorGUILayout.EndHorizontal();
	}

	static Rect BeginTrackRect( float height )
	{
		Rect rect = GUILayoutUtility.GetRect( height, height, GUILayout.ExpandWidth( true ) );
		EditorGUI.DrawRect( rect, new Color( 0.12f, 0.12f, 0.12f, 1f ) );
		return rect;
	}

	static void DrawPlayhead( float elapsed, float total )
	{
		Rect rect = GUILayoutUtility.GetRect( 4f, 4f, GUILayout.ExpandWidth( true ) );
		float t = Mathf.Clamp01( elapsed / Mathf.Max( 0.01f, total ) );
		float x = rect.x + t * rect.width;
		EditorGUI.DrawRect( new Rect( x - 1f, rect.y - 80f, 2f, 84f ), new Color( 1f, 0.2f, 0.2f, 0.85f ) );
	}

	static void DrawSegment( ref float x, float y, float height, float duration, float total, float fullWidth, Color color, string label )
	{
		if ( duration <= 0.0001f || total <= 0.0001f )
			return;

		float width = fullWidth * ( duration / total );
		Rect segment = new Rect( x, y, Mathf.Max( 1f, width ), height );
		EditorGUI.DrawRect( segment, color );
		if ( !string.IsNullOrEmpty( label ) && width > 28f )
		{
			GUIStyle style = new GUIStyle( EditorStyles.miniLabel );
			style.alignment = TextAnchor.MiddleCenter;
			style.normal.textColor = Color.white;
			GUI.Label( segment, label, style );
		}

		x += width;
	}
}
#endif
