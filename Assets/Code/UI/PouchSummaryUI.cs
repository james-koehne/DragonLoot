using System.Collections.Generic;
using System.Text;

using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Left-middle toast summarizing the selected pouch after a category switch.
/// Slides in from the left, holds, then slides back out. Wire <see cref="group"/>
/// and <see cref="label"/> on the Interface prefab.
/// </summary>
public class PouchSummaryUI : MonoBehaviour
{
	[SerializeField] CanvasGroup group;
	[SerializeField] Text label;

	[Tooltip( "Extra pixels beyond the panel width used as the off-screen rest position." )]
	[SerializeField] float offscreenPadding = 48f;

	bool _ready;
	bool _subscribed;
	float _phaseElapsed;
	float _slideIn = 0.2f;
	float _hold = 2f;
	float _slideOut = 0.35f;
	int _phase; // 0 hidden, 1 slide in, 2 hold, 3 slide out

	RectTransform _rect;
	Vector2 _shownPos;
	Vector2 _hiddenPos;
	bool _positionsCached;

	readonly List<TreasureDefinition> _defs = new List<TreasureDefinition>( 16 );
	readonly List<int> _counts = new List<int>( 16 );
	readonly StringBuilder _builder = new StringBuilder( 256 );

	public void Setup()
	{
		Subscribe();
		CachePositions();
		SnapHidden();
		_phase = 0;
		_ready = true;
	}

	void OnEnable()
	{
		Subscribe();
	}

	void OnDisable()
	{
		Unsubscribe();
	}

	void OnDestroy()
	{
		Unsubscribe();
	}

	void Subscribe()
	{
		if ( _subscribed )
			return;
		EventBus.Subscribe<PouchChangedEvent>( OnPouchChanged );
		_subscribed = true;
	}

	void Unsubscribe()
	{
		if ( !_subscribed )
			return;
		EventBus.Unsubscribe<PouchChangedEvent>( OnPouchChanged );
		_subscribed = false;
	}

	void Update()
	{
		if ( !_ready || _phase == 0 )
			return;

		CachePositions();
		if ( _rect == null )
			return;

		float dt = Time.unscaledDeltaTime;
		_phaseElapsed += dt;

		if ( _phase == 1 )
		{
			float t = _slideIn <= 0.001f ? 1f : Mathf.Clamp01( _phaseElapsed / _slideIn );
			ApplySlide( CoinFlipMotion.SmoothStep( t ) );
			SetVisible( true );
			if ( t >= 1f )
			{
				_rect.anchoredPosition = _shownPos;
				_phase = 2;
				_phaseElapsed = 0f;
			}
		}
		else if ( _phase == 2 )
		{
			_rect.anchoredPosition = _shownPos;
			SetVisible( true );
			if ( _phaseElapsed >= _hold )
			{
				_phase = 3;
				_phaseElapsed = 0f;
			}
		}
		else if ( _phase == 3 )
		{
			float t = _slideOut <= 0.001f ? 1f : Mathf.Clamp01( _phaseElapsed / _slideOut );
			ApplySlide( 1f - CoinFlipMotion.SmoothStep( t ) );
			if ( t >= 1f )
				SnapHidden();
			else
				SetVisible( true );
		}
	}

	void OnPouchChanged( PouchChangedEvent evt )
	{
		if ( label == null )
			return;

		PlayerCarry carry = evt.Carry;
		if ( carry == null )
			return;

		ResolveTimings();
		CachePositions();
		carry.BuildBucketSummary( evt.Bucket, _defs, _counts );

		_builder.Length = 0;
		_builder.AppendLine( BucketTitle( evt.Bucket ) );
		if ( _defs.Count == 0 )
			_builder.Append( "(empty)" );
		else
		{
			for ( int i = 0; i < _defs.Count; i++ )
			{
				TreasureDefinition def = _defs[ i ];
				string name = def != null && !string.IsNullOrEmpty( def.displayName )
					? def.displayName
					: ( def != null ? def.name : "Item" );
				int count = _counts[ i ];
				if ( count > 1 )
					_builder.Append( name ).Append( " x" ).Append( count );
				else
					_builder.Append( name );
				if ( i < _defs.Count - 1 )
					_builder.AppendLine();
			}
		}

		label.text = _builder.ToString();

		// Restart from off-screen so rapid pouch switches re-slide cleanly.
		if ( _rect != null )
			_rect.anchoredPosition = _hiddenPos;
		SetVisible( true );
		_phase = 1;
		_phaseElapsed = 0f;
	}

	void ApplySlide( float shownAmount01 )
	{
		if ( _rect == null )
			return;

		_rect.anchoredPosition = Vector2.LerpUnclamped( _hiddenPos, _shownPos, shownAmount01 );
	}

	void SnapHidden()
	{
		CachePositions();
		if ( _rect != null )
			_rect.anchoredPosition = _hiddenPos;
		SetVisible( false );
		_phase = 0;
		_phaseElapsed = 0f;
	}

	void SetVisible( bool visible )
	{
		if ( group != null )
		{
			group.alpha = visible ? 1f : 0f;
			group.blocksRaycasts = false;
			group.interactable = false;
		}
	}

	void CachePositions()
	{
		if ( _rect == null )
			_rect = group != null ? group.GetComponent<RectTransform>() : GetComponent<RectTransform>();
		if ( _rect == null )
			return;

		if ( !_positionsCached )
		{
			_shownPos = _rect.anchoredPosition;
			_positionsCached = true;
		}

		float width = Mathf.Max( _rect.rect.width, _rect.sizeDelta.x );
		if ( width < 1f )
			width = 560f;

		_hiddenPos = new Vector2( _shownPos.x - width - Mathf.Max( 0f, offscreenPadding ), _shownPos.y );
	}

	void ResolveTimings()
	{
		_slideIn = 0.2f;
		_hold = 2f;
		_slideOut = 0.35f;

		CarryDefinition resolved = null;
		resolved = RuntimeDefinition.Resolve( ref resolved );
		if ( resolved != null )
		{
			_slideIn = resolved.pouchSummaryFadeIn;
			_hold = resolved.pouchSummaryHold;
			_slideOut = resolved.pouchSummaryFadeOut;
		}
	}

	static string BucketTitle( CarryBucketKind kind )
	{
		switch ( kind )
		{
			case CarryBucketKind.Coin:
				return "Coins";
			case CarryBucketKind.Gem:
				return "Gems";
			case CarryBucketKind.Artifact:
				return "Artifacts";
			case CarryBucketKind.General:
				return "General";
			default:
				return "Pouch";
		}
	}
}
