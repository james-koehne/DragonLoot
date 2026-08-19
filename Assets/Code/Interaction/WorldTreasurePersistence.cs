using System.Collections;
using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Distance parks loose (owner-null) gems/artifacts: despawn when far, respawn at the same pose when near.
/// Parked records count toward <see cref="LooseTreasureManager"/> caps.
/// </summary>
public class WorldTreasurePersistence : MonoBehaviour
{
	struct ParkRecord
	{
		public TreasureDefinition Definition;
		public Vector3 Position;
		public Quaternion Rotation;
		public TreasurePileVisual OriginPile;
		public bool SurfaceMode;
	}

	static WorldTreasurePersistence _instance;

	readonly List<ParkRecord> _parked = new List<ParkRecord>( 64 );
	readonly HashSet<TreasureItem> _tracked = new HashSet<TreasureItem>();

	GoldPileLootStreamSettings _streamSettings;
	int _spawnBudget = 3;
	int _parkBudget = 3;

	public static int ParkedCount => _instance != null ? _instance._parked.Count : 0;

	public static void EnsureExists()
	{
		if ( _instance != null )
			return;
		if ( !Application.isPlaying )
			return;

		GameObject go = new GameObject( "WorldTreasurePersistence" );
		_instance = go.AddComponent<WorldTreasurePersistence>();
		Object.DontDestroyOnLoad( go );
	}

	public static void NotifyLoose( TreasureItem item )
	{
		if ( item == null )
			return;
		if ( !Application.isPlaying )
			return;

		EnsureExists();
		if ( _instance == null )
			return;

		_instance._tracked.Add( item );
	}

	public static void NotifyOwned( TreasureItem item )
	{
		if ( item == null || _instance == null )
			return;

		_instance._tracked.Remove( item );
	}

	public static void CancelParkForItem( TreasureItem item )
	{
		if ( item == null || _instance == null )
			return;

		_instance._tracked.Remove( item );
	}

	public static void CancelParkMatching( TreasureDefinition definition, Vector3 nearWorld, float radius )
	{
		if ( _instance == null || definition == null )
			return;

		float radiusSq = radius * radius;
		for ( int i = _instance._parked.Count - 1; i >= 0; i-- )
		{
			ParkRecord park = _instance._parked[ i ];
			if ( park.Definition != definition )
				continue;
			if ( ( park.Position - nearWorld ).sqrMagnitude > radiusSq )
				continue;
			_instance._parked.RemoveAt( i );
		}
	}

	public static int CountParkedCategory( TreasureCategory category )
	{
		if ( _instance == null )
			return 0;

		int n = 0;
		for ( int i = 0; i < _instance._parked.Count; i++ )
		{
			TreasureDefinition def = _instance._parked[ i ].Definition;
			if ( def == null )
				continue;
			if ( def.category == category )
				n++;
		}

		return n;
	}

	public static int CountParkedGems()
	{
		return CountParkedCategory( TreasureCategory.Gem );
	}

	public static int CountParkedArtifacts()
	{
		if ( _instance == null )
			return 0;

		int n = 0;
		for ( int i = 0; i < _instance._parked.Count; i++ )
		{
			TreasureDefinition def = _instance._parked[ i ].Definition;
			if ( def != null && GoldPileLootStreamSettings.IsArtifactStreamBucket( def.category ) )
				n++;
		}

		return n;
	}

	/// <summary>
	/// Removes one parked gem/artifact (prefer farthest from player / nearest origin) and returns its record.
	/// </summary>
	public static bool TryTakeParkedForReclaim( bool gems, out TreasureDefinition definition, out Vector3 position, out TreasurePileVisual origin )
	{
		definition = null;
		position = Vector3.zero;
		origin = null;
		if ( _instance == null || _instance._parked.Count == 0 )
			return false;

		int best = -1;
		float bestScore = float.MinValue;
		for ( int i = 0; i < _instance._parked.Count; i++ )
		{
			ParkRecord park = _instance._parked[ i ];
			if ( park.Definition == null )
				continue;

			bool isGem = park.Definition.category == TreasureCategory.Gem;
			bool isArt = GoldPileLootStreamSettings.IsArtifactStreamBucket( park.Definition.category );
			if ( gems && !isGem )
				continue;
			if ( !gems && !isArt )
				continue;

			float score = park.OriginPile != null ? 1f : 0f;
			if ( score > bestScore )
			{
				bestScore = score;
				best = i;
			}
		}

		if ( best < 0 )
			return false;

		ParkRecord chosen = _instance._parked[ best ];
		_instance._parked.RemoveAt( best );
		definition = chosen.Definition;
		position = chosen.Position;
		origin = chosen.OriginPile;
		return true;
	}

	void Awake()
	{
		if ( _instance != null && _instance != this )
		{
			Destroy( gameObject );
			return;
		}

		_instance = this;
		EnsureStreamSettings();
	}

	void LateUpdate()
	{
		EnsureStreamSettings();
		_spawnBudget = 3;
		_parkBudget = 3;

		if ( !TreasureProximitySleep.TryGetPlayerPosition( out Vector3 playerPos ) )
		{
			Camera cam = Camera.main;
			if ( cam == null )
				return;
			playerPos = cam.transform.position;
		}

		TickParkLive( playerPos );
		TickRespawn( playerPos );
	}

	void TickParkLive( Vector3 playerPos )
	{
		if ( _tracked.Count == 0 || _parkBudget <= 0 )
			return;

		List<TreasureItem> snapshot = new List<TreasureItem>( _tracked );
		for ( int i = 0; i < snapshot.Count && _parkBudget > 0; i++ )
		{
			TreasureItem item = snapshot[ i ];
			if ( item == null )
			{
				_tracked.Remove( item );
				continue;
			}

			if ( !CanPark( item ) )
				continue;

			TreasureCategory category = item.Definition.category;
			float distSqr = PlanarDistanceSqr( playerPos, item.transform.position );
			if ( IsInRange( category, distSqr, currentlyResident: true ) )
				continue;

			Park( item );
			_parkBudget--;
		}
	}

	void TickRespawn( Vector3 playerPos )
	{
		for ( int i = _parked.Count - 1; i >= 0 && _spawnBudget > 0; i-- )
		{
			ParkRecord park = _parked[ i ];
			if ( park.Definition == null )
			{
				_parked.RemoveAt( i );
				continue;
			}

			float distSqr = PlanarDistanceSqr( playerPos, park.Position );
			if ( !IsInRange( park.Definition.category, distSqr, currentlyResident: false ) )
				continue;

			_parked.RemoveAt( i );
			_spawnBudget--;
			StartCoroutine( RespawnRoutine( park ) );
		}
	}

	IEnumerator RespawnRoutine( ParkRecord park )
	{
		var task = TreasureItemFactory.SpawnAsync(
			park.Definition,
			park.Position,
			park.Rotation,
			null );
		while ( !task.IsCompleted )
			yield return null;

		TreasureItem item = task.Status == System.Threading.Tasks.TaskStatus.RanToCompletion
			? task.Result
			: null;
		if ( item == null )
			yield break;

		if ( park.OriginPile != null )
			item.SetOriginPile( park.OriginPile );

		if ( park.SurfaceMode )
			item.EnterSurface( park.Position, park.Rotation, Vector3.zero );
		else
			item.EnterSettledPhysics( park.Position, park.Rotation );

		NotifyLoose( item );
		LooseTreasureManager.NotifyParkedChanged();
	}

	bool CanPark( TreasureItem item )
	{
		if ( item.Owner != null )
			return false;
		if ( item.IsReclaiming )
			return false;
		if ( !item.IsWorldLoose )
			return false;
		if ( item.Definition == null )
			return false;

		TreasureCategory category = item.Definition.category;
		if ( category == TreasureCategory.Coin )
			return false;
		if ( category != TreasureCategory.Gem && !GoldPileLootStreamSettings.IsArtifactStreamBucket( category ) )
			return false;

		return true;
	}

	void Park( TreasureItem item )
	{
		if ( item == null || item.Definition == null )
			return;

		ParkRecord park = new ParkRecord
		{
			Definition = item.Definition,
			Position = item.transform.position,
			Rotation = item.transform.rotation,
			OriginPile = item.OriginPile,
			SurfaceMode = item.State == TreasureItemState.SurfaceRolling
		};

		_tracked.Remove( item );
		LooseTreasureManager.Unregister( item );
		TreasureProximitySleep.Unregister( item );
		_parked.Add( park );
		TreasureItemFactory.Despawn( item );
		LooseTreasureManager.NotifyParkedChanged();
	}

	bool IsInRange( TreasureCategory category, float distanceMetersSqr, bool currentlyResident )
	{
		if ( _streamSettings == null )
			return true;
		return _streamSettings.IsPropInStreamRange( category, distanceMetersSqr, currentlyResident );
	}

	void EnsureStreamSettings()
	{
		if ( _streamSettings != null )
			return;

#if UNITY_EDITOR
		_streamSettings = UnityEditor.AssetDatabase.LoadAssetAtPath<GoldPileLootStreamSettings>(
			GoldPileLootStreamSettings.AssetPath );
		if ( _streamSettings == null )
		{
			_streamSettings = UnityEditor.AssetDatabase.LoadAssetAtPath<GoldPileLootStreamSettings>(
				GoldPileLootStreamSettings.LegacyAssetPath );
		}
#endif
		if ( _streamSettings == null )
			_streamSettings = ScriptableObject.CreateInstance<GoldPileLootStreamSettings>();
	}

	static float PlanarDistanceSqr( Vector3 a, Vector3 b )
	{
		float dx = a.x - b.x;
		float dz = a.z - b.z;
		return dx * dx + dz * dz;
	}

	void OnDestroy()
	{
		if ( _instance == this )
			_instance = null;
	}
}
