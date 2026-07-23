using System.Collections.Generic;

using UnityEngine;

public static class CollectionUtil
{
	public static T PeekSecond<T>( this Stack<T> stack )
	{
		if ( stack.Count == 0 )
		{
			Debug.LogWarning( "PeekSecond called but stack has no elements." );
			return stack.Peek(); // returns null for ref types, 0/false for value types
		}

		if ( stack.Count == 1 )
		{
			return stack.Peek();
		}

		T top = stack.Pop();
		T second = stack.Peek();
		stack.Push( top );

		return second;
	}
}
