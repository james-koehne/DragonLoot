using System;
using System.Collections.Generic;

using UnityEngine;

namespace FeedbackSystem
{
	[Serializable]
	public abstract class Feedback
	{
		[SerializeField]
		[HideInInspector]
		bool _enabled = true;

		[NonSerialized]
		Feedbacks _owner;

		[NonSerialized]
		FeedbackContext _context;

		public bool Enabled
		{
			get { return _enabled; }
			set { _enabled = value; }
		}

		public Feedbacks Owner
		{
			get { return _owner; }
		}

		public FeedbackContext Context
		{
			get { return _context; }
		}

		public void Bind( Feedbacks owner )
		{
			_owner = owner;
		}

		public void SetContext( FeedbackContext context )
		{
			_context = context;
		}

		public virtual void Initialize()
		{
		}

		public abstract void Play();

		public virtual void Stop()
		{
		}

		public virtual void Reset()
		{
		}

		public virtual float GetHoldDuration()
		{
			return 0f;
		}

		protected void InitializeChildren( List<Feedback> children )
		{
			if ( children == null )
				return;

			for ( int i = 0; i < children.Count; i++ )
			{
				Feedback child = children[i];
				if ( child == null )
					continue;

				child.Bind( _owner );
				child.Initialize();
			}
		}

		protected void StopChildren( List<Feedback> children )
		{
			if ( children == null )
				return;

			for ( int i = 0; i < children.Count; i++ )
			{
				Feedback child = children[i];
				if ( child == null )
					continue;

				child.Stop();
			}
		}

		protected void ResetChildren( List<Feedback> children )
		{
			if ( children == null )
				return;

			for ( int i = 0; i < children.Count; i++ )
			{
				Feedback child = children[i];
				if ( child == null )
					continue;

				child.Reset();
			}
		}

		protected void RegisterTick( IFeedbackTick tick )
		{
			if ( _owner == null || _owner.Ticker == null )
				return;

			_owner.Ticker.Register( tick );
		}

		protected void UnregisterTick( IFeedbackTick tick )
		{
			if ( _owner == null || _owner.Ticker == null )
				return;

			_owner.Ticker.Unregister( tick );
		}
	}
}
