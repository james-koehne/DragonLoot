#if UNITY_EDITOR
using Unity.Mathematics;

using UnityEditor;
using UnityEngine;
using UnityEngine.Splines;
using UnityEngine.SceneManagement;

/// <summary>
/// Rebuilds baked track mesh when the spline or track settings change.
/// </summary>
[CustomEditor( typeof( MinecartTrack ) )]
public class MinecartTrackEditor : Editor
{
	void OnEnable()
	{
		Spline.Changed += OnSplineChanged;
	}

	void OnDisable()
	{
		Spline.Changed -= OnSplineChanged;
	}

	public override void OnInspectorGUI()
	{
		serializedObject.Update();
		DrawDefaultInspector();
		if ( GUILayout.Button( "Rebuild Track" ) )
		{
			MinecartTrack track = (MinecartTrack)target;
			MinecartTrackMeshBuilder.Rebuild( track );
		}

		if ( serializedObject.ApplyModifiedProperties() )
			MinecartTrackMeshBuilder.Rebuild( (MinecartTrack)target );
	}

	void OnSplineChanged( Spline spline, int knotIndex, SplineModification modification )
	{
		MinecartTrack track = target as MinecartTrack;
		if ( track == null || track.Spline != spline )
			return;

		MinecartTrackMeshBuilder.Rebuild( track );
	}

	[MenuItem( DragonLootMenus.MinecartCreateTrack )]
	[MenuItem( DragonLootMenus.GameObjectMinecartTrack )]
	static void CreateTrack()
	{
		Scene scene = SceneManager.GetActiveScene();
		if ( !scene.IsValid() || !scene.isLoaded )
			return;

		GameObject go = new GameObject( "MinecartTrack" );
		SplineContainer container = go.AddComponent<SplineContainer>();
		Spline spline = container.Spline;
		if ( spline == null )
		{
			spline = new Spline();
			container.AddSpline( spline );
		}
		else
			spline.Clear();

		spline.Add( new BezierKnot( new float3( 0f, 0.05f, 0f ) ), TangentMode.AutoSmooth );
		spline.Add( new BezierKnot( new float3( 0f, 0.05f, 6f ) ), TangentMode.AutoSmooth );
		spline.Add( new BezierKnot( new float3( 2f, 0.05f, 10f ) ), TangentMode.AutoSmooth );

		MinecartTrack track = go.AddComponent<MinecartTrack>();
		track.EditorSetMaterials( MinecartTrackMeshBuilder.EnsureRailMaterial(), MinecartTrackMeshBuilder.EnsureSleeperMaterial() );
		Undo.RegisterCreatedObjectUndo( go, "Create Minecart Track" );
		Selection.activeGameObject = go;
		MinecartTrackMeshBuilder.Rebuild( track );
	}
}
#endif
