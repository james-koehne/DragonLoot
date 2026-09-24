using System.Collections.Generic;

using UnityEngine;

namespace FeedbackSystem
{
	/// <summary>
	/// Shared rest pose for overlapping shake feedbacks on the same transform.
	/// Prevents each new shake from baking the previous offset into its base.
	/// </summary>
	public static class TransformShakeAnchor
	{
		class State
		{
			public Vector3 LocalPosition;
			public Quaternion LocalRotation;
			public int RetainCount;
		}

		static readonly Dictionary<int, State> States = new Dictionary<int, State>( 8 );

		public static int Retain( Transform target, out Vector3 basePosition, out Quaternion baseRotation )
		{
			int id = target.GetInstanceID();
			State state;
			if ( States.TryGetValue( id, out state ) )
			{
				state.RetainCount++;
				basePosition = state.LocalPosition;
				baseRotation = state.LocalRotation;
				return id;
			}

			state = new State
			{
				LocalPosition = target.localPosition,
				LocalRotation = target.localRotation,
				RetainCount = 1
			};
			States.Add( id, state );
			basePosition = state.LocalPosition;
			baseRotation = state.LocalRotation;
			return id;
		}

		public static void Release( int anchorId, Transform target, bool restore )
		{
			State state;
			if ( !States.TryGetValue( anchorId, out state ) )
				return;

			state.RetainCount--;
			if ( state.RetainCount > 0 )
				return;

			States.Remove( anchorId );
			if ( !restore || target == null )
				return;

			target.localPosition = state.LocalPosition;
			target.localRotation = state.LocalRotation;
		}
	}
}
