using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Circular progress ring around the crosshair for hold charges
/// (sorter pickup, whole-stack E/F, and non-coin pickup hold).
/// </summary>
public class InteractionProgressRingUI : MonoBehaviour
{
	[SerializeField] Image _ring;

	float _size = 96f;

	public void Setup()
	{
		SetProgress( 0f );
	}

	void LateUpdate()
	{
		RefreshFromPlayer();
	}

	void RefreshFromPlayer()
	{
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

		PlayerBuildMode buildMode = player.BuildMode;
		if ( buildMode != null && buildMode.ChargeProgress01 > 0.001f )
		{
			SetProgress( buildMode.ChargeProgress01, valid: true );
			return;
		}

		PlayerWholeStackInteraction wholeStack = player.WholeStack;
		if ( wholeStack != null && wholeStack.ChargeProgress01 > 0.001f )
		{
			SetProgress( wholeStack.ChargeProgress01, valid: !wholeStack.IsPlaceChargeInvalid );
			return;
		}

		PlayerInteraction interaction = player.Interaction;
		if ( interaction != null && interaction.NonCoinPickupHoldProgress > 0.001f )
		{
			SetProgress( interaction.NonCoinPickupHoldProgress, valid: true );
			return;
		}

		SetProgress( 0f );
	}

	public void SetRingSize( float size )
	{
		_size = Mathf.Max( 8f, size );
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
}
