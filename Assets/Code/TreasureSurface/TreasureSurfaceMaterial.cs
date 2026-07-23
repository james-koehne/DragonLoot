/// <summary>
/// Painted surface material. Affects rolling friction, bounce, and speed scale.
/// </summary>
public enum TreasureSurfaceMaterial : byte
{
	Stone = 0,
	Gold = 1,
	Gems = 2,
	Wood = 3,
	Metal = 4,
	Cloth = 5
}

/// <summary>
/// Tunable params for a surface material.
/// </summary>
[System.Serializable]
public struct TreasureSurfaceMaterialParams
{
	public TreasureSurfaceMaterial material;

	[UnityEngine.Range( 0.05f, 2f )]
	public float friction;

	[UnityEngine.Range( 0f, 1f )]
	public float bounce;

	[UnityEngine.Range( 0.1f, 3f )]
	public float rollSpeedScale;

	public static TreasureSurfaceMaterialParams DefaultFor( TreasureSurfaceMaterial material )
	{
		switch ( material )
		{
			case TreasureSurfaceMaterial.Gold:
				return new TreasureSurfaceMaterialParams
				{
					material = material,
					friction = 0.55f,
					bounce = 0.12f,
					rollSpeedScale = 1.15f
				};
			case TreasureSurfaceMaterial.Gems:
				return new TreasureSurfaceMaterialParams
				{
					material = material,
					friction = 0.35f,
					bounce = 0.28f,
					rollSpeedScale = 1.35f
				};
			case TreasureSurfaceMaterial.Wood:
				return new TreasureSurfaceMaterialParams
				{
					material = material,
					friction = 0.75f,
					bounce = 0.08f,
					rollSpeedScale = 0.9f
				};
			case TreasureSurfaceMaterial.Metal:
				return new TreasureSurfaceMaterialParams
				{
					material = material,
					friction = 0.4f,
					bounce = 0.22f,
					rollSpeedScale = 1.2f
				};
			case TreasureSurfaceMaterial.Cloth:
				return new TreasureSurfaceMaterialParams
				{
					material = material,
					friction = 1.1f,
					bounce = 0.02f,
					rollSpeedScale = 0.65f
				};
			default:
				return new TreasureSurfaceMaterialParams
				{
					material = TreasureSurfaceMaterial.Stone,
					friction = 0.7f,
					bounce = 0.1f,
					rollSpeedScale = 1f
				};
		}
	}
}
