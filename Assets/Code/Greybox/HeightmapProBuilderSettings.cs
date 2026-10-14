using System;

using UnityEngine;

/// <summary>Bake settings for greyscale heightmap → ProBuilder greybox meshes.</summary>
[Serializable]
public sealed class HeightmapProBuilderSettings
{
	public enum SampleChannel
	{
		Luminance = 0,
		R = 1,
		G = 2,
		B = 3,
		A = 4
	}

	public enum PivotMode
	{
		Center = 0,
		Corner = 1
	}

	public enum UvMode
	{
		Normalized = 0,
		WorldMeters = 1
	}

	public Texture2D heightmap;
	public SampleChannel sampleChannel = SampleChannel.Luminance;
	public bool invertSample;
	public bool flipU;
	public bool flipV;

	public AnimationCurve heightCurve = AnimationCurve.Linear( 0f, 0f, 1f, 1f );
	[Tooltip( "World Y when remapped sample is 0 (black)." )]
	public float blackHeight;
	[Tooltip( "World Y when remapped sample is 1 (white)." )]
	public float whiteHeight = 20f;
	public float yOffset;

	public float worldSizeX = 64f;
	public float worldSizeZ = 64f;
	[Min( 2 )]
	public int resolutionX = 65;
	[Min( 2 )]
	public int resolutionZ = 65;
	public PivotMode pivot = PivotMode.Center;

	public string objectName = "Heightmap ProBuilder";
	public Material material;
	public bool addMeshCollider = true;
	public UvMode uvMode = UvMode.Normalized;
	[Tooltip( "Used when UV mode is World Meters (UV = worldXZ / tiling)." )]
	public float uvWorldTiling = 1f;
	public bool cullBelowThreshold;
	[Tooltip( "Cull quads when all four corners are at or below this world Y." )]
	public float cullThresholdY;

	public void CopyFrom( HeightmapProBuilderSettings other )
	{
		if ( other == null )
			return;

		heightmap = other.heightmap;
		sampleChannel = other.sampleChannel;
		invertSample = other.invertSample;
		flipU = other.flipU;
		flipV = other.flipV;
		heightCurve = other.heightCurve != null
			? new AnimationCurve( other.heightCurve.keys )
			: AnimationCurve.Linear( 0f, 0f, 1f, 1f );
		blackHeight = other.blackHeight;
		whiteHeight = other.whiteHeight;
		yOffset = other.yOffset;
		worldSizeX = other.worldSizeX;
		worldSizeZ = other.worldSizeZ;
		resolutionX = other.resolutionX;
		resolutionZ = other.resolutionZ;
		pivot = other.pivot;
		objectName = other.objectName;
		material = other.material;
		addMeshCollider = other.addMeshCollider;
		uvMode = other.uvMode;
		uvWorldTiling = other.uvWorldTiling;
		cullBelowThreshold = other.cullBelowThreshold;
		cullThresholdY = other.cullThresholdY;
	}

	public void ClampResolutions()
	{
		resolutionX = Mathf.Clamp( resolutionX, 2, 512 );
		resolutionZ = Mathf.Clamp( resolutionZ, 2, 512 );
		worldSizeX = Mathf.Max( 0.01f, worldSizeX );
		worldSizeZ = Mathf.Max( 0.01f, worldSizeZ );
		uvWorldTiling = Mathf.Max( 0.0001f, uvWorldTiling );
	}
}
