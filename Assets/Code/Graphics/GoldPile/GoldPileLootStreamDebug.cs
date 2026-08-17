using System.Collections.Generic;

using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Debug visualisation and hotkeys for loot instance chunk streaming.
/// Overlay lists every active pile. F1 = toggle streaming on all, F2 = toggle loot instances on all.
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
		// Hotkeys are handled once by the first active instance so all piles stay in sync.
		if ( s_active.Count == 0 || s_active[ 0 ] != this )
			return;

		Keyboard keyboard = Keyboard.current;
		if ( keyboard == null )
			return;

		if ( keyboard[ toggleStreamingKey ].wasPressedThisFrame )
			ToggleStreamingAll();

		if ( keyboard[ toggleLootKey ].wasPressedThisFrame )
			ToggleLootAll();
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

	static void ToggleStreamingAll()
	{
		bool anyOn = false;
		for ( int i = 0; i < s_active.Count; i++ )
		{
			GoldPileLootStreamDebug pile = s_active[ i ];
			if ( pile != null && pile.StreamingEnabled )
			{
				anyOn = true;
				break;
			}
		}

		bool next = !anyOn;
		for ( int i = 0; i < s_active.Count; i++ )
		{
			GoldPileLootStreamDebug pile = s_active[ i ];
			if ( pile == null || pile._loot == null )
				continue;
			pile._loot.SetStreamingEnabled( next );
		}
	}

	static void ToggleLootAll()
	{
		bool anyOn = false;
		for ( int i = 0; i < s_active.Count; i++ )
		{
			GoldPileLootStreamDebug pile = s_active[ i ];
			if ( pile != null && pile.LootEnabled )
			{
				anyOn = true;
				break;
			}
		}

		bool next = !anyOn;
		for ( int i = 0; i < s_active.Count; i++ )
		{
			GoldPileLootStreamDebug pile = s_active[ i ];
			if ( pile == null || pile._loot == null )
				continue;
			pile._loot.enabled = next;
		}
	}

	static bool AnyOverlayEnabled()
	{
		for ( int i = 0; i < s_active.Count; i++ )
		{
			GoldPileLootStreamDebug pile = s_active[ i ];
			if ( pile == null || !pile.drawOverlay )
				continue;

			GoldPileLootStreamSettings settings = pile._loot != null ? pile._loot.StreamSettings : null;
			if ( settings != null && !settings.drawOverlayStats )
				continue;

			return true;
		}

		return false;
	}

	void OnGUI()
	{
		if ( s_active.Count == 0 || s_active[ 0 ] != this )
			return;
		if ( !AnyOverlayEnabled() )
			return;

		const float pad = 8f;
		const float width = 640f;
		const float line = 18f;
		const float headerLines = 5f;
		const float linesPerPile = 11f;
		float height = pad * 2f + line * ( headerLines + linesPerPile * Mathf.Max( 1, s_active.Count ) );
		float x = pad;
		if ( DebugOverlay.IsOpen )
			x = DebugOverlay.PanelWidth + pad * 2f;

		Rect r = new Rect( x, pad, width, height );
		GUI.Box( r, GUIContent.none );
		GUILayout.BeginArea( new Rect( x + 6f, pad + 4f, width - 12f, height - 8f ) );

		Key streamKey = toggleStreamingKey;
		Key lootKey = toggleLootKey;
		GUILayout.Label( $"Gold Pile Loot Stream Debug  ({s_active.Count} piles)" );
		GUILayout.Label( $"[{streamKey}] Streaming all  [{lootKey}] Loot inst all" );

		bool anyStreaming = false;
		bool anyLoot = false;
		for ( int i = 0; i < s_active.Count; i++ )
		{
			GoldPileLootStreamDebug pile = s_active[ i ];
			if ( pile == null )
				continue;
			if ( pile.StreamingEnabled )
				anyStreaming = true;
			if ( pile.LootEnabled )
				anyLoot = true;
		}

		GUILayout.Label( $"Streaming: {( anyStreaming ? "ON" : "OFF (draw all Drawn)" )}  Loot Inst: {( anyLoot ? "ON" : "OFF" )}" );
		GUILayout.Label( $"World parked props: {WorldTreasurePersistence.ParkedCount}" );
		GUILayout.Space( 4f );

		for ( int i = 0; i < s_active.Count; i++ )
		{
			GoldPileLootStreamDebug pile = s_active[ i ];
			if ( pile == null )
				continue;

			GoldPileLootInstances loot = pile._loot;
			string name = pile.gameObject != null ? pile.gameObject.name : "?";
			if ( loot == null )
			{
				GUILayout.Label( $"[{i}] {name}: (no loot component)" );
				continue;
			}

			string stream = loot.StreamingEnabled ? "stream ON" : "stream OFF";
			string lootOn = loot.enabled ? "loot ON" : "loot OFF";
			if ( !loot.IsReady )
			{
				GUILayout.Label( $"[{i}] {name}: NOT READY  {stream}  {lootOn}" );
				continue;
			}

			GoldPileChunkStreamer streamer = loot.Streamer;
			GoldPileLootStreamSettings settings = loot.StreamSettings;
			int lod0Budget = loot.Lod0InstancesPerChunk;
			GUILayout.Label(
				$"[{i}] {name}: {stream}  {lootOn}  " +
				$"L{streamer.LoadedCount} V{streamer.VisibleCount} R{streamer.RenderedCount} F{streamer.FrustumCulledCount}" );
			GUILayout.Label(
				$"    Submitted {loot.LastDrawnCount}  Culled {loot.LastCulledCount}  Pool {loot.DrawnPoolCount}  " +
				$"Steady {loot.SteadyVisibleBudget}/{loot.MaxVisibleTotal}" );
			GUILayout.Label(
				$"    Mix G/S/C submitted {loot.LastSubmittedGold}/{loot.LastSubmittedSilver}/{loot.LastSubmittedCopper}" +
				( loot.LastSubmittedOther > 0 ? $" other {loot.LastSubmittedOther}" : "" ) +
				$"  authored {loot.AuthoredMixLabel}" );
			GUILayout.Label(
				$"    Chunk Drawn pool min/avg/max {loot.LastMinChunkDrawnPool}/{loot.LastAvgChunkDrawnPool}/{loot.LastMaxChunkDrawnPool}  " +
				$"LOD0/chunk {lod0Budget}" );
			if ( settings != null )
			{
				GUILayout.Label(
					$"    LOD budgets {settings.lod0InstancesPerChunk}/{settings.lod1InstancesPerChunk}/{settings.lod2InstancesPerChunk}  " +
					$"dither {settings.ditherFadeWidth:0.#}m @ {settings.lod2End:0.#}m" );
				GUILayout.Label(
					$"    Modes emb={settings.useEmbeddedVolumeSeats} surf={settings.useSurfaceDecorSeats}  " +
					$"rel={settings.releaseEmbeddedSeatsOnDig} phys={settings.spawnPhysicalCoinsOnDig}  " +
					$"overlap={settings.enforceCoinOverlap} spacing={settings.coinPlacementMinSpacing:0.##}" );
				GUILayout.Label(
					$"    Pose tilt={settings.coinTiltStrength:0.##} tip={settings.coinTipJitterDegrees:0.#}  " +
					$"yaw={settings.coinYawJitterDegrees:0.#} sink={settings.coinEmbedSinkFraction:0.###}  " +
					$"lastPhysSpawn {loot.LastPhysicalSpawnCount}" );
			}
			GUILayout.Label(
				$"    Rebuilds {loot.CacheRebuildCount}  Dirty {loot.LastDirtyChunkRebuildCount}" );

			GoldPileArtifactProps props = pile.GetComponent<GoldPileArtifactProps>();
			if ( props == null )
				props = pile.GetComponentInParent<GoldPileArtifactProps>();
			if ( props != null )
			{
				GUILayout.Label(
					$"    Props live {props.LivePropCount}  latent {props.LatentCount}  " +
					$"exposed {props.ExposedLatentCount}  streamed-out {props.StreamedOutCount}" );
			}
		}

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
