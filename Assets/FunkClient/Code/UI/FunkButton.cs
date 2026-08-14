using UnityEngine;
using UnityEngine.EventSystems;
using FeedbackSystem;
using System;
using System.Threading.Tasks;
using UnityEngine.Events;
using System.Collections;

public enum FunkSelectionState
{
	Normal,
	Highlighted,
	Pressed,
	Selected,
	Disabled
}

[RequireComponent( typeof( RectTransform ) )]
public class FunkButton : MonoBehaviour, ISubmitHandler, IPointerDownHandler, IPointerUpHandler, IPointerEnterHandler, IPointerExitHandler
{
	public enum SelectionBehavior
	{
		Normal,
		LockedSelected,
		NonSelectable
	}

	public RectTransform rectTransform = null;
	public RectTransform RectTransform
	{
		get
		{
			if ( rectTransform == null )
			{
				rectTransform = GetComponent<RectTransform>();
			}

			return rectTransform;
		}
	}

	[Header( "Selection Behavior" )]
	public SelectionBehavior selectionBehavior = SelectionBehavior.Normal;
	public bool deselectOnClickAgain = false;
	public FunkButtonGroup buttonGroup;
	public bool startSelected = false;

	[Header( "Feedback Players per State" )]
	public Feedbacks normalStateFeedback;
	public Feedbacks highlightedStateFeedback;
	public Feedbacks pressedStateFeedback;
	public Feedbacks selectedStateFeedback;
	public Feedbacks disabledStateFeedback;

	[Header( "Click Event" )]
	public UnityEvent onClick = new UnityEvent();
	public Func<Task> onClickAsync;

	[Header( "State Events" )]
	public UnityEvent<FunkSelectionState> onStateChanged;
	public UnityEvent onSelected;
	public UnityEvent onDeselected;
	public UnityEvent onPressed;

	[Header( "Interaction Settings" )]
	public float clickCooldown = 0f;
	public float holdThreshold = 0.5f;

	[Header( "Optional" )]
	public bool interactable = true;

	private bool _lockedSelected;
	private float _lastClickTime;

	private FunkSelectionState _currentState = FunkSelectionState.Normal;

	private void Awake()
	{
		if ( buttonGroup != null )
			buttonGroup.Register( this );

		// Set initial state
		if ( selectionBehavior == SelectionBehavior.LockedSelected && startSelected )
		{
			SetSelected( true );
		}
		else
		{
			SetState( interactable ? FunkSelectionState.Normal : FunkSelectionState.Disabled, true );
		}
	}

	public void Setup()
	{
		if ( buttonGroup != null )
			buttonGroup.Register( this );
	}

	public async void SetEnabled( bool enabled )
	{
		this.enabled = enabled;
		await Task.Yield();
		if ( gameObject != null )
		{
			gameObject.SetActive( enabled );
		}
	}

	void OnDisable()
	{
		_ = DeferredDisable();
	}

	private async Task DeferredDisable()
	{
		await Task.Yield();
		SetState( FunkSelectionState.Normal );
	}

	public void SetState( FunkSelectionState state, bool instant = false )
	{
		_currentState = state;

		StopAllFeedbacks();

		switch ( state )
		{
			case FunkSelectionState.Normal:
				PlayFeedback( normalStateFeedback );
				break;
			case FunkSelectionState.Highlighted:
				PlayFeedback( highlightedStateFeedback );
				break;
			case FunkSelectionState.Pressed:
				PlayFeedback( pressedStateFeedback );
				break;
			case FunkSelectionState.Selected:
				PlayFeedback( selectedStateFeedback );
				break;
			case FunkSelectionState.Disabled:
				PlayFeedback( disabledStateFeedback );
				break;
		}

		onStateChanged?.Invoke( state );
	}

	private void PlayFeedback( Feedbacks feedbacks )
	{
		if ( feedbacks != null )
			feedbacks.Play();
	}

	private void StopAllFeedbacks()
	{
		if ( normalStateFeedback != null )
			normalStateFeedback.Stop();
		if ( highlightedStateFeedback != null )
			highlightedStateFeedback.Stop();
		if ( pressedStateFeedback != null )
			pressedStateFeedback.Stop();
		if ( selectedStateFeedback != null )
			selectedStateFeedback.Stop();
		if ( disabledStateFeedback != null )
			disabledStateFeedback.Stop();
	}

	public void OnSubmit( BaseEventData eventData )
	{
		if ( !interactable )
			return;
		TryPress();
	}

	public void OnPointerDown( PointerEventData eventData )
	{
		if ( !interactable )
			return;
		SetState( FunkSelectionState.Pressed );
	}

	public void OnPointerUp( PointerEventData eventData )
	{
		if ( selectionBehavior == SelectionBehavior.LockedSelected && _lockedSelected )
		{
			SetState( FunkSelectionState.Selected );
		}
		else
		{
			SetState( FunkSelectionState.Normal );
		}

		TryPress();
	}

	public void OnPointerEnter( PointerEventData eventData )
	{
		if ( interactable && _currentState != FunkSelectionState.Pressed && _currentState != FunkSelectionState.Selected )
			SetState( FunkSelectionState.Highlighted );
	}

	public void OnPointerExit( PointerEventData eventData )
	{
		if ( _currentState != FunkSelectionState.Pressed && ( _lockedSelected && selectionBehavior == SelectionBehavior.LockedSelected ? false : true ) )
		{
			SetState( selectionBehavior == SelectionBehavior.LockedSelected && _lockedSelected ? FunkSelectionState.Selected : FunkSelectionState.Normal );
		}
	}

	private async void TryPress()
	{
		if ( Time.unscaledTime - _lastClickTime < clickCooldown )
			return;
		_lastClickTime = Time.unscaledTime;

		Press();

		if ( onClickAsync != null )
			await onClickAsync.Invoke();
	}

	private void Press()
	{
		if ( !interactable )
			return;

		if ( selectionBehavior == SelectionBehavior.LockedSelected )
		{
			if ( _lockedSelected && deselectOnClickAgain )
			{
				SetSelected( false );
			}
			else
			{
				SetSelected( true );
			}
		}

		onPressed?.Invoke();
		onClick?.Invoke();
	}

	public void SetSelected( bool selected, bool force = false )
	{
		if ( selectionBehavior != SelectionBehavior.LockedSelected && !force )
			return;

		bool prev = _lockedSelected;
		_lockedSelected = selected;

		if ( !force )
		{
			if ( selected && !prev )
			{
				onSelected?.Invoke();
				buttonGroup?.NotifyButtonSelected( this );
			}

			if ( !selected && prev )
			{
				onDeselected?.Invoke();
			}
		}
		else
		{
			if ( selected )
			{
				onSelected?.Invoke();
				buttonGroup?.NotifyButtonSelected( this );
			}

			if ( !selected )
			{
				onDeselected?.Invoke();
			}
		}

		SetState( selected ? FunkSelectionState.Selected : FunkSelectionState.Normal );
	}
}
