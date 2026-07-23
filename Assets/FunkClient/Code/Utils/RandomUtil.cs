using System;
using System.Collections.Generic;

public static class RandomUtil
{
	public static T Choose<T>( T[] array )
	{
		if ( array == null || array.Length == 0 )
			throw new ArgumentException( "Array is null or empty." );

		return array[ UnityEngine.Random.Range( 0, array.Length ) ];
	}
	public static T Choose<T>( List<T> list )
	{
		if ( list == null || list.Count == 0 )
			throw new ArgumentException( "List is null or empty." );

		return list[ UnityEngine.Random.Range( 0, list.Count ) ];
	}

	public static T ChooseWeighted<T>( T[] items, float[] weights )
	{
		if ( items == null || weights == null || items.Length != weights.Length || items.Length == 0 )
			throw new ArgumentException( "Arrays must be non-null, same length, and not empty." );

		float totalWeight = 0f;
		for ( int i = 0; i < weights.Length; i++ )
			totalWeight += weights[ i ];

		float randomValue = UnityEngine.Random.value * totalWeight;
		float cumulative = 0f;

		for ( int i = 0; i < items.Length; i++ )
		{
			cumulative += weights[ i ];
			if ( randomValue <= cumulative )
				return items[ i ];
		}

		return items[ items.Length - 1 ]; // Fallback
	}

	public static T ChooseWeighted<T>( List<T> items, List<float> weights )
	{
		if ( items == null || weights == null || items.Count != weights.Count || items.Count == 0 )
			throw new ArgumentException( "Lists must be non-null, same length, and not empty." );

		float totalWeight = 0f;
		for ( int i = 0; i < weights.Count; i++ )
			totalWeight += weights[ i ];

		float randomValue = UnityEngine.Random.value * totalWeight;
		float cumulative = 0f;

		for ( int i = 0; i < items.Count; i++ )
		{
			cumulative += weights[ i ];
			if ( randomValue <= cumulative )
				return items[ i ];
		}

		return items[ items.Count - 1 ]; // Fallback
	}

	public static T ChooseAndRemove<T>( List<T> list )
	{
		if ( list == null || list.Count == 0 )
			throw new ArgumentException( "List is null or empty." );

		int index = UnityEngine.Random.Range( 0, list.Count );
		T item = list[ index ];
		list.RemoveAt( index );
		return item;
	}

	public static T ChooseAndRemove<T>( ref T[] array )
	{
		if ( array == null || array.Length == 0 )
			throw new ArgumentException( "Array is null or empty." );

		int index = UnityEngine.Random.Range( 0, array.Length );
		T item = array[ index ];

		T[] newArray = new T[ array.Length - 1 ];
		if ( index > 0 )
			Array.Copy( array, 0, newArray, 0, index );
		if ( index < array.Length - 1 )
			Array.Copy( array, index + 1, newArray, index, array.Length - index - 1 );

		array = newArray;
		return item;
	}
}
