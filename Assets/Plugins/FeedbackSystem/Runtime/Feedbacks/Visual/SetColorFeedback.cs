using System;

using UnityEngine;

namespace FeedbackSystem
{
	[Serializable]
	[FeedbackInfo( "Visual/Set Color" )]
	public class SetColorFeedback : Feedback
	{
		public Renderer Renderer;
		public Color Color = Color.white;
		public string PropertyName = "_BaseColor";

		MaterialPropertyBlock _block;
		int _propertyId;
		int _baseColorId;
		int _colorId;

		public override void Initialize()
		{
			_block = new MaterialPropertyBlock();
			_baseColorId = Shader.PropertyToID( "_BaseColor" );
			_colorId = Shader.PropertyToID( "_Color" );
			_propertyId = string.IsNullOrEmpty( PropertyName ) ? 0 : Shader.PropertyToID( PropertyName );
		}

		public override void Play()
		{
			if ( Renderer == null )
				return;

			if ( _block == null )
				Initialize();

			Renderer.GetPropertyBlock( _block );

			if ( !string.IsNullOrEmpty( PropertyName ) )
			{
				_block.SetColor( _propertyId, Color );
			}
			else
			{
				Material sharedMaterial = Renderer.sharedMaterial;
				if ( sharedMaterial != null && sharedMaterial.HasProperty( _baseColorId ) )
					_block.SetColor( _baseColorId, Color );
				else
					_block.SetColor( _colorId, Color );
			}

			Renderer.SetPropertyBlock( _block );
		}
	}
}
