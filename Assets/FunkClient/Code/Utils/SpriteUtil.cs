using System.Collections.Generic;

using UnityEngine;

public static class SpriteUtil
{
	private static readonly Dictionary<Sprite, Sprite> _flippedCache = new Dictionary<Sprite, Sprite>();

	private static readonly Dictionary<Sprite, Sprite> _reverseCache = new Dictionary<Sprite, Sprite>();

	public static Sprite GetFlipped( Sprite original )
	{
		if ( original == null )
			return null;

		if ( _flippedCache.TryGetValue( original, out Sprite flipped ) )
			return flipped;

		if ( _reverseCache.TryGetValue( original, out Sprite originalFromFlip ) )
			return originalFromFlip;

		flipped = CreateFlippedSprite( original );

		_flippedCache[ original ] = flipped;
		_reverseCache[ flipped ] = original;

		return flipped;
	}

	private static Sprite CreateFlippedSprite( Sprite original )
	{
		Texture2D srcTex = original.texture;

		Texture2D newTex = new Texture2D( (int)original.rect.width, (int)original.rect.height );
		Color[] pixels = srcTex.GetPixels(
			(int)original.rect.x,
			(int)original.rect.y,
			(int)original.rect.width,
			(int)original.rect.height
		);

		int w = (int)original.rect.width;
		int h = (int)original.rect.height;
		Color[] flippedPixels = new Color[ pixels.Length ];

		for ( int y = 0; y < h; y++ )
		{
			for ( int x = 0; x < w; x++ )
			{
				flippedPixels[ y * w + x ] = pixels[ y * w + ( w - 1 - x ) ];
			}
		}

		newTex.SetPixels( flippedPixels );
		newTex.Apply();

		Sprite flippedSprite = Sprite.Create(
			newTex,
			new Rect( 0, 0, w, h ),
			original.pivot / original.rect.size,
			original.pixelsPerUnit
		);

		flippedSprite.name = original.name + "_Flipped";
		return flippedSprite;
	}
}
