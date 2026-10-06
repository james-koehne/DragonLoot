using System.Collections;
using System.Collections.Generic;

using FeedbackSystem;

using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Mid-screen "pouch is full" hint with a scale pop on each rejected pickup attempt.
/// Self-builds under the Interface canvas when missing from the prefab.
/// </summary>
public class PouchFullHintUI : MonoBehaviour
{
	public static PouchFullHintUI Instance { get; private set; }

	[SerializeField] CanvasGroup group;
	[SerializeField] Text label;
	[SerializeField] RectTransform punchTarget;
	[SerializeField] Feedbacks showFeedback;
	[SerializeField] float holdDuration = 0.85f;
	[SerializeField] float fadeOutDuration = 0.25f;

	bool _ready;
	Coroutine _hideRoutine;
	Vector3 _baseScale = Vector3.one;

	public void Setup()
	{
		Instance = this;
		EnsureUi();
		HideImmediate();
		_ready = true;
	}

	void Awake()
	{
		Instance = this;
	}

	void OnEnable()
	{
		Instance = this;
		if ( !_ready )
			Setup();
	}

	void OnDestroy()
	{
		if ( Instance == this )
			Instance = null;
	}

	/// <summary>Show / refresh the mid-screen hint and punch scale.</summary>
	public static void Notify( string message )
	{
		if ( string.IsNullOrEmpty( message ) )
			return;

		if ( Instance == null )
			TryCreateOnInterface();

		if ( Instance != null )
			Instance.Show( message );
	}

	static void TryCreateOnInterface()
	{
		Transform parent = null;
		if ( PouchBarUI.Instance != null )
			parent = PouchBarUI.Instance.transform.parent;
		else if ( DiscoveryToastUI.Instance != null )
			parent = DiscoveryToastUI.Instance.transform.parent;

		if ( parent == null )
			return;

		Transform existing = parent.Find( "PouchFullHint" );
		GameObject go = existing != null
			? existing.gameObject
			: new GameObject( "PouchFullHint", typeof( RectTransform ), typeof( CanvasGroup ), typeof( PouchFullHintUI ) );
		if ( existing == null )
			go.transform.SetParent( parent, false );

		PouchFullHintUI hint = go.GetComponent<PouchFullHintUI>();
		if ( hint == null )
			hint = go.AddComponent<PouchFullHintUI>();
		hint.Setup();
	}

	void Show( string message )
	{
		if ( !_ready )
			Setup();

		EnsureUi();
		if ( label != null )
			label.text = message;

		if ( group != null )
		{
			group.alpha = 1f;
			group.blocksRaycasts = false;
			group.interactable = false;
		}

		if ( punchTarget != null )
			punchTarget.localScale = _baseScale;

		if ( showFeedback != null )
			showFeedback.Play();

		if ( _hideRoutine != null )
			StopCoroutine( _hideRoutine );
		_hideRoutine = StartCoroutine( HideAfterHold() );
	}

	IEnumerator HideAfterHold()
	{
		float hold = Mathf.Max( 0.05f, holdDuration );
		yield return new WaitForSecondsRealtime( hold );

		float fade = Mathf.Max( 0.01f, fadeOutDuration );
		float elapsed = 0f;
		float startAlpha = group != null ? group.alpha : 1f;
		while ( elapsed < fade )
		{
			elapsed += Time.unscaledDeltaTime;
			float u = Mathf.Clamp01( elapsed / fade );
			if ( group != null )
				group.alpha = Mathf.Lerp( startAlpha, 0f, u );
			yield return null;
		}

		HideImmediate();
		_hideRoutine = null;
	}

	void HideImmediate()
	{
		if ( group != null )
		{
			group.alpha = 0f;
			group.blocksRaycasts = false;
			group.interactable = false;
		}

		if ( punchTarget != null )
			punchTarget.localScale = _baseScale;
	}

	void EnsureUi()
	{
		RectTransform root = transform as RectTransform;
		if ( root == null )
			root = gameObject.AddComponent<RectTransform>();

		root.anchorMin = new Vector2( 0.5f, 0.5f );
		root.anchorMax = new Vector2( 0.5f, 0.5f );
		root.pivot = new Vector2( 0.5f, 0.5f );
		root.anchoredPosition = new Vector2( 0f, 36f );
		root.sizeDelta = new Vector2( 520f, 64f );

		if ( group == null )
			group = GetComponent<CanvasGroup>();
		if ( group == null )
			group = gameObject.AddComponent<CanvasGroup>();

		if ( punchTarget == null )
			punchTarget = root;

		_baseScale = punchTarget.localScale;

		if ( label == null )
		{
			Transform existing = transform.Find( "Label" );
			GameObject labelGo = existing != null
				? existing.gameObject
				: new GameObject( "Label", typeof( RectTransform ), typeof( Text ) );
			if ( existing == null )
				labelGo.transform.SetParent( transform, false );

			RectTransform labelRect = labelGo.transform as RectTransform;
			labelRect.anchorMin = Vector2.zero;
			labelRect.anchorMax = Vector2.one;
			labelRect.offsetMin = Vector2.zero;
			labelRect.offsetMax = Vector2.zero;

			label = labelGo.GetComponent<Text>();
			label.alignment = TextAnchor.MiddleCenter;
			label.fontSize = 28;
			label.color = Color.white;
			label.raycastTarget = false;
			label.horizontalOverflow = HorizontalWrapMode.Wrap;
			label.verticalOverflow = VerticalWrapMode.Overflow;

			Font font = Resources.GetBuiltinResource<Font>( "LegacyRuntime.ttf" );
			if ( font == null )
				font = Resources.GetBuiltinResource<Font>( "Arial.ttf" );
			if ( font != null )
				label.font = font;

			Outline outline = labelGo.GetComponent<Outline>();
			if ( outline == null )
				outline = labelGo.AddComponent<Outline>();
			outline.effectColor = new Color( 0f, 0f, 0f, 0.85f );
			outline.effectDistance = new Vector2( 1.5f, -1.5f );
		}

		if ( showFeedback == null )
		{
			showFeedback = GetComponent<Feedbacks>();
			if ( showFeedback == null )
				showFeedback = gameObject.AddComponent<Feedbacks>();
		}

		EnsurePunchFeedback();
	}

	void EnsurePunchFeedback()
	{
		if ( showFeedback == null || punchTarget == null )
			return;

		UiPunchScaleFeedback punch = null;
		List<Feedback> list = showFeedback.FeedbackList;
		if ( list != null )
		{
			for ( int i = 0; i < list.Count; i++ )
			{
				Feedback entry = list[ i ];
				if ( entry is UiPunchScaleFeedback existing )
				{
					punch = existing;
					break;
				}
			}
		}

		if ( punch == null )
		{
			punch = new UiPunchScaleFeedback();
			showFeedback.AddFeedback( punch );
		}

		punch.Target = punchTarget;
		punch.Punch = new Vector3( 0.18f, 0.18f, 0f );
		punch.Duration = 0.28f;
		punch.UseUnscaledTime = true;
	}
}
