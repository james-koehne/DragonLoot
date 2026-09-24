using UnityEngine;

/// <summary>
/// Debug: place two transforms and draw a treasure-surface path between them.
/// Recalculates when <see cref="TreasureSurfaceWorld.GeometryVersion"/> changes and dirty rebuilds settle.
/// </summary>
[DisallowMultipleComponent]
public class TreasureSurfacePathDebug : MonoBehaviour
{
	static TreasureSurfacePathDebug _instance;

	[SerializeField]
	Transform start;

	[SerializeField]
	Transform end;

	[SerializeField]
	TreasureSurfacePathSettings settings = TreasureSurfacePathSettings.Default;

	[SerializeField]
	LineRenderer lineRenderer;

	[SerializeField]
	[Tooltip( "Lift the drawn line slightly above sampled surface height." )]
	float lineHeightOffset = 0.08f;

	[SerializeField]
	Color successColor = new Color( 0.2f, 0.95f, 0.45f, 0.95f );

	[SerializeField]
	Color failColor = new Color( 1f, 0.25f, 0.2f, 0.85f );

	[SerializeField]
	float gizmoRadius = 0.35f;

	int _lastGeometryVersion = -1;
	bool _lastSuccess;
	int _lastWaypointCount;
	float _lastLength;
	Vector3 _lastStartPos;
	Vector3 _lastEndPos;

	public static TreasureSurfacePathDebug Instance => _instance;

	public bool LastSuccess => _lastSuccess;
	public int LastWaypointCount => _lastWaypointCount;
	public float LastLength => _lastLength;
	public TreasureSurfacePathSettings Settings => settings;

	public void SetEndpoints( Transform pathStart, Transform pathEnd )
	{
		start = pathStart;
		end = pathEnd;
		_lastGeometryVersion = -1;
	}

	public void SetLineRenderer( LineRenderer renderer )
	{
		lineRenderer = renderer;
	}

	void OnEnable()
	{
		_instance = this;
		EnsureLineRenderer();
		_lastGeometryVersion = -1;
	}

	void OnDisable()
	{
		if ( _instance == this )
			_instance = null;
		ClearLine();
	}

	void LateUpdate()
	{
		if ( start == null || end == null )
		{
			ClearLine();
			_lastSuccess = false;
			_lastWaypointCount = 0;
			_lastLength = 0f;
			return;
		}

		TreasureSurfaceWorld world = TreasureSurfaceWorld.Instance;
		if ( world == null || !world.IsInitialized )
		{
			ClearLine();
			return;
		}

		Vector3 startPos = start.position;
		Vector3 endPos = end.position;
		bool endpointsMoved =
			( startPos - _lastStartPos ).sqrMagnitude > 0.0001f
			|| ( endPos - _lastEndPos ).sqrMagnitude > 0.0001f;

		int version = world.GeometryVersion;
		bool versionChanged = version != _lastGeometryVersion;
		// Keep trying after a failure (chunks may still be loading / settling).
		if ( !versionChanged && !endpointsMoved && _lastSuccess )
			return;

		// Wait for dirty rebuilds to settle after a geometry bump so heights/flags are current.
		if ( versionChanged && !endpointsMoved && world.DirtyChunkCount > 0 )
			return;

		_lastGeometryVersion = version;
		_lastStartPos = startPos;
		_lastEndPos = endPos;
		Recalculate( startPos, endPos );
	}

	void Recalculate( Vector3 startPos, Vector3 endPos )
	{
		EnsureLineRenderer();
		if ( !TreasureSurfacePathfinder.TryFindPath( startPos, endPos, settings, out TreasureSurfacePath path )
			|| !path.Success
			|| path.WaypointCount == 0 )
		{
			_lastSuccess = false;
			_lastWaypointCount = 0;
			_lastLength = 0f;
			ApplyFailLine( startPos, endPos );
			return;
		}

		_lastSuccess = true;
		_lastWaypointCount = path.WaypointCount;
		_lastLength = path.Length;

		int count = path.WaypointCount;
		lineRenderer.positionCount = count;
		lineRenderer.startColor = successColor;
		lineRenderer.endColor = successColor;
		for ( int i = 0; i < count; i++ )
		{
			Vector3 p = path.Waypoints[ i ];
			p.y += lineHeightOffset;
			lineRenderer.SetPosition( i, p );
		}
	}

	void ApplyFailLine( Vector3 startPos, Vector3 endPos )
	{
		if ( lineRenderer == null )
			return;

		lineRenderer.positionCount = 2;
		lineRenderer.startColor = failColor;
		lineRenderer.endColor = failColor;
		lineRenderer.SetPosition( 0, startPos + Vector3.up * lineHeightOffset );
		lineRenderer.SetPosition( 1, endPos + Vector3.up * lineHeightOffset );
	}

	void ClearLine()
	{
		if ( lineRenderer != null )
			lineRenderer.positionCount = 0;
	}

	void EnsureLineRenderer()
	{
		if ( lineRenderer != null )
			return;

		lineRenderer = GetComponent<LineRenderer>();
		if ( lineRenderer != null )
			return;

		lineRenderer = gameObject.AddComponent<LineRenderer>();
		lineRenderer.name = "TreasureSurfacePathLine";
		lineRenderer.useWorldSpace = true;
		lineRenderer.loop = false;
		lineRenderer.widthMultiplier = 0.08f;
		lineRenderer.numCapVertices = 2;
		lineRenderer.numCornerVertices = 2;
		lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
		lineRenderer.receiveShadows = false;
		lineRenderer.material = new Material( Shader.Find( "Sprites/Default" ) );
		lineRenderer.startColor = successColor;
		lineRenderer.endColor = successColor;
		lineRenderer.positionCount = 0;
	}

	void OnDrawGizmos()
	{
		DrawEndpointGizmo( start, successColor );
		DrawEndpointGizmo( end, failColor );
	}

	void DrawEndpointGizmo( Transform t, Color color )
	{
		if ( t == null )
			return;

		Gizmos.color = color;
		Gizmos.DrawSphere( t.position, gizmoRadius );
		Gizmos.DrawWireSphere( t.position, gizmoRadius * 1.35f );
	}
}
