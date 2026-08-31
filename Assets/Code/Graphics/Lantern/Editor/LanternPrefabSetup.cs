#if UNITY_EDITOR
using FeedbackSystem;

using UnityEditor;
using UnityEngine;

static class LanternPrefabSetup
{
	const string PrefabPath = "Assets/Prefabs/Props/Lantern.prefab";

	[MenuItem( "Dragon Loot/Graphics/Wire Lantern Prefab" )]
	public static void WireLanternPrefabFromMenu()
	{
		WireLanternPrefab();
	}

	public static void WireLanternPrefab()
	{
		GameObject prefabRoot = PrefabUtility.LoadPrefabContents( PrefabPath );
		if ( prefabRoot == null )
		{
			Debug.LogError( "LanternPrefabSetup: could not load " + PrefabPath );
			return;
		}

		bool changed = false;
		LanternActivator activator = prefabRoot.GetComponent<LanternActivator>();
		if ( activator == null )
		{
			activator = prefabRoot.AddComponent<LanternActivator>();
			changed = true;
		}

		Light light = prefabRoot.GetComponentInChildren<Light>( true );
		LightFlicker flicker = light != null ? light.GetComponent<LightFlicker>() : null;
		Feedbacks ignite = EnsureFeedbackChild( prefabRoot.transform, "OnIgniteFeedbacks", out bool igniteChanged );
		Feedbacks extinguish = EnsureFeedbackChild( prefabRoot.transform, "OnExtinguishFeedbacks", out bool extinguishChanged );
		changed |= igniteChanged || extinguishChanged;

		EnsurePlaySfxFeedback( ignite, spatialBlend: 1f, minDistance: 1f, maxDistance: 12f, out bool igniteSfxChanged );
		EnsurePlaySfxFeedback( extinguish, spatialBlend: 1f, minDistance: 1f, maxDistance: 12f, out bool extinguishSfxChanged );
		changed |= igniteSfxChanged || extinguishSfxChanged;

		SerializedObject serializedActivator = new SerializedObject( activator );
		SetReference( serializedActivator, "_light", light );
		SetReference( serializedActivator, "_lightFlicker", flicker );
		SetReference( serializedActivator, "_onIgnite", ignite );
		SetReference( serializedActivator, "_onExtinguish", extinguish );
		serializedActivator.ApplyModifiedPropertiesWithoutUndo();
		changed = true;

		if ( changed )
		{
			PrefabUtility.SaveAsPrefabAsset( prefabRoot, PrefabPath );
			Debug.Log( "LanternPrefabSetup: updated " + PrefabPath );
		}
		else
		{
			Debug.Log( "LanternPrefabSetup: " + PrefabPath + " already wired." );
		}

		PrefabUtility.UnloadPrefabContents( prefabRoot );
	}

	static Feedbacks EnsureFeedbackChild( Transform parent, string childName, out bool changed )
	{
		changed = false;
		Transform existing = parent.Find( childName );
		GameObject go = existing != null ? existing.gameObject : null;
		if ( go == null )
		{
			go = new GameObject( childName );
			go.transform.SetParent( parent, false );
			changed = true;
		}

		Feedbacks feedbacks = go.GetComponent<Feedbacks>();
		if ( feedbacks == null )
		{
			feedbacks = go.AddComponent<Feedbacks>();
			changed = true;
		}

		return feedbacks;
	}

	static void EnsurePlaySfxFeedback( Feedbacks feedbacks, float spatialBlend, float minDistance, float maxDistance, out bool changed )
	{
		changed = false;
		if ( feedbacks == null )
			return;

		feedbacks.Initialize();
		if ( feedbacks.FeedbackList == null || feedbacks.FeedbackList.Count == 0 )
		{
			feedbacks.AddFeedback( new PlaySFXFeedback
			{
				VolumeMin = 0.7f,
				VolumeMax = 0.9f,
				PitchMin = 0.95f,
				PitchMax = 1.05f,
				SpatialBlend = spatialBlend,
				MinDistance = minDistance,
				MaxDistance = maxDistance
			} );
			changed = true;
			return;
		}

		for ( int i = 0; i < feedbacks.FeedbackList.Count; i++ )
		{
			if ( feedbacks.FeedbackList[ i ] is PlaySFXFeedback playSfx )
			{
				playSfx.SpatialBlend = spatialBlend;
				playSfx.MinDistance = minDistance;
				playSfx.MaxDistance = maxDistance;
				changed = true;
			}
		}
	}

	static void SetReference( SerializedObject serializedObject, string propertyName, Object value )
	{
		SerializedProperty property = serializedObject.FindProperty( propertyName );
		if ( property != null )
			property.objectReferenceValue = value;
	}
}
#endif
