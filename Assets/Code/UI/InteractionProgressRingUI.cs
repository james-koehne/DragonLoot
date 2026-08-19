using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Circular progress ring around the crosshair for hold charges
/// (sorter pickup and whole-stack E/F).
/// </summary>
public class InteractionProgressRingUI : MonoBehaviour
{
	Image _ring;
	float _size = 96f;

	public void Setup()
	{
		EnsureUi();
		SetProgress( 0f );
	}

	void LateUpdate()
	{
		RefreshFromPlayer();
	}

	void RefreshFromPlayer()
	{
		EnsureUi();
		if ( _ring == null )
			return;

		if ( GameMode.Instance == null )
		{
			SetProgress( 0f );
			return;
		}

		PlayerController player = GameMode.Instance.Player;
		if ( player == null )
		{
			SetProgress( 0f );
			return;
		}

		float size = 96f;
		CarryDefinition resolved = null;
		resolved = RuntimeDefinition.Resolve( ref resolved );
		if ( resolved != null )
			size = resolved.wholeStackProgressRingSize;
		SetRingSize( size );

		PlayerSorterReposition sorter = player.SorterReposition;
		if ( sorter != null && sorter.IsCharging )
		{
			SetProgress( sorter.ChargeProgress01, valid: true );
			return;
		}

		PlayerWholeStackInteraction wholeStack = player.WholeStack;
		if ( wholeStack != null && wholeStack.ChargeProgress01 > 0.001f )
		{
			SetProgress( wholeStack.ChargeProgress01, valid: !wholeStack.IsPlaceChargeInvalid );
			return;
		}

		SetProgress( 0f );
	}

	public void SetRingSize( float size )
	{
		_size = Mathf.Max( 8f, size );
		EnsureUi();
		if ( _ring == null )
			return;

		RectTransform rect = _ring.rectTransform;
		rect.sizeDelta = new Vector2( _size, _size );
	}

	/// <summary>0 hides the ring; (0,1] shows fill amount.</summary>
	public void SetProgress( float progress01 )
	{
		SetProgress( progress01, valid: true );
	}

	public void SetProgress( float progress01, bool valid )
	{
		EnsureUi();
		if ( _ring == null )
			return;

		float clamped = Mathf.Clamp01( progress01 );
		if ( clamped <= 0.001f )
		{
			_ring.enabled = false;
			_ring.fillAmount = 0f;
			return;
		}

		_ring.enabled = true;
		_ring.fillAmount = clamped;
		_ring.color = valid
			? new Color( 1f, 0.92f, 0.45f, 0.85f )
			: new Color( 1f, 0.35f, 0.3f, 0.85f );
	}

	void EnsureUi()
	{
		if ( _ring != null )
			return;

		GameObject ringGo = new GameObject( "ProgressRing", typeof( RectTransform ), typeof( CanvasRenderer ), typeof( Image ) );
		ringGo.transform.SetParent( transform, false );

		RectTransform host = transform as RectTransform;
		if ( host != null )
		{
			Vector2 hostSize = host.sizeDelta;
			if ( hostSize.x < _size )
				hostSize.x = _size;
			if ( hostSize.y < _size )
				hostSize.y = _size;
			host.sizeDelta = hostSize;
		}

		RectTransform rect = ringGo.GetComponent<RectTransform>();
		rect.anchorMin = new Vector2( 0.5f, 0.5f );
		rect.anchorMax = new Vector2( 0.5f, 0.5f );
		rect.pivot = new Vector2( 0.5f, 0.5f );
		rect.anchoredPosition = Vector2.zero;
		rect.sizeDelta = new Vector2( _size, _size );

		_ring = ringGo.GetComponent<Image>();
		_ring.raycastTarget = false;
		_ring.type = Image.Type.Filled;
		_ring.fillMethod = Image.FillMethod.Radial360;
		_ring.fillOrigin = (int)Image.Origin360.Top;
		_ring.fillClockwise = true;
		_ring.color = new Color( 1f, 0.92f, 0.45f, 0.85f );
		_ring.sprite = CreateRingSprite();
		_ring.fillAmount = 0f;
		_ring.enabled = false;
	}

	static Sprite CreateRingSprite()
	{
		const int size = 64;
		const float outer = 0.48f;
		const float inner = 0.34f;
		Texture2D tex = new Texture2D( size, size, TextureFormat.RGBA32, false );
		tex.wrapMode = TextureWrapMode.Clamp;
		tex.filterMode = FilterMode.Bilinear;

		Color clear = new Color( 0f, 0f, 0f, 0f );
		Color solid = Color.white;
		float center = ( size - 1 ) * 0.5f;
		for ( int y = 0; y < size; y++ )
		{
			for ( int x = 0; x < size; x++ )
			{
				float dx = ( x - center ) / size;
				float dy = ( y - center ) / size;
				float r = Mathf.Sqrt( dx * dx + dy * dy );
				tex.SetPixel( x, y, r <= outer && r >= inner ? solid : clear );
			}
		}

		tex.Apply( false, false );
		return Sprite.Create( tex, new Rect( 0f, 0f, size, size ), new Vector2( 0.5f, 0.5f ), 100f );
	}
}
