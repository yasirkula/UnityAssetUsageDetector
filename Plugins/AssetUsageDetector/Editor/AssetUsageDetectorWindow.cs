// Asset Usage Detector - by Suleyman Yasir KULA (yasirkula@gmail.com)

using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Reflection;
using Object = UnityEngine.Object;
#if UNITY_2021_2_OR_NEWER
using PrefabStage = UnityEditor.SceneManagement.PrefabStage;
using PrefabStageUtility = UnityEditor.SceneManagement.PrefabStageUtility;
#elif UNITY_2018_3_OR_NEWER
using PrefabStage = UnityEditor.Experimental.SceneManagement.PrefabStage;
using PrefabStageUtility = UnityEditor.Experimental.SceneManagement.PrefabStageUtility;
#endif

namespace AssetUsageDetectorNamespace
{
	public enum Phase { Setup, Processing, Complete };

	public class AssetUsageDetectorWindow : EditorWindow, IHasCustomMenu
	{
		private enum WindowFilter { AlwaysReturnActive, ReturnActiveIfNotLocked, AlwaysReturnNew };

		private const string PREFS_SEARCH_SCENES = "AUD_SceneSearch";
		private const string PREFS_SEARCH_ASSETS = "AUD_AssetsSearch";
		private const string PREFS_SEARCH_PROJECT_SETTINGS = "AUD_ProjectSettingsSearch";
		private const string PREFS_DONT_SEARCH_SOURCE_ASSETS = "AUD_AssetsExcludeSrc";
		private const string PREFS_SEARCH_DEPTH_LIMIT = "AUD_Depth";
		private const string PREFS_SEARCH_FIELDS = "AUD_Fields";
		private const string PREFS_SEARCH_PROPERTIES = "AUD_Properties";
		private const string PREFS_SEARCH_NON_SERIALIZABLES = "AUD_NonSerializables";
		private const string PREFS_SEARCH_UNUSED_MATERIAL_PROPERTIES = "AUD_SearchUnusedMaterialProps";
		private const string PREFS_LAZY_SCENE_SEARCH = "AUD_LazySceneSearch";
		private const string PREFS_ADDRESSABLES_SUPPORT = "AUD_AddressablesSupport";
		private const string PREFS_CALCULATE_UNUSED_OBJECTS = "AUD_FindUnusedObjs";
		private const string PREFS_HIDE_DUPLICATE_ROWS = "AUD_HideDuplicates";
		private const string PREFS_HIDE_REDUNDANT_PREFAB_VARIANT_LINKS = "AUD_HideRedundantPVariantLinks";
		private const string PREFS_SHOW_PROGRESS = "AUD_Progress";

		private static readonly GUIContent windowTitle = new GUIContent( "Asset Usage Detector" );
		private static readonly Vector2 windowMinSize = new Vector2( 325f, 220f );

		private readonly GUILayoutOption GL_WIDTH_12 = GUILayout.Width( 12f );

		private GUIStyle lockButtonStyle;

		private readonly AssetUsageDetector core = new AssetUsageDetector();
		private SearchResult searchResult; // Overall search results

		// This isn't readonly so that it can be serialized
		private List<ObjectToSearch> objectsToSearch = new List<ObjectToSearch>() { new ObjectToSearch( null ) };

#pragma warning disable 0649
		[SerializeField] // Since titleContent persists between Editor sessions, so should the IsLocked property because otherwise, "[L]" in title becomes confusing when the EditorWindow isn't actually locked
		private bool m_isLocked;
		private bool IsLocked
		{
			get { return m_isLocked; }
			set
			{
				if( m_isLocked != value )
				{
					m_isLocked = value;
					titleContent = value ? new GUIContent( "[L] " + windowTitle.text, EditorGUIUtility.IconContent( "InspectorLock" ).image ) : windowTitle;
				}
			}
		}
#pragma warning restore 0649

		private Phase currentPhase = Phase.Setup;

		private bool searchInOpenScenes = true; // Scenes currently open in Hierarchy view
		private bool searchInScenesInBuild = true; // Scenes in build
		private bool searchInScenesInBuildTickedOnly = true; // Scenes in build (ticked only or not)
		private bool searchInAllScenes = true; // All scenes (including scenes that are not in build)
		private bool searchInAssetsFolder = true; // Assets in Project window
		private bool dontSearchInSourceAssets = true; // objectsToSearch won't be searched for internal references
		private bool searchInProjectSettings = true; // Player Settings, Graphics Settings etc.

		private List<Object> searchInAssetsSubset = new List<Object>() { null }; // If not empty, only these assets are searched for references
		private List<Object> excludedAssets = new List<Object>() { null }; // These assets won't be searched for references
		private List<Object> excludedScenes = new List<Object>() { null }; // These scenes won't be searched for references

		private int searchDepthLimit = 4; // Depth limit for recursively searching variables of objects

		private bool lazySceneSearch = true;
#if ASSET_USAGE_ADDRESSABLES
		private bool addressablesSupport = false;
#endif
		private bool searchNonSerializableVariables = true;
		private bool searchUnusedMaterialProperties = true;
		private bool calculateUnusedObjects = false;
		private bool hideDuplicateRows = true;
		private bool hideReduntantPrefabVariantLinks = true;
		private bool noAssetDatabaseChanges = false;
		private bool showDetailedProgressBar = true;

		private BindingFlags fieldModifiers, propertyModifiers;

		private SearchRefactoring searchRefactoring = null; // Its value can be assigned via ShowAndSearch

		private readonly ObjectToSearchListDrawer objectsToSearchDrawer = new ObjectToSearchListDrawer();
		private readonly ObjectListDrawer searchInAssetsSubsetDrawer = new ObjectListDrawer( "Search following asset(s) only:", false );
		private readonly ObjectListDrawer excludedAssetsDrawer = new ObjectListDrawer( "Don't search following asset(s):", false );
		private readonly ObjectListDrawer excludedScenesDrawer = new ObjectListDrawer( "Don't search in following scene(s):", false );

		private bool drawObjectsToSearchSection = true;

		private Vector2 scrollPosition = Vector2.zero;

		private bool shouldRepositionSelf;
		private Rect windowTargetPosition;

		void IHasCustomMenu.AddItemsToMenu( GenericMenu contextMenu )
		{
			contextMenu.AddItem( new GUIContent( "Lock" ), IsLocked, () => IsLocked = !IsLocked );
			contextMenu.AddSeparator( "" );

#if UNITY_2018_3_OR_NEWER
			contextMenu.AddItem( new GUIContent( "Settings" ), false, () => SettingsService.OpenProjectSettings( "Project/yasirkula/Asset Usage Detector" ) );
#else
			contextMenu.AddItem( new GUIContent( "Settings" ), false, () =>
			{
				System.Type preferencesWindowType = typeof( EditorWindow ).Assembly.GetType( "UnityEditor.PreferencesWindow" );
				preferencesWindowType.GetMethod( "ShowPreferencesWindow", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static ).Invoke( null, null );

				EditorWindow preferencesWindow = GetWindow( preferencesWindowType );
				if( (bool) preferencesWindowType.GetField( "m_RefreshCustomPreferences", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance ).GetValue( preferencesWindow ) )
				{
					preferencesWindowType.GetMethod( "AddCustomSections", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance ).Invoke( preferencesWindow, null );
					preferencesWindowType.GetField( "m_RefreshCustomPreferences", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance ).SetValue( preferencesWindow, false );
				}

				int targetSectionIndex = -1;
				System.Collections.IList sections = (System.Collections.IList) preferencesWindowType.GetField( "m_Sections", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance ).GetValue( preferencesWindow );
				for( int i = 0; i < sections.Count; i++ )
				{
					if( ( (GUIContent) sections[i].GetType().GetField( "content", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance ).GetValue( sections[i] ) ).text == "Asset Usage Detector" )
					{
						targetSectionIndex = i;
						break;
					}
				}

				if( targetSectionIndex >= 0 )
					preferencesWindowType.GetProperty( "selectedSectionIndex", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance ).SetValue( preferencesWindow, targetSectionIndex, null );
			} );
#endif

			if( currentPhase == Phase.Setup )
			{
				contextMenu.AddSeparator( "" );
				contextMenu.AddItem( new GUIContent( "Refresh Sub-Assets of Searched Objects" ), false, () =>
				{
					for( int i = objectsToSearch.Count - 1; i >= 0; i-- )
						objectsToSearch[i].RefreshSubAssets();
				} );
			}
			else if( currentPhase == Phase.Complete )
			{
				if( searchResult != null && searchResult.NumberOfGroups > 0 )
				{
					contextMenu.AddSeparator( "" );
					contextMenu.AddItem( new GUIContent( "Collapse All" ), false, searchResult.CollapseAllSearchResultGroups );
				}
			}
		}

		// Shows lock button at the top-right corner
		// Credit: http://leahayes.co.uk/2013/04/30/adding-the-little-padlock-button-to-your-editorwindow.html
		private void ShowButton( Rect position )
		{
			if( lockButtonStyle == null )
				lockButtonStyle = "IN LockButton";

			IsLocked = GUI.Toggle( position, IsLocked, GUIContent.none, lockButtonStyle );
		}

		private static AssetUsageDetectorWindow GetWindow( WindowFilter filter )
		{
			AssetUsageDetectorWindow[] windows = Resources.FindObjectsOfTypeAll<AssetUsageDetectorWindow>();
			AssetUsageDetectorWindow window = System.Array.Find( windows, ( w ) => w && !w.IsLocked );
			if( !window )
				window = System.Array.Find( windows, ( w ) => w );

			if( window && ( filter == WindowFilter.AlwaysReturnActive || ( !window.IsLocked && filter == WindowFilter.ReturnActiveIfNotLocked ) ) )
			{
				window.Show();
				window.Focus();

				return window;
			}

			Rect? windowTargetPosition = null;
			if( window )
			{
				Rect position = window.position;
				position.position += new Vector2( 50f, 50f );
				windowTargetPosition = position;
			}

			window = CreateInstance<AssetUsageDetectorWindow>();
			window.titleContent = windowTitle;
			window.minSize = windowMinSize;

			if( windowTargetPosition.HasValue )
			{
				window.shouldRepositionSelf = true;
				window.windowTargetPosition = windowTargetPosition.Value;
			}

			window.Show( true );
			window.Focus();

			return window;
		}

		[MenuItem( "Window/Asset Usage Detector/Active Window" )]
		private static void OpenActiveWindow()
		{
			GetWindow( WindowFilter.AlwaysReturnActive );
		}

		[MenuItem( "Window/Asset Usage Detector/New Window" )]
		private static void OpenNewWindow()
		{
			GetWindow( WindowFilter.AlwaysReturnNew );
		}

		// Quickly initiate search for the selected assets
		[MenuItem( "GameObject/Search for References/This Object Only", priority = 49 )]
		[MenuItem( "Assets/Search for References", priority = 1000 )]
		private static void SearchSelectedAssetReferences( MenuCommand command )
		{
			// This happens when this button is clicked via hierarchy's right click context menu
			// and is called once for each object in the selection. We don't want that, we want
			// the function to be called only once
			if( command.context )
			{
				EditorApplication.update -= CallSearchSelectedAssetReferencesOnce;
				EditorApplication.update += CallSearchSelectedAssetReferencesOnce;
			}
			else
				ShowAndSearch( Selection.objects );
		}

		[MenuItem( "GameObject/Search for References/Include Children", priority = 49 )]
		private static void SearchSelectedAssetReferencesWithChildren( MenuCommand command )
		{
			if( command.context )
			{
				EditorApplication.update -= CallSearchSelectedAssetReferencesWithChildrenOnce;
				EditorApplication.update += CallSearchSelectedAssetReferencesWithChildrenOnce;
			}
			else
				ShowAndSearch( Selection.objects, true );
		}

		// Show the menu item only if there is a selection in the Editor
		[MenuItem( "GameObject/Search for References/This Object Only", validate = true )]
		[MenuItem( "GameObject/Search for References/Include Children", validate = true )]
		[MenuItem( "Assets/Search for References", validate = true )]
		private static bool SearchSelectedAssetReferencesValidate( MenuCommand command )
		{
			return Selection.objects.Length > 0;
		}

		// Quickly show the AssetUsageDetector window and initiate a search
		public static void ShowAndSearch( IEnumerable<Object> searchObjects, bool? shouldSearchChildren = null )
		{
			GetWindow( WindowFilter.ReturnActiveIfNotLocked ).ShowAndSearchInternal( searchObjects, null, shouldSearchChildren );
		}

		// Quickly show the AssetUsageDetector window and initiate a search
		public static void ShowAndSearch( AssetUsageDetector.Parameters searchParameters, bool? shouldSearchChildren = null )
		{
			if( searchParameters == null )
			{
				Debug.LogError( "searchParameters can't be null!" );
				return;
			}

			GetWindow( WindowFilter.ReturnActiveIfNotLocked ).ShowAndSearchInternal( searchParameters.objectsToSearch, searchParameters, shouldSearchChildren );
		}

		private static void CallSearchSelectedAssetReferencesOnce()
		{
			EditorApplication.update -= CallSearchSelectedAssetReferencesOnce;
			SearchSelectedAssetReferences( new MenuCommand( null ) );
		}

		private static void CallSearchSelectedAssetReferencesWithChildrenOnce()
		{
			EditorApplication.update -= CallSearchSelectedAssetReferencesWithChildrenOnce;
			SearchSelectedAssetReferencesWithChildren( new MenuCommand( null ) );
		}

		private void ShowAndSearchInternal( IEnumerable<Object> searchObjects, AssetUsageDetector.Parameters searchParameters, bool? shouldSearchChildren )
		{
			if( !ReturnToSetupPhase() )
			{
				Debug.LogError( "Need to reset the previous search first!" );
				return;
			}

			objectsToSearch.Clear();
			if( searchObjects != null )
			{
				foreach( Object obj in searchObjects )
					objectsToSearch.Add( new ObjectToSearch( obj, shouldSearchChildren ) );
			}

			if( searchParameters != null )
			{
				ParseSceneSearchMode( searchParameters.searchInScenes );
				searchInAssetsFolder = searchParameters.searchInAssetsFolder;
				dontSearchInSourceAssets = searchParameters.dontSearchInSourceAssets;
				searchInProjectSettings = searchParameters.searchInProjectSettings;
				searchDepthLimit = searchParameters.searchDepthLimit;
				fieldModifiers = searchParameters.fieldModifiers;
				propertyModifiers = searchParameters.propertyModifiers;
				searchNonSerializableVariables = searchParameters.searchNonSerializableVariables;
				searchUnusedMaterialProperties = searchParameters.searchUnusedMaterialProperties;
				searchRefactoring = searchParameters.searchRefactoring;
				lazySceneSearch = searchParameters.lazySceneSearch;
#if ASSET_USAGE_ADDRESSABLES
				addressablesSupport = searchParameters.addressablesSupport;
#endif
				calculateUnusedObjects = searchParameters.calculateUnusedObjects;
				hideDuplicateRows = searchParameters.hideDuplicateRows;
				hideReduntantPrefabVariantLinks = searchParameters.hideReduntantPrefabVariantLinks;
				noAssetDatabaseChanges = searchParameters.noAssetDatabaseChanges;
				showDetailedProgressBar = searchParameters.showDetailedProgressBar;

				searchInAssetsSubset.Clear();
				if( searchParameters.searchInAssetsSubset != null )
				{
					foreach( Object obj in searchParameters.searchInAssetsSubset )
						searchInAssetsSubset.Add( obj );
				}

				excludedAssets.Clear();
				if( searchParameters.excludedAssetsFromSearch != null )
				{
					foreach( Object obj in searchParameters.excludedAssetsFromSearch )
						excludedAssets.Add( obj );
				}

				excludedScenes.Clear();
				if( searchParameters.excludedScenesFromSearch != null )
				{
					foreach( Object obj in searchParameters.excludedScenesFromSearch )
						excludedScenes.Add( obj );
				}
			}

			InitiateSearch();
			Repaint();
		}

		private void Awake()
		{
			LoadPrefs();
		}

		private void OnEnable()
		{
			if( currentPhase == Phase.Complete && AssetUsageDetectorSettings.ShowCustomTooltip )
				wantsMouseMove = wantsMouseEnterLeaveWindow = true; // These values aren't preserved during domain reload on Unity 2020.3.0f1

#if UNITY_2018_3_OR_NEWER
			PrefabStage.prefabStageClosing -= ReplacePrefabStageObjectsWithAssets;
			PrefabStage.prefabStageClosing += ReplacePrefabStageObjectsWithAssets;
#endif
		}

		private void OnDisable()
		{
#if UNITY_2018_3_OR_NEWER
			PrefabStage.prefabStageClosing -= ReplacePrefabStageObjectsWithAssets;
#endif
			SearchResultTooltip.Hide();
		}

		private void OnDestroy()
		{
			if( core != null )
				core.SaveCache();

			SavePrefs();

			if( searchResult != null && currentPhase == Phase.Complete )
				searchResult.RestoreInitialSceneSetup();
		}

		private void SavePrefs()
		{
			EditorPrefs.SetInt( PREFS_SEARCH_SCENES, (int) GetSceneSearchMode( false ) );
			EditorPrefs.SetBool( PREFS_SEARCH_ASSETS, searchInAssetsFolder );
			EditorPrefs.SetBool( PREFS_DONT_SEARCH_SOURCE_ASSETS, dontSearchInSourceAssets );
			EditorPrefs.SetBool( PREFS_SEARCH_PROJECT_SETTINGS, searchInProjectSettings );
			EditorPrefs.SetInt( PREFS_SEARCH_DEPTH_LIMIT, searchDepthLimit );
			EditorPrefs.SetInt( PREFS_SEARCH_FIELDS, (int) fieldModifiers );
			EditorPrefs.SetInt( PREFS_SEARCH_PROPERTIES, (int) propertyModifiers );
			EditorPrefs.SetBool( PREFS_SEARCH_NON_SERIALIZABLES, searchNonSerializableVariables );
			EditorPrefs.SetBool( PREFS_SEARCH_UNUSED_MATERIAL_PROPERTIES, searchUnusedMaterialProperties );
			EditorPrefs.SetBool( PREFS_LAZY_SCENE_SEARCH, lazySceneSearch );
#if ASSET_USAGE_ADDRESSABLES
			EditorPrefs.SetBool( PREFS_ADDRESSABLES_SUPPORT, addressablesSupport );
#endif
			EditorPrefs.SetBool( PREFS_CALCULATE_UNUSED_OBJECTS, calculateUnusedObjects );
			EditorPrefs.SetBool( PREFS_HIDE_DUPLICATE_ROWS, hideDuplicateRows );
			EditorPrefs.SetBool( PREFS_HIDE_REDUNDANT_PREFAB_VARIANT_LINKS, hideReduntantPrefabVariantLinks );
			EditorPrefs.SetBool( PREFS_SHOW_PROGRESS, showDetailedProgressBar );
		}

		private void LoadPrefs()
		{
			ParseSceneSearchMode( (SceneSearchMode) EditorPrefs.GetInt( PREFS_SEARCH_SCENES, (int) ( SceneSearchMode.OpenScenes | SceneSearchMode.ScenesInBuildSettingsTickedOnly | SceneSearchMode.AllScenes ) ) );

			searchInAssetsFolder = EditorPrefs.GetBool( PREFS_SEARCH_ASSETS, true );
			dontSearchInSourceAssets = EditorPrefs.GetBool( PREFS_DONT_SEARCH_SOURCE_ASSETS, true );
			searchInProjectSettings = EditorPrefs.GetBool( PREFS_SEARCH_PROJECT_SETTINGS, true );
			searchDepthLimit = EditorPrefs.GetInt( PREFS_SEARCH_DEPTH_LIMIT, 4 );
			fieldModifiers = (BindingFlags) EditorPrefs.GetInt( PREFS_SEARCH_FIELDS, (int) ( BindingFlags.Public | BindingFlags.NonPublic ) );
			propertyModifiers = (BindingFlags) EditorPrefs.GetInt( PREFS_SEARCH_PROPERTIES, (int) ( BindingFlags.Public | BindingFlags.NonPublic ) );
			searchNonSerializableVariables = EditorPrefs.GetBool( PREFS_SEARCH_NON_SERIALIZABLES, true );
			searchUnusedMaterialProperties = EditorPrefs.GetBool( PREFS_SEARCH_UNUSED_MATERIAL_PROPERTIES, true );
			lazySceneSearch = EditorPrefs.GetBool( PREFS_LAZY_SCENE_SEARCH, true );
#if ASSET_USAGE_ADDRESSABLES
			addressablesSupport = EditorPrefs.GetBool( PREFS_ADDRESSABLES_SUPPORT, false );
#endif
			calculateUnusedObjects = EditorPrefs.GetBool( PREFS_CALCULATE_UNUSED_OBJECTS, false );
			hideDuplicateRows = EditorPrefs.GetBool( PREFS_HIDE_DUPLICATE_ROWS, true );
			hideReduntantPrefabVariantLinks = EditorPrefs.GetBool( PREFS_HIDE_REDUNDANT_PREFAB_VARIANT_LINKS, true );
			showDetailedProgressBar = EditorPrefs.GetBool( PREFS_SHOW_PROGRESS, true );
		}

		private SceneSearchMode GetSceneSearchMode( bool hideOptionsInPlayMode )
		{
			SceneSearchMode sceneSearchMode = SceneSearchMode.None;
			if( searchInOpenScenes )
				sceneSearchMode |= SceneSearchMode.OpenScenes;
			if( !hideOptionsInPlayMode || !EditorApplication.isPlaying )
			{
				if( searchInScenesInBuild )
					sceneSearchMode |= searchInScenesInBuildTickedOnly ? SceneSearchMode.ScenesInBuildSettingsTickedOnly : SceneSearchMode.ScenesInBuildSettingsAll;
				if( searchInAllScenes )
					sceneSearchMode |= SceneSearchMode.AllScenes;
			}

			return sceneSearchMode;
		}

		private void ParseSceneSearchMode( SceneSearchMode sceneSearchMode )
		{
			searchInOpenScenes = ( sceneSearchMode & SceneSearchMode.OpenScenes ) == SceneSearchMode.OpenScenes;
			searchInScenesInBuild = ( sceneSearchMode & SceneSearchMode.ScenesInBuildSettingsAll ) == SceneSearchMode.ScenesInBuildSettingsAll || ( sceneSearchMode & SceneSearchMode.ScenesInBuildSettingsTickedOnly ) == SceneSearchMode.ScenesInBuildSettingsTickedOnly;
			searchInScenesInBuildTickedOnly = ( sceneSearchMode & SceneSearchMode.ScenesInBuildSettingsAll ) != SceneSearchMode.ScenesInBuildSettingsAll;
			searchInAllScenes = ( sceneSearchMode & SceneSearchMode.AllScenes ) == SceneSearchMode.AllScenes;
		}

		private void Update()
		{
			if( shouldRepositionSelf )
			{
				shouldRepositionSelf = false;
				position = windowTargetPosition;
			}
		}

		private void OnGUI()
		{
			// Make the window scrollable
			scrollPosition = EditorGUILayout.BeginScrollView( scrollPosition, Utilities.GL_EXPAND_WIDTH, Utilities.GL_EXPAND_HEIGHT );

			GUILayout.BeginVertical();

			if( currentPhase == Phase.Processing )
			{
				// If we are stuck at this phase, then we have encountered an exception
				GUILayout.Label( ". . . Search in progress or something went wrong (check console) . . ." );

				if( GUILayout.Button( "RETURN", Utilities.GL_HEIGHT_30 ) )
				{
					ReturnToSetupPhase();
					GUIUtility.ExitGUI();
				}
			}
			else if( currentPhase == Phase.Setup )
			{
				DrawObjectsToSearchSection();

				GUILayout.Space( 10f );

				Color c = GUI.backgroundColor;
				GUI.backgroundColor = AssetUsageDetectorSettings.SettingsHeaderColor;
				GUILayout.Box( "<b>SEARCH IN</b>", Utilities.BoxGUIStyle, Utilities.GL_EXPAND_WIDTH );
				GUI.backgroundColor = c;

				searchInAssetsFolder = WordWrappingToggleLeft( "Project window (Assets folder)", searchInAssetsFolder );

				if( searchInAssetsFolder )
				{
					GUILayout.BeginHorizontal();
					GUILayout.Space( 35f );
					GUILayout.BeginVertical();

					searchInAssetsSubsetDrawer.Draw( searchInAssetsSubset );
					excludedAssetsDrawer.Draw( excludedAssets );

					GUILayout.EndVertical();
					GUILayout.EndHorizontal();
				}

				GUILayout.Space( 5f );

				dontSearchInSourceAssets = WordWrappingToggleLeft( "Don't search \"SEARCHED OBJECTS\" themselves for references", dontSearchInSourceAssets );
				searchUnusedMaterialProperties = WordWrappingToggleLeft( "Search unused material properties (e.g. normal map of a material that no longer uses normal mapping)", searchUnusedMaterialProperties );

				Utilities.DrawSeparatorLine();

				if( searchInAllScenes && !EditorApplication.isPlaying )
					GUI.enabled = false;

				searchInOpenScenes = WordWrappingToggleLeft( "Currently open (loaded) scene(s)", searchInOpenScenes );

				if( !EditorApplication.isPlaying )
				{
					searchInScenesInBuild = WordWrappingToggleLeft( "Scenes in Build Settings", searchInScenesInBuild );

					if( searchInScenesInBuild )
					{
						GUILayout.BeginHorizontal();
						GUILayout.Space( 35f );

						searchInScenesInBuildTickedOnly = EditorGUILayout.ToggleLeft( "Ticked only", searchInScenesInBuildTickedOnly, Utilities.GL_WIDTH_100 );
						searchInScenesInBuildTickedOnly = !EditorGUILayout.ToggleLeft( "All", !searchInScenesInBuildTickedOnly, Utilities.GL_WIDTH_100 );

						GUILayout.EndHorizontal();
					}

					GUI.enabled = true;

					searchInAllScenes = WordWrappingToggleLeft( "All scenes in the project", searchInAllScenes );
				}

				GUILayout.BeginHorizontal();
				GUILayout.Space( 35f );
				GUILayout.BeginVertical();

				excludedScenesDrawer.Draw( excludedScenes );

				GUILayout.EndVertical();
				GUILayout.EndHorizontal();

				Utilities.DrawSeparatorLine();

				searchInProjectSettings = WordWrappingToggleLeft( "Project Settings (Player Settings, Graphics Settings etc.)", searchInProjectSettings );

				GUILayout.Space( 10f );

				GUI.backgroundColor = AssetUsageDetectorSettings.SettingsHeaderColor;
				GUILayout.Box( "<b>SETTINGS</b>", Utilities.BoxGUIStyle, Utilities.GL_EXPAND_WIDTH );
				GUI.backgroundColor = c;

#if ASSET_USAGE_ADDRESSABLES
				EditorGUI.BeginDisabledGroup( addressablesSupport );
#endif
				lazySceneSearch = WordWrappingToggleLeft( "Lazy scene search: scenes are searched in detail only when they are manually refreshed (faster search)", lazySceneSearch );
#if ASSET_USAGE_ADDRESSABLES
				EditorGUI.EndDisabledGroup();
				addressablesSupport = WordWrappingToggleLeft( "Addressables support (Experimental) (WARNING: 'Lazy scene search' will be disabled) (slower search)", addressablesSupport );
#endif
				calculateUnusedObjects = WordWrappingToggleLeft( "Calculate unused objects", calculateUnusedObjects );
				hideDuplicateRows = WordWrappingToggleLeft( "Hide duplicate rows in search results", hideDuplicateRows );
#if UNITY_2018_3_OR_NEWER
				hideReduntantPrefabVariantLinks = WordWrappingToggleLeft( "Hide redundant prefab variant links (when the same value is assigned to the same Component of a prefab and its variant(s))", hideReduntantPrefabVariantLinks );
#endif
				noAssetDatabaseChanges = WordWrappingToggleLeft( "I haven't modified any assets/scenes since the last search (faster search)", noAssetDatabaseChanges );
				showDetailedProgressBar = WordWrappingToggleLeft( "Update search progress bar more often (cancelable search) (slower search)", showDetailedProgressBar );

				GUILayout.Space( 10f );

				// Don't let the user press the GO button without any valid search location
				if( !searchInAllScenes && !searchInOpenScenes && !searchInScenesInBuild && !searchInAssetsFolder && !searchInProjectSettings )
					GUI.enabled = false;

				if( GUILayout.Button( "GO!", Utilities.GL_HEIGHT_30 ) )
				{
					InitiateSearch();
					GUIUtility.ExitGUI();
				}

				GUILayout.Space( 5f );
			}
			else if( currentPhase == Phase.Complete )
			{
				// Draw the results of the search
				GUI.enabled = false;

				DrawObjectsToSearchSection();

				if( drawObjectsToSearchSection )
					GUILayout.Space( 10f );

				GUI.enabled = true;

				if( GUILayout.Button( "Reset Search", Utilities.GL_HEIGHT_30 ) )
				{
					ReturnToSetupPhase();
					GUIUtility.ExitGUI();
				}

				if( searchResult == null )
				{
					EditorGUILayout.HelpBox( "ERROR: searchResult is null", MessageType.Error );
					return;
				}
				else if( !searchResult.SearchCompletedSuccessfully )
					EditorGUILayout.HelpBox( "ERROR: search was interrupted, check the logs for more info", MessageType.Error );

				if( searchResult.NumberOfGroups == 0 )
				{
					GUILayout.Space( 10f );
					GUILayout.Box( "No references found...", Utilities.BoxGUIStyle, Utilities.GL_EXPAND_WIDTH );
				}
				else
				{
					noAssetDatabaseChanges = WordWrappingToggleLeft( "I haven't modified any assets/scenes since the last search (faster Refresh)", noAssetDatabaseChanges );

					EditorGUILayout.Space();

					scrollPosition.y = searchResult.DrawOnGUI( this, scrollPosition.y, noAssetDatabaseChanges );
				}
			}

			if( Event.current.type == EventType.MouseLeaveWindow )
			{
				SearchResultTooltip.Hide();

				if( searchResult != null )
					searchResult.CancelDelayedTreeViewTooltip();
			}

			GUILayout.EndVertical();

			EditorGUILayout.EndScrollView();
		}

		private void DrawObjectsToSearchSection()
		{
			Color c = GUI.backgroundColor;
			GUI.backgroundColor = AssetUsageDetectorSettings.SettingsHeaderColor;
			GUILayout.Box( "<b>SEARCHED OBJECTS</b>", Utilities.BoxGUIStyle, Utilities.GL_EXPAND_WIDTH );
			GUI.backgroundColor = c;

			Rect searchedObjectsHeaderRect = GUILayoutUtility.GetLastRect();
			searchedObjectsHeaderRect.x += 5f;
			searchedObjectsHeaderRect.yMin += ( searchedObjectsHeaderRect.height - EditorGUIUtility.singleLineHeight ) * 0.5f;
			searchedObjectsHeaderRect.height = EditorGUIUtility.singleLineHeight;

			drawObjectsToSearchSection = EditorGUI.Foldout( searchedObjectsHeaderRect, drawObjectsToSearchSection, GUIContent.none, true );

			if( drawObjectsToSearchSection )
				objectsToSearchDrawer.Draw( objectsToSearch );
		}

		private bool WordWrappingToggleLeft( string label, bool value )
		{
			GUILayout.BeginHorizontal();
			bool result = EditorGUILayout.ToggleLeft( GUIContent.none, value, GL_WIDTH_12 );
			if( GUILayout.Button( label, EditorStyles.wordWrappedLabel ) )
			{
				GUI.FocusControl( null );
				result = !value;
			}
			GUILayout.EndHorizontal();

			return result;
		}

		private void InitiateSearch()
		{
			currentPhase = Phase.Processing;

			SavePrefs();

#if UNITY_2018_3_OR_NEWER
			ReplacePrefabStageObjectsWithAssets( PrefabStageUtility.GetCurrentPrefabStage() );
#endif

			// Start searching
			searchResult = core.Run( new AssetUsageDetector.Parameters()
			{
				objectsToSearch = !objectsToSearch.IsEmpty() ? new ObjectToSearchEnumerator( objectsToSearch ).ToArray() : null,
				searchInScenes = GetSceneSearchMode( true ),
				searchInAssetsFolder = searchInAssetsFolder,
				searchInAssetsSubset = !searchInAssetsSubset.IsEmpty() ? searchInAssetsSubset.ToArray() : null,
				excludedAssetsFromSearch = !excludedAssets.IsEmpty() ? excludedAssets.ToArray() : null,
				dontSearchInSourceAssets = dontSearchInSourceAssets,
				excludedScenesFromSearch = !excludedScenes.IsEmpty() ? excludedScenes.ToArray() : null,
				searchInProjectSettings = searchInProjectSettings,
				//fieldModifiers = fieldModifiers,
				//propertyModifiers = propertyModifiers,
				//searchDepthLimit = searchDepthLimit,
				//searchNonSerializableVariables = searchNonSerializableVariables,
				searchUnusedMaterialProperties = searchUnusedMaterialProperties,
				searchRefactoring = searchRefactoring,
#if ASSET_USAGE_ADDRESSABLES
				lazySceneSearch = lazySceneSearch && !addressablesSupport,
				addressablesSupport = addressablesSupport,
#else
				lazySceneSearch = lazySceneSearch,
#endif
				calculateUnusedObjects = calculateUnusedObjects,
				hideDuplicateRows = hideDuplicateRows,
				hideReduntantPrefabVariantLinks = hideReduntantPrefabVariantLinks,
				noAssetDatabaseChanges = noAssetDatabaseChanges,
				showDetailedProgressBar = showDetailedProgressBar
			} );

			currentPhase = Phase.Complete;

			// We really don't want SearchRefactoring to affect next searches unless the search is initiated via ShowAndSearch again
			searchRefactoring = null;

			if( AssetUsageDetectorSettings.ShowCustomTooltip )
				wantsMouseMove = wantsMouseEnterLeaveWindow = true;
		}

#if UNITY_2018_3_OR_NEWER
		// Try replacing searched objects who are part of currently open prefab stage with their corresponding prefab assets
		public void ReplacePrefabStageObjectsWithAssets( PrefabStage prefabStage )
		{
			if( prefabStage == null || !prefabStage.stageHandle.IsValid() )
				return;

#if UNITY_2020_1_OR_NEWER
			GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>( prefabStage.assetPath );
#else
			GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>( prefabStage.prefabAssetPath );
#endif
			if( prefabAsset == null || prefabAsset.Equals( null ) )
				return;

			for( int i = 0; i < objectsToSearch.Count; i++ )
			{
				Object obj = objectsToSearch[i].obj;
				if( obj != null && !obj.Equals( null ) && obj is GameObject && prefabStage.IsPartOfPrefabContents( (GameObject) obj ) )
				{
					GameObject prefabStageObjectSource = ( (GameObject) obj ).FollowSymmetricHierarchy( prefabStage.prefabContentsRoot, prefabAsset );
					if( prefabStageObjectSource != null )
						objectsToSearch[i].obj = prefabStageObjectSource;

					List<ObjectToSearch.SubAsset> subAssets = objectsToSearch[i].subAssets;
					for( int j = 0; j < subAssets.Count; j++ )
					{
						obj = subAssets[j].subAsset;
						if( obj != null && !obj.Equals( null ) && obj is GameObject && prefabStage.IsPartOfPrefabContents( (GameObject) obj ) )
						{
							prefabStageObjectSource = ( (GameObject) obj ).FollowSymmetricHierarchy( prefabStage.prefabContentsRoot, prefabAsset );
							if( prefabStageObjectSource != null )
								subAssets[j].subAsset = prefabStageObjectSource;
						}
					}
				}
			}
		}
#endif

		private bool ReturnToSetupPhase()
		{
			if( searchResult != null && !EditorApplication.isPlaying && !searchResult.RestoreInitialSceneSetup() )
				return false;

			searchResult = null;
			currentPhase = Phase.Setup;
			wantsMouseMove = wantsMouseEnterLeaveWindow = false;

			SearchResultTooltip.Hide();

			return true;
		}

		internal void OnSettingsChanged( bool highlightedSearchTextColorChanged = false, bool tooltipDescriptionsColorChanged = false )
		{
			if( searchResult == null )
				return;

			wantsMouseMove = wantsMouseEnterLeaveWindow = AssetUsageDetectorSettings.ShowCustomTooltip;

			for( int i = searchResult.NumberOfGroups - 1; i >= 0; i-- )
			{
				if( searchResult[i].treeView != null )
				{
					searchResult[i].treeView.rowHeight = EditorGUIUtility.singleLineHeight + AssetUsageDetectorSettings.ExtraRowHeight;
					searchResult[i].treeView.OnSettingsChanged( highlightedSearchTextColorChanged, tooltipDescriptionsColorChanged );
				}
			}
		}
	}
}