// Asset Usage Detector - by Suleyman Yasir KULA (yasirkula@gmail.com)

using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Reflection;
using Object = UnityEngine.Object;

namespace AssetUsageDetectorNamespace
{
	public enum Phase { Setup, Processing, Complete };

	public class AssetUsageDetectorWindow : EditorWindow
	{
		private const float PLAY_MODE_REFRESH_INTERVAL = 1f; // Interval to refresh the editor window in play mode

		private const string PREFS_SEARCH_SCENES = "AUD_SceneSearch";
		private const string PREFS_SEARCH_ASSETS = "AUD_AssetsSearch";
		private const string PREFS_DONT_SEARCH_SOURCE_ASSETS = "AUD_AssetsExcludeSrc";
		private const string PREFS_SEARCH_DEPTH_LIMIT = "AUD_Depth";
		private const string PREFS_SEARCH_FIELDS = "AUD_Fields";
		private const string PREFS_SEARCH_PROPERTIES = "AUD_Properties";
		private const string PREFS_SEARCH_NON_SERIALIZABLES = "AUD_NonSerializables";
		private const string PREFS_LAZY_SCENE_SEARCH = "AUD_LazySceneSearch";
		private const string PREFS_PATH_DRAWING_MODE = "AUD_PathDrawing";
		private const string PREFS_SHOW_PROGRESS = "AUD_Progress";
		private const string PREFS_SHOW_TOOLTIPS = "AUD_Tooltips";

		private static readonly GUIContent windowTitle = new GUIContent( "Asset Usage Detector" );
		private static readonly Vector2 windowMinSize = new Vector2( 325f, 220f );

		private AssetUsageDetector core = new AssetUsageDetector();
		private SearchResult searchResult; // Overall search results

		private List<ObjectToSearch> objectsToSearch = new List<ObjectToSearch>() { new ObjectToSearch( null ) };

		private Phase currentPhase = Phase.Setup;

		private bool searchInOpenScenes = true; // Scenes currently open in Hierarchy view
		private bool searchInScenesInBuild = true; // Scenes in build
		private bool searchInScenesInBuildTickedOnly = true; // Scenes in build (ticked only or not)
		private bool searchInAllScenes = true; // All scenes (including scenes that are not in build)
		private bool searchInAssetsFolder = true; // Assets in Project window
		private bool dontSearchInSourceAssets = true; // objectsToSearch won't be searched for internal references

		private List<Object> searchInAssetsSubset = new List<Object>() { null }; // If not empty, only these assets are searched for references
		private List<Object> excludedAssets = new List<Object>() { null }; // These assets won't be searched for references
		private List<Object> excludedScenes = new List<Object>() { null }; // These scenes won't be searched for references

		private int searchDepthLimit = 4; // Depth limit for recursively searching variables of objects

		private bool restoreInitialSceneSetup = true; // Close the additively loaded scenes that were not part of the initial scene setup

		private string errorMessage = string.Empty;

		private bool lazySceneSearch = false;
		private bool searchNonSerializableVariables = true;
		private bool noAssetDatabaseChanges = false;
		private bool showDetailedProgressBar = false;

		private BindingFlags fieldModifiers, propertyModifiers;

		private SearchResultDrawParameters searchResultDrawParameters = new SearchResultDrawParameters( PathDrawingMode.ShortRelevantParts, false, false );

		private double nextPlayModeRefreshTime = 0f;

		private readonly ObjectToSearchListDrawer objectsToSearchDrawer = new ObjectToSearchListDrawer();
		private readonly ObjectListDrawer searchInAssetsSubsetDrawer = new ObjectListDrawer( "Search following asset(s) only:", false );
		private readonly ObjectListDrawer excludedAssetsDrawer = new ObjectListDrawer( "Don't search following asset(s):", false );
		private readonly ObjectListDrawer excludedScenesDrawer = new ObjectListDrawer( "Don't search in following scene(s):", false );

		private Vector2 scrollPosition = Vector2.zero;

		private bool shouldRepositionSelf;
		private Rect windowTargetPosition;

		private static AssetUsageDetectorWindow mainWindow = null;

		// Add "Asset Usage Detector" menu item to the Window menu
		[MenuItem( "Window/Asset Usage Detector/Active Window" )]
		private static void OpenActiveWindow()
		{
			if( !mainWindow )
			{
				mainWindow = GetWindow<AssetUsageDetectorWindow>();
				mainWindow.titleContent = windowTitle;
				mainWindow.minSize = windowMinSize;
			}

			mainWindow.Show();
		}

		[MenuItem( "Window/Asset Usage Detector/New Window" )]
		private static void OpenNewWindow()
		{
			Rect? windowTargetPosition = null;
			if( mainWindow )
			{
				Rect position = mainWindow.position;
				position.position += new Vector2( 50f, 50f );
				windowTargetPosition = position;
			}

			mainWindow = CreateInstance<AssetUsageDetectorWindow>();
			mainWindow.titleContent = windowTitle;
			mainWindow.minSize = windowMinSize;

			if( windowTargetPosition.HasValue )
			{
				mainWindow.shouldRepositionSelf = true;
				mainWindow.windowTargetPosition = windowTargetPosition.Value;
			}

			mainWindow.Show( true );
		}

		// Quickly initiate search for the selected assets
		[MenuItem( "GameObject/Search for References", priority = 49 )]
		[MenuItem( "Assets/Search for References", priority = 1000 )]
		private static void SearchSelectedAssetReferences( MenuCommand command )
		{
			// This happens when this button is clicked via hierarchy's right click context menu
			// and is called once for each object in the selection. We don't want that, we want
			// the function to be called only once so that there aren't multiple empty parents 
			// generated in one call
			if( command.context )
			{
				EditorApplication.update -= CallSearchSelectedAssetReferencesOnce;
				EditorApplication.update += CallSearchSelectedAssetReferencesOnce;
			}
			else
				ShowAndSearch( Selection.objects );
		}

		// Show the menu item only if there is a selection in the Editor
		[MenuItem( "GameObject/Search for References", validate = true )]
		[MenuItem( "Assets/Search for References", validate = true )]
		private static bool SearchSelectedAssetReferencesValidate( MenuCommand command )
		{
			return Selection.objects.Length > 0;
		}

		// Quickly show the AssetUsageDetector window and initiate a search
		public static void ShowAndSearch( IEnumerable<Object> searchObjects )
		{
			ShowAndSearchInternal( searchObjects, null );
		}

		// Quickly show the AssetUsageDetector window and initiate a search
		public static void ShowAndSearch( AssetUsageDetector.Parameters searchParameters )
		{
			if( searchParameters == null )
			{
				Debug.LogError( "searchParameters can't be null!" );
				return;
			}

			ShowAndSearchInternal( searchParameters.objectsToSearch, searchParameters );
		}

		private static void CallSearchSelectedAssetReferencesOnce()
		{
			EditorApplication.update -= CallSearchSelectedAssetReferencesOnce;
			SearchSelectedAssetReferences( new MenuCommand( null ) );
		}

		private static void ShowAndSearchInternal( IEnumerable<Object> searchObjects, AssetUsageDetector.Parameters searchParameters )
		{
			if( mainWindow != null && !mainWindow.ReturnToSetupPhase( true ) )
			{
				Debug.LogError( "Need to reset the previous search first!" );
				return;
			}

			OpenActiveWindow();

			mainWindow.objectsToSearch.Clear();
			if( searchObjects != null )
			{
				foreach( Object obj in searchObjects )
					mainWindow.objectsToSearch.Add( new ObjectToSearch( obj ) );
			}

			if( searchParameters != null )
			{
				mainWindow.ParseSceneSearchMode( searchParameters.searchInScenes );
				mainWindow.searchInAssetsFolder = searchParameters.searchInAssetsFolder;
				mainWindow.dontSearchInSourceAssets = searchParameters.dontSearchInSourceAssets;
				mainWindow.searchDepthLimit = searchParameters.searchDepthLimit;
				mainWindow.fieldModifiers = searchParameters.fieldModifiers;
				mainWindow.propertyModifiers = searchParameters.propertyModifiers;
				mainWindow.searchNonSerializableVariables = searchParameters.searchNonSerializableVariables;
				mainWindow.lazySceneSearch = searchParameters.lazySceneSearch;
				mainWindow.noAssetDatabaseChanges = searchParameters.noAssetDatabaseChanges;
				mainWindow.showDetailedProgressBar = searchParameters.showDetailedProgressBar;

				mainWindow.searchInAssetsSubset.Clear();
				if( searchParameters.searchInAssetsSubset != null )
				{
					foreach( Object obj in searchParameters.searchInAssetsSubset )
						mainWindow.searchInAssetsSubset.Add( obj );
				}

				mainWindow.excludedAssets.Clear();
				if( searchParameters.excludedAssetsFromSearch != null )
				{
					foreach( Object obj in searchParameters.excludedAssetsFromSearch )
						mainWindow.excludedAssets.Add( obj );
				}

				mainWindow.excludedScenes.Clear();
				if( searchParameters.excludedScenesFromSearch != null )
				{
					foreach( Object obj in searchParameters.excludedScenesFromSearch )
						mainWindow.excludedScenes.Add( obj );
				}
			}

			mainWindow.InitiateSearch();
			mainWindow.Repaint();
		}

		private void Awake()
		{
			LoadPrefs();
		}

		private void OnEnable()
		{
			mainWindow = this;

#if UNITY_2018_3_OR_NEWER
			UnityEditor.Experimental.SceneManagement.PrefabStage.prefabStageClosing -= ReplacePrefabStageObjectsWithAssets;
			UnityEditor.Experimental.SceneManagement.PrefabStage.prefabStageClosing += ReplacePrefabStageObjectsWithAssets;
#endif
		}

		private void OnDisable()
		{
#if UNITY_2018_3_OR_NEWER
			UnityEditor.Experimental.SceneManagement.PrefabStage.prefabStageClosing -= ReplacePrefabStageObjectsWithAssets;
#endif

			if( mainWindow == this )
				mainWindow = null;
		}

		private void OnDestroy()
		{
			if( core != null )
				core.SaveCache();

			SavePrefs();

			if( searchResult != null && currentPhase == Phase.Complete && !EditorApplication.isPlaying && searchResult.IsSceneSetupDifferentThanCurrentSetup() )
			{
				if( EditorUtility.DisplayDialog( "Scenes", "Restore initial scene setup?", "Yes", "Leave it as is" ) )
					searchResult.RestoreInitialSceneSetup();
			}
		}

		private void OnFocus()
		{
			mainWindow = this;
		}

		private void SavePrefs()
		{
			EditorPrefs.SetInt( PREFS_SEARCH_SCENES, (int) GetSceneSearchMode( false ) );
			EditorPrefs.SetBool( PREFS_SEARCH_ASSETS, searchInAssetsFolder );
			EditorPrefs.SetBool( PREFS_DONT_SEARCH_SOURCE_ASSETS, dontSearchInSourceAssets );
			EditorPrefs.SetInt( PREFS_SEARCH_DEPTH_LIMIT, searchDepthLimit );
			EditorPrefs.SetInt( PREFS_SEARCH_FIELDS, (int) fieldModifiers );
			EditorPrefs.SetInt( PREFS_SEARCH_PROPERTIES, (int) propertyModifiers );
			EditorPrefs.SetInt( PREFS_PATH_DRAWING_MODE, (int) searchResultDrawParameters.pathDrawingMode );
			EditorPrefs.SetBool( PREFS_SEARCH_NON_SERIALIZABLES, searchNonSerializableVariables );
			EditorPrefs.SetBool( PREFS_LAZY_SCENE_SEARCH, lazySceneSearch );
			EditorPrefs.SetBool( PREFS_SHOW_TOOLTIPS, searchResultDrawParameters.showTooltips );
			EditorPrefs.SetBool( PREFS_SHOW_PROGRESS, showDetailedProgressBar );
		}

		private void LoadPrefs()
		{
			ParseSceneSearchMode( (SceneSearchMode) EditorPrefs.GetInt( PREFS_SEARCH_SCENES, (int) ( SceneSearchMode.OpenScenes | SceneSearchMode.ScenesInBuildSettingsTickedOnly | SceneSearchMode.AllScenes ) ) );

			searchInAssetsFolder = EditorPrefs.GetBool( PREFS_SEARCH_ASSETS, true );
			dontSearchInSourceAssets = EditorPrefs.GetBool( PREFS_DONT_SEARCH_SOURCE_ASSETS, true );
			searchDepthLimit = EditorPrefs.GetInt( PREFS_SEARCH_DEPTH_LIMIT, 4 );

			// Fetch public, protected and private non-static fields and properties from objects by default
			fieldModifiers = (BindingFlags) EditorPrefs.GetInt( PREFS_SEARCH_FIELDS, (int) ( BindingFlags.Public | BindingFlags.NonPublic ) );
			propertyModifiers = (BindingFlags) EditorPrefs.GetInt( PREFS_SEARCH_PROPERTIES, (int) ( BindingFlags.Public | BindingFlags.NonPublic ) );

			try
			{
				searchResultDrawParameters.pathDrawingMode = (PathDrawingMode) EditorPrefs.GetInt( PREFS_PATH_DRAWING_MODE, (int) PathDrawingMode.ShortRelevantParts );
			}
			catch
			{
				searchResultDrawParameters.pathDrawingMode = PathDrawingMode.ShortRelevantParts;
			}

			searchNonSerializableVariables = EditorPrefs.GetBool( PREFS_SEARCH_NON_SERIALIZABLES, true );
			lazySceneSearch = EditorPrefs.GetBool( PREFS_LAZY_SCENE_SEARCH, false );
			searchResultDrawParameters.showTooltips = EditorPrefs.GetBool( PREFS_SHOW_TOOLTIPS, false );
			showDetailedProgressBar = EditorPrefs.GetBool( PREFS_SHOW_PROGRESS, false );
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
			// Refresh the window at a regular interval in play mode to update the tooltip
			if( EditorApplication.isPlaying && currentPhase == Phase.Complete && searchResultDrawParameters.showTooltips && EditorApplication.timeSinceStartup >= nextPlayModeRefreshTime )
			{
				nextPlayModeRefreshTime = EditorApplication.timeSinceStartup + PLAY_MODE_REFRESH_INTERVAL; ;
				Repaint();
			}

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

			GUILayout.Space( 10 );

			// Show the error message, if it is not empty
			if( errorMessage.Length > 0 )
				EditorGUILayout.HelpBox( errorMessage, MessageType.Error );

			GUILayout.Space( 10 );

			if( currentPhase == Phase.Processing )
			{
				// If we are stuck at this phase, then we have encountered an exception
				GUILayout.Label( ". . . Search in progress or something went wrong (check console) . . ." );

				restoreInitialSceneSetup = EditorGUILayout.ToggleLeft( "Restore initial scene setup (Recommended)", restoreInitialSceneSetup );

				if( GUILayout.Button( "RETURN", Utilities.GL_HEIGHT_30 ) )
					ReturnToSetupPhase( restoreInitialSceneSetup );
			}
			else if( currentPhase == Phase.Setup )
			{
				if( objectsToSearchDrawer.Draw( objectsToSearch ) )
					errorMessage = string.Empty;

				GUILayout.Space( 10 );

				GUILayout.Box( "SEARCH IN", Utilities.BoxGUIStyle, Utilities.GL_EXPAND_WIDTH );

				searchInAssetsFolder = EditorGUILayout.ToggleLeft( "Project window (Assets folder)", searchInAssetsFolder );

				if( searchInAssetsFolder )
				{
					GUILayout.BeginHorizontal();
					GUILayout.Space( 35 );
					GUILayout.BeginVertical();

					searchInAssetsSubsetDrawer.Draw( searchInAssetsSubset );
					excludedAssetsDrawer.Draw( excludedAssets );

					dontSearchInSourceAssets = EditorGUILayout.ToggleLeft( "Don't search \"Find references of\" themselves for references", dontSearchInSourceAssets );

					GUILayout.EndVertical();
					GUILayout.EndHorizontal();
				}

				GUILayout.Space( 10 );

				if( searchInAllScenes && !EditorApplication.isPlaying )
					GUI.enabled = false;

				searchInOpenScenes = EditorGUILayout.ToggleLeft( "Currently open (loaded) scene(s)", searchInOpenScenes );

				if( !EditorApplication.isPlaying )
				{
					searchInScenesInBuild = EditorGUILayout.ToggleLeft( "Scenes in Build Settings", searchInScenesInBuild );

					if( searchInScenesInBuild )
					{
						GUILayout.BeginHorizontal();
						GUILayout.Space( 35 );

						searchInScenesInBuildTickedOnly = EditorGUILayout.ToggleLeft( "Ticked only", searchInScenesInBuildTickedOnly, Utilities.GL_WIDTH_100 );
						searchInScenesInBuildTickedOnly = !EditorGUILayout.ToggleLeft( "All", !searchInScenesInBuildTickedOnly, Utilities.GL_WIDTH_100 );

						GUILayout.EndHorizontal();
					}

					GUI.enabled = true;

					searchInAllScenes = EditorGUILayout.ToggleLeft( "All scenes in the project", searchInAllScenes );
				}

				GUILayout.BeginHorizontal();
				GUILayout.Space( 35 );
				GUILayout.BeginVertical();

				excludedScenesDrawer.Draw( excludedScenes );

				GUILayout.EndVertical();
				GUILayout.EndHorizontal();

				GUILayout.Space( 10 );

				//GUILayout.Box( "SEARCH SETTINGS", Utilities.BoxGUIStyle, Utilities.GL_EXPAND_WIDTH );

				//GUILayout.BeginHorizontal();

				//GUILayout.Label( new GUIContent( "> Search depth: " + searchDepthLimit, "Depth limit for recursively searching variables of objects" ), Utilities.GL_WIDTH_250 );

				//searchDepthLimit = (int) GUILayout.HorizontalSlider( searchDepthLimit, 0, 4 );

				//GUILayout.EndHorizontal();

				//GUILayout.Label( "> Search variables:" );

				//GUILayout.BeginHorizontal();

				//GUILayout.Space( 35 );

				//if( EditorGUILayout.ToggleLeft( "Public", ( fieldModifiers & BindingFlags.Public ) == BindingFlags.Public, Utilities.GL_WIDTH_100 ) )
				//	fieldModifiers |= BindingFlags.Public;
				//else
				//	fieldModifiers &= ~BindingFlags.Public;

				//if( EditorGUILayout.ToggleLeft( "Non-public", ( fieldModifiers & BindingFlags.NonPublic ) == BindingFlags.NonPublic, Utilities.GL_WIDTH_100 ) )
				//	fieldModifiers |= BindingFlags.NonPublic;
				//else
				//	fieldModifiers &= ~BindingFlags.NonPublic;

				//GUILayout.EndHorizontal();

				//GUILayout.Label( "> Search properties:" );

				//GUILayout.BeginHorizontal();

				//GUILayout.Space( 35 );

				//if( EditorGUILayout.ToggleLeft( "Public", ( propertyModifiers & BindingFlags.Public ) == BindingFlags.Public, Utilities.GL_WIDTH_100 ) )
				//	propertyModifiers |= BindingFlags.Public;
				//else
				//	propertyModifiers &= ~BindingFlags.Public;

				//if( EditorGUILayout.ToggleLeft( "Non-public", ( propertyModifiers & BindingFlags.NonPublic ) == BindingFlags.NonPublic, Utilities.GL_WIDTH_100 ) )
				//	propertyModifiers |= BindingFlags.NonPublic;
				//else
				//	propertyModifiers &= ~BindingFlags.NonPublic;

				//GUILayout.EndHorizontal();

				//GUILayout.Space( 10 );

				//searchNonSerializableVariables = EditorGUILayout.ToggleLeft( "Search non-serializable fields and properties", searchNonSerializableVariables );

				//GUILayout.Space( 10 );

				GUILayout.Box( "SETTINGS", Utilities.BoxGUIStyle, Utilities.GL_EXPAND_WIDTH );

				lazySceneSearch = EditorGUILayout.ToggleLeft( "Lazy scene search: scenes are searched in detail only when they are manually refreshed (faster search)", lazySceneSearch );
				noAssetDatabaseChanges = EditorGUILayout.ToggleLeft( "I haven't modified any assets/scenes since the last search (faster search)", noAssetDatabaseChanges );
				showDetailedProgressBar = EditorGUILayout.ToggleLeft( "Show detailed progress bar (slower search)", showDetailedProgressBar );

				GUILayout.Space( 10 );

				// Don't let the user press the GO button without any valid search location
				if( !searchInAllScenes && !searchInOpenScenes && !searchInScenesInBuild && !searchInAssetsFolder )
					GUI.enabled = false;

				if( GUILayout.Button( "GO!", Utilities.GL_HEIGHT_30 ) )
					InitiateSearch();
			}
			else if( currentPhase == Phase.Complete )
			{
				// Draw the results of the search
				GUI.enabled = false;

				objectsToSearchDrawer.Draw( objectsToSearch );

				GUILayout.Space( 10 );
				GUI.enabled = true;

				restoreInitialSceneSetup = EditorGUILayout.ToggleLeft( "Restore initial scene setup after search is reset (Recommended)", restoreInitialSceneSetup );

				if( GUILayout.Button( "Reset Search", Utilities.GL_HEIGHT_30 ) )
					ReturnToSetupPhase( restoreInitialSceneSetup );

				if( searchResult == null )
				{
					EditorGUILayout.HelpBox( "ERROR: searchResult is null", MessageType.Error );
					return;
				}
				else if( !searchResult.SearchCompletedSuccessfully )
					EditorGUILayout.HelpBox( "ERROR: search was interrupted, check the logs for more info", MessageType.Error );

				Color c = GUI.color;
				GUI.color = Color.green;
				GUILayout.Box( "Don't forget to save scene(s) if you made any changes!", Utilities.BoxGUIStyle, Utilities.GL_EXPAND_WIDTH );
				GUI.color = c;

				if( searchResult.NumberOfGroups == 0 )
				{
					GUILayout.Space( 10 );
					GUILayout.Box( "No references found...", Utilities.BoxGUIStyle, Utilities.GL_EXPAND_WIDTH );
				}
				else
				{
					//GUILayout.Space( 10 );
					//GUILayout.BeginHorizontal();

					//// Select all the references after filtering them (select only the GameObject's)
					//if( GUILayout.Button( "Select All\n(GameObject-wise)", Utilities.GL_HEIGHT_35 ) )
					//{
					//	GameObject[] objects = searchResult.SelectAllAsGameObjects();
					//	if( objects != null && objects.Length > 0 )
					//		Selection.objects = objects;
					//}

					//// Select all the references without filtering them
					//if( GUILayout.Button( "Select All\n(Object-wise)", Utilities.GL_HEIGHT_35 ) )
					//{
					//	Object[] objects = searchResult.SelectAllAsObjects();
					//	if( objects != null && objects.Length > 0 )
					//		Selection.objects = objects;
					//}

					//GUILayout.EndHorizontal();

					//GUILayout.Space( 10 );

					searchResultDrawParameters.showTooltips = EditorGUILayout.ToggleLeft( "Show tooltips", searchResultDrawParameters.showTooltips );
					noAssetDatabaseChanges = EditorGUILayout.ToggleLeft( "I haven't modified any assets/scenes since the last search (faster Refresh)", noAssetDatabaseChanges );
					searchResultDrawParameters.noAssetDatabaseChanges = noAssetDatabaseChanges;

					GUILayout.Space( 10 );

					GUILayout.Label( "Path drawing mode:" );

					GUILayout.BeginHorizontal();

					GUILayout.Space( 35 );

					if( EditorGUILayout.ToggleLeft( "Full: Draw the complete paths to the references", searchResultDrawParameters.pathDrawingMode == PathDrawingMode.Full ) )
						searchResultDrawParameters.pathDrawingMode = PathDrawingMode.Full;

					GUILayout.EndHorizontal();

					GUILayout.BeginHorizontal();

					GUILayout.Space( 35 );

					if( EditorGUILayout.ToggleLeft( "Shorter: Draw only the most relevant parts (that start with a UnityEngine.Object) of the complete paths", searchResultDrawParameters.pathDrawingMode == PathDrawingMode.ShortRelevantParts ) )
						searchResultDrawParameters.pathDrawingMode = PathDrawingMode.ShortRelevantParts;

					GUILayout.EndHorizontal();

					GUILayout.BeginHorizontal();

					GUILayout.Space( 35 );

					if( EditorGUILayout.ToggleLeft( "Shortest: Draw only the last two nodes of complete paths", searchResultDrawParameters.pathDrawingMode == PathDrawingMode.Shortest ) )
						searchResultDrawParameters.pathDrawingMode = PathDrawingMode.Shortest;

					GUILayout.EndHorizontal();

					searchResult.DrawOnGUI( searchResultDrawParameters );
				}
			}

			GUILayout.Space( 10 );

			GUILayout.EndVertical();

			EditorGUILayout.EndScrollView();
		}

		private void InitiateSearch()
		{
			if( objectsToSearch.IsEmpty() )
				errorMessage = "ADD AN ASSET TO THE LIST FIRST!";
			else if( !EditorApplication.isPlaying && !Utilities.AreScenesSaved() )
			{
				// Don't start the search if at least one scene is currently dirty (not saved)
				errorMessage = "SAVE OPEN SCENES FIRST!";
			}
			else
			{
				errorMessage = string.Empty;
				currentPhase = Phase.Processing;

				SavePrefs();

#if UNITY_2018_3_OR_NEWER
				ReplacePrefabStageObjectsWithAssets( UnityEditor.Experimental.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage() );
#endif

				// Start searching
				searchResult = core.Run( new AssetUsageDetector.Parameters()
				{
					objectsToSearch = new ObjectToSearchEnumerator( objectsToSearch ).ToArray(),
					searchInScenes = GetSceneSearchMode( true ),
					searchInAssetsFolder = searchInAssetsFolder,
					searchInAssetsSubset = !searchInAssetsSubset.IsEmpty() ? searchInAssetsSubset.ToArray() : null,
					excludedAssetsFromSearch = !excludedAssets.IsEmpty() ? excludedAssets.ToArray() : null,
					dontSearchInSourceAssets = dontSearchInSourceAssets,
					excludedScenesFromSearch = !excludedScenes.IsEmpty() ? excludedScenes.ToArray() : null,
					//fieldModifiers = fieldModifiers,
					//propertyModifiers = propertyModifiers,
					//searchDepthLimit = searchDepthLimit,
					//searchNonSerializableVariables = searchNonSerializableVariables,
					lazySceneSearch = lazySceneSearch,
					noAssetDatabaseChanges = noAssetDatabaseChanges,
					showDetailedProgressBar = showDetailedProgressBar
				} );

				currentPhase = Phase.Complete;
			}
		}

#if UNITY_2018_3_OR_NEWER
		// Try replacing searched objects who are part of currently open prefab stage with their corresponding prefab assets
		public void ReplacePrefabStageObjectsWithAssets( UnityEditor.Experimental.SceneManagement.PrefabStage prefabStage )
		{
			if( prefabStage == null || !prefabStage.stageHandle.IsValid() )
				return;

			GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>( prefabStage.prefabAssetPath );
			if( prefabAsset == null )
				return;

			for( int i = 0; i < objectsToSearch.Count; i++ )
			{
				Object obj = objectsToSearch[i].obj;
				if( obj != null && !obj.Equals( null ) && obj is GameObject && prefabStage.IsPartOfPrefabContents( (GameObject) obj ) )
				{
					GameObject prefabStageObjectSource = ( (GameObject) obj ).FollowSymmetricHierarchy( prefabAsset );
					if( prefabStageObjectSource != null )
						objectsToSearch[i].obj = prefabStageObjectSource;

					List<ObjectToSearch.SubAsset> subAssets = objectsToSearch[i].subAssets;
					for( int j = 0; j < subAssets.Count; j++ )
					{
						obj = subAssets[j].subAsset;
						if( obj != null && !obj.Equals( null ) && obj is GameObject && prefabStage.IsPartOfPrefabContents( (GameObject) obj ) )
						{
							prefabStageObjectSource = ( (GameObject) obj ).FollowSymmetricHierarchy( prefabAsset );
							if( prefabStageObjectSource != null )
								subAssets[j].subAsset = prefabStageObjectSource;
						}
					}
				}
			}
		}
#endif

		private bool ReturnToSetupPhase( bool restoreInitialSceneSetup )
		{
			if( searchResult != null && restoreInitialSceneSetup && !EditorApplication.isPlaying && !searchResult.RestoreInitialSceneSetup() )
				return false;

			searchResult = null;

			errorMessage = string.Empty;
			currentPhase = Phase.Setup;

			return true;
		}
	}
}