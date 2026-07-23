using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;

using UnityEngine;

public class UIController : MonoBehaviour
{
	public InterfaceController InterfaceController => GameMode.Instance.InterfaceController;
	public InterfaceState State;

	public bool IsUIStackable = true;
	public bool IsInStack = false;

	public static Stack<UIController> TopMostUIController = new Stack<UIController>();

	public Task Loaded => TaskEx.WaitUntil( () => loaded );

	[SerializeField] private CanvasGroup canvasGroup;

	private bool visibility = false;
	protected bool loaded = false;

	void Awake()
	{
		canvasGroup = GetComponent<CanvasGroup>();

		FunkButton[] funkButtons = gameObject.GetComponentsInChildren<FunkButton>( true );

		for ( int i = 0; i < funkButtons.Length; i++ )
		{
			if ( funkButtons[ i ] != null )
			{
				funkButtons[ i ].Setup();
			}
		}
	}

	public virtual void setup()
	{
		InterfaceController.OnInterfaceStateChangeRequest.AddListener( onInterfaceStateChangeRequest );
		InterfaceController.OnInterfaceStateChange.AddListener( onInterfaceStateChange );
	}

	public virtual void GoBack()
	{
		SetVisibility( false );

		PopTopUIController();
	}

	public virtual bool CanGoBack()
	{
		return true;
	}

	public void PopUIController()
	{
		ForceRemove( TopMostUIController, this );

		IsInStack = false;

		//Debug.Log( "UI popped: " + name + " UI now: " + TopMostUIController.Peek().name );
	}

	public UIController PopUIUntil( params InterfaceState[] states )
	{
		if ( TopMostUIController.Count == 0 )
			return null;

		while ( TopMostUIController.Count > 0 )
		{
			UIController top = TopMostUIController.Peek();

			bool shouldStop = false;
			foreach ( InterfaceState state in states )
			{
				if ( top.State == state )
				{
					shouldStop = true;
					break;
				}
			}

			if ( shouldStop )
				return top;

			UIController popped = TopMostUIController.Pop();
			popped.IsInStack = false;

			//Debug.Log( "UI popped: " + name + " UI now: " + TopMostUIController.Peek().name );
		}

		return null;
	}

	public static void PopTopUIController()
	{
		UIController poppedUi = TopMostUIController.Pop();

		poppedUi.IsInStack = false;

		//Debug.Log( "UI popped: " + poppedUi.name + " UI now: " + TopMostUIController.Peek().name );
	}

	private void onInterfaceStateChangeRequest( InterfaceState state, bool force )
	{
		if ( State == state )
		{
			OnPanelRequested();
		}
	}

	public virtual void OnPanelRequested()
	{
		loaded = true;
	}

	private void onInterfaceStateChange( InterfaceState state, bool force )
	{
		if ( state == InterfaceState.None )
		{
			return;
		}

		SetVisibility( State == state );
	}

	public virtual void OnPanelActivated()
	{
		if ( IsUIStackable && !IsInStack )
		{
			TopMostUIController.Push( this );
			IsInStack = true;
			//Debug.Log( "UI pushed: " + name );
		}
	}

	public virtual void OnPanelDeactivated()
	{

	}

	public static void ForceRemove<T>( Stack<T> stack, T itemToRemove )
	{
		var temp = new Stack<T>();

		while ( stack.Count > 0 )
		{
			T current = stack.Pop();
			if ( !EqualityComparer<T>.Default.Equals( current, itemToRemove ) )
			{
				temp.Push( current );
			}
			else
			{
				break;
			}
		}

		while ( temp.Count > 0 )
		{
			stack.Push( temp.Pop() );
		}
	}

	public virtual bool SetGameObjectVisibility()
	{
		return true;
	}

	public virtual void SetVisibility( bool visible, bool removeFromNavStack = false )
	{
		bool visibilityChanged = false;

		if ( visibility != visible )
		{
			visibilityChanged = true;
		}

		visibility = visible;

		if ( SetGameObjectVisibility() )
		{
			if ( canvasGroup != null )
			{
				if ( visible )
				{
					canvasGroup.FadeIn();
				}
				else
				{
					canvasGroup.FadeOut();
				}
			}
			else
			{
				gameObject.SetActive( visibility );
			}
		}

		if ( visibilityChanged )
		{
			if ( visible )
			{
				OnPanelActivated();
			}
			else
			{
				OnPanelDeactivated();
			}
		}

		if ( !visibility && removeFromNavStack && IsUIStackable && IsInStack )
		{
			ForceRemove( TopMostUIController, this );
			IsInStack = false;
			//Debug.Log( "Ui popped: " + name + " UI now: " + TopMostUIController.Peek().name );
		}
	}

	public void ToggleVisibility()
	{
		SetVisibility( !visibility );
	}

	public bool IsVisible()
	{
		return visibility;
	}
}
