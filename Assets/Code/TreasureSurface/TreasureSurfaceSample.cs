using UnityEngine;

/// <summary>
/// Result of sampling the treasure surface at a world point.
/// </summary>
public struct TreasureSurfaceSample
{
	public bool Valid;
	public float Height;
	public Vector3 Normal;
	public Vector2 Flow;
	public float Slope;
	public TreasureSurfaceMaterial Material;
	public bool Traversable;
	public bool Stable;
	public TreasureChunkCoord ChunkCoord;
	public int CellX;
	public int CellZ;
}
