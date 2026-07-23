using System;
using System.Threading.Tasks;

using Tweens;

using UnityEngine;
using UnityEngine.Events;

public enum InterfaceState
{
	None,
	MainMenu,
	Game,
}

public class InterfaceController : MonoBehaviour, IInterfaceController
{
	public UnityEvent<InterfaceState, bool> OnInterfaceStateChangeRequest = new UnityEvent<InterfaceState, bool>();
	public UnityEvent<InterfaceState, bool> OnInterfaceStateChange = new UnityEvent<InterfaceState, bool>();

	public InterfaceState CurrentState { get; private set; } = InterfaceState.None;

	[SerializeField] private CanvasGroup fadeGroup;
	[SerializeField] private CanvasGroup loadingGroup;
	[SerializeField] private TweenInstance<Transform, float> currentFadeTween;

	private bool isSetup = false;
	private bool manualQuit = false;
	private bool quitting = false;
	private bool settingInterfaceState = false;
	private bool fading = false;

	private UIController[] uiControllers = new UIController[ 0 ];

	public void Setup()
	{
		CurrentState = InterfaceState.None;

		uiControllers = gameObject.GetComponentsInChildren<UIController>( true );

		for ( int i = 0; i < uiControllers.Length; i++ )
		{
			if ( uiControllers[ i ] != null )
			{
				uiControllers[ i ].setup();
				uiControllers[ i ].SetVisibility( false );
			}
		}

		isSetup = true;
	}

	public void HideError()
	{

	}

	public void ShowError( string message, UnityAction onClick = null, bool showCloseButton = true, bool playFx = true )
	{
		/* ShowPopup( message, Definition.popupErrorColor, Definition.popupErrorTextColor, onClick, showCloseButton );

		if ( playFx )
		{
			_ = GameEffects.GetOrSpawn( "ErrorPopup", GameMode.Instance.EffectDefinition.errorPopupEffectAssetReference );
		} */
	}

	private void ShowPopup( string message, Color backgroundColor, Color textColor, UnityAction onClick = null, bool showCloseButton = true )
	{
		/* errorText.text = message; */
		/* errorPopupObj.SetActive( true ); */

		/* errorBannerImage.color = backgroundColor; */
		/* errorBannerText.color = textColor; */
		/* errorButtonImage.color = backgroundColor; */

		/* errorButton.gameObject.SetActive( showCloseButton ); */

		/* errorButton.onClick.RemoveAllListeners(); */
		/* errorButton.onClick.AddListener( HideError ); */

		/* if ( onClick != null ) */
		/* 	errorButton.onClick.AddListener( onClick ); */
	}

	public void OnNotificationsUpdated()
	{

	}

	public void GoBack( bool ignoreUICount = false )
	{
		if ( settingInterfaceState )
			return;

		if ( UIController.TopMostUIController.TryPeek( out UIController ui ) )
		{
			if ( ui.CanGoBack() )
			{
				ui.GoBack();
			}
		}
	}

	public async Task SetInterfaceState( InterfaceState state, bool force = false )
	{
		if ( settingInterfaceState )
			return;

		settingInterfaceState = true;

		if ( CurrentState == state && !force )
		{
			settingInterfaceState = false;
			return;
		}

		CurrentState = state;

		OnInterfaceStateChangeRequest.Invoke( state, force );

		for ( int i = 0; i < uiControllers.Length; i++ )
		{
			if ( uiControllers[ i ] != null && uiControllers[ i ].State == CurrentState )
			{
				await uiControllers[ i ].Loaded;
			}
		}

		OnInterfaceStateChange.Invoke( state, force );

		handleInterfaceStateChanged( state );

		settingInterfaceState = false;
	}

	public async Task<bool> Quit( InterfaceState quitTarget, Action onQuitAction = null, bool fadeToBlack = true )
	{
		await TaskEx.WaitWhile( () => quitting || fading );

		if ( quitting || fading )
			return false;

		quitting = true;

		if ( fadeToBlack )
		{
			await FadeToBlack( 0.1f );
		}

		await SetInterfaceState( quitTarget );

		if ( onQuitAction != null )
		{
			onQuitAction.Invoke();
		}

		if ( fadeToBlack )
		{
			await FadeOutFromBlack( 0.25f );
		}

		quitting = false;

		return true;
	}

	public void SetManualQuit( bool quit )
	{
		manualQuit = quit;
	}

	private void handleInterfaceStateChanged( InterfaceState state )
	{
		switch ( state )
		{
			case InterfaceState.None:
				break;
			case InterfaceState.MainMenu:
				break;
			case InterfaceState.Game:
				break;
		}
	}

	public async Task FadeToBlackAndLoad( Func<Task> loadingTask, float timeToFade )
	{
		await FadeToBlack( timeToFade );

		if ( loadingTask != null )
			await loadingTask();
	}

	public async Task FadeToBlack( float timeToFade )
	{
		await FadeAsync( fadeGroup, 1f, timeToFade );
	}

	public async Task FadeOutFromBlack( float timeToFade )
	{
		await FadeAsync( fadeGroup, 0f, timeToFade );
	}

	private async Task FadeAsync( CanvasGroup fadeCanvas, float targetAlpha, float timeToFade )
	{
		if ( fadeCanvas == null )
			return;

		fading = true;

		fadeCanvas.gameObject.SetActive( true );

		if ( currentFadeTween != null )
		{
			currentFadeTween.Cancel();
		}

		TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();

		FloatTween tween = new FloatTween
		{
			from = fadeCanvas.alpha,
			to = targetAlpha,
			duration = timeToFade,
			easeType = EaseType.SineInOut,
			onUpdate = ( _, value ) => fadeCanvas.alpha = value,
			onEnd = ( _ ) =>
			{
				fading = false;

				tcs.TrySetResult( true );
			}
		};

		currentFadeTween = gameObject.AddTween( tween );

		await tcs.Task;

		fadeCanvas.gameObject.SetActive( targetAlpha > 0.0f );
	}
}