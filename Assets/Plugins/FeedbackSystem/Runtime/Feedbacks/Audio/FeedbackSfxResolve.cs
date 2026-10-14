using UnityEngine;

namespace FeedbackSystem
{
	public static class FeedbackSfxResolve
	{
		public static Vector3 ResolvePosition( FeedbackContext context, GameObject owner )
		{
			if ( context != null )
				return context.Position;

			if ( owner != null )
				return owner.transform.position;

			return Vector3.zero;
		}

		public static Transform ResolveFollowTransform( bool followCaller, FeedbackContext context, GameObject owner )
		{
			if ( !followCaller )
				return null;

			if ( context != null && context.Source != null )
				return context.Source.transform;

			if ( owner != null )
				return owner.transform;

			return null;
		}
	}
}
