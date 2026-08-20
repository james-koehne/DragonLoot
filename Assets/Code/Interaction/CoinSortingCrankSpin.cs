using UnityEngine;

/// <summary>
/// Spins the crank cube while charging or discharging, and fires click ticks every N degrees while charging.
/// </summary>
public class CoinSortingCrankSpin : MonoBehaviour
{
	[SerializeField]
	CoinSortingStation station;

	[SerializeField]
	Transform visual;

	float _clickAccumulator;
	bool _clickArmed;

	public void BindStation( CoinSortingStation owner )
	{
		station = owner;
	}

	void Awake()
	{
		if ( station == null )
			station = GetComponentInParent<CoinSortingStation>();
		if ( visual == null )
			visual = transform;
	}

	void Update()
	{
		if ( station == null || visual == null )
			return;

		CoinSortingStationDefinition def = station.Definition;
		if ( def == null || !def.RequiresCrank( station.StationLevel ) )
			return;

		float speed = 0f;
		if ( station.IsCranking )
			speed = def.crankChargeSpinDegreesPerSecond;
		else if ( station.IsReserveDischarging )
			speed = def.crankDischargeSpinDegreesPerSecond;

		if ( speed <= 0.01f )
			return;

		float delta = speed * Time.deltaTime;
		visual.Rotate( delta, 0f, 0f, Space.Self );

		if ( !station.IsCranking )
		{
			_clickArmed = false;
			return;
		}

		if ( !_clickArmed )
			return;

		_clickAccumulator += delta;
		float step = Mathf.Max( 15f, def.crankClickDegrees );
		while ( _clickAccumulator >= step )
		{
			_clickAccumulator -= step;
			station.NotifyCrankClick();
		}
	}

	public void NotifyChargeStarted()
	{
		_clickAccumulator = 0f;
		_clickArmed = true;
	}

#if UNITY_EDITOR
	public void EditorSetVisual( Transform value )
	{
		visual = value;
	}
#endif
}
