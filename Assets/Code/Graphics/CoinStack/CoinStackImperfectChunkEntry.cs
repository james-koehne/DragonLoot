using System;

using UnityEngine;

/// <summary>
/// One editor-baked imperfect chunk: colour-agnostic mesh + per-coin local XZ offsets.
/// </summary>
[Serializable]
public class CoinStackImperfectChunkEntry
{
	public int coinCount;
	public int seedIndex;
	public Mesh mesh;
	public float height;
	public Vector2[] localXzOffsets;
}
