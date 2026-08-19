using System.Collections.Generic;

using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Game HUD for section treasure counters. Refreshes only on EventBus value changes.
/// </summary>
public class TreasureCounterHUD : MonoBehaviour
{
	readonly Dictionary<string, TreasureCounterRowUI> _rows = new Dictionary<string, TreasureCounterRowUI>();

	RectTransform _panel;
	RectTransform _rowRoot;
	Text _sectionTitle;
	Text _sectionProgress;
	Text _sectionPercent;
	AudioSource _audio;
	bool _subscribed;

	[SerializeField]
	AudioClip categoryCompleteClip;

	public void Setup()
	{
		EnsureUi();
		Subscribe();
		RebuildAll( false );
	}

	void OnEnable()
	{
		Subscribe();
	}

	void OnDisable()
	{
		Unsubscribe();
	}

	void OnDestroy()
	{
		Unsubscribe();
	}

	void Subscribe()
	{
		if ( _subscribed )
			return;

		EventBus.Subscribe<TreasureCounterChangedEvent>( OnCounterChanged );
		EventBus.Subscribe<CategoryCompletedEvent>( OnCategoryCompleted );
		EventBus.Subscribe<CategoryReopenedEvent>( OnCategoryReopened );
		EventBus.Subscribe<SectionCompletedEvent>( OnSectionCompleted );
		_subscribed = true;
	}

	void Unsubscribe()
	{
		if ( !_subscribed )
			return;

		EventBus.Unsubscribe<TreasureCounterChangedEvent>( OnCounterChanged );
		EventBus.Unsubscribe<CategoryCompletedEvent>( OnCategoryCompleted );
		EventBus.Unsubscribe<CategoryReopenedEvent>( OnCategoryReopened );
		EventBus.Unsubscribe<SectionCompletedEvent>( OnSectionCompleted );
		_subscribed = false;
	}

	void OnCounterChanged( TreasureCounterChangedEvent evt )
	{
		if ( evt.Entry == null || evt.Entry.Total <= 0 )
			return;

		ApplyEntry( evt.Entry, false );
		RefreshSectionHeader();
	}

	void OnCategoryCompleted( CategoryCompletedEvent evt )
	{
		TreasureCounterManager manager = TreasureCounterManager.Instance;
		if ( manager != null && manager.TryGetEntry( evt.Id, out TreasureCounterEntry entry ) )
			ApplyEntry( entry, true );

		RefreshSectionHeader();
		PlayCompleteSound();
	}

	void OnCategoryReopened( CategoryReopenedEvent evt )
	{
		TreasureCounterManager manager = TreasureCounterManager.Instance;
		if ( manager != null && manager.TryGetEntry( evt.Id, out TreasureCounterEntry entry ) )
			ApplyEntry( entry, false );

		RefreshSectionHeader();
	}

	void OnSectionCompleted( SectionCompletedEvent evt )
	{
		RefreshSectionHeader();
		PlayCompleteSound();
	}

	void RebuildAll( bool flash )
	{
		TreasureCounterManager manager = TreasureCounterManager.Instance;
		if ( manager == null )
		{
			RefreshSectionHeader();
			return;
		}

		IReadOnlyList<TreasureCounterEntry> entries = manager.Entries;
		HashSet<string> keep = new HashSet<string>();

		for ( int i = 0; i < entries.Count; i++ )
		{
			TreasureCounterEntry entry = entries[ i ];
			if ( entry == null || entry.Total <= 0 )
				continue;

			keep.Add( entry.Id );
			ApplyEntry( entry, flash );
		}

		List<string> remove = null;
		foreach ( KeyValuePair<string, TreasureCounterRowUI> pair in _rows )
		{
			if ( keep.Contains( pair.Key ) )
				continue;

			if ( remove == null )
				remove = new List<string>();
			remove.Add( pair.Key );
		}

		if ( remove != null )
		{
			for ( int i = 0; i < remove.Count; i++ )
			{
				string id = remove[ i ];
				if ( _rows.TryGetValue( id, out TreasureCounterRowUI row ) && row != null )
					Destroy( row.gameObject );
				_rows.Remove( id );
			}
		}

		RefreshSectionHeader();
	}

	void ApplyEntry( TreasureCounterEntry entry, bool flash )
	{
		if ( entry == null || entry.Total <= 0 )
			return;

		EnsureUi();

		if ( !_rows.TryGetValue( entry.Id, out TreasureCounterRowUI row ) || row == null )
		{
			GameObject rowGo = new GameObject( "Row_" + entry.Id, typeof( RectTransform ) );
			rowGo.transform.SetParent( _rowRoot, false );
			row = rowGo.AddComponent<TreasureCounterRowUI>();
			row.Build();
			_rows[ entry.Id ] = row;
		}

		row.Bind( entry, flash );
	}

	void RefreshSectionHeader()
	{
		EnsureUi();

		TreasureCounterManager manager = TreasureCounterManager.Instance;
		if ( manager == null )
		{
			if ( _sectionTitle != null )
				_sectionTitle.text = "Section";
			if ( _sectionProgress != null )
				_sectionProgress.text = "0/0 cats";
			if ( _sectionPercent != null )
				_sectionPercent.text = "0%";
			return;
		}

		if ( _sectionTitle != null )
			_sectionTitle.text = manager.SectionId;

		if ( _sectionProgress != null )
			_sectionProgress.text = manager.CategoriesCompleted + "/" + manager.TotalCategories + " cats";

		if ( _sectionPercent != null )
			_sectionPercent.text = manager.OverallCompletionPercent + "%";
	}

	void PlayCompleteSound()
	{
		if ( _audio == null || categoryCompleteClip == null )
			return;
		if ( !AudioMaster.IsChannelEnabled( AudioChannel.Fx ) )
			return;

		_audio.PlayOneShot( categoryCompleteClip );
	}

	void EnsureUi()
	{
		if ( _panel != null )
			return;

		_audio = gameObject.GetComponent<AudioSource>();
		if ( _audio == null )
			_audio = gameObject.AddComponent<AudioSource>();
		_audio.playOnAwake = false;

		GameObject panelGo = new GameObject( "TreasureCounterPanel", typeof( RectTransform ), typeof( CanvasRenderer ), typeof( Image ), typeof( VerticalLayoutGroup ), typeof( ContentSizeFitter ) );
		panelGo.transform.SetParent( transform, false );

		_panel = panelGo.GetComponent<RectTransform>();
		_panel.anchorMin = new Vector2( 0f, 1f );
		_panel.anchorMax = new Vector2( 0f, 1f );
		_panel.pivot = new Vector2( 0f, 1f );
		_panel.anchoredPosition = new Vector2( 12f, -16f );
		_panel.sizeDelta = new Vector2( 296f, 128f );

		Image panelBg = panelGo.GetComponent<Image>();
		panelBg.color = new Color( 0f, 0f, 0f, 0.28f );
		panelBg.raycastTarget = false;

		VerticalLayoutGroup layout = panelGo.GetComponent<VerticalLayoutGroup>();
		layout.padding = new RectOffset( 8, 8, 8, 8 );
		layout.spacing = 4f;
		layout.childAlignment = TextAnchor.UpperLeft;
		layout.childControlHeight = false;
		layout.childControlWidth = true;
		layout.childForceExpandHeight = false;
		layout.childForceExpandWidth = true;

		ContentSizeFitter fitter = panelGo.GetComponent<ContentSizeFitter>();
		fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
		fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

		_sectionTitle = CreateHeaderText( "SectionTitle", 22, FontStyle.Bold );
		_sectionProgress = CreateHeaderText( "SectionProgress", 18, FontStyle.Normal );
		_sectionPercent = CreateHeaderText( "SectionPercent", 18, FontStyle.Normal );

		GameObject rowRootGo = new GameObject( "Rows", typeof( RectTransform ), typeof( VerticalLayoutGroup ), typeof( ContentSizeFitter ) );
		rowRootGo.transform.SetParent( _panel, false );
		_rowRoot = rowRootGo.GetComponent<RectTransform>();

		VerticalLayoutGroup rowLayout = rowRootGo.GetComponent<VerticalLayoutGroup>();
		rowLayout.spacing = 4f;
		rowLayout.childAlignment = TextAnchor.UpperLeft;
		rowLayout.childControlHeight = false;
		rowLayout.childControlWidth = true;
		rowLayout.childForceExpandHeight = false;
		rowLayout.childForceExpandWidth = true;

		ContentSizeFitter rowFitter = rowRootGo.GetComponent<ContentSizeFitter>();
		rowFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
		rowFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
	}

	Text CreateHeaderText( string name, int fontSize, FontStyle style )
	{
		GameObject go = new GameObject( name, typeof( RectTransform ), typeof( CanvasRenderer ), typeof( Text ), typeof( LayoutElement ) );
		go.transform.SetParent( _panel, false );

		LayoutElement layoutElement = go.GetComponent<LayoutElement>();
		layoutElement.minHeight = fontSize + 2;
		layoutElement.preferredHeight = fontSize + 3;

		Text text = go.GetComponent<Text>();
		text.font = Resources.GetBuiltinResource<Font>( "LegacyRuntime.ttf" );
		if ( text.font == null )
			text.font = Resources.GetBuiltinResource<Font>( "Arial.ttf" );
		text.fontSize = fontSize;
		text.fontStyle = style;
		text.alignment = TextAnchor.MiddleLeft;
		text.color = new Color( 1f, 1f, 1f, 0.9f );
		text.raycastTarget = false;
		text.horizontalOverflow = HorizontalWrapMode.Overflow;
		text.verticalOverflow = VerticalWrapMode.Overflow;
		return text;
	}
}
