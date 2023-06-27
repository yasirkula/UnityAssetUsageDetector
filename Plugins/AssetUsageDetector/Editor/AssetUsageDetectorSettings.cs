using UnityEditor;
using UnityEngine;

namespace AssetUsageDetectorNamespace
{
	public static class AssetUsageDetectorSettings
	{
		private static readonly GUILayoutOption GL_WIDTH_60 = GUILayout.Width( 60f );

		#region Colors
		private static Color? m_settingsHeaderColor = null;
		public static Color SettingsHeaderColor
		{
			get { if( m_settingsHeaderColor == null ) m_settingsHeaderColor = GetColor( "AUD_SettingsHeaderTint", Color.cyan ); return m_settingsHeaderColor.Value; }
			set { if( m_settingsHeaderColor == value ) return; m_settingsHeaderColor = value; SetColor( "AUD_SettingsHeaderTint", value ); }
		}

		private static Color? m_searchResultGroupHeaderColor = null;
		public static Color SearchResultGroupHeaderColor
		{
			get { if( m_searchResultGroupHeaderColor == null ) m_searchResultGroupHeaderColor = GetColor( "AUD_ResultGroupHeaderTint", Color.cyan ); return m_searchResultGroupHeaderColor.Value; }
			set { if( m_searchResultGroupHeaderColor == value ) return; m_searchResultGroupHeaderColor = value; SetColor( "AUD_ResultGroupHeaderTint", value ); }
		}

		private static Color? m_rootRowsBackgroundColor = null;
		public static Color RootRowsBackgroundColor
		{
			get { if( m_rootRowsBackgroundColor == null ) m_rootRowsBackgroundColor = GetColor( "AUD_RootRowsTint", EditorGUIUtility.isProSkin ? new Color( 0f, 1f, 1f, 0.15f ) : new Color( 0f, 1f, 1f, 0.25f ) ); return m_rootRowsBackgroundColor.Value; }
			set { if( m_rootRowsBackgroundColor == value ) return; m_rootRowsBackgroundColor = value; SetColor( "AUD_RootRowsTint", value ); }
		}

		private static Color? m_rootRowsBorderColor = null;
		public static Color RootRowsBorderColor
		{
			get { if( m_rootRowsBorderColor == null ) m_rootRowsBorderColor = GetColor( "AUD_RootRowsBorderColor", EditorGUIUtility.isProSkin ? new Color( 0.15f, 0.15f, 0.15f, 1f ) : new Color( 0.375f, 0.375f, 0.375f, 1f ) ); return m_rootRowsBorderColor.Value; }
			set { if( m_rootRowsBorderColor == value ) return; m_rootRowsBorderColor = value; SetColor( "AUD_RootRowsBorderColor", value ); }
		}

		private static Color? m_mainReferencesBackgroundColor = null;
		public static Color MainReferencesBackgroundColor
		{
			get { if( m_mainReferencesBackgroundColor == null ) m_mainReferencesBackgroundColor = GetColor( "AUD_MainRefRowsTint", EditorGUIUtility.isProSkin ? new Color( 0f, 0.35f, 0f, 1f ) : new Color( 0.25f, 0.75f, 0.25f, 1f ) ); return m_mainReferencesBackgroundColor.Value; }
			set { if( m_mainReferencesBackgroundColor == value ) return; m_mainReferencesBackgroundColor = value; SetColor( "AUD_MainRefRowsTint", value ); }
		}

		private static Color? m_selectedRowsParentTint = null;
		public static Color SelectedRowParentsTint
		{
			get { if( m_selectedRowsParentTint == null ) m_selectedRowsParentTint = GetColor( "AUD_SelectedRowParentsTint", EditorGUIUtility.isProSkin ? new Color( 0.36f, 0.36f, 0.18f, 1f ) : new Color( 0.825f, 0.825f, 0.55f, 1f ) ); return m_selectedRowsParentTint.Value; }
			set { if( m_selectedRowsParentTint == value ) return; m_selectedRowsParentTint = value; SetColor( "AUD_SelectedRowParentsTint", value ); }
		}

		private static Color? m_selectedRowOccurrencesColor = null;
		public static Color SelectedRowOccurrencesColor
		{
			get { if( m_selectedRowOccurrencesColor == null ) m_selectedRowOccurrencesColor = GetColor( "AUD_SelectedRowOccurrencesTint", EditorGUIUtility.isProSkin ? new Color( 0f, 0.3f, 0.75f, 1f ) : new Color( 0.25f, 0.75f, 1f, 1f ) ); return m_selectedRowOccurrencesColor.Value; }
			set { if( m_selectedRowOccurrencesColor == value ) return; m_selectedRowOccurrencesColor = value; SetColor( "AUD_SelectedRowOccurrencesTint", value ); }
		}

		private static Color? m_treeLinesColor = null;
		public static Color TreeLinesColor
		{
			get { if( m_treeLinesColor == null ) m_treeLinesColor = GetColor( "AUD_TreeLinesColor", EditorGUIUtility.isProSkin ? new Color( 0.65f, 0.65f, 0.65f, 1f ) : new Color( 0.375f, 0.375f, 0.375f, 1f ) ); return m_treeLinesColor.Value; }
			set { if( m_treeLinesColor == value ) return; m_treeLinesColor = value; SetColor( "AUD_TreeLinesColor", value ); }
		}

		private static Color? m_highlightedTreeLinesColor = null;
		public static Color HighlightedTreeLinesColor
		{
			get { if( m_highlightedTreeLinesColor == null ) m_highlightedTreeLinesColor = GetColor( "AUD_HighlightTreeLinesColor", Color.cyan ); return m_highlightedTreeLinesColor.Value; }
			set { if( m_highlightedTreeLinesColor == value ) return; m_highlightedTreeLinesColor = value; SetColor( "AUD_HighlightTreeLinesColor", value ); }
		}

		private static Color? m_searchMatchingTextColor = null;
		public static Color SearchMatchingTextColor
		{
			get { if( m_searchMatchingTextColor == null ) m_searchMatchingTextColor = GetColor( "AUD_SearchTextColor", Color.red ); return m_searchMatchingTextColor.Value; }
			set { if( m_searchMatchingTextColor == value ) return; m_searchMatchingTextColor = value; SetColor( "AUD_SearchTextColor", value ); ForEachAssetUsageDetectorWindow( ( window ) => window.OnSettingsChanged( highlightedSearchTextColorChanged: true ) ); }
		}

		private static Color? m_tooltipDescriptionTextColor = null;
		public static Color TooltipDescriptionTextColor
		{
			get { if( m_tooltipDescriptionTextColor == null ) m_tooltipDescriptionTextColor = GetColor( "AUD_TooltipUsageTextColor", EditorGUIUtility.isProSkin ? new Color( 0f, 0.9f, 0.9f, 1f ) : new Color( 0.9f, 0f, 0f, 1f ) ); return m_tooltipDescriptionTextColor.Value; }
			set { if( m_tooltipDescriptionTextColor == value ) return; m_tooltipDescriptionTextColor = value; SetColor( "AUD_TooltipUsageTextColor", value ); ForEachAssetUsageDetectorWindow( ( window ) => window.OnSettingsChanged( tooltipDescriptionsColorChanged: true ) ); }
		}
		#endregion

		#region Size Adjustments
		private static float? m_extraRowHeight = null;
		public static float ExtraRowHeight
		{
			get { if( m_extraRowHeight == null ) m_extraRowHeight = EditorPrefs.GetFloat( "AUD_ExtraRowHeight", 0f ); return m_extraRowHeight.Value; }
			set { if( m_extraRowHeight == value ) return; m_extraRowHeight = value; EditorPrefs.SetFloat( "AUD_ExtraRowHeight", value ); ForEachAssetUsageDetectorWindow( ( window ) => window.OnSettingsChanged() ); }
		}
		#endregion

		#region Other Settings
		private static bool? m_showRootAssetName = null;
		public static bool ShowRootAssetName
		{
			get { if( m_showRootAssetName == null ) m_showRootAssetName = EditorPrefs.GetBool( "AUD_ShowRootAssetName", false ); return m_showRootAssetName.Value; }
			set { if( m_showRootAssetName == value ) return; m_showRootAssetName = value; EditorPrefs.SetBool( "AUD_ShowRootAssetName", value ); }
		}

		private static bool? m_pingClickedObjects = null;
		public static bool PingClickedObjects
		{
			get { if( m_pingClickedObjects == null ) m_pingClickedObjects = EditorPrefs.GetBool( "AUD_PingClickedObj", true ); return m_pingClickedObjects.Value; }
			set { if( m_pingClickedObjects == value ) return; m_pingClickedObjects = value; EditorPrefs.SetBool( "AUD_PingClickedObj", value ); }
		}

		private static bool? m_selectClickedObjects = null;
		public static bool SelectClickedObjects
		{
			get { if( m_selectClickedObjects == null ) m_selectClickedObjects = EditorPrefs.GetBool( "AUD_SelectClickedObj", false ); return m_selectClickedObjects.Value; }
			set { if( m_selectClickedObjects == value ) return; m_selectClickedObjects = value; EditorPrefs.SetBool( "AUD_SelectClickedObj", value ); }
		}

		private static bool? m_selectDoubleClickedObjects = null;
		public static bool SelectDoubleClickedObjects
		{
			get { if( m_selectDoubleClickedObjects == null ) m_selectDoubleClickedObjects = EditorPrefs.GetBool( "AUD_SelectDoubleClickedObj", true ); return m_selectDoubleClickedObjects.Value; }
			set { if( m_selectDoubleClickedObjects == value ) return; m_selectDoubleClickedObjects = value; EditorPrefs.SetBool( "AUD_SelectDoubleClickedObj", value ); }
		}

		private static bool? m_showUnityTooltip = null;
		public static bool ShowUnityTooltip
		{
			get { if( m_showUnityTooltip == null ) m_showUnityTooltip = EditorPrefs.GetBool( "AUD_ShowUnityTooltip", false ); return m_showUnityTooltip.Value; }
			set { if( m_showUnityTooltip == value ) return; m_showUnityTooltip = value; EditorPrefs.SetBool( "AUD_ShowUnityTooltip", value ); }
		}

		private static bool? m_showCustomTooltip = null;
		public static bool ShowCustomTooltip
		{
			get { if( m_showCustomTooltip == null ) m_showCustomTooltip = EditorPrefs.GetBool( "AUD_ShowCustomTooltip", true ); return m_showCustomTooltip.Value; }
			set { if( m_showCustomTooltip == value ) return; m_showCustomTooltip = value; EditorPrefs.SetBool( "AUD_ShowCustomTooltip", value ); ForEachAssetUsageDetectorWindow( ( window ) => window.OnSettingsChanged() ); }
		}

		private static float? m_customTooltipDelay = null;
		public static float CustomTooltipDelay
		{
			get { if( m_customTooltipDelay == null ) m_customTooltipDelay = EditorPrefs.GetFloat( "AUD_CustomTooltipDelay", 0.7f ); return m_customTooltipDelay.Value; }
			set { if( m_customTooltipDelay == value ) return; m_customTooltipDelay = value; EditorPrefs.SetFloat( "AUD_CustomTooltipDelay", value ); }
		}

		private static bool? m_showTreeLines = null;
		public static bool ShowTreeLines
		{
			get { if( m_showTreeLines == null ) m_showTreeLines = EditorPrefs.GetBool( "AUD_ShowTreeLines", true ); return m_showTreeLines.Value; }
			set { if( m_showTreeLines == value ) return; m_showTreeLines = value; EditorPrefs.SetBool( "AUD_ShowTreeLines", value ); }
		}

		private static bool? m_applySelectedRowParentsTintToRootRows = null;
		public static bool ApplySelectedRowParentsTintToRootRows
		{
			get { if( m_applySelectedRowParentsTintToRootRows == null ) m_applySelectedRowParentsTintToRootRows = EditorPrefs.GetBool( "AUD_SelectedRowParentsTintAtRoot", true ); return m_applySelectedRowParentsTintToRootRows.Value; }
			set { if( m_applySelectedRowParentsTintToRootRows == value ) return; m_applySelectedRowParentsTintToRootRows = value; EditorPrefs.SetBool( "AUD_SelectedRowParentsTintAtRoot", value ); }
		}
		#endregion

#if UNITY_2018_3_OR_NEWER
		[SettingsProvider]
		public static SettingsProvider CreatePreferencesGUI()
		{
			return new SettingsProvider( "Project/yasirkula/Asset Usage Detector", SettingsScope.Project )
			{
				guiHandler = ( searchContext ) => PreferencesGUI(),
				keywords = new System.Collections.Generic.HashSet<string>() { "Asset", "Usage", "Detector" }
			};
		}
#endif

#if !UNITY_2018_3_OR_NEWER
		[PreferenceItem( "Asset Usage Detector" )]
#endif
		public static void PreferencesGUI()
		{
			float labelWidth = EditorGUIUtility.labelWidth;
#if UNITY_2018_3_OR_NEWER
			EditorGUIUtility.labelWidth += 60f;
#else
			EditorGUIUtility.labelWidth += 20f;
#endif

			EditorGUI.BeginChangeCheck();

			EditorGUIUtility.labelWidth += 140f;
			ShowRootAssetName = EditorGUILayout.Toggle( "Show Root Asset's Name For Sub-Assets (Requires Refresh)", ShowRootAssetName );
			EditorGUIUtility.labelWidth -= 140f;

			EditorGUILayout.Space();

			PingClickedObjects = EditorGUILayout.Toggle( "Ping Clicked Objects", PingClickedObjects );
			SelectClickedObjects = EditorGUILayout.Toggle( "Select Clicked Objects", SelectClickedObjects );
			SelectDoubleClickedObjects = EditorGUILayout.Toggle( "Select Double Clicked Objects", SelectDoubleClickedObjects );

			EditorGUILayout.Space();

			ShowUnityTooltip = EditorGUILayout.Toggle( "Show Unity Tooltip", ShowUnityTooltip );
			ShowCustomTooltip = EditorGUILayout.Toggle( "Show Custom Tooltip", ShowCustomTooltip );
			EditorGUI.indentLevel++;
			CustomTooltipDelay = FloatField( "Delay", CustomTooltipDelay, 0.7f );
			EditorGUI.indentLevel--;
			TooltipDescriptionTextColor = ColorField( "Tooltip Descriptions Text Color", TooltipDescriptionTextColor, EditorGUIUtility.isProSkin ? new Color( 0f, 0.9f, 0.9f, 1f ) : new Color( 0.9f, 0f, 0f, 1f ) );

			EditorGUILayout.Space();

			ExtraRowHeight = Mathf.Max( 0f, FloatField( "Extra Row Height", ExtraRowHeight, 0f ) );

			EditorGUILayout.Space();

			SettingsHeaderColor = ColorField( "Settings Header Color", SettingsHeaderColor, Color.cyan );
			SearchResultGroupHeaderColor = ColorField( "Group Header Color", SearchResultGroupHeaderColor, Color.cyan );
			RootRowsBackgroundColor = ColorField( "Root Rows Background Color", RootRowsBackgroundColor, EditorGUIUtility.isProSkin ? new Color( 0f, 1f, 1f, 0.15f ) : new Color( 0f, 1f, 1f, 0.25f ) );
			RootRowsBorderColor = ColorField( "Root Rows Border Color", RootRowsBorderColor, EditorGUIUtility.isProSkin ? new Color( 0.15f, 0.15f, 0.15f, 1f ) : new Color( 0.375f, 0.375f, 0.375f, 1f ) );
			MainReferencesBackgroundColor = ColorField( "Main References Background Color", MainReferencesBackgroundColor, EditorGUIUtility.isProSkin ? new Color( 0f, 0.35f, 0f, 1f ) : new Color( 0.25f, 0.75f, 0.25f, 1f ) );
			SelectedRowParentsTint = ColorField( "Selected Row Parents Tint", SelectedRowParentsTint, EditorGUIUtility.isProSkin ? new Color( 0.36f, 0.36f, 0.18f, 1f ) : new Color( 0.825f, 0.825f, 0.55f, 1f ) );
			EditorGUI.indentLevel++;
			ApplySelectedRowParentsTintToRootRows = !EditorGUILayout.Toggle( "Ignore Root Rows", !ApplySelectedRowParentsTintToRootRows );
			EditorGUI.indentLevel--;
			SelectedRowOccurrencesColor = ColorField( "Selected Row All Occurrences Tint", SelectedRowOccurrencesColor, EditorGUIUtility.isProSkin ? new Color( 0f, 0.3f, 0.75f, 1f ) : new Color( 0.25f, 0.75f, 1f, 1f ) );
			SearchMatchingTextColor = ColorField( "Matching Search Text Color", SearchMatchingTextColor, Color.red );

			ShowTreeLines = EditorGUILayout.Toggle( "Show Tree Lines", ShowTreeLines );
			EditorGUI.indentLevel++;
			TreeLinesColor = ColorField( "Normal Color", TreeLinesColor, EditorGUIUtility.isProSkin ? new Color( 0.65f, 0.65f, 0.65f, 1f ) : new Color( 0.375f, 0.375f, 0.375f, 1f ) );
			HighlightedTreeLinesColor = ColorField( "Highlighted Color", HighlightedTreeLinesColor, Color.cyan );
			EditorGUI.indentLevel--;

			EditorGUIUtility.labelWidth = labelWidth;

			if( EditorGUI.EndChangeCheck() )
				ForEachAssetUsageDetectorWindow( ( window ) => window.Repaint() );
		}

		private static Color ColorField( string label, Color value, Color defaultValue )
		{
			GUILayout.BeginHorizontal();
			Color result = EditorGUILayout.ColorField( label, value );
			if( GUILayout.Button( "Reset", GL_WIDTH_60 ) )
				result = defaultValue;
			GUILayout.EndHorizontal();

			return result;
		}

		private static float FloatField( string label, float value, float defaultValue )
		{
			GUILayout.BeginHorizontal();
			float result = EditorGUILayout.FloatField( label, value );
			if( GUILayout.Button( "Reset", GL_WIDTH_60 ) )
				result = defaultValue;
			GUILayout.EndHorizontal();

			return result;
		}

		private static Color GetColor( string pref, Color defaultColor )
		{
			if( EditorGUIUtility.isProSkin )
				pref += "_Pro";

			if( !EditorPrefs.HasKey( pref ) )
				return defaultColor;

			string[] parts = EditorPrefs.GetString( pref ).Split( ';' );
			return new Color32( byte.Parse( parts[0] ), byte.Parse( parts[1] ), byte.Parse( parts[2] ), byte.Parse( parts[3] ) );
		}

		private static void SetColor( string pref, Color32 value )
		{
			if( EditorGUIUtility.isProSkin )
				pref += "_Pro";

			EditorPrefs.SetString( pref, string.Concat( value.r.ToString(), ";", value.g.ToString(), ";", value.b.ToString(), ";", value.a.ToString() ) );
		}

		private static void ForEachAssetUsageDetectorWindow( System.Action<AssetUsageDetectorWindow> action )
		{
			foreach( AssetUsageDetectorWindow window in Resources.FindObjectsOfTypeAll<AssetUsageDetectorWindow>() )
			{
				if( window )
					action( window );
			}
		}
	}
}