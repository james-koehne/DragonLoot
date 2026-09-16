using System.Collections.Generic;

using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// World-space speech bubble that queues timed lines and billboards toward the camera.
/// </summary>
[DisallowMultipleComponent]
public class FairySpeechBubble : MonoBehaviour
{
	[SerializeField]
	Canvas canvas;

	[SerializeField]
	CanvasGroup canvasGroup;

	[SerializeField]
	Text body;

	[SerializeField]
	[Min( 0.5f )]
	float defaultHoldSeconds = 3.5f;

	[SerializeField]
	[Min( 0f )]
	float fadeSeconds = 0.2f;

	[SerializeField]
	Vector3 worldOffset = new Vector3( 0f, 0.55f, 0f );

	readonly Queue<string> _queue = new Queue<string>( 4 );

	Transform _follow;
	float _lineEndsAt;
	float _fadeOutAt = -1f;
	bool _showing;

	public bool IsShowing => _showing || _queue.Count > 0;

	void Awake()
	{
		if ( canvas == null )
			canvas = GetComponentInChildren<Canvas>( true );
		if ( canvasGroup == null && canvas != null )
			canvasGroup = canvas.GetComponent<CanvasGroup>();
		if ( body == null && canvas != null )
			body = canvas.GetComponentInChildren<Text>( true );

		SetVisible( false );
	}

	void Start()
	{
		if ( _follow == null && transform.parent != null )
			_follow = transform.parent;
	}

	void LateUpdate()
	{
		UpdateFollowPosition();

		Camera cam = Camera.main;
		if ( cam != null )
			transform.rotation = Quaternion.LookRotation( transform.position - cam.transform.position, Vector3.up );

		TickSpeech();
	}

	public void BindFollow( Transform follow )
	{
		if ( follow == null || follow == transform )
			_follow = transform.parent != null ? transform.parent : null;
		else
			_follow = follow;
		UpdateFollowPosition();
	}

	public void EditorBind( Canvas canvasRef, CanvasGroup groupRef, Text bodyRef )
	{
		canvas = canvasRef;
		canvasGroup = groupRef;
		body = bodyRef;
	}

	public void Say( string line )
	{
		if ( string.IsNullOrEmpty( line ) )
			return;

		_queue.Enqueue( line );
		if ( !_showing )
			ShowNext();
	}

	public void Say( params string[] lines )
	{
		if ( lines == null || lines.Length == 0 )
			return;

		for ( int i = 0; i < lines.Length; i++ )
		{
			if ( !string.IsNullOrEmpty( lines[ i ] ) )
				_queue.Enqueue( lines[ i ] );
		}

		if ( !_showing )
			ShowNext();
	}

	public void Clear()
	{
		_queue.Clear();
		_showing = false;
		_fadeOutAt = -1f;
		SetVisible( false );
	}

	void UpdateFollowPosition()
	{
		if ( _follow == null || _follow == transform )
			return;

		transform.position = _follow.position + worldOffset;
	}

	void TickSpeech()
	{
		if ( !_showing )
			return;

		if ( _fadeOutAt > 0f )
		{
			float t = fadeSeconds <= 0.001f
				? 1f
				: Mathf.Clamp01( 1f - ( _fadeOutAt - Time.unscaledTime ) / fadeSeconds );
			if ( canvasGroup != null )
				canvasGroup.alpha = 1f - t;
			if ( Time.unscaledTime >= _fadeOutAt )
			{
				_fadeOutAt = -1f;
				ShowNext();
			}
			return;
		}

		if ( Time.unscaledTime < _lineEndsAt )
			return;

		if ( _queue.Count > 0 )
		{
			BeginFadeOut();
			return;
		}

		BeginFadeOut();
	}

	void BeginFadeOut()
	{
		if ( fadeSeconds <= 0.001f )
		{
			ShowNext();
			return;
		}

		_fadeOutAt = Time.unscaledTime + fadeSeconds;
	}

	void ShowNext()
	{
		_fadeOutAt = -1f;
		if ( _queue.Count == 0 )
		{
			_showing = false;
			SetVisible( false );
			return;
		}

		string line = _queue.Dequeue();
		if ( body != null )
			body.text = line;
		_lineEndsAt = Time.unscaledTime + EstimateHoldSeconds( line );
		_showing = true;
		SetVisible( true );
		if ( canvasGroup != null )
			canvasGroup.alpha = 1f;
	}

	float EstimateHoldSeconds( string text )
	{
		if ( string.IsNullOrEmpty( text ) )
			return defaultHoldSeconds;
		return Mathf.Max( defaultHoldSeconds, Mathf.Clamp( text.Length / 12f, 2.5f, 8f ) );
	}

	void SetVisible( bool visible )
	{
		if ( canvasGroup != null )
		{
			canvasGroup.alpha = visible ? 1f : 0f;
			canvasGroup.blocksRaycasts = false;
			canvasGroup.interactable = false;
			return;
		}

		if ( canvas != null )
			canvas.enabled = visible;
	}
}
