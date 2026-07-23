using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// GPU-instanced decorative surface coins conforming to the heightfield. Visual-only.
/// </summary>
[DisallowMultipleComponent]
public class GoldPileInstanceScatter : MonoBehaviour
{
	const int BatchSize = 1023;

	struct Slot
	{
		public Vector3 LocalPos;
		public Quaternion LocalRot;
		public float Scale;
		public bool Active;
		public int CellX;
		public int CellZ;
	}

	[SerializeField]
	Mesh coinMesh;

	[SerializeField]
	Material coinMaterial;

	[SerializeField]
	[Min( 100 )]
	int instanceCount = 3000;

	[SerializeField]
	[Min( 0.01f )]
	float coinScale = 0.12f;

	[SerializeField]
	[Min( 0.01f )]
	float pickRadius = 0.35f;

	[SerializeField]
	[Range( 0f, 1f )]
	float edgeDensityBoost = 0.55f;

	[SerializeField]
	[Min( 1 )]
	int spatialCells = 16;

	GoldPileHeightfield _heightfield;
	Transform _pileRoot;
	Slot[] _slots;
	Matrix4x4[] _matrices;
	Matrix4x4[] _batch;
	List<int>[] _cellLists;
	RenderParams _renderParams;
	bool _ready;
	Material _runtimeMaterial;

	public int ActiveCount
	{
		get
		{
			if ( _slots == null )
				return 0;
			int n = 0;
			for ( int i = 0; i < _slots.Length; i++ )
			{
				if ( _slots[ i ].Active )
					n++;
			}
			return n;
		}
	}

	public void Configure( Mesh mesh, Material material, int count )
	{
		if ( mesh != null )
			coinMesh = mesh;
		if ( material != null )
			coinMaterial = material;
		instanceCount = Mathf.Clamp( count, 100, 5000 );
	}

	public void Bind( GoldPileHeightfield heightfield, Transform pileRoot )
	{
		_heightfield = heightfield;
		_pileRoot = pileRoot != null ? pileRoot : transform;
		EnsureMaterial();
		BuildSlots();
		RebuildMatrices();
		_ready = coinMesh != null && _runtimeMaterial != null && _slots != null;
	}

	public void RefreshAffected( Vector3 worldCenter, float radius )
	{
		if ( !_ready || _heightfield == null || _pileRoot == null )
			return;

		Vector3 local = _pileRoot.InverseTransformPoint( worldCenter );
		float radiusSq = radius * radius;
		for ( int i = 0; i < _slots.Length; i++ )
		{
			if ( !_slots[ i ].Active )
				continue;

			Vector3 p = _slots[ i ].LocalPos;
			float dx = p.x - local.x;
			float dz = p.z - local.z;
			if ( dx * dx + dz * dz > radiusSq )
				continue;

			ConformSlot( ref _slots[ i ] );
			_matrices[ i ] = BuildMatrix( _slots[ i ] );
		}
	}

	public void RefreshAll()
	{
		if ( !_ready || _heightfield == null )
			return;

		for ( int i = 0; i < _slots.Length; i++ )
		{
			if ( !_slots[ i ].Active )
				continue;
			ConformSlot( ref _slots[ i ] );
			_matrices[ i ] = BuildMatrix( _slots[ i ] );
		}
	}

	public bool TryPickNearest( Vector3 worldPoint, float maxDistance, out int slotIndex, out Vector3 worldPos, out Quaternion worldRot )
	{
		slotIndex = -1;
		worldPos = worldPoint;
		worldRot = Quaternion.identity;
		if ( !_ready || _pileRoot == null )
			return false;

		float maxDist = maxDistance > 0f ? maxDistance : pickRadius;
		float bestSq = maxDist * maxDist;
		Vector3 local = _pileRoot.InverseTransformPoint( worldPoint );

		int cellX = LocalToCell( local.x );
		int cellZ = LocalToCell( local.z );
		for ( int oz = -1; oz <= 1; oz++ )
		{
			for ( int ox = -1; ox <= 1; ox++ )
			{
				int cx = cellX + ox;
				int cz = cellZ + oz;
				if ( cx < 0 || cz < 0 || cx >= spatialCells || cz >= spatialCells )
					continue;

				List<int> list = _cellLists[ CellIndex( cx, cz ) ];
				for ( int i = 0; i < list.Count; i++ )
				{
					int idx = list[ i ];
					Slot slot = _slots[ idx ];
					if ( !slot.Active )
						continue;

					float dx = slot.LocalPos.x - local.x;
					float dy = slot.LocalPos.y - local.y;
					float dz = slot.LocalPos.z - local.z;
					float sq = dx * dx + dy * dy + dz * dz;
					if ( sq > bestSq )
						continue;

					bestSq = sq;
					slotIndex = idx;
				}
			}
		}

		if ( slotIndex < 0 )
			return false;

		Slot best = _slots[ slotIndex ];
		worldPos = _pileRoot.TransformPoint( best.LocalPos );
		worldRot = _pileRoot.rotation * best.LocalRot;
		return true;
	}

	public void HideSlot( int slotIndex )
	{
		if ( _slots == null || slotIndex < 0 || slotIndex >= _slots.Length )
			return;

		RemoveFromCell( slotIndex );
		_slots[ slotIndex ].Active = false;
		_matrices[ slotIndex ] = Matrix4x4.zero;
	}

	public void RespawnHiddenNear( Vector3 avoidWorld, float avoidRadius )
	{
		if ( !_ready || _heightfield == null || _pileRoot == null )
			return;

		Vector3 avoidLocal = _pileRoot.InverseTransformPoint( avoidWorld );
		float avoidSq = avoidRadius * avoidRadius;
		float half = _heightfield.WorldSize * 0.5f;

		for ( int i = 0; i < _slots.Length; i++ )
		{
			if ( _slots[ i ].Active )
				continue;

			for ( int attempt = 0; attempt < 12; attempt++ )
			{
				float lx = Random.Range( -half, half );
				float lz = Random.Range( -half, half );
				float h = _heightfield.SampleNormalized( lx, lz );
				if ( h < 0.05f )
					continue;

				float dx = lx - avoidLocal.x;
				float dz = lz - avoidLocal.z;
				if ( dx * dx + dz * dz < avoidSq )
					continue;

				if ( Random.value > AcceptanceProbability( lx, lz ) )
					continue;

				RemoveFromCell( i );
				_slots[ i ].LocalPos = new Vector3( lx, 0f, lz );
				_slots[ i ].Scale = coinScale * Random.Range( 0.85f, 1.15f );
				_slots[ i ].Active = true;
				ConformSlot( ref _slots[ i ] );
				AssignCell( i );
				_matrices[ i ] = BuildMatrix( _slots[ i ] );
				return;
			}
		}
	}

	void LateUpdate()
	{
		if ( !_ready || coinMesh == null || _runtimeMaterial == null )
			return;

		_renderParams = new RenderParams( _runtimeMaterial )
		{
			layer = gameObject.layer,
			shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off,
			receiveShadows = false,
			renderingLayerMask = 1
		};

		int total = _matrices.Length;
		for ( int start = 0; start < total; start += BatchSize )
		{
			int count = Mathf.Min( BatchSize, total - start );
			int drawn = 0;
			for ( int i = 0; i < count; i++ )
			{
				int idx = start + i;
				if ( !_slots[ idx ].Active )
					continue;
				_batch[ drawn++ ] = _matrices[ idx ];
			}

			if ( drawn > 0 )
				Graphics.RenderMeshInstanced( _renderParams, coinMesh, 0, _batch, drawn );
		}
	}

	void OnDestroy()
	{
		if ( _runtimeMaterial != null && _runtimeMaterial != coinMaterial )
			Destroy( _runtimeMaterial );
	}

	void EnsureMaterial()
	{
		if ( coinMaterial == null )
			return;

		if ( _runtimeMaterial != null && _runtimeMaterial != coinMaterial )
			Destroy( _runtimeMaterial );

		_runtimeMaterial = new Material( coinMaterial );
		_runtimeMaterial.enableInstancing = true;
	}

	void BuildSlots()
	{
		if ( _heightfield == null )
			return;

		int count = Mathf.Clamp( instanceCount, 100, 5000 );
		_slots = new Slot[ count ];
		_matrices = new Matrix4x4[ count ];
		_batch = new Matrix4x4[ BatchSize ];
		_cellLists = new List<int>[ spatialCells * spatialCells ];
		for ( int i = 0; i < _cellLists.Length; i++ )
			_cellLists[ i ] = new List<int>( 32 );

		float half = _heightfield.WorldSize * 0.5f;
		int placed = 0;
		int attempts = 0;
		int maxAttempts = count * 20;

		while ( placed < count && attempts < maxAttempts )
		{
			attempts++;
			float lx = Random.Range( -half, half );
			float lz = Random.Range( -half, half );
			float h = _heightfield.SampleNormalized( lx, lz );
			if ( h < 0.05f )
				continue;
			if ( Random.value > AcceptanceProbability( lx, lz ) )
				continue;

			_slots[ placed ] = new Slot
			{
				LocalPos = new Vector3( lx, 0f, lz ),
				LocalRot = Quaternion.identity,
				Scale = coinScale * Random.Range( 0.85f, 1.15f ),
				Active = true
			};
			ConformSlot( ref _slots[ placed ] );
			AssignCell( placed );
			_matrices[ placed ] = BuildMatrix( _slots[ placed ] );
			placed++;
		}

		for ( int i = placed; i < count; i++ )
		{
			_slots[ i ].Active = false;
			_matrices[ i ] = Matrix4x4.zero;
		}
	}

	void RebuildMatrices()
	{
		if ( _slots == null )
			return;
		for ( int i = 0; i < _slots.Length; i++ )
			_matrices[ i ] = _slots[ i ].Active ? BuildMatrix( _slots[ i ] ) : Matrix4x4.zero;
	}

	float AcceptanceProbability( float localX, float localZ )
	{
		float grad = _heightfield.GradientMagnitude( localX, localZ );
		float edge = Mathf.Clamp01( grad * 8f );
		return Mathf.Lerp( 0.35f, 1f, edge * edgeDensityBoost + ( 1f - edgeDensityBoost ) * 0.5f );
	}

	void ConformSlot( ref Slot slot )
	{
		float h = _heightfield.SampleNormalized( slot.LocalPos.x, slot.LocalPos.z );
		slot.LocalPos.y = h * _heightfield.MaxHeight + slot.Scale * 0.15f;

		float step = _heightfield.WorldSize / Mathf.Max( 1, _heightfield.Resolution - 1 );
		float hL = _heightfield.SampleNormalized( slot.LocalPos.x - step, slot.LocalPos.z ) * _heightfield.MaxHeight;
		float hR = _heightfield.SampleNormalized( slot.LocalPos.x + step, slot.LocalPos.z ) * _heightfield.MaxHeight;
		float hD = _heightfield.SampleNormalized( slot.LocalPos.x, slot.LocalPos.z - step ) * _heightfield.MaxHeight;
		float hU = _heightfield.SampleNormalized( slot.LocalPos.x, slot.LocalPos.z + step ) * _heightfield.MaxHeight;
		Vector3 normal = new Vector3( hL - hR, step * 2f, hD - hU ).normalized;
		if ( normal.sqrMagnitude < 0.0001f )
			normal = Vector3.up;

		Quaternion tilt = Quaternion.FromToRotation( Vector3.up, normal );
		float yaw = ( slot.LocalPos.x * 37.1f + slot.LocalPos.z * 91.7f ) * 25f;
		float tipX = Mathf.Sin( slot.LocalPos.x * 12.3f + slot.LocalPos.z * 4.1f ) * 12f;
		float tipZ = Mathf.Cos( slot.LocalPos.x * 7.7f + slot.LocalPos.z * 9.2f ) * 12f;
		slot.LocalRot = tilt * Quaternion.Euler( tipX, yaw, tipZ );
	}

	Matrix4x4 BuildMatrix( Slot slot )
	{
		Vector3 worldPos = _pileRoot.TransformPoint( slot.LocalPos );
		Quaternion worldRot = _pileRoot.rotation * slot.LocalRot;
		Vector3 scale = Vector3.one * slot.Scale;
		return Matrix4x4.TRS( worldPos, worldRot, scale );
	}

	void AssignCell( int slotIndex )
	{
		Slot slot = _slots[ slotIndex ];
		int cx = LocalToCell( slot.LocalPos.x );
		int cz = LocalToCell( slot.LocalPos.z );
		slot.CellX = cx;
		slot.CellZ = cz;
		_slots[ slotIndex ] = slot;
		_cellLists[ CellIndex( cx, cz ) ].Add( slotIndex );
	}

	void RemoveFromCell( int slotIndex )
	{
		Slot slot = _slots[ slotIndex ];
		int cell = CellIndex( slot.CellX, slot.CellZ );
		if ( cell < 0 || cell >= _cellLists.Length )
			return;
		_cellLists[ cell ].Remove( slotIndex );
	}

	int LocalToCell( float local )
	{
		float half = _heightfield.WorldSize * 0.5f;
		float u = ( local + half ) / _heightfield.WorldSize;
		int c = Mathf.FloorToInt( Mathf.Clamp01( u ) * spatialCells );
		return Mathf.Clamp( c, 0, spatialCells - 1 );
	}

	int CellIndex( int cx, int cz )
	{
		return cz * spatialCells + cx;
	}
}
