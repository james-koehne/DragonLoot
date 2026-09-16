#if UNITY_EDITOR
using FeedbackSystem;

using UnityEditor;

using UnityEngine;

/// <summary>
/// Ensures QuestObjective completion Feedbacks children exist on the Interface prefab.
/// </summary>
public static class QuestObjectiveFeedbackPostprocessor
{
	const string InterfacePrefabPath = "Assets/Addressables/Interface.prefab";
	const string SubClipPath = "Assets/Audio/SFX/Rewarding/ObjectiveComplete_01_TEMP_DELETE.wav";
	const string CompleteClipPath = "Assets/Audio/SFX/Rewarding/ObjectiveComplete_02_TEMP_DELETE.wav";

	static bool _ranThisDomain;

	[InitializeOnLoadMethod]
	static void QueuePatch()
	{
		if ( _ranThisDomain )
			return;
		_ranThisDomain = true;
		EditorApplication.delayCall += EnsureFeedbacksOnPrefab;
	}

	public static void EnsureFeedbacksOnPrefab()
	{
		GameObject root = PrefabUtility.LoadPrefabContents( InterfacePrefabPath );
		if ( root == null )
			return;

		try
		{
			Transform quest = FindDeepChild( root.transform, "QuestObjective" );
			if ( quest == null )
			{
				Debug.LogWarning( "QuestObjectiveFeedbackPostprocessor: QuestObjective missing on Interface prefab." );
				return;
			}

			QuestObjectiveUI ui = quest.GetComponent<QuestObjectiveUI>();
			if ( ui == null )
			{
				Debug.LogWarning( "QuestObjectiveFeedbackPostprocessor: QuestObjectiveUI missing." );
				return;
			}

			Feedbacks subFeedback = EnsureSfxChild( quest, "SubObjectiveCompleteFeedbacks", SubClipPath );
			Feedbacks completeFeedback = EnsureSfxChild( quest, "ObjectiveCompleteFeedbacks", CompleteClipPath );

			SerializedObject so = new SerializedObject( ui );
			so.FindProperty( "subObjectiveCompleteFeedback" ).objectReferenceValue = subFeedback;
			so.FindProperty( "objectiveCompleteFeedback" ).objectReferenceValue = completeFeedback;
			so.ApplyModifiedPropertiesWithoutUndo();

			PrefabUtility.SaveAsPrefabAsset( root, InterfacePrefabPath );
		}
		finally
		{
			PrefabUtility.UnloadPrefabContents( root );
		}
	}

	static Feedbacks EnsureSfxChild( Transform parent, string childName, string clipPath )
	{
		Transform existing = parent.Find( childName );
		GameObject go = existing != null ? existing.gameObject : new GameObject( childName );
		if ( existing == null )
			go.transform.SetParent( parent, false );

		Feedbacks feedbacks = go.GetComponent<Feedbacks>();
		if ( feedbacks == null )
			feedbacks = go.AddComponent<Feedbacks>();

		if ( feedbacks.FeedbackList == null || feedbacks.FeedbackList.Count == 0 )
		{
			AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>( clipPath );
			PlaySFXFeedback sfx = new PlaySFXFeedback
			{
				Clip = clip,
				VolumeMin = 0.85f,
				VolumeMax = 1f,
				PitchMin = 0.98f,
				PitchMax = 1.02f,
				SpatialBlend = 0f
			};
			feedbacks.AddFeedback( sfx );
			EditorUtility.SetDirty( feedbacks );
		}

		return feedbacks;
	}

	static Transform FindDeepChild( Transform parent, string name )
	{
		if ( parent.name == name )
			return parent;

		for ( int i = 0; i < parent.childCount; i++ )
		{
			Transform found = FindDeepChild( parent.GetChild( i ), name );
			if ( found != null )
				return found;
		}

		return null;
	}
}
#endif
