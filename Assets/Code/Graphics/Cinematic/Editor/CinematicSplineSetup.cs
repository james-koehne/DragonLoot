#if UNITY_EDITOR
using Unity.Mathematics;

using UnityEditor;

using UnityEngine;
using UnityEngine.Splines;

/// <summary>
/// Creates empty camera + look spline containers under a <see cref="CinematicPresentationController"/>.
/// Draw the rails in the Scene view after running this menu.
/// </summary>
public static class CinematicSplineSetup
{
	[MenuItem( DragonLootMenus.CinematicCreateIntroSplines )]
	static void CreateIntroSplines()
	{
		CinematicPresentationController controller = ResolveController();
		if ( controller == null )
		{
			EditorUtility.DisplayDialog(
				"Create Intro Splines",
				"Select a GameObject with CinematicPresentationController first.",
				"OK" );
			return;
		}

		Undo.IncrementCurrentGroup();
		int undoGroup = Undo.GetCurrentGroup();
		Undo.SetCurrentGroupName( "Create Intro Splines" );

		SplineContainer cameraPath = EnsureChildSpline(
			controller.transform,
			CinematicPresentationController.IntroCameraPathName,
			new float3( 0f, 2f, 0f ),
			new float3( 4f, 3f, 8f ),
			new float3( 0f, 4f, 16f ) );
		SplineContainer lookPath = EnsureChildSpline(
			controller.transform,
			CinematicPresentationController.IntroLookPathName,
			new float3( 0f, 1f, 4f ),
			new float3( 2f, 1.5f, 10f ),
			new float3( 0f, 2f, 18f ) );

		SerializedObject so = new SerializedObject( controller );
		SerializedProperty cameraProp = so.FindProperty( "_cameraPath" );
		SerializedProperty lookProp = so.FindProperty( "_lookPath" );
		bool assignedCamera = false;
		bool assignedLook = false;
		if ( cameraProp != null && cameraProp.objectReferenceValue == null )
		{
			cameraProp.objectReferenceValue = cameraPath;
			assignedCamera = true;
		}

		if ( lookProp != null && lookProp.objectReferenceValue == null )
		{
			lookProp.objectReferenceValue = lookPath;
			assignedLook = true;
		}

		so.ApplyModifiedProperties();
		if ( assignedCamera || assignedLook )
			controller.EditorAssignPaths(
				assignedCamera ? cameraPath : null,
				assignedLook ? lookPath : null );

		Undo.CollapseUndoOperations( undoGroup );
		Selection.activeGameObject = cameraPath.gameObject;
		EditorGUIUtility.PingObject( cameraPath.gameObject );
	}

	[MenuItem( DragonLootMenus.CinematicCreateIntroSplines, true )]
	static bool CreateIntroSplinesValidate()
	{
		return ResolveController() != null;
	}

	static CinematicPresentationController ResolveController()
	{
		if ( Selection.activeGameObject == null )
			return null;

		CinematicPresentationController onSelection = Selection.activeGameObject.GetComponent<CinematicPresentationController>();
		if ( onSelection != null )
			return onSelection;

		return Selection.activeGameObject.GetComponentInParent<CinematicPresentationController>();
	}

	static SplineContainer EnsureChildSpline( Transform parent, string childName, float3 a, float3 b, float3 c )
	{
		Transform existing = parent.Find( childName );
		GameObject go;
		if ( existing != null )
		{
			go = existing.gameObject;
			SplineContainer existingContainer = go.GetComponent<SplineContainer>();
			if ( existingContainer != null )
				return existingContainer;
		}
		else
		{
			go = new GameObject( childName );
			Undo.RegisterCreatedObjectUndo( go, "Create " + childName );
			go.transform.SetParent( parent, false );
			go.transform.localPosition = Vector3.zero;
			go.transform.localRotation = Quaternion.identity;
			go.transform.localScale = Vector3.one;
		}

		SplineContainer container = Undo.AddComponent<SplineContainer>( go );
		Spline spline = container.Spline;
		if ( spline == null )
		{
			spline = new Spline();
			container.AddSpline( spline );
		}
		else
			spline.Clear();

		spline.Add( new BezierKnot( a ), TangentMode.AutoSmooth );
		spline.Add( new BezierKnot( b ), TangentMode.AutoSmooth );
		spline.Add( new BezierKnot( c ), TangentMode.AutoSmooth );
		EditorUtility.SetDirty( go );
		return container;
	}
}
#endif
