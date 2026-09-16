#if UNITY_EDITOR
using System.IO;

using FeedbackSystem;

using UnityEditor;

using UnityEngine;

/// <summary>
/// Wires speed-linked move loop + brake one-shot onto Addressable minecart prefabs.
/// </summary>
public static class MinecartAudioPrefabBuilder
{
	const string CargoPrefabPath = "Assets/Addressables/Minecart/Minecart.prefab";
	const string DrivePrefabPath = "Assets/Addressables/Minecart/MinecartDrive.prefab";
	const string MoveClipPath = "Assets/Audio/SFX/Minecart/minecart_move.mp3";
	const string BrakeClipPath = "Assets/Audio/SFX/Minecart/minecart_brake.mp3";

	static bool _ranThisDomain;

	[InitializeOnLoadMethod]
	static void QueuePatch()
	{
		if ( _ranThisDomain )
			return;
		_ranThisDomain = true;
		EditorApplication.delayCall += EnsureAudioOnPrefabs;
	}

	[MenuItem( DragonLootMenus.MinecartPatchAudio )]
	public static void PatchFromMenu()
	{
		EnsureAudioOnPrefabs();
	}

	public static void EnsureAudioOnPrefabs()
	{
		try
		{
			bool dirty = false;
			if ( File.Exists( CargoPrefabPath ) )
				dirty |= PatchPrefab( CargoPrefabPath, includeBrake: false );
			if ( File.Exists( DrivePrefabPath ) )
				dirty |= PatchPrefab( DrivePrefabPath, includeBrake: true );
			if ( dirty )
			{
				AssetDatabase.SaveAssets();
				AssetDatabase.Refresh();
				Debug.Log( "MinecartAudioPrefabBuilder: patched minecart move/brake audio." );
			}
		}
		catch ( System.Exception ex )
		{
			Debug.LogWarning( "MinecartAudioPrefabBuilder: " + ex.Message );
		}
	}

	public static void BuildFromCommandLine()
	{
		EnsureAudioOnPrefabs();
	}

	static bool PatchPrefab( string path, bool includeBrake )
	{
		AudioClip moveClip = AssetDatabase.LoadAssetAtPath<AudioClip>( MoveClipPath );
		AudioClip brakeClip = includeBrake ? AssetDatabase.LoadAssetAtPath<AudioClip>( BrakeClipPath ) : null;
		if ( moveClip == null )
		{
			Debug.LogWarning( "MinecartAudioPrefabBuilder: missing move clip at " + MoveClipPath );
			return false;
		}

		if ( includeBrake && brakeClip == null )
			Debug.LogWarning( "MinecartAudioPrefabBuilder: missing brake clip at " + BrakeClipPath );

		GameObject root = PrefabUtility.LoadPrefabContents( path );
		if ( root == null )
			return false;

		bool changed = false;
		try
		{
			MinecartInteractable cart = root.GetComponent<MinecartInteractable>();
			if ( cart == null )
			{
				Debug.LogWarning( "MinecartAudioPrefabBuilder: no MinecartInteractable on " + path );
				return false;
			}

			Feedbacks move = EnsureFeedbackChild( root.transform, "OnDriveMoveFeedbacks" );
			AudioSource loopSource = EnsureLoopAudioSource( move.gameObject );
			changed |= EnsureLoopSfx( move, moveClip, loopSource );

			Feedbacks brake = null;
			if ( includeBrake )
			{
				brake = EnsureFeedbackChild( root.transform, "OnDriveBrakeFeedbacks" );
				if ( brakeClip != null )
					changed |= EnsureBrakeSfx( brake, brakeClip );
			}

			SerializedObject so = new SerializedObject( cart );
			SerializedProperty moveProp = so.FindProperty( "onDriveMoveFeedbacks" );
			if ( moveProp != null && moveProp.objectReferenceValue != move )
			{
				moveProp.objectReferenceValue = move;
				changed = true;
			}

			if ( includeBrake && brake != null )
			{
				SerializedProperty brakeProp = so.FindProperty( "onDriveBrakeFeedbacks" );
				if ( brakeProp != null && brakeProp.objectReferenceValue != brake )
				{
					brakeProp.objectReferenceValue = brake;
					changed = true;
				}
			}

			if ( changed )
				so.ApplyModifiedPropertiesWithoutUndo();

			if ( changed )
				PrefabUtility.SaveAsPrefabAsset( root, path );
		}
		finally
		{
			PrefabUtility.UnloadPrefabContents( root );
		}

		return changed;
	}

	static Feedbacks EnsureFeedbackChild( Transform root, string childName )
	{
		Transform existing = root.Find( childName );
		GameObject go = existing != null ? existing.gameObject : new GameObject( childName );
		if ( existing == null )
			go.transform.SetParent( root, false );

		Feedbacks feedbacks = go.GetComponent<Feedbacks>();
		if ( feedbacks == null )
			feedbacks = go.AddComponent<Feedbacks>();
		return feedbacks;
	}

	static AudioSource EnsureLoopAudioSource( GameObject host )
	{
		AudioSource source = host.GetComponent<AudioSource>();
		if ( source == null )
			source = host.AddComponent<AudioSource>();

		source.playOnAwake = false;
		source.loop = true;
		source.spatialBlend = 1f;
		source.dopplerLevel = 0.4f;
		source.minDistance = 2f;
		source.maxDistance = 30f;
		source.rolloffMode = AudioRolloffMode.Linear;
		source.volume = 0f;
		return source;
	}

	static bool EnsureLoopSfx( Feedbacks feedbacks, AudioClip clip, AudioSource source )
	{
		bool changed = false;
		LoopSfxFeedback loop = null;
		for ( int i = 0; i < feedbacks.FeedbackList.Count; i++ )
		{
			loop = feedbacks.FeedbackList[ i ] as LoopSfxFeedback;
			if ( loop != null )
				break;
		}

		if ( loop == null )
		{
			loop = new LoopSfxFeedback();
			feedbacks.AddFeedback( loop );
			changed = true;
		}

		if ( loop.Clip != clip )
		{
			loop.Clip = clip;
			changed = true;
		}

		if ( loop.AudioSource != source )
		{
			loop.AudioSource = source;
			changed = true;
		}

		loop.PitchAtRest = 0.75f;
		loop.PitchAtMax = 1.2f;
		loop.VolumeAtRest = 0f;
		loop.VolumeAtMax = 0.85f;
		loop.FadeInSeconds = 0.12f;
		loop.FadeOutSeconds = 0.28f;
		loop.BrakeDuck = 0.55f;
		loop.SpatialBlend = 1f;
		loop.MinDistance = 2f;
		loop.MaxDistance = 30f;
		loop.DopplerLevel = 0.4f;

		EditorUtility.SetDirty( feedbacks );
		return changed;
	}

	static bool EnsureBrakeSfx( Feedbacks feedbacks, AudioClip clip )
	{
		bool changed = false;

		// Replace legacy one-shot brake if present.
		for ( int i = feedbacks.FeedbackList.Count - 1; i >= 0; i-- )
		{
			if ( feedbacks.FeedbackList[ i ] is PlaySFXFeedback )
			{
				feedbacks.FeedbackList.RemoveAt( i );
				changed = true;
			}
		}

		LoopSfxFeedback loop = null;
		for ( int i = 0; i < feedbacks.FeedbackList.Count; i++ )
		{
			loop = feedbacks.FeedbackList[ i ] as LoopSfxFeedback;
			if ( loop != null )
				break;
		}

		if ( loop == null )
		{
			loop = new LoopSfxFeedback();
			feedbacks.AddFeedback( loop );
			changed = true;
		}

		if ( loop.Clip != clip )
		{
			loop.Clip = clip;
			changed = true;
		}

		loop.RequireBraking = true;
		loop.PitchAtRest = 0.9f;
		loop.PitchAtMax = 1.1f;
		loop.VolumeAtRest = 0f;
		loop.VolumeAtMax = 0.95f;
		loop.FadeInSeconds = 0.06f;
		loop.FadeOutSeconds = 0.2f;
		loop.BrakeDuck = 1f;
		loop.SpatialBlend = 1f;
		loop.MinDistance = 2f;
		loop.MaxDistance = 30f;
		loop.DopplerLevel = 0.25f;
		loop.AudioSource = null;

		EditorUtility.SetDirty( feedbacks );
		return changed;
	}
}
#endif
