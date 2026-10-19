#if UNITY_EDITOR
using FeedbackSystem;

using UnityEditor;
using UnityEngine;

/// <summary>
/// Ensures FairyHelper has a GuideTrail + GuideTrailFeedbacks juice chain and trail material.
/// </summary>
public static class FairyGuideTrailPrefabBuilder
{
	const string PrefabPath = "Assets/Addressables/Companions/FairyHelper.prefab";
	const string MatFolder = "Assets/Materials/Fairy";
	const string MatPath = MatFolder + "/M_FairyGuideTrail.mat";
	const string ShaderName = "DragonLoot/ParticleUnlitEmissive";

	[MenuItem( DragonLootMenus.CompanionsEnsureFairyGuideTrail )]
	public static void BuildFromMenu()
	{
		if ( EnsureGuideTrailOnPrefab() )
			Debug.Log( "FairyHelper guide trail ready on " + PrefabPath );
	}

	[InitializeOnLoadMethod]
	static void AutoEnsure()
	{
		EditorApplication.delayCall += () => EnsureGuideTrailOnPrefab();
	}

	public static bool EnsureGuideTrailOnPrefab()
	{
		GameObject prefabRoot = PrefabUtility.LoadPrefabContents( PrefabPath );
		if ( prefabRoot == null )
		{
			Debug.LogWarning( "FairyGuideTrailPrefabBuilder: missing prefab at " + PrefabPath );
			return false;
		}

		bool changed = false;
		try
		{
			Material trailMat = EnsureTrailMaterial();
			FairyHelper helper = prefabRoot.GetComponent<FairyHelper>();
			if ( helper == null )
			{
				Debug.LogWarning( "FairyGuideTrailPrefabBuilder: FairyHelper missing on prefab." );
				return false;
			}

			TrailRenderer trail = EnsureTrailRenderer( prefabRoot.transform, trailMat, ref changed );
			Feedbacks trailFeedbacks = EnsureTrailFeedbacks( prefabRoot.transform, trail, ref changed );

			SerializedObject so = new SerializedObject( helper );
			SerializedProperty trailProp = so.FindProperty( "onGuideTrailFeedback" );
			if ( trailProp != null && trailProp.objectReferenceValue != trailFeedbacks )
			{
				trailProp.objectReferenceValue = trailFeedbacks;
				so.ApplyModifiedPropertiesWithoutUndo();
				changed = true;
			}

			if ( changed )
				PrefabUtility.SaveAsPrefabAsset( prefabRoot, PrefabPath );
		}
		finally
		{
			PrefabUtility.UnloadPrefabContents( prefabRoot );
		}

		return changed;
	}

	static Material EnsureTrailMaterial()
	{
		Material existing = AssetDatabase.LoadAssetAtPath<Material>( MatPath );
		if ( existing != null )
			return existing;

		if ( !AssetDatabase.IsValidFolder( "Assets/Materials" ) )
			AssetDatabase.CreateFolder( "Assets", "Materials" );
		if ( !AssetDatabase.IsValidFolder( MatFolder ) )
			AssetDatabase.CreateFolder( "Assets/Materials", "Fairy" );

		Shader shader = Shader.Find( ShaderName );
		if ( shader == null )
		{
			Debug.LogWarning( "FairyGuideTrailPrefabBuilder: shader '" + ShaderName + "' not found." );
			return null;
		}

		Material mat = new Material( shader );
		mat.name = "M_FairyGuideTrail";
		// Softness 0 — SoftDisc is for billboards; trails use width/color curves instead.
		mat.SetFloat( "_Softness", 0f );
		mat.SetFloat( "_EmissionIntensity", 4.5f );
		// Warm gold / soft lavender HDR tint for bloom.
		mat.SetColor( "_Color", new Color( 1.35f, 0.95f, 1.55f, 1f ) );
		AssetDatabase.CreateAsset( mat, MatPath );
		AssetDatabase.SaveAssets();
		return mat;
	}

	static TrailRenderer EnsureTrailRenderer( Transform root, Material trailMat, ref bool changed )
	{
		Transform existing = root.Find( "GuideTrail" );
		GameObject trailGo;
		if ( existing != null )
			trailGo = existing.gameObject;
		else
		{
			trailGo = new GameObject( "GuideTrail" );
			trailGo.layer = root.gameObject.layer;
			trailGo.transform.SetParent( root, false );
			trailGo.transform.localPosition = new Vector3( 0f, 0.05f, 0f );
			trailGo.transform.localRotation = Quaternion.identity;
			trailGo.transform.localScale = Vector3.one;
			changed = true;
		}

		TrailRenderer trail = trailGo.GetComponent<TrailRenderer>();
		if ( trail == null )
		{
			trail = trailGo.AddComponent<TrailRenderer>();
			changed = true;
		}

		trail.time = 0.75f;
		trail.minVertexDistance = 0.05f;
		trail.autodestruct = false;
		trail.emitting = false;
		trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
		trail.receiveShadows = false;
		trail.numCapVertices = 2;
		trail.numCornerVertices = 2;
		trail.alignment = LineAlignment.View;
		trail.textureMode = LineTextureMode.Stretch;

		AnimationCurve width = new AnimationCurve(
			new Keyframe( 0f, 0.18f ),
			new Keyframe( 0.55f, 0.1f ),
			new Keyframe( 1f, 0f ) );
		trail.widthCurve = width;
		trail.widthMultiplier = 1f;

		Gradient color = new Gradient();
		color.SetKeys(
			new[]
			{
				new GradientColorKey( new Color( 1f, 0.95f, 1f ), 0f ),
				new GradientColorKey( new Color( 0.85f, 0.7f, 1f ), 0.45f ),
				new GradientColorKey( new Color( 1f, 0.75f, 0.45f ), 1f )
			},
			new[]
			{
				new GradientAlphaKey( 1f, 0f ),
				new GradientAlphaKey( 0.65f, 0.35f ),
				new GradientAlphaKey( 0f, 1f )
			} );
		trail.colorGradient = color;

		if ( trailMat != null && trail.sharedMaterial != trailMat )
		{
			trail.sharedMaterial = trailMat;
			changed = true;
		}

		return trail;
	}

	static Feedbacks EnsureTrailFeedbacks( Transform root, TrailRenderer trail, ref bool changed )
	{
		Transform existing = root.Find( "GuideTrailFeedbacks" );
		GameObject feedbackGo;
		if ( existing != null )
			feedbackGo = existing.gameObject;
		else
		{
			feedbackGo = new GameObject( "GuideTrailFeedbacks" );
			feedbackGo.layer = root.gameObject.layer;
			feedbackGo.transform.SetParent( root, false );
			feedbackGo.transform.localPosition = Vector3.zero;
			feedbackGo.transform.localRotation = Quaternion.identity;
			feedbackGo.transform.localScale = Vector3.one;
			changed = true;
		}

		Feedbacks feedbacks = feedbackGo.GetComponent<Feedbacks>();
		if ( feedbacks == null )
		{
			feedbacks = feedbackGo.AddComponent<Feedbacks>();
			changed = true;
		}

		TrailRendererFeedback trailFeedback = null;
		if ( feedbacks.FeedbackList != null )
		{
			for ( int i = 0; i < feedbacks.FeedbackList.Count; i++ )
			{
				TrailRendererFeedback typed = feedbacks.FeedbackList[ i ] as TrailRendererFeedback;
				if ( typed != null )
				{
					trailFeedback = typed;
					break;
				}
			}
		}

		if ( trailFeedback == null )
		{
			trailFeedback = new TrailRendererFeedback();
			feedbacks.AddFeedback( trailFeedback );
			changed = true;
		}

		if ( trailFeedback.Trail != trail )
		{
			trailFeedback.Trail = trail;
			changed = true;
		}

		EditorUtility.SetDirty( feedbacks );
		return feedbacks;
	}
}
#endif
