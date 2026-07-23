using System.Collections.Generic;

using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Debug visualisation and hotkeys for loot instance chunk streaming.
/// F1 = toggle streaming filter, F2 = toggle loot instances.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent( typeof( GoldPileLootInstances ) )]
public class GoldPileLootStreamDebug : MonoBehaviour
{
	static readonly List<GoldPileLootStreamDebug> s_active = new List<GoldPileLootStreamDebug>();

	public static IReadOnlyList<GoldPileLootStreamDebug> ActiveInstances => s_active;

	[SerializeField]
	bool drawGizmos = true;

	[SerializeField]
	bool drawOverlay = false;

	[SerializeField]
	Key toggleStreamingKey = Key.F1;

	[SerializeField]
	Key toggleLootKey = Key.F2;

	GoldPileLootInstances _loot;

	public bool DrawOverlay => drawOverlay;
	public bool StreamingEnabled => _loot != null && _loot.StreamingEnabled;
	public bool LootEnabled => _loot != null && _loot.enabled;

	void Awake()
	{
		_loot = GetComponent<GoldPileLootInstances>();
	}

	void OnEnable()
	{
		if ( _loot == null )
			_loot = GetComponent<GoldPileLootInstances>();

		if ( !s_active.Contains( this ) )
			s_active.Add( this );
	}

	void OnDisable()
	{
		s_active.Remove( this );
	}

	void Update()
	{
		if ( _loot == null )
			return;

		Keyboard keyboard = Keyboard.current;
		if ( keyboard == null )
			return;

		if ( keyboard[ toggleStreamingKey ].wasPressedThisFrame )
			ToggleStreaming();

		if ( keyboard[ toggleLootKey ].wasPressedThisFrame )
			ToggleLoot();
	}

	public void ToggleStreaming()
	{
		if ( _loot == null )
			return;
		_loot.SetStreamingEnabled( !_loot.StreamingEnabled );
	}

	public void ToggleLoot()
	{
		if ( _loot == null )
			return;
		_loot.enabled = !_loot.enabled;
	}

	public void SetDrawOverlay( bool enabled )
	{
		drawOverlay = enabled;
	}

	void OnGUI()
	{
		if ( !drawOverlay || _loot == null )
			return;

		GoldPileLootStreamSettings settings = _loot.StreamSettings;
		if ( settings != null && !settings.drawOverlayStats )
			return;

		const float pad = 8f;
		const float width = 460f;
		const float height = 168f;
		float x = pad;
		if ( DebugOverlay.IsOpen )
			x = DebugOverlay.PanelWidth + pad * 2f;

		Rect r = new Rect( x, pad, width, height );
		GUI.Box( r, GUIContent.none );
		GUILayout.BeginArea( new Rect( x + 6f, pad + 4f, width - 12f, height - 8f ) );

		GUILayout.Label( "Gold Pile Loot Stream Debug" );
		GUILayout.Label( $"[{toggleStreamingKey}] Streaming: {( _loot.StreamingEnabled ? "ON" : "OFF (draw all Drawn)" )}" );
		GUILayout.Label( $"[{toggleLootKey}] Loot Inst: {( _loot.enabled ? "ON" : "OFF" )}" );

		if ( !_loot.IsReady )
		{
			GUILayout.Label( "NOT READY" );
			GUILayout.EndArea();
			return;
		}

		GoldPileChunkStreamer streamer = _loot.Streamer;
		GUILayout.Label( $"Loaded {streamer.LoadedCount}  Visible {streamer.VisibleCount}  Rendered {streamer.RenderedCount}" );
		GUILayout.Label( $"Frustum culled {streamer.FrustumCulledCount}" );
		GUILayout.Label( $"Drawn {_loot.LastDrawnCount}  Culled {_loot.LastCulledCount}  Pool {_loot.DrawnPoolCount}" );
		GUILayout.Label( $"Cache rebuilds {_loot.CacheRebuildCount}" );

		GUILayout.EndArea();
	}

	void OnDrawGizmos()
	{
		if ( !drawGizmos )
			return;
		if ( _loot == null )
			_loot = GetComponent<GoldPileLootInstances>();
		if ( _loot == null || _loot.ChunkGrid == null || _loot.ChunkGrid.ChunkCount == 0 )
			return;

		GoldPileLootStreamSettings settings = _loot.StreamSettings;
		if ( settings != null && !settings.drawChunkGizmos )
			return;

		var chunks = _loot.ChunkGrid.Chunks;
		for ( int i = 0; i < chunks.Count; i++ )
		{
			GoldPileChunk chunk = chunks[ i ];
			Color c;
			switch ( chunk.State )
			{
				case GoldPileChunkStreamState.Rendered:
					c = LodColor( chunk.Lod );
					break;
				case GoldPileChunkStreamState.Visible:
					c = new Color( 0.2f, 0.8f, 1f, 0.35f );
					break;
				case GoldPileChunkStreamState.Loaded:
					c = chunk.FrustumVisible
						? new Color( 0.5f, 0.5f, 0.5f, 0.25f )
						: new Color( 0.35f, 0.2f, 0.6f, 0.3f );
					break;
				default:
					c = new Color( 0.15f, 0.15f, 0.15f, 0.12f );
					break;
			}

			if ( chunk.Dirty )
				c = Color.Lerp( c, Color.red, 0.55f );

			Gizmos.color = c;
			Gizmos.DrawWireCube( chunk.WorldBounds.center, chunk.WorldBounds.size );
		}
	}

	static Color LodColor( int lod )
	{
		switch ( lod )
		{
			case 0: return new Color( 0.2f, 1f, 0.3f, 0.7f );
			case 1: return new Color( 0.85f, 1f, 0.2f, 0.65f );
			case 2: return new Color( 1f, 0.55f, 0.15f, 0.6f );
			default: return new Color( 0.6f, 0.6f, 0.6f, 0.4f );
		}
	}
}
