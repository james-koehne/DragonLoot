using System.Collections.Generic;

namespace FeedbackSystem
{
	public sealed class FeedbackTicker
	{
		readonly List<IFeedbackTick> _active = new List<IFeedbackTick>( 8 );
		bool _ticking;

		public bool HasActive
		{
			get
			{
				for ( int i = 0; i < _active.Count; i++ )
				{
					if ( _active[i] != null )
						return true;
				}

				return false;
			}
		}

		public void Register( IFeedbackTick op )
		{
			if ( op == null )
				return;

			for ( int i = 0; i < _active.Count; i++ )
			{
				if ( _active[i] == op )
					return;
			}

			_active.Add( op );
		}

		public void Unregister( IFeedbackTick op )
		{
			if ( op == null )
				return;

			for ( int i = 0; i < _active.Count; i++ )
			{
				if ( _active[i] != op )
					continue;

				if ( _ticking )
					_active[i] = null;
				else
					_active.RemoveAt( i );
				return;
			}
		}

		public void Tick( float deltaTime )
		{
			_ticking = true;
			for ( int i = _active.Count - 1; i >= 0; i-- )
			{
				IFeedbackTick op = _active[i];
				if ( op == null )
					continue;

				if ( !op.Tick( deltaTime ) )
					_active[i] = null;
			}

			_ticking = false;
			Compact();
		}

		public void StopAll()
		{
			_ticking = true;
			for ( int i = _active.Count - 1; i >= 0; i-- )
			{
				IFeedbackTick op = _active[i];
				if ( op != null )
					op.Cancel();
			}

			_ticking = false;
			_active.Clear();
		}

		void Compact()
		{
			for ( int i = _active.Count - 1; i >= 0; i-- )
			{
				if ( _active[i] == null )
					_active.RemoveAt( i );
			}
		}
	}
}
