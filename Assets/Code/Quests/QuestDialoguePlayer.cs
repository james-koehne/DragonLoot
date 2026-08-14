using System;
using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Queues dragon dialogue lines and drives <see cref="QuestDialogueChangedEvent"/>.
/// </summary>
public class QuestDialoguePlayer
{
	readonly Queue<QuestDialogueLine> _queue = new Queue<QuestDialogueLine>();

	QuestDialogueLine _current;
	float _lineEndsAt = -1f;
	bool _playing;
	Action _onFinished;

	public bool IsPlaying => _playing;

	public void Play( QuestDialogueLine[] lines, Action onFinished = null )
	{
		_queue.Clear();
		_current = null;
		_onFinished = onFinished;

		if ( lines != null )
		{
			for ( int i = 0; i < lines.Length; i++ )
			{
				QuestDialogueLine line = lines[ i ];
				if ( line == null || string.IsNullOrEmpty( line.text ) )
					continue;
				_queue.Enqueue( line );
			}
		}

		if ( _queue.Count == 0 )
		{
			_playing = false;
			PublishHidden();
			if ( onFinished != null )
				onFinished();
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
		_current = null;
		_playing = false;
		_onFinished = null;
		PublishHidden();
	}

	void ShowNext()
	{
		if ( _queue.Count == 0 )
		{
			Action done = _onFinished;
			_onFinished = null;
			_current = null;
			_playing = false;
			PublishHidden();
			if ( done != null )
				done();
			return;
		}

		_current = _queue.Dequeue();
		float hold = Mathf.Max( 1.5f, EstimateReadSeconds( _current.text ) ) + Mathf.Max( 0f, _current.pauseAfter );
		_lineEndsAt = Time.unscaledTime + hold;

		QuestDialogueSfx.PlayLine( _current );

		EventBus.Publish( new QuestDialogueChangedEvent
		{
			Visible = true,
			Speaker = string.IsNullOrEmpty( _current.speaker ) ? "Dragon" : _current.speaker,
			Text = _current.text,
			CanSkip = true
		} );
	}

	static float EstimateReadSeconds( string text )
	{
		if ( string.IsNullOrEmpty( text ) )
			return 1.5f;
		// ~18 chars/sec reading pace, clamped.
		return Mathf.Clamp( text.Length / 18f, 1.75f, 8f );
	}

	static void PublishHidden()
	{
		EventBus.Publish( new QuestDialogueChangedEvent
		{
			Visible = false,
			Speaker = string.Empty,
			Text = string.Empty,
			CanSkip = false
		} );
	}
}
