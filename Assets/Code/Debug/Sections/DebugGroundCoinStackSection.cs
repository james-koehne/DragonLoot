using System;
using System.Collections.Generic;
using System.Diagnostics;

using UnityEngine;
using UnityEngine.InputSystem;

public class DebugGroundCoinStackSection : DebugOverlaySection
{
	const float AimMaxDistance = 80f;

	enum FillMode
	{
		Homogeneous = 0,
		Alternating = 1,
		Random = 2,
	}

	static int s_amount = 10;
	static int s_gridCount = 9;
	static float s_gridSpacing = GroundCoinStack.DefaultJoinRadius * 2f;
	static int s_selectedIndex;
	static int s_fillMode;
	static string s_lastStatus = "";

	static List<TreasureDefinition> s_catalog;
	static readonly List<TreasureDefinition> s_slotBuffer = new List<TreasureDefinition>( 1024 );
	static readonly List<GroundCoinStack> s_despawnScratch = new List<GroundCoinStack>( 128 );

	public string Title => "Ground Coin Stack";

	public void Draw()
	{
		EnsureCatalog();
		if ( s_catalog == null || s_catalog.Count == 0 )
		{
			GUILayout.Label( "No stackable coin definitions" );
			return;
		}

		if ( s_selectedIndex >= s_catalog.Count )
			s_selectedIndex = 0;

		TreasureDefinition selected = s_catalog[ s_selectedIndex ];
		string selectedLabel = FormatLabel( selected );

		GUILayout.Label( $"Active stacks: {GroundCoinStack.ActiveStacks.Count}" );
		GUILayout.Label(
			$"Stream resident {WorldTreasureStreamer.ResidentCoinStackCount}  hidden {WorldTreasureStreamer.HiddenCoinStackCount}  pool {WorldTreasureStreamer.CoinPoolFreeCount}" );
		GUILayout.Label(
			$"Bars resident {WorldTreasureStreamer.ResidentBarStackCount}  hidden {WorldTreasureStreamer.HiddenBarStackCount}" );
		GUILayout.Label(
			$"Loose hidden {WorldTreasureStreamer.HiddenLooseCount}  wake {WorldTreasureStreamer.WakesLastTick} evict {WorldTreasureStreamer.EvictsLastTick} extract {WorldTreasureStreamer.ExtractsLastTick} cells {WorldTreasureStreamer.CellsTicked}" );

		GUILayout.BeginHorizontal();
		if ( GUILayout.Button( "◀", GUILayout.Width( 28f ) ) )
			s_selectedIndex = ( s_selectedIndex + s_catalog.Count - 1 ) % s_catalog.Count;
		GUILayout.Label( selectedLabel, GUILayout.MinWidth( 80f ) );
		if ( GUILayout.Button( "▶", GUILayout.Width( 28f ) ) )
			s_selectedIndex = ( s_selectedIndex + 1 ) % s_catalog.Count;
		GUILayout.EndHorizontal();

		DrawFillMode();

		GUILayout.Label( $"Amount: {s_amount}" );
		GUILayout.BeginHorizontal();
		if ( GUILayout.Button( "1" ) )
			s_amount = 1;
		if ( GUILayout.Button( "10" ) )
			s_amount = 10;
		if ( GUILayout.Button( "50" ) )
			s_amount = 50;
		if ( GUILayout.Button( "100" ) )
			s_amount = 100;
		if ( GUILayout.Button( "500" ) )
			s_amount = 500;
		if ( GUILayout.Button( "1000" ) )
			s_amount = 1000;
		GUILayout.EndHorizontal();
		s_amount = Mathf.RoundToInt( GUILayout.HorizontalSlider( s_amount, 1, GroundCoinStack.DefaultMaxHeight ) );

		if ( GUILayout.Button( $"Spawn at cursor ({s_amount})" ) )
			SpawnSingle( selected );

		GUILayout.Space( 6f );
		GUILayout.Label( $"Grid stacks: {s_gridCount}" );
		GUILayout.BeginHorizontal();
		if ( GUILayout.Button( "4" ) )
			s_gridCount = 4;
		if ( GUILayout.Button( "9" ) )
			s_gridCount = 9;
		if ( GUILayout.Button( "16" ) )
			s_gridCount = 16;
		if ( GUILayout.Button( "25" ) )
			s_gridCount = 25;
		if ( GUILayout.Button( "100" ) )
			s_gridCount = 100;
		GUILayout.EndHorizontal();
		GUILayout.BeginHorizontal();
		if ( GUILayout.Button( "100" ) )
			s_gridCount = 100;
		if ( GUILayout.Button( "1000" ) )
			s_gridCount = 1000;
		if ( GUILayout.Button( "10000" ) )
			s_gridCount = 10000;
		GUILayout.EndHorizontal();
		s_gridCount = Mathf.RoundToInt( GUILayout.HorizontalSlider( s_gridCount, 1, 10000 ) );

		GUILayout.Label( $"Spacing: {s_gridSpacing:0.00}" );
		GUILayout.BeginHorizontal();
		if ( GUILayout.Button( "Loose" ) )
			s_gridSpacing = GroundCoinStack.DefaultJoinRadius * 2f;
		if ( GUILayout.Button( "Tight" ) )
			s_gridSpacing = GroundCoinStack.DefaultJoinRadius * 0.5f;
		GUILayout.EndHorizontal();
		s_gridSpacing = GUILayout.HorizontalSlider( s_gridSpacing, 0.05f, 3f );

		if ( GUILayout.Button( $"Spawn grid at cursor ({s_gridCount} x {s_amount})" ) )
			SpawnGrid( selected, s_gridCount, s_amount, s_gridSpacing, (FillMode)s_fillMode );

		GUILayout.Space( 6f );
		if ( GUILayout.Button( "Stress 100 x 1000" ) )
			SpawnStress( selected );

		GUILayout.Space( 4f );
		GUILayout.BeginHorizontal();
		if ( GUILayout.Button( "Despawn nearest" ) )
			DespawnNearest();
		if ( GUILayout.Button( "Despawn all" ) )
			DespawnAll();
		GUILayout.EndHorizontal();

		if ( !string.IsNullOrEmpty( s_lastStatus ) )
			GUILayout.Label( s_lastStatus );
	}

	static void DrawFillMode()
	{
		GUILayout.Label( "Fill mode" );
		GUILayout.BeginHorizontal();
		DrawFillModeButton( "Same", (int)FillMode.Homogeneous );
		DrawFillModeButton( "Alt", (int)FillMode.Alternating );
		DrawFillModeButton( "Rand", (int)FillMode.Random );
		GUILayout.EndHorizontal();
	}

	static void DrawFillModeButton( string label, int mode )
	{
		bool selected = s_fillMode == mode;
		if ( GUILayout.Toggle( selected, label, GUI.skin.button ) && !selected )
			s_fillMode = mode;
	}

	void SpawnSingle( TreasureDefinition selected )
	{
		if ( !TryResolveAimPoint( out Vector3 contact ) )
		{
			s_lastStatus = "No aim hit";
			return;
		}

		Stopwatch sw = Stopwatch.StartNew();
		GroundCoinStack stack = GroundCoinStack.CreateAt( contact, Quaternion.identity );
		bool filled = TryFillStack( stack, selected, s_amount, (FillMode)s_fillMode );
		sw.Stop();

		if ( !filled )
		{
			if ( stack != null )
				stack.DebugDespawn();
			s_lastStatus = "Fill failed";
			return;
		}

		s_lastStatus = $"Spawned 1 stack x{stack.Count} in {sw.Elapsed.TotalMilliseconds:0.0} ms";
	}

	void SpawnGrid( TreasureDefinition selected, int stackCount, int amount, float spacing, FillMode mode )
	{
		if ( !TryResolveAimPoint( out Vector3 origin ) )
		{
			s_lastStatus = "No aim hit";
			return;
		}

		Stopwatch sw = Stopwatch.StartNew();
		int created = SpawnGridAt( origin, selected, stackCount, amount, spacing, mode, out long createMs, out long fillMs );
		sw.Stop();

		s_lastStatus =
			$"Grid {created}/{stackCount} x{amount} in {sw.Elapsed.TotalMilliseconds:0.0} ms "
			+ $"(create {createMs} ms, fill {fillMs} ms)";
	}

	void SpawnStress( TreasureDefinition selected )
	{
		if ( !TryResolveAimPoint( out Vector3 origin ) )
		{
			s_lastStatus = "No aim hit";
			return;
		}

		float spacing = GroundCoinStack.DefaultJoinRadius * 2f;
		Stopwatch sw = Stopwatch.StartNew();
		int created = SpawnGridAt(
			origin,
			selected,
			100,
			GroundCoinStack.DefaultMaxHeight,
			spacing,
			FillMode.Homogeneous,
			out long createMs,
			out long fillMs );
		sw.Stop();

		s_lastStatus =
			$"Stress {created}/100 x1000 in {sw.Elapsed.TotalMilliseconds:0.0} ms "
			+ $"(create {createMs} ms, fill {fillMs} ms)";
	}

	static int SpawnGridAt(
		Vector3 origin,
		TreasureDefinition selected,
		int stackCount,
		int amount,
		float spacing,
		FillMode mode,
		out long createMs,
		out long fillMs )
	{
		createMs = 0;
		fillMs = 0;
		if ( stackCount <= 0 )
			return 0;

		int cols = Mathf.CeilToInt( Mathf.Sqrt( stackCount ) );
		int rows = Mathf.CeilToInt( stackCount / (float)cols );
		float halfW = ( cols - 1 ) * spacing * 0.5f;
		float halfD = ( rows - 1 ) * spacing * 0.5f;

		int created = 0;
		int index = 0;
		Stopwatch phase = new Stopwatch();
		for ( int row = 0; row < rows && index < stackCount; row++ )
		{
			for ( int col = 0; col < cols && index < stackCount; col++, index++ )
			{
				Vector3 pos = origin + new Vector3( col * spacing - halfW, 0f, row * spacing - halfD );

				phase.Restart();
				GroundCoinStack stack = GroundCoinStack.CreateAt( pos, Quaternion.identity );
				phase.Stop();
				createMs += phase.ElapsedMilliseconds;

				phase.Restart();
				bool filled = TryFillStack( stack, selected, amount, mode );
				phase.Stop();
				fillMs += phase.ElapsedMilliseconds;

				if ( !filled )
				{
					if ( stack != null )
						stack.DebugDespawn();
					continue;
				}

				created++;
			}
		}

		return created;
	}

	static bool TryFillStack( GroundCoinStack stack, TreasureDefinition selected, int amount, FillMode mode )
	{
		if ( stack == null )
			return false;

		int count = Mathf.Clamp( amount, 1, stack.MaxHeight );
		if ( mode == FillMode.Homogeneous )
			return stack.DebugFillHomogeneous( selected, count );

		BuildSlotList( selected, count, mode );
		return stack.DebugFillSlots( s_slotBuffer );
	}

	static void BuildSlotList( TreasureDefinition selected, int count, FillMode mode )
	{
		s_slotBuffer.Clear();
		if ( s_catalog == null || s_catalog.Count == 0 )
		{
			for ( int i = 0; i < count; i++ )
				s_slotBuffer.Add( selected );
			return;
		}

		if ( mode == FillMode.Alternating )
		{
			for ( int i = 0; i < count; i++ )
				s_slotBuffer.Add( s_catalog[ i % s_catalog.Count ] );
			return;
		}

		// Random
		for ( int i = 0; i < count; i++ )
			s_slotBuffer.Add( s_catalog[ UnityEngine.Random.Range( 0, s_catalog.Count ) ] );
	}

	static bool TryResolveAimPoint( out Vector3 point )
	{
		point = default;
		FirstPersonCameraController cameraLook = DebugOverlay.GetCameraLook();
		if ( cameraLook == null )
			return false;

		Camera cam = cameraLook.Camera;
		Ray ray;
		Mouse mouse = Mouse.current;
		if ( cam != null && mouse != null )
		{
			Vector2 screen = mouse.position.ReadValue();
			ray = cam.ScreenPointToRay( screen );
		}
		else
			ray = new Ray( cameraLook.transform.position, cameraLook.GetCameraForward() );

		if ( !Physics.Raycast( ray, out RaycastHit hit, AimMaxDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore ) )
			return false;

		point = hit.point;
		return true;
	}

	static void DespawnNearest()
	{
		FirstPersonCameraController cameraLook = DebugOverlay.GetCameraLook();
		Vector3 from = cameraLook != null
			? cameraLook.transform.position
			: Vector3.zero;

		if ( TryResolveAimPoint( out Vector3 aim ) )
			from = aim;

		IReadOnlyList<GroundCoinStack> stacks = GroundCoinStack.ActiveStacks;
		GroundCoinStack best = null;
		float bestSq = float.MaxValue;
		for ( int i = 0; i < stacks.Count; i++ )
		{
			GroundCoinStack stack = stacks[ i ];
			if ( stack == null )
				continue;

			float sq = ( stack.ContactPosition - from ).sqrMagnitude;
			if ( sq >= bestSq )
				continue;

			bestSq = sq;
			best = stack;
		}

		if ( best == null )
		{
			s_lastStatus = "No stacks to despawn";
			return;
		}

		best.DebugDespawn();
		s_lastStatus = "Despawned nearest stack";
	}

	static void DespawnAll()
	{
		IReadOnlyList<GroundCoinStack> stacks = GroundCoinStack.ActiveStacks;
		s_despawnScratch.Clear();
		for ( int i = 0; i < stacks.Count; i++ )
		{
			if ( stacks[ i ] != null )
				s_despawnScratch.Add( stacks[ i ] );
		}

		int count = s_despawnScratch.Count;
		for ( int i = 0; i < count; i++ )
			s_despawnScratch[ i ].DebugDespawn();

		s_despawnScratch.Clear();
		s_lastStatus = $"Despawned {count} stacks";
	}

	static void EnsureCatalog()
	{
		if ( s_catalog != null && s_catalog.Count > 0 )
			return;

		HashSet<TreasureDefinition> seen = new HashSet<TreasureDefinition>();
		s_catalog = new List<TreasureDefinition>();

#if UNITY_EDITOR
		string[] guids = UnityEditor.AssetDatabase.FindAssets( "t:TreasureDefinition" );
		for ( int i = 0; i < guids.Length; i++ )
		{
			string path = UnityEditor.AssetDatabase.GUIDToAssetPath( guids[ i ] );
			TreasureDefinition def = UnityEditor.AssetDatabase.LoadAssetAtPath<TreasureDefinition>( path );
			TryAddToCatalog( def, seen );
		}
#endif

		TreasureDefinition[] loaded = Resources.FindObjectsOfTypeAll<TreasureDefinition>();
		for ( int i = 0; i < loaded.Length; i++ )
			TryAddToCatalog( loaded[ i ], seen );

		s_catalog.Sort( CompareDefs );
	}

	static void TryAddToCatalog( TreasureDefinition def, HashSet<TreasureDefinition> seen )
	{
		if ( !GroundCoinStack.IsGroundStackableCoin( def ) || !seen.Add( def ) )
			return;
		s_catalog.Add( def );
	}

	static int CompareDefs( TreasureDefinition a, TreasureDefinition b )
	{
		return string.Compare( FormatLabel( a ), FormatLabel( b ), StringComparison.OrdinalIgnoreCase );
	}

	static string FormatLabel( TreasureDefinition def )
	{
		if ( def == null )
			return "(null)";
		if ( !string.IsNullOrEmpty( def.displayName ) )
			return def.displayName;
		if ( !string.IsNullOrEmpty( def.id ) )
			return def.id;
		return def.name;
	}
}
