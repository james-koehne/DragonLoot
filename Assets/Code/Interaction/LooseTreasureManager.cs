using System.Collections;
using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Tracks loose (Physics) treasure with category caps and reclaim sink toward origin piles.
/// </summary>
public class LooseTreasureManager : MonoBehaviour
{
	static LooseTreasureManager _instance;
	static readonly List<TreasureItem> Loose = new List<TreasureItem>();

	[SerializeField]
	[Min( 1 )]
	int maxLooseCoins = 300;

	[SerializeField]
	[Min( 1 )]
	int maxLooseGems = 100;

	[SerializeField]
	[Min( 1 )]
	int maxLooseOther = 50;

	[SerializeField]
	[Min( 0.05f )]
	float reclaimDuration = 0.35f;

	public static void EnsureExists()
	{
		if ( _instance != null )
			return;

		GameObject go = new GameObject( "LooseTreasureManager" );
		_instance = go.AddComponent<LooseTreasureManager>();
		Object.DontDestroyOnLoad( go );
	}

	public static void Register( TreasureItem item )
	{
		if ( item == null )
			return;

		EnsureExists();
		if ( !Loose.Contains( item ) )
			Loose.Add( item );

		_instance.EnforceCaps();
	}

	public static void Unregister( TreasureItem item )
	{
		if ( item == null )
			return;

		Loose.Remove( item );
	}

	void Awake()
	{
		if ( _instance != null && _instance != this )
		{
			Destroy( gameObject );
			return;
		}

		_instance = this;
	}

	void EnforceCaps()
	{
		EnforceCategory( TreasureCategory.Coin, maxLooseCoins );
		EnforceCategory( TreasureCategory.Gem, maxLooseGems );
		EnforceOther( maxLooseOther );
	}

	void EnforceCategory( TreasureCategory category, int max )
	{
		int count = CountCategory( category );
		while ( count > max )
		{
			TreasureItem victim = FindReclaimCandidate( category );
			if ( victim == null )
				break;

			StartCoroutine( ReclaimRoutine( victim ) );
			count--;
		}
	}

	void EnforceOther( int max )
	{
		int count = CountOther();
		while ( count > max )
		{
			TreasureItem victim = FindReclaimCandidateOther();
			if ( victim == null )
				break;

			StartCoroutine( ReclaimRoutine( victim ) );
			count--;
		}
	}

	int CountCategory( TreasureCategory category )
	{
		int n = 0;
		for ( int i = 0; i < Loose.Count; i++ )
		{
			TreasureItem item = Loose[ i ];
			if ( item == null || item.IsReclaiming || !item.IsWorldLoose )
				continue;
			if ( item.Definition != null && item.Definition.category == category )
				n++;
		}

		return n;
	}

	int CountOther()
	{
		int n = 0;
		for ( int i = 0; i < Loose.Count; i++ )
		{
			TreasureItem item = Loose[ i ];
			if ( item == null || item.IsReclaiming || !item.IsWorldLoose )
				continue;
			if ( item.Definition == null )
			{
				n++;
				continue;
			}

			TreasureCategory cat = item.Definition.category;
			if ( cat != TreasureCategory.Coin && cat != TreasureCategory.Gem )
				n++;
		}

		return n;
	}

	TreasureItem FindReclaimCandidate( TreasureCategory category )
	{
		TreasureItem best = null;
		float bestDistSq = float.MaxValue;

		for ( int i = 0; i < Loose.Count; i++ )
		{
			TreasureItem item = Loose[ i ];
			if ( item == null || item.IsReclaiming )
				continue;
			if ( !item.IsWorldLoose )
				continue;
			if ( item.Definition == null || item.Definition.category != category )
				continue;
			if ( item.Body != null && !item.Body.IsSleeping() )
				continue;

			TreasurePileVisual origin = item.OriginPile;
			float distSq = origin != null
				? ( item.transform.position - origin.transform.position ).sqrMagnitude
				: float.MaxValue * 0.5f;

			if ( distSq < bestDistSq )
			{
				bestDistSq = distSq;
				best = item;
			}
		}

		return best;
	}

	TreasureItem FindReclaimCandidateOther()
	{
		TreasureItem best = null;
		float bestDistSq = float.MaxValue;

		for ( int i = 0; i < Loose.Count; i++ )
		{
			TreasureItem item = Loose[ i ];
			if ( item == null || item.IsReclaiming )
				continue;
			if ( !item.IsWorldLoose )
				continue;
			if ( item.Definition != null )
			{
				TreasureCategory cat = item.Definition.category;
				if ( cat == TreasureCategory.Coin || cat == TreasureCategory.Gem )
					continue;
			}

			if ( item.Body != null && !item.Body.IsSleeping() )
				continue;

			TreasurePileVisual origin = item.OriginPile;
			float distSq = origin != null
				? ( item.transform.position - origin.transform.position ).sqrMagnitude
				: float.MaxValue * 0.5f;

			if ( distSq < bestDistSq )
			{
				bestDistSq = distSq;
				best = item;
			}
		}

		return best;
	}

	IEnumerator ReclaimRoutine( TreasureItem item )
	{
		if ( item == null || item.IsReclaiming )
			yield break;

		item.SetReclaiming( true );
		Unregister( item );
		TreasureProximitySleep.Unregister( item );

		Vector3 start = item.transform.position;
		Vector3 end = start;
		TreasurePileVisual origin = item.OriginPile;
		if ( origin != null )
			end = origin.transform.position;
		else
			end = start + Vector3.down * 0.5f;

		Vector3 startScale = item.transform.localScale;
		float duration = Mathf.Max( 0.05f, reclaimDuration );
		float elapsed = 0f;

		if ( item.Body != null )
		{
			item.Body.isKinematic = true;
			item.Body.detectCollisions = false;
		}

		while ( elapsed < duration )
		{
			if ( item == null )
				yield break;

			elapsed += Time.deltaTime;
			float u = Mathf.Clamp01( elapsed / duration );
			u = u * u * ( 3f - 2f * u );
			item.transform.position = Vector3.Lerp( start, end, u );
			item.transform.localScale = Vector3.Lerp( startScale, Vector3.zero, u );
			yield return null;
		}

		if ( item != null )
			TreasureItemFactory.Despawn( item );
	}

	void OnDestroy()
	{
		if ( _instance == this )
			_instance = null;
	}
}
