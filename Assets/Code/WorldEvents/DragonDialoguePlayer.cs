using System;
using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Queues dragon dialogue lines and drives <see cref="DragonDialogueChangedEvent"/>.
/// Enqueue appends without dropping the current line or finish callbacks.
/// </summary>
public class DragonDialoguePlayer
{
	readonly Queue<DragonDialogueLine> _queue = new Queue<DragonDialogueLine>();
	readonly Queue<Action> _onFinished = new Queue<Action>();

	DragonDialogueLine _current;
	float _lineEndsAt = -1f;
	bool _playing;

	public bool IsPlaying => _playing;

	public void Play( DragonDialogueLine[] lines, Action onFinished = null )
	{
		Enqueue( lines, onFinished );
	}

	public void Enqueue( DragonDialogueLine[] lines, Action onFinished = null )
	{
		int added = 0;
		if ( lines != null )
		{
			for ( int i = 0; i < lines.Length; i++ )
			{
				DragonDialogueLine line = lines[ i ];
				if ( line == null || string.IsNullOrEmpty( line.text ) )
					continue;
				_queue.Enqueue( line );
				added++;
			}
		}

		if ( onFinished != null )
			_onFinished.Enqueue( onFinished );

		if ( _playing )
			return;

		if ( added == 0 && _queue.Count == 0 )
		{
			InvokeFinished();
			return;
		}

		_playing = true;
		ShowNext();
	}

	public void Skip()
	{
		if ( !_playing )
			return;

		ShowNext();
	}

	public void Tick()
	{
		if ( !_playing || _current == null )
			return;

		if ( Time.unscaledTime < _lineEndsAt )
			return;

		ShowNext();
	}

	public void Stop()
	{
		_queue.Clear();
		_onFinished.Clear();
		_current = null;
		_playing = false;
		PublishHidden();
	}

	void ShowNext()
	{
		if ( _queue.Count == 0 )
		{
			_current = null;
			_playing = false;
			PublishHidden();
			InvokeFinished();
			return;
		}

		_current = _queue.Dequeue();
		float hold = Mathf.Max( 4.5f, EstimateReadSeconds( _current.text ) ) + Mathf.Max( 0f, _current.pauseAfter );
		_lineEndsAt = Time.unscaledTime + hold;

		DragonDialogueSfx.PlayLine( _current );

		EventBus.Publish( new DragonDialogueChangedEvent
		{
			Visible = true,
			Speaker = string.IsNullOrEmpty( _current.speaker ) ? "Dragon" : _current.speaker,
			Text = _current.text,
			CanSkip = true
		} );
	}

	void InvokeFinished()
	{
		while ( _onFinished.Count > 0 )
		{
			Action done = _onFinished.Dequeue();
			if ( done != null )
				done();
		}
	}

	static float EstimateReadSeconds( string text )
	{
		if ( string.IsNullOrEmpty( text ) )
			return 4.5f;
		return Mathf.Clamp( text.Length / 6f, 5.25f, 24f );
	}

	static void PublishHidden()
	{
		EventBus.Publish( new DragonDialogueChangedEvent
		{
			Visible = false,
			Speaker = string.Empty,
			Text = string.Empty,
			CanSkip = false
		} );
	}
}
