#if UNITY_EDITOR
using UnityEditor;

using UnityEngine;

[CustomEditor( typeof( LanternRevealSweepController ) )]
public class LanternRevealSweepControllerEditor : Editor
{
	public override void OnInspectorGUI()
	{
		DrawDefaultInspector();

		LanternRevealSweepController controller = (LanternRevealSweepController)target;
		EditorGUILayout.Space( 8f );
		EditorGUILayout.LabelField( "Timing Readout", EditorStyles.boldLabel );

		float sweep = Mathf.Max( controller.SweepDurationZ, controller.SweepDurationY );
		float skylight = controller.SkylightFadeDelay + ResolveMaxSkylightFade( controller );
		EditorGUILayout.HelpBox(
			"Lantern sweep ~" + sweep.ToString( "0.##" ) + "s after start delay " +
			controller.LanternStartDelay.ToString( "0.##" ) + "s (fade " +
			controller.LanternFadeDuration.ToString( "0.##" ) + "s each).\n" +
			"Skylight fade window ~" + skylight.ToString( "0.##" ) + "s (controller default " +
			controller.SkylightFadeDuration.ToString( "0.##" ) + "s).\n" +
			"Prefer editing from CinematicPresentationController — it shows the master timeline with punches.",
			MessageType.Info );
	}

	static float ResolveMaxSkylightFade( LanternRevealSweepController controller )
	{
		float maxFade = controller.SkylightFadeDuration;
		SkylightReveal[] skylights = controller.Skylights;
		if ( skylights == null )
			return maxFade;

		for ( int i = 0; i < skylights.Length; i++ )
		{
			SkylightReveal skylight = skylights[ i ];
			if ( skylight == null )
				continue;

			float fade = skylight.FadeDuration > 0f ? skylight.FadeDuration : controller.SkylightFadeDuration;
			maxFade = Mathf.Max( maxFade, skylight.StartDelay + fade );
		}

		return maxFade;
	}
}
#endif
