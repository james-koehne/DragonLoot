using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Pools one <see cref="LineRenderer"/> per resolved constellation edge and toggles visibility
/// when both endpoint slots hold a gem. Uses <c>DragonLoot/ConstellationLine</c> for glowy energy flow.
/// </summary>
public class GemConstellationLineVisual : MonoBehaviour
{
	const string EnergyShaderName = "DragonLoot/ConstellationLine";

	[SerializeField]
	GemConstellationInteractable constellation;

	[SerializeField]
	Material lineMaterial;

	[SerializeField]
	[Min( 0.001f )]
	float lineWidth = 0.045f;

	[SerializeField]
	[ColorUsage( true, true )]
	Color litColor = new Color( 0.45f, 1.1f, 1.8f, 1f );

	[SerializeField]
	[ColorUsage( true, true )]
	Color coreColor = new Color( 1.6f, 2.2f, 3f, 1f );

	[SerializeField]
	[ColorUsage( true, true )]
	Color pulseColor = new Color( 2f, 1.2f, 3.2f, 1f );

	[SerializeField]
	Color dimColor = new Color( 0.45f, 0.85f, 1f, 0f );

	[SerializeField]
	[Min( 0.1f )]
	float scrollSpeed = 1.35f;

	[SerializeField]
	[Range( 1f, 12f )]
	float pulseCount = 3.5f;

	readonly List<LineRenderer> _lines = new List<LineRenderer>();
	readonly List<GemConstellationResolvedConnection> _connections = new List<GemConstellationResolvedConnection>();
	readonly List<bool> _previousLit = new List<bool>();
	Material _runtimeMaterial;
	bool _suppressConnectionEvents;

	void Awake()
	{
		EnsureMaterial();
		ApplyMaterialProperties( ResolveMaterial() );
	}

	void OnEnable()
	{
		if ( constellation != null )
			Rebuild( constellation.ResolvedConnections );
	}

	void OnDestroy()
	{
		if ( _runtimeMaterial != null )
		{
			Destroy( _runtimeMaterial );
			_runtimeMaterial = null;
		}
	}

	void LateUpdate()
	{
		if ( constellation == null || _connections.Count == 0 )
			return;

		ApplyGeometry();
		RefreshLitState();
	}

	public void Bind( GemConstellationInteractable owner )
	{
		constellation = owner;
	}

	public void Rebuild( IReadOnlyList<GemConstellationResolvedConnection> connections )
	{
		_connections.Clear();
		if ( connections != null )
		{
			for ( int i = 0; i < connections.Count; i++ )
				_connections.Add( connections[ i ] );
		}

		EnsureMaterial();
		ApplyMaterialProperties( ResolveMaterial() );
		EnsureLineCount( _connections.Count );
		ApplyGeometry();
		_suppressConnectionEvents = true;
		RefreshLitState();
		_suppressConnectionEvents = false;
	}

	public void RefreshLitState()
	{
		if ( constellation == null )
			return;

		EnsurePreviousLitCount( _connections.Count );

		for ( int i = 0; i < _connections.Count; i++ )
		{
			GemConstellationResolvedConnection edge = _connections[ i ];
			bool lit = constellation.IsSlotOccupied( edge.SlotA ) && constellation.IsSlotOccupied( edge.SlotB );
			bool wasLit = i < _previousLit.Count && _previousLit[ i ];

			if ( i < _lines.Count && _lines[ i ] != null )
			{
				LineRenderer lr = _lines[ i ];
				lr.enabled = lit;
				Color c = lit ? Color.white : dimColor;
				lr.startColor = c;
				lr.endColor = c;
				lr.startWidth = lineWidth;
				lr.endWidth = lineWidth;
			}

			if ( !_suppressConnectionEvents && lit != wasLit )
			{
				EventBus.Publish( new GemConstellationConnectionChangedEvent
				{
					Constellation = constellation,
					SlotA = edge.SlotA,
					SlotB = edge.SlotB,
					IsLit = lit
				} );
			}

			if ( i < _previousLit.Count )
				_previousLit[ i ] = lit;
		}
	}

	void EnsurePreviousLitCount( int count )
	{
		while ( _previousLit.Count < count )
			_previousLit.Add( false );

		while ( _previousLit.Count > count )
			_previousLit.RemoveAt( _previousLit.Count - 1 );
	}

	void EnsureLineCount( int count )
	{
		EnsureMaterial();

		while ( _lines.Count < count )
		{
			GameObject go = new GameObject( "Connection_" + _lines.Count );
			go.transform.SetParent( transform, false );

			LineRenderer lr = go.AddComponent<LineRenderer>();
			ConfigureLineRenderer( lr );
			_lines.Add( lr );
		}

		for ( int i = 0; i < _lines.Count; i++ )
		{
			if ( _lines[ i ] == null )
				continue;

			ConfigureLineRenderer( _lines[ i ] );
			_lines[ i ].gameObject.SetActive( i < count );
		}
	}

	void ConfigureLineRenderer( LineRenderer lr )
	{
		if ( lr == null )
			return;

		lr.useWorldSpace = true;
		lr.positionCount = 2;
		lr.numCapVertices = 8;
		lr.numCornerVertices = 4;
		lr.startWidth = lineWidth;
		lr.endWidth = lineWidth;
		lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
		lr.receiveShadows = false;
		lr.textureMode = LineTextureMode.Stretch;
		lr.alignment = LineAlignment.View;
		lr.sharedMaterial = ResolveMaterial();
		lr.startColor = dimColor;
		lr.endColor = dimColor;
		lr.enabled = false;
	}

	void ApplyGeometry()
	{
		if ( constellation == null )
			return;

		for ( int i = 0; i < _connections.Count; i++ )
		{
			if ( i >= _lines.Count || _lines[ i ] == null )
				continue;

			GemConstellationResolvedConnection edge = _connections[ i ];
			constellation.GetSlotWorldPose( edge.SlotA, out Vector3 a, out _ );
			constellation.GetSlotWorldPose( edge.SlotB, out Vector3 b, out _ );

			LineRenderer lr = _lines[ i ];
			lr.SetPosition( 0, a );
			lr.SetPosition( 1, b );
		}
	}

	void EnsureMaterial()
	{
		if ( _runtimeMaterial != null )
			return;

		if ( lineMaterial != null )
		{
			_runtimeMaterial = new Material( lineMaterial );
			_runtimeMaterial.name = lineMaterial.name + " (Instance)";
			ApplyMaterialProperties( _runtimeMaterial );
			return;
		}

		Shader shader = Shader.Find( EnergyShaderName );
		if ( shader == null )
		{
			shader = Shader.Find( "Universal Render Pipeline/Unlit" );
			if ( shader == null )
				shader = Shader.Find( "Sprites/Default" );
			if ( shader == null )
				return;
		}

		_runtimeMaterial = new Material( shader );
		_runtimeMaterial.name = "GemConstellationLine (Runtime)";
		ApplyMaterialProperties( _runtimeMaterial );
	}

	Material ResolveMaterial()
	{
		EnsureMaterial();
		return _runtimeMaterial != null ? _runtimeMaterial : lineMaterial;
	}

	void ApplyMaterialProperties( Material mat )
	{
		if ( mat == null )
			return;

		if ( mat.HasProperty( "_Color" ) )
			mat.SetColor( "_Color", litColor );
		if ( mat.HasProperty( "_CoreColor" ) )
			mat.SetColor( "_CoreColor", coreColor );
		if ( mat.HasProperty( "_PulseColor" ) )
			mat.SetColor( "_PulseColor", pulseColor );
		if ( mat.HasProperty( "_ScrollSpeed" ) )
			mat.SetFloat( "_ScrollSpeed", scrollSpeed );
		if ( mat.HasProperty( "_PulseCount" ) )
			mat.SetFloat( "_PulseCount", pulseCount );
		if ( mat.HasProperty( "_BaseColor" ) )
			mat.SetColor( "_BaseColor", litColor );
	}

	void OnValidate()
	{
		lineWidth = Mathf.Max( 0.001f, lineWidth );
		scrollSpeed = Mathf.Max( 0.1f, scrollSpeed );
		pulseCount = Mathf.Clamp( pulseCount, 1f, 12f );

		if ( _runtimeMaterial != null )
			ApplyMaterialProperties( _runtimeMaterial );

		for ( int i = 0; i < _lines.Count; i++ )
		{
			if ( _lines[ i ] == null )
				continue;

			_lines[ i ].startWidth = lineWidth;
			_lines[ i ].endWidth = lineWidth;
			if ( _runtimeMaterial != null )
				_lines[ i ].sharedMaterial = _runtimeMaterial;
		}
	}
}
