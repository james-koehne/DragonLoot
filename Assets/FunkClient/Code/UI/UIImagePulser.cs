using UnityEngine;
using UnityEngine.UI;

public class UIImagePulser : MonoBehaviour
{
	public enum PulseMode
	{
		Linear,
		Smooth,
		EaseInOut,
		Randomized,
		Spike,
		Off
	}

	public Image targetImage;

	public bool pulseAlpha = true;
	[Range( 0f, 1f )] public float minAlpha = 0.2f;
	[Range( 0f, 1f )] public float maxAlpha = 1f;

	public bool pulseScale = false;
	public float minScale = 0.95f;
	public float maxScale = 1.05f;

	public PulseMode pulseMode = PulseMode.Smooth;
	public float speed = 1.0f;

	public float randomVariation = 0.5f;

	private float randomOffset;

	private void Reset()
	{
		targetImage = GetComponent<Image>();
	}

	private void Awake()
	{
		if ( targetImage == null )
			targetImage = GetComponent<Image>();

		randomOffset = Random.Range( 0f, 999f );
	}

	void Update()
	{
		float t = ComputePulseValue();

		if ( pulseAlpha && targetImage != null )
		{
			Color c = targetImage.color;
			c.a = Mathf.Lerp( minAlpha, maxAlpha, t );
			targetImage.color = c;
		}

		if ( pulseScale )
		{
			float s = Mathf.Lerp( minScale, maxScale, t );
			transform.localScale = new Vector3( s, s, 1f );
		}
	}

	float ComputePulseValue()
	{
		if ( pulseMode == PulseMode.Off )
			return 1f;

		float time = Time.time * speed;

		switch ( pulseMode )
		{
			case PulseMode.Linear:
				return Mathf.PingPong( time, 1f );

			case PulseMode.Smooth:
				return Mathf.SmoothStep( 0f, 1f, Mathf.PingPong( time, 1f ) );

			case PulseMode.EaseInOut:
			{
				float t = Mathf.PingPong( time, 1f );
				return t * t * ( 3f - 2f * t ); // classic ease-in-out
			}

			case PulseMode.Randomized:
			{
				float modTime = ( time + randomOffset ) * ( 1f + Mathf.Sin( time * 0.5f ) * randomVariation );
				float t = Mathf.PingPong( modTime, 1f );
				return Mathf.SmoothStep( 0f, 1f, t );
			}

			case PulseMode.Spike:
			{
				float t = Mathf.PingPong( time, 1f );
				return Mathf.Pow( t, 4 ); // soft fade-in, hard spike out
			}

			default:
				return 1f;
		}
	}
}
