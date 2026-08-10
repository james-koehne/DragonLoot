using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Registers this object's renderers for depth-tested sparkle-mask redraw.
/// </summary>
[DisallowMultipleComponent]
public sealed class TreasureSparkleMaskContributor : MonoBehaviour
{
	static readonly List<Renderer> Scratch = new List<Renderer>( 16 );

	[SerializeField]
	TreasureSparkleDefinition.SparkleSourceKind kind = TreasureSparkleDefinition.SparkleSourceKind.Artifact;

	Renderer[] _renderers;
	bool _registered;

	public TreasureSparkleDefinition.SparkleSourceKind Kind
	{
		get => kind;
		set => kind = value;
	}

	void OnEnable()
	{
		RefreshRegistration();
	}

	void OnDisable()
	{
		Unregister();
	}

	void OnDestroy()
	{
		Unregister();
	}

	public void SetKind( TreasureSparkleDefinition.SparkleSourceKind sourceKind )
	{
		kind = sourceKind;
		if ( isActiveAndEnabled )
			RefreshRegistration();
	}

	public void RefreshRegistration()
	{
		Unregister();

		EnsureRenderers();
		Scratch.Clear();
		for ( int i = 0; i < _renderers.Length; i++ )
		{
			Renderer renderer = _renderers[ i ];
			// Register even when disabled — CollectEntries filters by enabled each frame.
			// Skipping here leaves stacks unmasked after SetStack re-enables the mesh.
			if ( renderer == null )
				continue;

			Scratch.Add( renderer );
		}

		if ( Scratch.Count == 0 )
			return;

		TreasureSparkleMaskRegistrar.Register( Scratch, kind );
		_registered = true;
	}

	void Unregister()
	{
		if ( !_registered )
			return;

		EnsureRenderers();
		TreasureSparkleMaskRegistrar.Unregister( _renderers );
		_registered = false;
	}

	void EnsureRenderers()
	{
		if ( _renderers != null )
			return;

		_renderers = GetComponentsInChildren<Renderer>( true );
	}

	/// <summary>Call after runtime mesh/renderer creation.</summary>
	public void InvalidateRendererCache()
	{
		_renderers = null;
		if ( isActiveAndEnabled )
			RefreshRegistration();
	}
}
