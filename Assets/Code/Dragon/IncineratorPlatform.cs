using System.Collections;
using System.Collections.Generic;

using FeedbackSystem;

using UnityEngine;

/// <summary>
/// Junk disposal platform: stages items on a MixedDisplay grid, then asks the dragon
/// to incinerate them with fire breath when the player is clear of the safe radius.
/// </summary>
[DisallowMultipleComponent]
public class IncineratorPlatform : MonoBehaviour
{
	enum Phase
	{
		Idle,
		AwaitClear,
		Burning,
		Cooling
	}

	static readonly int EmissionColorId = Shader.PropertyToID( "_EmissionColor" );

	[Header( "References" )]
	[SerializeField] MixedDisplayTableInteractable _storage;
	[SerializeField] DragonController _dragon;
	[SerializeField] Renderer _scorchRenderer;
	[SerializeField] Transform _burnAimPoint;

	[Header( "Distances" )]
	[SerializeField] float _safeDistance = 10f;
	[SerializeField] float _pushStrength = 18f;
	[SerializeField] float _pushMaxSpeed = 8f;

	[Header( "Timing" )]
	[SerializeField] float _lookLeadTime = 0.35f;
	[SerializeField] float _breathDuration = 2.2f;
	[SerializeField] float _itemBurnDuration = 1.1f;
	[SerializeField] float _heatDuration = 0.45f;
	[SerializeField] float _coolDuration = 1.6f;
	[SerializeField] float _spellEndPadding = 0.35f;

	[Header( "Scorch" )]
	[SerializeField] Color _scorchHotColor = new Color( 2.2f, 0.55f, 0.08f, 1f );
	[SerializeField] Color _scorchLingerColor = new Color( 0.35f, 0.08f, 0.02f, 1f );
	[SerializeField] Color _scorchColdColor = Color.black;

	[Header( "Item Burn" )]
	[SerializeField] Color _itemHotColor = new Color( 3f, 0.9f, 0.15f, 1f );

	[Header( "Feedbacks" )]
	[SerializeField] Feedbacks _onBurnStart;
	[SerializeField] Feedbacks _onBreath;
	[SerializeField] Feedbacks _onItemBurn;
	[SerializeField] Feedbacks _onBurnEnd;

	readonly List<TreasureItem> _burnItems = new List<TreasureItem>( 16 );
	readonly List<Vector3> _burnBaseScales = new List<Vector3>( 16 );
	MaterialPropertyBlock _scorchBlock;
	MaterialPropertyBlock _itemBlock;
	Phase _phase = Phase.Idle;
	Coroutine _burnRoutine;

	public bool IsBurning => _phase == Phase.Burning || _phase == Phase.Cooling;

	void Reset()
	{
		_safeDistance = 10f;
		_storage = GetComponentInChildren<MixedDisplayTableInteractable>( true );
	}

	void Awake()
	{
		if ( _storage == null )
			_storage = GetComponentInChildren<MixedDisplayTableInteractable>( true );
		if ( _burnAimPoint == null )
			_burnAimPoint = transform;
		if ( _scorchBlock == null )
			_scorchBlock = new MaterialPropertyBlock();
		if ( _itemBlock == null )
			_itemBlock = new MaterialPropertyBlock();

		ApplyScorchEmission( _scorchColdColor );
	}

	void OnDisable()
	{
		if ( _burnRoutine != null )
		{
			StopCoroutine( _burnRoutine );
			_burnRoutine = null;
		}

		if ( _storage != null )
			_storage.SetBurnLocked( false );

		if ( _dragon != null && _dragon.IsCasting )
			_dragon.EndSpellCast();

		DragonFireBreathVFX fireBreath = ResolveFireBreath();
		if ( fireBreath != null )
			fireBreath.StopBreath();
	}

	void Update()
	{
		if ( _phase == Phase.Burning || _phase == Phase.Cooling )
			PushPlayerIfTooClose();

		if ( _phase != Phase.Idle && _phase != Phase.AwaitClear )
			return;

		if ( _storage == null || _storage.ItemCount <= 0 )
		{
			_phase = Phase.Idle;
			return;
		}

		_phase = Phase.AwaitClear;
		if ( !IsPlayerClear() )
			return;

		BeginBurn();
	}

	void BeginBurn()
	{
		if ( _burnRoutine != null )
			return;

		_phase = Phase.Burning;
		_burnRoutine = StartCoroutine( BurnSequence() );
	}

	IEnumerator BurnSequence()
	{
		if ( _storage != null )
			_storage.SetBurnLocked( true );

		PlayFeedback( _onBurnStart );

		Transform aim = _burnAimPoint != null ? _burnAimPoint : transform;
		if ( _dragon != null )
			_dragon.BeginSpellCast( aim );

		if ( _lookLeadTime > 0f )
			yield return new WaitForSeconds( _lookLeadTime );

		Vector3 targetPoint = aim.position;
		DragonFireBreathVFX fireBreath = ResolveFireBreath();
		if ( fireBreath != null )
			fireBreath.PlayToward( targetPoint );

		PlayFeedback( _onBreath );

		float elapsed = 0f;
		float total = Mathf.Max( _breathDuration, _itemBurnDuration + _heatDuration * 0.5f );
		bool itemsStarted = false;
		Coroutine itemBurn = null;

		while ( elapsed < total )
		{
			float dt = Time.deltaTime;
			elapsed += dt;

			float heatU = Mathf.Clamp01( elapsed / Mathf.Max( 0.01f, _heatDuration ) );
			ApplyScorchEmission( Color.Lerp( _scorchColdColor, _scorchHotColor, heatU ) );

			if ( fireBreath != null )
				fireBreath.UpdateTarget( aim.position );

			if ( !itemsStarted && elapsed >= _heatDuration * 0.5f )
			{
				itemsStarted = true;
				itemBurn = StartCoroutine( BurnItemsRoutine() );
			}

			yield return null;
		}

		if ( !itemsStarted )
			yield return StartCoroutine( BurnItemsRoutine() );
		else if ( itemBurn != null )
			yield return itemBurn;

		if ( fireBreath != null )
			fireBreath.StopBreath();

		if ( _dragon != null )
			_dragon.EndSpellCast();

		if ( _spellEndPadding > 0f )
			yield return new WaitForSeconds( _spellEndPadding );

		_phase = Phase.Cooling;
		PlayFeedback( _onBurnEnd );

		float coolT = 0f;
		while ( coolT < 1f )
		{
			coolT += Time.deltaTime / Mathf.Max( 0.01f, _coolDuration );
			float u = Mathf.Clamp01( coolT );
			ApplyScorchEmission( Color.Lerp( _scorchHotColor, _scorchLingerColor, u ) );
			yield return null;
		}

		ApplyScorchEmission( _scorchLingerColor );

		if ( _storage != null )
			_storage.SetBurnLocked( false );

		_burnRoutine = null;
		_phase = _storage != null && _storage.ItemCount > 0 ? Phase.AwaitClear : Phase.Idle;
	}

	IEnumerator BurnItemsRoutine()
	{
		_burnItems.Clear();
		_burnBaseScales.Clear();
		if ( _storage == null )
			yield break;

		_storage.CopyDisplayedItems( _burnItems );
		if ( _burnItems.Count == 0 )
			yield break;

		PlayFeedback( _onItemBurn );

		for ( int i = 0; i < _burnItems.Count; i++ )
		{
			TreasureItem item = _burnItems[ i ];
			_burnBaseScales.Add( item != null ? item.transform.localScale : Vector3.one );
		}

		float t = 0f;
		while ( t < _itemBurnDuration )
		{
			t += Time.deltaTime;
			float u = Mathf.Clamp01( t / Mathf.Max( 0.01f, _itemBurnDuration ) );
			float glowU = Mathf.Clamp01( u * 1.4f );
			float scaleU = Mathf.SmoothStep( 0f, 1f, Mathf.Clamp01( ( u - 0.2f ) / 0.8f ) );

			for ( int i = 0; i < _burnItems.Count; i++ )
			{
				TreasureItem item = _burnItems[ i ];
				if ( item == null )
					continue;

				ApplyItemBurnVisual( item, Color.Lerp( Color.black, _itemHotColor, glowU ), Vector3.Lerp( _burnBaseScales[ i ], Vector3.zero, scaleU ) );
			}

			yield return null;
		}

		for ( int i = 0; i < _burnItems.Count; i++ )
		{
			TreasureItem item = _burnItems[ i ];
			if ( item == null )
				continue;

			_storage.Remove( item );
			TreasureItemFactory.Despawn( item );
		}

		_burnItems.Clear();
		_burnBaseScales.Clear();
	}

	void PushPlayerIfTooClose()
	{
		if ( GameMode.Instance == null )
			return;

		PlayerController player = GameMode.Instance.Player;
		if ( player == null )
			return;

		Vector3 platformPos = transform.position;
		Vector3 playerPos = player.transform.position;
		Vector3 flat = playerPos - platformPos;
		flat.y = 0f;
		float dist = flat.magnitude;
		float safe = Mathf.Max( 0.5f, _safeDistance );
		if ( dist >= safe )
			return;

		Vector3 dir = dist > 0.001f ? flat / dist : -Flatten( player.transform.forward );
		float penetration = 1f - ( dist / safe );
		float push = _pushStrength * penetration;
		Vector3 planar = player.PlanarVelocity;
		float outwardSpeed = Vector3.Dot( planar, dir );
		if ( outwardSpeed < _pushMaxSpeed )
			player.AddPlanarVelocity( dir * ( push * Time.deltaTime ) );
	}

	bool IsPlayerClear()
	{
		if ( GameMode.Instance == null )
			return true;

		PlayerController player = GameMode.Instance.Player;
		if ( player == null )
			return true;

		Vector3 flat = player.transform.position - transform.position;
		flat.y = 0f;
		return flat.magnitude >= Mathf.Max( 0.5f, _safeDistance );
	}

	void ApplyScorchEmission( Color emission )
	{
		if ( _scorchRenderer == null )
			return;

		if ( _scorchBlock == null )
			_scorchBlock = new MaterialPropertyBlock();

		_scorchRenderer.GetPropertyBlock( _scorchBlock );
		_scorchBlock.SetColor( EmissionColorId, emission );
		_scorchRenderer.SetPropertyBlock( _scorchBlock );
	}

	void ApplyItemBurnVisual( TreasureItem item, Color emission, Vector3 localScale )
	{
		if ( item == null )
			return;

		item.transform.localScale = localScale;

		Renderer[] renderers = item.GetComponentsInChildren<Renderer>( true );
		if ( renderers == null || renderers.Length == 0 )
			return;

		if ( _itemBlock == null )
			_itemBlock = new MaterialPropertyBlock();

		for ( int i = 0; i < renderers.Length; i++ )
		{
			Renderer renderer = renderers[ i ];
			if ( renderer == null )
				continue;

			renderer.GetPropertyBlock( _itemBlock );
			_itemBlock.SetColor( EmissionColorId, emission );
			renderer.SetPropertyBlock( _itemBlock );
		}
	}

	DragonFireBreathVFX ResolveFireBreath()
	{
		if ( _dragon == null )
			return null;
		return _dragon.FireBreath;
	}

	static void PlayFeedback( Feedbacks feedbacks )
	{
		if ( feedbacks == null )
			return;
		feedbacks.Play();
	}

	static Vector3 Flatten( Vector3 v )
	{
		v.y = 0f;
		if ( v.sqrMagnitude < 0.0001f )
			return Vector3.forward;
		return v.normalized;
	}

#if UNITY_EDITOR
	void OnDrawGizmosSelected()
	{
		Gizmos.color = new Color( 1f, 0.4f, 0.1f, 0.35f );
		Gizmos.DrawWireSphere( transform.position, Mathf.Max( 0.5f, _safeDistance ) );
	}

	public void EditorAssign(
		MixedDisplayTableInteractable storage,
		DragonController dragon,
		Renderer scorchRenderer,
		Transform burnAimPoint,
		Feedbacks onBurnStart,
		Feedbacks onBreath,
		Feedbacks onItemBurn,
		Feedbacks onBurnEnd )
	{
		_storage = storage;
		_dragon = dragon;
		_scorchRenderer = scorchRenderer;
		_burnAimPoint = burnAimPoint;
		_onBurnStart = onBurnStart;
		_onBreath = onBreath;
		_onItemBurn = onItemBurn;
		_onBurnEnd = onBurnEnd;
	}
#endif
}
