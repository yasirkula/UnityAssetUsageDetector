// Asset Usage Detector - by Suleyman Yasir KULA (yasirkula@gmail.com)

using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System;
using System.IO;
using UnityEngine.UI;
using System.Text;
#if UNITY_2017_1_OR_NEWER
using UnityEngine.U2D;
#if UNITY_2018_2_OR_NEWER
using UnityEditor.U2D;
#endif
using UnityEngine.Playables;
#endif
#if UNITY_2017_2_OR_NEWER
using UnityEngine.Tilemaps;
#endif
using Object = UnityEngine.Object;

namespace AssetUsageDetectorNamespace
{
	[Flags]
	public enum SceneSearchMode { None = 0, OpenScenes = 1, ScenesInBuildSettingsAll = 2, ScenesInBuildSettingsTickedOnly = 4, AllScenes = 8 };
	public enum PathDrawingMode { Full = 0, ShortRelevantParts = 1, Shortest = 2 };

	public class AssetUsageDetector
	{
		#region Helper Classes
		[Serializable]
		public class Parameters
		{
			public Object[] objectsToSearch = null;

			public SceneSearchMode searchInScenes = SceneSearchMode.AllScenes;
			public Object[] searchInScenesSubset = null;
			public bool searchInAssetsFolder = true;
			public Object[] searchInAssetsSubset = null;
			public Object[] excludedAssetsFromSearch = null;
			public bool dontSearchInSourceAssets = true;
			public Object[] excludedScenesFromSearch = null;

			public int searchDepthLimit = 4;
			public BindingFlags fieldModifiers = BindingFlags.Public | BindingFlags.NonPublic;
			public BindingFlags propertyModifiers = BindingFlags.Public | BindingFlags.NonPublic;
			public bool searchNonSerializableVariables = true;

			public bool lazySceneSearch = false;
			public bool noAssetDatabaseChanges = false;
			public bool showDetailedProgressBar = false;
		}

		private class CacheEntry
		{
			public enum Result { Unknown = 0, No = 1, Yes = 2 };

			public string hash;
			public string[] dependencies;
			public long[] fileSizes;

			public bool verified;
			public Result searchResult;

			public CacheEntry( string path )
			{
				Verify( path );
			}

			public CacheEntry( string hash, string[] dependencies, long[] fileSizes )
			{
				this.hash = hash;
				this.dependencies = dependencies;
				this.fileSizes = fileSizes;
			}

			public void Verify( string path )
			{
				string hash = AssetDatabase.GetAssetDependencyHash( path ).ToString();
				if( this.hash != hash )
				{
					this.hash = hash;
					Refresh( path );
				}

				verified = true;
			}

			public void Refresh( string path )
			{
				dependencies = AssetDatabase.GetDependencies( path, false );
				if( fileSizes == null || fileSizes.Length != dependencies.Length )
					fileSizes = new long[dependencies.Length];

				for( int i = 0; i < dependencies.Length; i++ )
				{
					FileInfo assetFile = new FileInfo( dependencies[i] );
					fileSizes[i] = assetFile.Exists ? assetFile.Length : 0L;
				}
			}
		}
		#endregion

		// A set that contains the searched asset(s) and their sub-assets (if any)
		private readonly HashSet<Object> objectsToSearchSet = new HashSet<Object>();

		// Scene object(s) in objectsToSearchSet
		private readonly HashSet<Object> sceneObjectsToSearchSet = new HashSet<Object>();
		// sceneObjectsToSearchSet's scene(s)
		private readonly HashSet<string> sceneObjectsToSearchScenesSet = new HashSet<string>();

		// Project asset(s) in objectsToSearchSet
		private readonly HashSet<Object> assetsToSearchSet = new HashSet<Object>();
		// assetsToSearchSet's path(s)
		private readonly HashSet<string> assetsToSearchPathsSet = new HashSet<string>();
		// The root prefab objects in assetsToSearch that will be used to search for prefab references
		private readonly List<GameObject> assetsToSearchRootPrefabs = new List<GameObject>( 4 );
		// Path(s) of the assets that should be excluded from the search
		private readonly HashSet<string> excludedAssetsPathsSet = new HashSet<string>();

		// Results for the currently searched scene
		private SearchResultGroup currentSearchResultGroup;

		// An optimization to fetch & filter fields and properties of a class only once
		private readonly Dictionary<Type, VariableGetterHolder[]> typeToVariables = new Dictionary<Type, VariableGetterHolder[]>( 4096 );
		// An optimization to search an object only once (key is a hash of the searched object)
		private readonly Dictionary<string, ReferenceNode> searchedObjects = new Dictionary<string, ReferenceNode>( 32768 );
		// An optimization to fetch an animation clip's curve bindings only once
		private readonly Dictionary<AnimationClip, EditorCurveBinding[]> animationClipUniqueBindings = new Dictionary<AnimationClip, EditorCurveBinding[]>( 256 );
		// An optimization to fetch the dependencies of an asset only once (key is the path of the asset)
		private Dictionary<string, CacheEntry> assetDependencyCache;
		private CacheEntry lastRefreshedCacheEntry;

		// Dictionary to quickly find the function to search a specific type with
		private Dictionary<Type, Func<Object, ReferenceNode>> typeToSearchFunction;

		// Stack of SearchObject function parameters to avoid infinite loops (which happens when same object is passed as parameter to function)
		private readonly List<object> callStack = new List<object>( 64 );

		private bool searchPrefabConnections;
		private bool searchMonoBehavioursForScript;
		private bool searchRenderers;
		private bool searchMaterialsForShader;
		private bool searchMaterialsForTexture;

		private bool searchSerializableVariablesOnly;
		private bool prevSearchSerializableVariablesOnly;

		private int searchDepthLimit; // Depth limit for recursively searching variables of objects

		private Object currentSearchedObject;
		private int currentDepth;

		private bool dontSearchInSourceAssets;
		private bool searchingSourceAssets;
		private bool isInPlayMode;

#if UNITY_2018_3_OR_NEWER
		private UnityEditor.Experimental.SceneManagement.PrefabStage openPrefabStage;
		private GameObject openPrefabStagePrefabAsset;
#endif

		private BindingFlags fieldModifiers, propertyModifiers;
		private BindingFlags prevFieldModifiers, prevPropertyModifiers;

		private int searchedObjectsCount; // Number of searched objects
		private double searchStartTime;

		private readonly List<ReferenceNode> nodesPool = new List<ReferenceNode>( 32 );
		private readonly List<VariableGetterHolder> validVariables = new List<VariableGetterHolder>( 32 );

		private string CachePath { get { return Application.dataPath + "/../Library/AssetUsageDetector.cache"; } } // Path of the cache file

		// Search for references!
		public SearchResult Run( Parameters searchParameters )
		{
			if( searchParameters == null )
			{
				Debug.LogError( "searchParameters must not be null!" );
				return new SearchResult( false, null, null, this, searchParameters );
			}

			if( searchParameters.objectsToSearch == null )
			{
				Debug.LogError( "objectsToSearch list is empty!" );
				return new SearchResult( false, null, null, this, searchParameters );
			}

			if( !EditorApplication.isPlaying && !Utilities.AreScenesSaved() )
			{
				// Don't start the search if at least one scene is currently dirty (not saved)
				Debug.LogError( "Save open scenes first!" );
				return new SearchResult( false, null, null, this, searchParameters );
			}

			List<SearchResultGroup> searchResult = null;

			isInPlayMode = EditorApplication.isPlaying;
#if UNITY_2018_3_OR_NEWER
			openPrefabStagePrefabAsset = null;
			string openPrefabStageAssetPath = null;
			openPrefabStage = UnityEditor.Experimental.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
			if( openPrefabStage != null )
			{
				if( !openPrefabStage.stageHandle.IsValid() )
					openPrefabStage = null;
				else
				{
					if( openPrefabStage.scene.isDirty )
					{
						// Don't start the search if a prefab stage is currently open and dirty (not saved)
						Debug.LogError( "Save open prefabs first!" );
						return new SearchResult( false, null, null, this, searchParameters );
					}

					openPrefabStagePrefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>( openPrefabStage.prefabAssetPath );
					openPrefabStageAssetPath = openPrefabStage.prefabAssetPath;
				}
			}
#endif

			// Get the scenes that are open right now
			SceneSetup[] initialSceneSetup = !isInPlayMode ? EditorSceneManager.GetSceneManagerSetup() : null;

			// Make sure the AssetDatabase is up-to-date
			AssetDatabase.SaveAssets();

			try
			{
				currentDepth = 0;
				searchedObjectsCount = 0;
				searchStartTime = EditorApplication.timeSinceStartup;

				this.fieldModifiers = searchParameters.fieldModifiers | BindingFlags.Instance | BindingFlags.DeclaredOnly;
				this.propertyModifiers = searchParameters.propertyModifiers | BindingFlags.Instance | BindingFlags.DeclaredOnly;
				this.searchDepthLimit = searchParameters.searchDepthLimit;
				this.searchSerializableVariablesOnly = !searchParameters.searchNonSerializableVariables;
				this.dontSearchInSourceAssets = searchParameters.dontSearchInSourceAssets;

				// Initialize commonly used variables
				searchResult = new List<SearchResultGroup>(); // Overall search results

				if( prevFieldModifiers != fieldModifiers || prevPropertyModifiers != propertyModifiers || prevSearchSerializableVariablesOnly != searchSerializableVariablesOnly )
					typeToVariables.Clear();

				searchedObjects.Clear();
				animationClipUniqueBindings.Clear();
				callStack.Clear();
				objectsToSearchSet.Clear();
				sceneObjectsToSearchSet.Clear();
				sceneObjectsToSearchScenesSet.Clear();
				assetsToSearchSet.Clear();
				assetsToSearchPathsSet.Clear();
				assetsToSearchRootPrefabs.Clear();
				excludedAssetsPathsSet.Clear();

				if( assetDependencyCache == null )
				{
					LoadCache();
					searchStartTime = EditorApplication.timeSinceStartup;
				}
				else if( !searchParameters.noAssetDatabaseChanges )
				{
					foreach( var cacheEntry in assetDependencyCache.Values )
						cacheEntry.verified = false;
				}

				foreach( var cacheEntry in assetDependencyCache.Values )
					cacheEntry.searchResult = CacheEntry.Result.Unknown;

				lastRefreshedCacheEntry = null;

				if( typeToSearchFunction == null )
				{
					typeToSearchFunction = new Dictionary<Type, Func<Object, ReferenceNode>>()
					{
						{ typeof( GameObject ), SearchGameObject },
						{ typeof( Material ), SearchMaterial },
						{ typeof( RuntimeAnimatorController ), SearchAnimatorController },
						{ typeof( AnimatorOverrideController ), SearchAnimatorController },
						{ typeof( AnimatorController ), SearchAnimatorController },
						{ typeof( AnimatorStateMachine ), SearchAnimatorStateMachine },
						{ typeof( AnimatorState ), SearchAnimatorState },
						{ typeof( AnimatorStateTransition ), SearchAnimatorStateTransition },
						{ typeof( BlendTree ), SearchBlendTree },
						{ typeof( AnimationClip ), SearchAnimationClip },
#if UNITY_2017_1_OR_NEWER
						{ typeof( SpriteAtlas ), SearchSpriteAtlas },
#endif
					};
				}

				prevFieldModifiers = fieldModifiers;
				prevPropertyModifiers = propertyModifiers;
				prevSearchSerializableVariablesOnly = searchSerializableVariablesOnly;

				searchPrefabConnections = false;
				searchMonoBehavioursForScript = false;
				searchRenderers = false;
				searchMaterialsForShader = false;
				searchMaterialsForTexture = false;

				// Store the searched objects(s) in HashSets
				HashSet<string> folderContentsSet = new HashSet<string>();
				foreach( Object obj in searchParameters.objectsToSearch )
				{
					if( obj == null || obj.Equals( null ) )
						continue;

					if( obj.IsFolder() )
						folderContentsSet.UnionWith( Utilities.EnumerateFolderContents( obj ) );
					else
						AddSearchedObjectToFilteredSets( obj, true );
				}

				foreach( string filePath in folderContentsSet )
				{
					// Skip scene assets
					if( filePath.EndsWith( ".unity" ) )
						continue;

					Object[] assets = AssetDatabase.LoadAllAssetsAtPath( filePath );
					if( assets == null || assets.Length == 0 )
						continue;

					for( int i = 0; i < assets.Length; i++ )
						AddSearchedObjectToFilteredSets( assets[i], true );
				}

				foreach( Object obj in objectsToSearchSet )
				{
					if( obj is Texture )
					{
						searchRenderers = true;
						searchMaterialsForTexture = true;
					}
					else if( obj is Material )
					{
						searchRenderers = true;
					}
					else if( obj is MonoScript )
					{
						searchMonoBehavioursForScript = true;
					}
					else if( obj is Shader )
					{
						searchRenderers = true;
						searchMaterialsForShader = true;
					}
					else if( obj is GameObject )
					{
						searchPrefabConnections = true;
					}
				}

				// Find the scenes to search for references
				HashSet<string> scenesToSearch = new HashSet<string>();
				if( searchParameters.searchInScenesSubset != null )
				{
					foreach( Object obj in searchParameters.searchInScenesSubset )
					{
						if( obj == null || obj.Equals( null ) )
							continue;

						if( !obj.IsAsset() )
							continue;

						if( obj.IsFolder() )
						{
							string[] folderContents = AssetDatabase.FindAssets( "t:SceneAsset", new string[] { AssetDatabase.GetAssetPath( obj ) } );
							if( folderContents == null )
								continue;

							for( int i = 0; i < folderContents.Length; i++ )
								scenesToSearch.Add( AssetDatabase.GUIDToAssetPath( folderContents[i] ) );
						}
						else if( obj is SceneAsset )
							scenesToSearch.Add( AssetDatabase.GetAssetPath( obj ) );
					}
				}
				else if( ( searchParameters.searchInScenes & SceneSearchMode.AllScenes ) == SceneSearchMode.AllScenes )
				{
					// Get all scenes from the Assets folder
					string[] sceneGuids = AssetDatabase.FindAssets( "t:SceneAsset" );
					for( int i = 0; i < sceneGuids.Length; i++ )
						scenesToSearch.Add( AssetDatabase.GUIDToAssetPath( sceneGuids[i] ) );
				}
				else
				{
					if( ( searchParameters.searchInScenes & SceneSearchMode.OpenScenes ) == SceneSearchMode.OpenScenes )
					{
						// Get all open (and loaded) scenes
						for( int i = 0; i < EditorSceneManager.loadedSceneCount; i++ )
						{
							Scene scene = EditorSceneManager.GetSceneAt( i );
							if( scene.IsValid() )
								scenesToSearch.Add( scene.path );
						}
					}

					bool searchInScenesInBuildTickedAll = ( searchParameters.searchInScenes & SceneSearchMode.ScenesInBuildSettingsAll ) == SceneSearchMode.ScenesInBuildSettingsAll;
					if( searchInScenesInBuildTickedAll || ( searchParameters.searchInScenes & SceneSearchMode.ScenesInBuildSettingsTickedOnly ) == SceneSearchMode.ScenesInBuildSettingsTickedOnly )
					{
						// Get all scenes in build settings
						EditorBuildSettingsScene[] scenesTemp = EditorBuildSettings.scenes;
						for( int i = 0; i < scenesTemp.Length; i++ )
						{
							if( ( searchInScenesInBuildTickedAll || scenesTemp[i].enabled ) )
								scenesToSearch.Add( scenesTemp[i].path );
						}
					}
				}

				// In Play mode, only open scenes can be searched
				if( isInPlayMode )
				{
					HashSet<string> openScenes = new HashSet<string>();
					for( int i = 0; i < EditorSceneManager.loadedSceneCount; i++ )
					{
						Scene scene = EditorSceneManager.GetSceneAt( i );
						if( scene.IsValid() )
							openScenes.Add( scene.path );
					}

					scenesToSearch.RemoveWhere( ( path ) =>
					{
						if( !openScenes.Contains( path ) )
						{
							Debug.Log( "Can't search unloaded scenes while in play mode, skipped " + path );
							return true;
						}

						return false;
					} );
				}

				// Initialize the nodes of searched asset(s)
				foreach( Object obj in objectsToSearchSet )
					searchedObjects.Add( obj.Hash(), PopReferenceNode( obj ) );

				// Progressbar values
				int searchProgress = 0;
				int searchTotalProgress = scenesToSearch.Count;
				if( isInPlayMode && searchParameters.searchInScenes != SceneSearchMode.None )
					searchTotalProgress++; // DontDestroyOnLoad scene

				// Don't search assets if searched object(s) are all scene objects as assets can't hold references to scene objects
				if( searchParameters.searchInAssetsFolder && assetsToSearchSet.Count > 0 )
				{
					currentSearchResultGroup = new SearchResultGroup( "Project Window (Assets)", SearchResultGroup.GroupType.Assets );

					// Get the paths of all assets that are to be searched
					IEnumerable<string> assetPaths;
					if( searchParameters.searchInAssetsSubset == null )
					{
						string[] allAssetPaths = AssetDatabase.GetAllAssetPaths();
						assetPaths = allAssetPaths;

						if( searchParameters.showDetailedProgressBar )
							searchTotalProgress += allAssetPaths.Length;
					}
					else
					{
						folderContentsSet.Clear();

						foreach( Object obj in searchParameters.searchInAssetsSubset )
						{
							if( obj == null || obj.Equals( null ) )
								continue;

							if( !obj.IsAsset() )
								continue;

							if( obj.IsFolder() )
								folderContentsSet.UnionWith( Utilities.EnumerateFolderContents( obj ) );
							else
								folderContentsSet.Add( AssetDatabase.GetAssetPath( obj ) );
						}

						assetPaths = folderContentsSet;

						if( searchParameters.showDetailedProgressBar )
							searchTotalProgress += folderContentsSet.Count;
					}

					// Calculate the path(s) of the assets that won't be searched for references
					if( searchParameters.excludedAssetsFromSearch != null )
					{
						foreach( Object obj in searchParameters.excludedAssetsFromSearch )
						{
							if( obj == null || obj.Equals( null ) )
								continue;

							if( !obj.IsAsset() )
								continue;

							if( obj.IsFolder() )
								excludedAssetsPathsSet.UnionWith( Utilities.EnumerateFolderContents( obj ) );
							else
								excludedAssetsPathsSet.Add( AssetDatabase.GetAssetPath( obj ) );
						}
					}

					if( EditorUtility.DisplayCancelableProgressBar( "Please wait...", "Searching assets", 0f ) )
						throw new Exception( "Search aborted" );

					foreach( string path in assetPaths )
					{
						if( searchParameters.showDetailedProgressBar && ++searchProgress % 30 == 1 && EditorUtility.DisplayCancelableProgressBar( "Please wait...", "Searching assets", (float) searchProgress / searchTotalProgress ) )
							throw new Exception( "Search aborted" );

						if( excludedAssetsPathsSet.Contains( path ) )
							continue;

						// If asset resides inside the Assets directory and is not a scene asset
						if( path.StartsWith( "Assets/" ) && !path.EndsWith( ".unity" ) )
						{
							if( !AssetHasAnyReference( path ) )
								continue;

							Object[] assets = AssetDatabase.LoadAllAssetsAtPath( path );
							if( assets == null || assets.Length == 0 )
								continue;

							for( int i = 0; i < assets.Length; i++ )
							{
								// Components are already searched while searching the GameObject
								if( assets[i] is Component )
									continue;

								BeginSearchObject( assets[i] );
							}
						}
					}

					// If a reference is found in the Project view, save the results
					if( currentSearchResultGroup.NumberOfReferences > 0 )
						searchResult.Add( currentSearchResultGroup );
				}

				// Search non-serializable variables for references while searching a scene in play mode
				if( isInPlayMode )
					searchSerializableVariablesOnly = false;

				if( scenesToSearch.Count > 0 )
				{
					// Calculate the path(s) of the scenes that won't be searched for references
					HashSet<string> excludedScenesPathsSet = new HashSet<string>();
					if( searchParameters.excludedScenesFromSearch != null )
					{
						foreach( Object obj in searchParameters.excludedScenesFromSearch )
						{
							if( obj == null || obj.Equals( null ) )
								continue;

							if( !obj.IsAsset() )
								continue;

							if( obj.IsFolder() )
							{
								string[] folderContents = AssetDatabase.FindAssets( "t:SceneAsset", new string[] { AssetDatabase.GetAssetPath( obj ) } );
								if( folderContents == null )
									continue;

								for( int i = 0; i < folderContents.Length; i++ )
									excludedScenesPathsSet.Add( AssetDatabase.GUIDToAssetPath( folderContents[i] ) );
							}
							else if( obj is SceneAsset )
								excludedScenesPathsSet.Add( AssetDatabase.GetAssetPath( obj ) );
						}
					}

					foreach( string scenePath in scenesToSearch )
					{
						if( EditorUtility.DisplayCancelableProgressBar( "Please wait...", "Searching scene: " + scenePath, (float) ++searchProgress / searchTotalProgress ) )
							throw new Exception( "Search aborted" );

						// Search scene for references
						if( string.IsNullOrEmpty( scenePath ) )
							continue;

						if( excludedScenesPathsSet.Contains( scenePath ) )
							continue;

						SearchScene( scenePath, searchResult, searchParameters.lazySceneSearch, initialSceneSetup );
					}
				}

				// Search through all the GameObjects under the DontDestroyOnLoad scene (if exists)
				if( isInPlayMode && searchParameters.searchInScenes != SceneSearchMode.None )
				{
					if( searchParameters.showDetailedProgressBar && EditorUtility.DisplayCancelableProgressBar( "Please wait...", "Searching scene: DontDestroyOnLoad", 1f ) )
						throw new Exception( "Search aborted" );

					currentSearchResultGroup = new SearchResultGroup( "DontDestroyOnLoad", SearchResultGroup.GroupType.DontDestroyOnLoad );

					GameObject[] rootGameObjects = GetDontDestroyOnLoadObjects();
					for( int i = 0; i < rootGameObjects.Length; i++ )
						SearchGameObjectRecursively( rootGameObjects[i] );

					if( currentSearchResultGroup.NumberOfReferences > 0 )
						searchResult.Add( currentSearchResultGroup );
				}

				// Searching source assets last prevents some references from being excluded due to callStack.ContainsFast
				if( !dontSearchInSourceAssets )
				{
					searchingSourceAssets = true;

					foreach( Object obj in objectsToSearchSet )
					{
						currentSearchedObject = obj;
						SearchObject( obj );
					}

					searchingSourceAssets = false;
				}

				InitializeSearchResultNodes( searchResult );

				// Log some c00l stuff to console
				Debug.Log( "Searched " + searchedObjectsCount + " objects in " + ( EditorApplication.timeSinceStartup - searchStartTime ).ToString( "F2" ) + " seconds" );

				return new SearchResult( true, searchResult, initialSceneSetup, this, searchParameters );
			}
			catch( Exception e )
			{
				StringBuilder sb = new StringBuilder( objectsToSearchSet.Count * 50 + callStack.Count * 50 + 500 );
				sb.AppendLine( "<b>AssetUsageDetector Error:</b>" ).AppendLine();
				if( callStack.Count > 0 )
				{
					sb.AppendLine( "Stack contents: " );
					for( int i = callStack.Count - 1; i >= 0; i-- )
					{
						sb.Append( i ).Append( ": " );

						Object unityObject = callStack[i] as Object;
						if( unityObject )
							sb.Append( unityObject.name ).Append( " (" ).Append( unityObject.GetType() ).AppendLine( ")" );
						else if( callStack[i] != null )
							sb.Append( callStack[i].GetType() ).AppendLine( " object" );
						else
							sb.AppendLine( "<<destroyed>>" );
					}

					sb.AppendLine();
				}

				sb.AppendLine( "Searching references of: " );
				foreach( Object obj in objectsToSearchSet )
				{
					if( obj )
						sb.Append( obj.name ).Append( " (" ).Append( obj.GetType() ).AppendLine( ")" );
				}

				sb.AppendLine();
				sb.Append( e ).AppendLine();

				Debug.LogError( sb.ToString() );

				try
				{
					InitializeSearchResultNodes( searchResult );
				}
				catch
				{ }

				return new SearchResult( false, searchResult, initialSceneSetup, this, searchParameters );
			}
			finally
			{
				currentSearchResultGroup = null;
				currentSearchedObject = null;

				EditorUtility.ClearProgressBar();

#if UNITY_2018_3_OR_NEWER
				// If a prefab stage was open when the search was triggered, try reopening the prefab stage after the search is completed
				if( !string.IsNullOrEmpty( openPrefabStageAssetPath ) )
					AssetDatabase.OpenAsset( AssetDatabase.LoadAssetAtPath<GameObject>( openPrefabStageAssetPath ) );
#endif
			}
		}

		private void InitializeSearchResultNodes( List<SearchResultGroup> searchResult )
		{
			for( int i = 0; i < searchResult.Count; i++ )
				searchResult[i].InitializeNodes( GetReferenceNode );

			// If there are any empty groups after node initialization, remove those groups
			for( int i = searchResult.Count - 1; i >= 0; i-- )
			{
				if( !searchResult[i].PendingSearch && searchResult[i].NumberOfReferences == 0 )
					searchResult.RemoveAtFast( i );
			}
		}

		// Checks if object is asset or scene object and adds it to the corresponding HashSet(s)
		private void AddSearchedObjectToFilteredSets( Object obj, bool expandGameObjects )
		{
			if( obj == null || obj.Equals( null ) )
				return;

			if( obj is SceneAsset )
				return;

			objectsToSearchSet.Add( obj );

#if UNITY_2018_3_OR_NEWER
			// When searching for references of a prefab stage object, try adding its corresponding prefab asset to the searched assets, as well
			if( openPrefabStage != null && openPrefabStagePrefabAsset != null && obj is GameObject && openPrefabStage.IsPartOfPrefabContents( (GameObject) obj ) )
			{
				GameObject prefabStageObjectSource = ( (GameObject) obj ).FollowSymmetricHierarchy( openPrefabStagePrefabAsset );
				if( prefabStageObjectSource != null )
					AddSearchedObjectToFilteredSets( prefabStageObjectSource, expandGameObjects );
			}
#endif

			bool isAsset = obj.IsAsset();
			if( isAsset )
			{
				assetsToSearchSet.Add( obj );

				string assetPath = AssetDatabase.GetAssetPath( obj );
				if( !string.IsNullOrEmpty( assetPath ) )
				{
					assetsToSearchPathsSet.Add( assetPath );
					if( dontSearchInSourceAssets && AssetDatabase.IsMainAsset( obj ) )
						excludedAssetsPathsSet.Add( assetPath );
				}

				GameObject go = null;
				if( obj is GameObject )
					go = (GameObject) obj;
				else if( obj is Component )
					go = ( (Component) obj ).gameObject;

				if( go != null )
				{
					Transform transform = go.transform;
					bool shouldAddRootPrefabEntry = true;
					for( int i = assetsToSearchRootPrefabs.Count - 1; i >= 0; i-- )
					{
						Transform rootTransform = assetsToSearchRootPrefabs[i].transform;
						if( transform.IsChildOf( rootTransform ) )
						{
							shouldAddRootPrefabEntry = false;
							break;
						}

						if( rootTransform.IsChildOf( transform ) )
							assetsToSearchRootPrefabs.RemoveAt( i );
					}

					if( shouldAddRootPrefabEntry )
						assetsToSearchRootPrefabs.Add( go );
				}
			}
			else
			{
				sceneObjectsToSearchSet.Add( obj );

				if( obj is GameObject )
					sceneObjectsToSearchScenesSet.Add( ( (GameObject) obj ).scene.path );
				else if( obj is Component )
					sceneObjectsToSearchScenesSet.Add( ( (Component) obj ).gameObject.scene.path );
			}

			if( expandGameObjects && obj is GameObject )
			{
				// If searched asset is a GameObject, include its components in the search
				Component[] components = ( (GameObject) obj ).GetComponents<Component>();
				for( int i = 0; i < components.Length; i++ )
				{
					if( components[i] == null || components[i].Equals( null ) )
						continue;

					objectsToSearchSet.Add( components[i] );

					if( isAsset )
						assetsToSearchSet.Add( components[i] );
					else
						sceneObjectsToSearchSet.Add( components[i] );
				}
			}
			else if( obj is Component )
			{
				// Include searched components' GameObjects in the search, as well
				AddSearchedObjectToFilteredSets( ( (Component) obj ).gameObject, false );
			}
		}

		// Search a scene for references
		private void SearchScene( string scenePath, List<SearchResultGroup> searchResult, bool lazySearch, SceneSetup[] initialSceneSetup )
		{
			Scene scene = EditorSceneManager.GetSceneByPath( scenePath );
			if( isInPlayMode && !scene.isLoaded )
				return;

			bool canContainSceneObjectReference = scene.isLoaded && ( !EditorSceneManager.preventCrossSceneReferences || sceneObjectsToSearchScenesSet.Contains( scenePath ) );
			if( !canContainSceneObjectReference )
			{
				bool canContainAssetReference = assetsToSearchSet.Count > 0 && ( isInPlayMode || AssetHasAnyReference( scenePath ) );
				if( !canContainAssetReference )
					return;
			}

			if( !scene.isLoaded )
			{
				if( lazySearch )
				{
					searchResult.Add( new SearchResultGroup( scenePath, SearchResultGroup.GroupType.Scene, true, true ) );
					return;
				}

				scene = EditorSceneManager.OpenScene( scenePath, OpenSceneMode.Additive );
			}

			currentSearchResultGroup = new SearchResultGroup( scenePath, SearchResultGroup.GroupType.Scene );

			// Search through all the GameObjects in the scene
			GameObject[] rootGameObjects = scene.GetRootGameObjects();
			for( int i = 0; i < rootGameObjects.Length; i++ )
				SearchGameObjectRecursively( rootGameObjects[i] );

			// If no references are found in the scene and if the scene is not part of the initial scene setup, close it
			if( currentSearchResultGroup.NumberOfReferences == 0 )
			{
				if( !isInPlayMode )
				{
					bool sceneIsOneOfInitials = false;
					for( int i = 0; i < initialSceneSetup.Length; i++ )
					{
						if( initialSceneSetup[i].path == scenePath )
						{
							if( !initialSceneSetup[i].isLoaded )
								EditorSceneManager.CloseScene( scene, false );

							sceneIsOneOfInitials = true;
							break;
						}
					}

					if( !sceneIsOneOfInitials )
						EditorSceneManager.CloseScene( scene, true );
				}
			}
			else
			{
				// Some references are found in this scene, save the results
				searchResult.Add( currentSearchResultGroup );
			}
		}

		// Search a GameObject and its children for references recursively
		private void SearchGameObjectRecursively( GameObject go )
		{
			BeginSearchObject( go );

			Transform tr = go.transform;
			for( int i = 0; i < tr.childCount; i++ )
				SearchGameObjectRecursively( tr.GetChild( i ).gameObject );
		}

		// Begin searching a root object (like a GameObject or an asset)
		private void BeginSearchObject( Object obj )
		{
			if( obj is SceneAsset )
				return;

			currentSearchedObject = obj;

			ReferenceNode searchResult = SearchObject( obj );
			if( searchResult != null )
				currentSearchResultGroup.AddReference( searchResult );
		}

		// Search an object for references
		private ReferenceNode SearchObject( object obj )
		{
			if( obj == null || obj.Equals( null ) )
				return null;

			// Avoid recursion (which leads to stackoverflow exception) using a stack
			if( callStack.ContainsFast( obj ) )
				return null;

			bool searchingSourceAsset = searchingSourceAssets && ReferenceEquals( currentSearchedObject, obj );

			// Hashing does not work well with structs all the time, don't cache search results for structs
			string objHash = null;
			if( !( obj is ValueType ) )
			{
				objHash = obj.Hash();

				if( !searchingSourceAsset )
				{
					// If object was searched before, return the cached result
					ReferenceNode cachedResult;
					if( searchedObjects.TryGetValue( objHash, out cachedResult ) )
						return cachedResult;
				}
			}

			searchedObjectsCount++;

			ReferenceNode result;
			Object unityObject = obj as Object;
			if( unityObject != null )
			{
				// If the Object is an asset, search it in detail only if its dependencies contain at least one of the searched asset(s)
				if( unityObject.IsAsset() && ( assetsToSearchSet.Count == 0 || !AssetHasAnyReference( AssetDatabase.GetAssetPath( unityObject ) ) ) )
				{
					searchedObjects.Add( objHash, null );
					return null;
				}

				callStack.Add( unityObject );

				// Search the Object in detail
				Func<Object, ReferenceNode> func;
				if( typeToSearchFunction.TryGetValue( unityObject.GetType(), out func ) )
					result = func( unityObject );
				else if( unityObject is Component )
					result = SearchComponent( unityObject );
				else
				{
					result = PopReferenceNode( unityObject );
					SearchWithSerializedObject( result );
				}

				callStack.RemoveAt( callStack.Count - 1 );
			}
			else
			{
				// Comply with the recursive search limit
				if( currentDepth >= searchDepthLimit )
					return null;

				callStack.Add( obj );
				currentDepth++;

				result = PopReferenceNode( obj );
				SearchFieldsAndPropertiesOf( result );

				currentDepth--;
				callStack.RemoveAt( callStack.Count - 1 );
			}

			if( result != null && result.NumberOfOutgoingLinks == 0 )
			{
				PoolReferenceNode( result );
				result = null;
			}

			// Cache the search result if we are skimming through a class (not a struct; i.e. objHash != null)
			// and if the object is a UnityEngine.Object (if not, cache the result only if we have actually found something
			// or we are at the root of the search; i.e. currentDepth == 0)
			if( objHash != null && ( result != null || unityObject != null || currentDepth == 0 ) )
			{
				if( !searchingSourceAsset )
					searchedObjects.Add( objHash, result );
				else if( result != null )
				{
					result.CopyReferencesTo( searchedObjects[objHash] );
					PoolReferenceNode( result );
				}
			}

			return result;
		}

		private ReferenceNode SearchGameObject( Object unityObject )
		{
			GameObject go = (GameObject) unityObject;
			ReferenceNode referenceNode = PopReferenceNode( go );

			// Check if this GameObject's prefab is one of the selected assets
			if( searchPrefabConnections )
			{
#if UNITY_2018_3_OR_NEWER
				Object prefab = PrefabUtility.GetCorrespondingObjectFromSource( go );
#else
				Object prefab = PrefabUtility.GetPrefabParent( go );
#endif
				if( objectsToSearchSet.Contains( prefab ) && assetsToSearchRootPrefabs.ContainsFast( prefab as GameObject ) )
					referenceNode.AddLinkTo( GetReferenceNode( prefab ), "Prefab object" );
			}

			// Search through all the components of the object
			Component[] components = go.GetComponents<Component>();
			for( int i = 0; i < components.Length; i++ )
				referenceNode.AddLinkTo( SearchObject( components[i] ) );

			return referenceNode;
		}

		private ReferenceNode SearchComponent( Object unityObject )
		{
			Component component = (Component) unityObject;

			// Ignore Transform component (no object field to search for)
			if( component is Transform )
				return null;

			ReferenceNode referenceNode = PopReferenceNode( component );

			if( searchMonoBehavioursForScript && component is MonoBehaviour )
			{
				// If a searched asset is script, check if this component is an instance of it
				MonoScript script = MonoScript.FromMonoBehaviour( (MonoBehaviour) component );
				if( objectsToSearchSet.Contains( script ) )
					referenceNode.AddLinkTo( GetReferenceNode( script ) );
			}
			else if( searchRenderers && component is Renderer )
			{
				// If an asset is a shader, texture or material, and this component is a Renderer,
				// search it for references
				Material[] materials = ( (Renderer) component ).sharedMaterials;
				for( int i = 0; i < materials.Length; i++ )
					referenceNode.AddLinkTo( SearchObject( materials[i] ) );
			}
			else if( component is Animation )
			{
				// If this component is an Animation, search its animation clips for references
				foreach( AnimationState anim in (Animation) component )
					referenceNode.AddLinkTo( SearchObject( anim.clip ) );

				// Search the objects that are animated by this Animation component for references
				SearchAnimatedObjects( referenceNode );
			}
			else if( component is Animator )
			{
				// If this component is an Animator, search its animation clips for references (via AnimatorController)
				referenceNode.AddLinkTo( SearchObject( ( (Animator) component ).runtimeAnimatorController ) );

				// Search the objects that are animated by this Animator component for references
				SearchAnimatedObjects( referenceNode );
			}
#if UNITY_2017_2_OR_NEWER
			else if( component is Tilemap )
			{
				// If this component is a Tilemap, search its tiles for references
				TileBase[] tiles = new TileBase[( (Tilemap) component ).GetUsedTilesCount()];
				( (Tilemap) component ).GetUsedTilesNonAlloc( tiles );

				if( tiles != null )
				{
					for( int i = 0; i < tiles.Length; i++ )
						referenceNode.AddLinkTo( SearchObject( tiles[i] ), "Tile" );
				}
			}
#endif
#if UNITY_2017_1_OR_NEWER
			else if( component is PlayableDirector )
			{
				// If this component is a PlayableDirectory, search its PlayableAsset's scene bindings for references
				PlayableAsset playableAsset = ( (PlayableDirector) component ).playableAsset;
				if( playableAsset != null && !playableAsset.Equals( null ) )
				{
					foreach( PlayableBinding binding in playableAsset.outputs )
						referenceNode.AddLinkTo( SearchObject( ( (PlayableDirector) component ).GetGenericBinding( binding.sourceObject ) ), "Binding: " + binding.streamName );
				}
			}
#endif

			SearchWithSerializedObject( referenceNode );
			return referenceNode;
		}

		private ReferenceNode SearchMaterial( Object unityObject )
		{
			Material material = (Material) unityObject;
			ReferenceNode referenceNode = PopReferenceNode( material );

			if( searchMaterialsForShader && objectsToSearchSet.Contains( material.shader ) )
				referenceNode.AddLinkTo( GetReferenceNode( material.shader ), "Shader" );

			if( searchMaterialsForTexture )
			{
				// Search through all the textures attached to this material
				// Credit: http://answers.unity3d.com/answers/1116025/view.html
				Shader shader = material.shader;
				int shaderPropertyCount = ShaderUtil.GetPropertyCount( shader );
				for( int i = 0; i < shaderPropertyCount; i++ )
				{
					if( ShaderUtil.GetPropertyType( shader, i ) == ShaderUtil.ShaderPropertyType.TexEnv )
					{
						string propertyName = ShaderUtil.GetPropertyName( shader, i );
						Texture assignedTexture = material.GetTexture( propertyName );
						if( objectsToSearchSet.Contains( assignedTexture ) )
							referenceNode.AddLinkTo( GetReferenceNode( assignedTexture ), "Shader property: " + propertyName );
					}
				}
			}

			return referenceNode;
		}

		private ReferenceNode SearchAnimatorController( Object unityObject )
		{
			RuntimeAnimatorController controller = (RuntimeAnimatorController) unityObject;
			ReferenceNode referenceNode = PopReferenceNode( controller );

			if( controller is AnimatorController )
			{
				AnimatorControllerLayer[] layers = ( (AnimatorController) controller ).layers;
				for( int i = 0; i < layers.Length; i++ )
				{
					if( objectsToSearchSet.Contains( layers[i].avatarMask ) )
						referenceNode.AddLinkTo( GetReferenceNode( layers[i].avatarMask ), layers[i].name + " Mask" );

					referenceNode.AddLinkTo( SearchObject( layers[i].stateMachine ) );
				}
			}
			else
			{
				AnimationClip[] animClips = controller.animationClips;
				for( int i = 0; i < animClips.Length; i++ )
					referenceNode.AddLinkTo( SearchObject( animClips[i] ) );
			}

			return referenceNode;
		}

		private ReferenceNode SearchAnimatorStateMachine( Object unityObject )
		{
			AnimatorStateMachine animatorStateMachine = (AnimatorStateMachine) unityObject;
			ReferenceNode referenceNode = PopReferenceNode( animatorStateMachine );

			ChildAnimatorStateMachine[] stateMachines = animatorStateMachine.stateMachines;
			for( int i = 0; i < stateMachines.Length; i++ )
				referenceNode.AddLinkTo( SearchObject( stateMachines[i].stateMachine ), "Child State Machine" );

			ChildAnimatorState[] states = animatorStateMachine.states;
			for( int i = 0; i < states.Length; i++ )
				referenceNode.AddLinkTo( SearchObject( states[i].state ) );

			if( searchMonoBehavioursForScript )
			{
				StateMachineBehaviour[] behaviours = animatorStateMachine.behaviours;
				for( int i = 0; i < behaviours.Length; i++ )
				{
					MonoScript script = MonoScript.FromScriptableObject( behaviours[i] );
					if( objectsToSearchSet.Contains( script ) )
						referenceNode.AddLinkTo( GetReferenceNode( script ) );
				}
			}

			return referenceNode;
		}

		private ReferenceNode SearchAnimatorState( Object unityObject )
		{
			AnimatorState animatorState = (AnimatorState) unityObject;
			ReferenceNode referenceNode = PopReferenceNode( animatorState );

			referenceNode.AddLinkTo( SearchObject( animatorState.motion ), "Motion" );

			if( searchMonoBehavioursForScript )
			{
				StateMachineBehaviour[] behaviours = animatorState.behaviours;
				for( int i = 0; i < behaviours.Length; i++ )
				{
					MonoScript script = MonoScript.FromScriptableObject( behaviours[i] );
					if( objectsToSearchSet.Contains( script ) )
						referenceNode.AddLinkTo( GetReferenceNode( script ) );
				}
			}

			return referenceNode;
		}

		private ReferenceNode SearchAnimatorStateTransition( Object unityObject )
		{
			// Don't search AnimatorStateTransition objects, it will just return duplicate results of SearchAnimatorStateMachine
			return PopReferenceNode( unityObject );
		}

		private ReferenceNode SearchBlendTree( Object unityObject )
		{
			BlendTree blendTree = (BlendTree) unityObject;
			ReferenceNode referenceNode = PopReferenceNode( blendTree );

			ChildMotion[] children = blendTree.children;
			for( int i = 0; i < children.Length; i++ )
				referenceNode.AddLinkTo( SearchObject( children[i].motion ), "Motion" );

			return referenceNode;
		}

		private ReferenceNode SearchAnimationClip( Object unityObject )
		{
			AnimationClip clip = (AnimationClip) unityObject;
			ReferenceNode referenceNode = PopReferenceNode( clip );

			// Get all curves from animation clip
			EditorCurveBinding[] objectCurves = AnimationUtility.GetObjectReferenceCurveBindings( clip );
			for( int i = 0; i < objectCurves.Length; i++ )
			{
				// Search through all the keyframes in this curve
				ObjectReferenceKeyframe[] keyframes = AnimationUtility.GetObjectReferenceCurve( clip, objectCurves[i] );
				for( int j = 0; j < keyframes.Length; j++ )
					referenceNode.AddLinkTo( SearchObject( keyframes[j].value ), "Keyframe: " + keyframes[j].time );
			}

			// Get all events from animation clip
			AnimationEvent[] events = AnimationUtility.GetAnimationEvents( clip );
			for( int i = 0; i < events.Length; i++ )
				referenceNode.AddLinkTo( SearchObject( events[i].objectReferenceParameter ), "AnimationEvent: " + events[i].time );

			return referenceNode;
		}

#if UNITY_2017_1_OR_NEWER
		private ReferenceNode SearchSpriteAtlas( Object unityObject )
		{
			SpriteAtlas spriteAtlas = (SpriteAtlas) unityObject;
			ReferenceNode referenceNode = PopReferenceNode( spriteAtlas );

			SerializedObject spriteAtlasSO = new SerializedObject( spriteAtlas );
			if( spriteAtlas.isVariant )
			{
				Object masterAtlas = spriteAtlasSO.FindProperty( "m_MasterAtlas" ).objectReferenceValue;
				if( objectsToSearchSet.Contains( masterAtlas ) )
					referenceNode.AddLinkTo( SearchObject( masterAtlas ), "Master Atlas" );
			}

#if UNITY_2018_2_OR_NEWER
			Object[] packables = spriteAtlas.GetPackables();
			if( packables != null )
			{
				for( int i = 0; i < packables.Length; i++ )
					referenceNode.AddLinkTo( SearchObject( packables[i] ), "Packed Texture" );
			}
#else
			SerializedProperty packables = spriteAtlasSO.FindProperty( "m_EditorData.packables" );
			if( packables != null )
			{
				for( int i = 0, length = packables.arraySize; i < length; i++ )
					referenceNode.AddLinkTo( SearchObject( packables.GetArrayElementAtIndex( i ).objectReferenceValue ), "Packed Texture" );
			}
#endif

			return referenceNode;
		}
#endif

		// Find references from an Animation/Animator component to the objects that it animates
		private void SearchAnimatedObjects( ReferenceNode referenceNode )
		{
			GameObject root = ( (Component) referenceNode.nodeObject ).gameObject;
			AnimationClip[] clips = AnimationUtility.GetAnimationClips( root );
			for( int i = 0; i < clips.Length; i++ )
			{
				AnimationClip clip = clips[i];
				bool isClipUnique = true;
				for( int j = i - 1; j >= 0; j-- )
				{
					if( clips[j] == clip )
					{
						isClipUnique = false;
						break;
					}
				}

				if( !isClipUnique )
					continue;

				EditorCurveBinding[] uniqueBindings;
				if( !animationClipUniqueBindings.TryGetValue( clip, out uniqueBindings ) )
				{
					// Calculate all the "unique" paths that the animation clip's curves have
					// Both float curves (GetCurveBindings) and object reference curves (GetObjectReferenceCurveBindings) are checked
					List<EditorCurveBinding> _uniqueBindings = new List<EditorCurveBinding>( 2 );
					EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings( clip );
					for( int j = 0; j < bindings.Length; j++ )
					{
						string bindingPath = bindings[j].path;
						if( string.IsNullOrEmpty( bindingPath ) ) // Ignore the root animated object
							continue;

						bool isBindingUnique = true;
						for( int k = _uniqueBindings.Count - 1; k >= 0; k-- )
						{
							if( bindingPath == _uniqueBindings[k].path )
							{
								isBindingUnique = false;
								break;
							}
						}

						if( isBindingUnique )
							_uniqueBindings.Add( bindings[j] );
					}

					bindings = AnimationUtility.GetObjectReferenceCurveBindings( clip );
					for( int j = 0; j < bindings.Length; j++ )
					{
						string bindingPath = bindings[j].path;
						if( string.IsNullOrEmpty( bindingPath ) ) // Ignore the root animated object
							continue;

						bool isBindingUnique = true;
						for( int k = _uniqueBindings.Count - 1; k >= 0; k-- )
						{
							if( bindingPath == _uniqueBindings[k].path )
							{
								isBindingUnique = false;
								break;
							}
						}

						if( isBindingUnique )
							_uniqueBindings.Add( bindings[j] );
					}

					uniqueBindings = _uniqueBindings.ToArray();
					animationClipUniqueBindings[clip] = uniqueBindings;
				}

				string clipName = clip.name;
				for( int j = 0; j < uniqueBindings.Length; j++ )
					referenceNode.AddLinkTo( SearchObject( AnimationUtility.GetAnimatedObject( root, uniqueBindings[j] ) ), "Animated via clip: " + clipName );
			}
		}

		// Search through field and properties of an object for references with SerializedObject
		private void SearchWithSerializedObject( ReferenceNode referenceNode )
		{
			if( !isInPlayMode || referenceNode.nodeObject.IsAsset() )
			{
				SerializedObject so = new SerializedObject( (Object) referenceNode.nodeObject );
				SerializedProperty iterator = so.GetIterator();
				if( iterator.NextVisible( true ) )
				{
					bool enterChildren;
					do
					{
						ReferenceNode searchResult;
						switch( iterator.propertyType )
						{
							case SerializedPropertyType.ObjectReference:
								searchResult = SearchObject( iterator.objectReferenceValue );
								enterChildren = false;
								break;
							case SerializedPropertyType.ExposedReference:
								searchResult = SearchObject( iterator.exposedReferenceValue );
								enterChildren = false;
								break;
							case SerializedPropertyType.Generic:
								searchResult = null;
								enterChildren = true;
								break;
							default:
								searchResult = null;
								enterChildren = false;
								break;
						}

						if( searchResult != null && searchResult != referenceNode )
						{
							string propertyPath = iterator.propertyPath;

							// m_RD.texture is a redundant reference that shows up when searching sprites
							if( !propertyPath.EndsWith( "m_RD.texture", StringComparison.Ordinal ) )
								referenceNode.AddLinkTo( searchResult, "Variable: " + propertyPath );
						}
					} while( iterator.NextVisible( enterChildren ) );

					return;
				}
			}

			// Use reflection algorithm as fallback
			SearchFieldsAndPropertiesOf( referenceNode );
		}

		// Search through field and properties of an object for references
		private void SearchFieldsAndPropertiesOf( ReferenceNode referenceNode )
		{
			// Get filtered variables for this object
			VariableGetterHolder[] variables = GetFilteredVariablesForType( referenceNode.nodeObject.GetType() );
			for( int i = 0; i < variables.Length; i++ )
			{
				// When possible, don't search non-serializable variables
				if( searchSerializableVariablesOnly && !variables[i].isSerializable )
					continue;

				try
				{
					object variableValue = variables[i].Get( referenceNode.nodeObject );
					if( variableValue == null )
						continue;

					// Values stored inside ICollection objects are searched using IEnumerable,
					// no need to have duplicate search entries
					if( !( variableValue is ICollection ) )
					{
						ReferenceNode searchResult = SearchObject( variableValue );
						if( searchResult != null && searchResult != referenceNode )
							referenceNode.AddLinkTo( searchResult, ( variables[i].isProperty ? "Property: " : "Variable: " ) + variables[i].name );
					}

					if( variableValue is IEnumerable && !( variableValue is Transform ) )
					{
						// If the field is IEnumerable (possibly an array or collection), search through members of it
						// Note that Transform IEnumerable (children of the transform) is not iterated
						int index = 0;
						foreach( object element in (IEnumerable) variableValue )
						{
							ReferenceNode searchResult = SearchObject( element );
							if( searchResult != null && searchResult != referenceNode )
								referenceNode.AddLinkTo( searchResult, string.Concat( variables[i].isProperty ? "Property: " : "Variable: ", variables[i].name, "[", index + "]" ) );

							index++;
						}
					}
				}
				catch( UnassignedReferenceException ) { }
				catch( MissingReferenceException ) { }
				catch( MissingComponentException ) { }
				catch( NotImplementedException ) { }
			}
		}

		// Get filtered variables for a type
		private VariableGetterHolder[] GetFilteredVariablesForType( Type type )
		{
			VariableGetterHolder[] result;
			if( typeToVariables.TryGetValue( type, out result ) )
				return result;

			// This is the first time this type of object is seen, filter and cache its variables
			// Variable filtering process:
			// 1- skip Obsolete variables
			// 2- skip primitive types, enums and strings
			// 3- skip common Unity types that can't hold any references (e.g. Vector3, Rect, Color, Quaternion)
			// 
			// P.S. IsIgnoredUnityType() extension function handles steps 2) and 3)

			validVariables.Clear();

			// Filter the fields
			if( fieldModifiers != ( BindingFlags.Instance | BindingFlags.DeclaredOnly ) )
			{
				Type currType = type;
				while( currType != typeof( object ) )
				{
					FieldInfo[] fields = currType.GetFields( fieldModifiers );
					for( int i = 0; i < fields.Length; i++ )
					{
						FieldInfo field = fields[i];

						// Skip obsolete fields
						if( Attribute.IsDefined( field, typeof( ObsoleteAttribute ) ) )
							continue;

						// Skip primitive types
						if( field.FieldType.IsIgnoredUnityType() )
							continue;

						// Additional filtering for fields:
						// 1- Ignore "m_RectTransform", "m_CanvasRenderer" and "m_Canvas" fields of Graphic components
						string fieldName = field.Name;
						if( typeof( Graphic ).IsAssignableFrom( currType ) &&
							( fieldName == "m_RectTransform" || fieldName == "m_CanvasRenderer" || fieldName == "m_Canvas" ) )
							continue;

						VariableGetVal getter = field.CreateGetter( type );
						if( getter != null )
							validVariables.Add( new VariableGetterHolder( field, getter, searchSerializableVariablesOnly ? field.IsSerializable() : true ) );
					}

					currType = currType.BaseType;
				}
			}

			if( propertyModifiers != ( BindingFlags.Instance | BindingFlags.DeclaredOnly ) )
			{
				Type currType = type;
				while( currType != typeof( object ) )
				{
					PropertyInfo[] properties = currType.GetProperties( propertyModifiers );
					for( int i = 0; i < properties.Length; i++ )
					{
						PropertyInfo property = properties[i];

						// Skip obsolete properties
						if( Attribute.IsDefined( property, typeof( ObsoleteAttribute ) ) )
							continue;

						// Skip primitive types
						if( property.PropertyType.IsIgnoredUnityType() )
							continue;

						// Skip properties without a getter function
						MethodInfo propertyGetter = property.GetGetMethod( true );
						if( propertyGetter == null )
							continue;

						// Skip indexer properties
						if( property.GetIndexParameters().Length > 0 )
							continue;

						// No need to check properties with 'override' keyword
						if( propertyGetter.GetBaseDefinition().DeclaringType != propertyGetter.DeclaringType )
							continue;

						// Additional filtering for properties:
						// 1- Ignore "gameObject", "transform", "rectTransform" and "attachedRigidbody" properties of Component's to get more useful results
						// 2- Ignore "canvasRenderer" and "canvas" properties of Graphic components
						// 3 & 4- Prevent accessing properties of Unity that instantiate an existing resource (causing memory leak)
						string propertyName = property.Name;
						if( typeof( Component ).IsAssignableFrom( currType ) && ( propertyName == "gameObject" ||
							propertyName == "transform" || propertyName == "attachedRigidbody" || propertyName == "rectTransform" ) )
							continue;
						else if( typeof( Graphic ).IsAssignableFrom( currType ) &&
							( propertyName == "canvasRenderer" || propertyName == "canvas" ) )
							continue;
						else if( typeof( MeshFilter ).IsAssignableFrom( currType ) && propertyName == "mesh" )
							continue;
						else if( typeof( Renderer ).IsAssignableFrom( currType ) &&
							( propertyName == "sharedMaterial" || propertyName == "sharedMaterials" ) )
							continue;
						else if( ( propertyName == "material" || propertyName == "materials" ) &&
							( typeof( Renderer ).IsAssignableFrom( currType ) || typeof( Collider ).IsAssignableFrom( currType ) ||
#if !UNITY_2019_3_OR_NEWER
#pragma warning disable 0618
							typeof( GUIText ).IsAssignableFrom( currType ) ||
#pragma warning restore 0618
#endif
							typeof( Collider2D ).IsAssignableFrom( currType ) ) )
							continue;
						else
						{
							VariableGetVal getter = property.CreateGetter();
							if( getter != null )
								validVariables.Add( new VariableGetterHolder( property, getter, searchSerializableVariablesOnly ? property.IsSerializable() : true ) );
						}
					}

					currType = currType.BaseType;
				}
			}

			result = validVariables.ToArray();

			// Cache the filtered fields
			typeToVariables.Add( type, result );

			return result;
		}

		// Check if the asset at specified path depends on any of the references
		private bool AssetHasAnyReference( string assetPath )
		{
			// "Find references of" assets can contain internal references
			if( assetsToSearchPathsSet.Contains( assetPath ) )
				return true;

			CacheEntry cacheEntry;
			if( !assetDependencyCache.TryGetValue( assetPath, out cacheEntry ) )
			{
				cacheEntry = new CacheEntry( assetPath );
				assetDependencyCache[assetPath] = cacheEntry;
			}
			else if( !cacheEntry.verified )
				cacheEntry.Verify( assetPath );

			if( cacheEntry.searchResult != CacheEntry.Result.Unknown )
				return cacheEntry.searchResult == CacheEntry.Result.Yes;

			cacheEntry.searchResult = CacheEntry.Result.No;

			string[] dependencies = cacheEntry.dependencies;
			long[] fileSizes = cacheEntry.fileSizes;
			for( int i = 0; i < dependencies.Length; i++ )
			{
				// If a dependency was renamed (which doesn't affect the verified hash, unfortunately),
				// force refresh the asset's dependencies and search it again
				if( !Directory.Exists( dependencies[i] ) ) // Calling FileInfo.Length on a directory throws FileNotFoundException
				{
					FileInfo assetFile = new FileInfo( dependencies[i] );
					if( !assetFile.Exists || assetFile.Length != fileSizes[i] )
					{
						// Although not reproduced, it is reported that this section caused StackOverflowException due to infinite loop,
						// if that happens, log useful information to help reproduce the issue
						if( lastRefreshedCacheEntry == cacheEntry )
						{
							StringBuilder sb = new StringBuilder( 1000 );
							sb.AppendLine( "<b>Infinite loop while refreshing a cache entry, please report it to the author.</b>" ).AppendLine();
							sb.Append( "Asset path: " ).AppendLine( assetPath );

							for( int j = 0; j < 2; j++ )
							{
								if( j == 1 )
								{
									cacheEntry.Refresh( assetPath );
									dependencies = cacheEntry.dependencies;
									fileSizes = cacheEntry.fileSizes;
								}

								sb.AppendLine().AppendLine( j == 0 ? "Old Dependencies:" : "New Dependencies" );
								for( int k = 0; k < dependencies.Length; k++ )
								{
									sb.Append( "- " ).Append( dependencies[k] );

									if( Directory.Exists( dependencies[k] ) )
									{
										sb.Append( " (Dir)" );
										if( fileSizes[k] != 0L )
											sb.Append( " WasCachedAsFile: " ).Append( fileSizes[k] );
									}
									else
									{
										assetFile = new FileInfo( dependencies[k] );
										sb.Append( " (File) " ).Append( "CachedSize: " ).Append( fileSizes[k] );
										if( assetFile.Exists )
											sb.Append( " RealSize: " ).Append( assetFile.Length );
										else
											sb.Append( " NoLongerExists" );
									}

									sb.AppendLine();
								}
							}

							Debug.LogError( sb.ToString() );
							return false;
						}

						cacheEntry.Refresh( assetPath );
						cacheEntry.searchResult = CacheEntry.Result.Unknown;
						lastRefreshedCacheEntry = cacheEntry;

						return AssetHasAnyReference( assetPath );
					}
				}

				if( assetsToSearchPathsSet.Contains( dependencies[i] ) )
				{
					cacheEntry.searchResult = CacheEntry.Result.Yes;
					return true;
				}
			}

			for( int i = 0; i < dependencies.Length; i++ )
			{
				if( AssetHasAnyReference( dependencies[i] ) )
				{
					cacheEntry.searchResult = CacheEntry.Result.Yes;
					return true;
				}
			}

			return false;
		}

		// Get reference node for object
		private ReferenceNode GetReferenceNode( object nodeObject )
		{
			ReferenceNode result;
			string hash = nodeObject.Hash();
			if( !searchedObjects.TryGetValue( hash, out result ) || result == null )
			{
				result = PopReferenceNode( nodeObject );
				searchedObjects[hash] = result;
			}

			return result;
		}

		// Fetch a reference node from pool
		private ReferenceNode PopReferenceNode( object nodeObject )
		{
			ReferenceNode node;
			if( nodesPool.Count == 0 )
				node = new ReferenceNode();
			else
			{
				int index = nodesPool.Count - 1;
				node = nodesPool[index];
				nodesPool.RemoveAt( index );
			}

			node.nodeObject = nodeObject;
			return node;
		}

		// Pool a reference node
		private void PoolReferenceNode( ReferenceNode node )
		{
			node.Clear();
			nodesPool.Add( node );
		}

		// Retrieve the game objects listed under the DontDestroyOnLoad scene
		private GameObject[] GetDontDestroyOnLoadObjects()
		{
			GameObject temp = null;
			try
			{
				temp = new GameObject();
				Object.DontDestroyOnLoad( temp );
				Scene dontDestroyOnLoad = temp.scene;
				Object.DestroyImmediate( temp );
				temp = null;

				return dontDestroyOnLoad.GetRootGameObjects();
			}
			finally
			{
				if( temp != null )
					Object.DestroyImmediate( temp );
			}
		}

		public void SaveCache()
		{
			if( assetDependencyCache == null )
				return;

			try
			{
				using( FileStream stream = new FileStream( CachePath, FileMode.Create ) )
				using( BinaryWriter writer = new BinaryWriter( stream ) )
				{
					writer.Write( assetDependencyCache.Count );

					foreach( var keyValuePair in assetDependencyCache )
					{
						CacheEntry cacheEntry = keyValuePair.Value;
						string[] dependencies = cacheEntry.dependencies;
						long[] fileSizes = cacheEntry.fileSizes;

						writer.Write( keyValuePair.Key );
						writer.Write( cacheEntry.hash );
						writer.Write( dependencies.Length );

						for( int i = 0; i < dependencies.Length; i++ )
						{
							writer.Write( dependencies[i] );
							writer.Write( fileSizes[i] );
						}
					}
				}
			}
			catch( Exception e )
			{
				Debug.LogException( e );
			}
		}

		private void LoadCache()
		{
			if( File.Exists( CachePath ) )
			{
				using( FileStream stream = new FileStream( CachePath, FileMode.Open, FileAccess.Read ) )
				using( BinaryReader reader = new BinaryReader( stream ) )
				{
					try
					{
						int cacheSize = reader.ReadInt32();
						assetDependencyCache = new Dictionary<string, CacheEntry>( cacheSize );

						for( int i = 0; i < cacheSize; i++ )
						{
							string assetPath = reader.ReadString();
							string hash = reader.ReadString();

							int dependenciesLength = reader.ReadInt32();
							string[] dependencies = new string[dependenciesLength];
							long[] fileSizes = new long[dependenciesLength];
							for( int j = 0; j < dependenciesLength; j++ )
							{
								dependencies[j] = reader.ReadString();
								fileSizes[j] = reader.ReadInt64();
							}

							assetDependencyCache[assetPath] = new CacheEntry( hash, dependencies, fileSizes );
						}
					}
					catch( Exception e )
					{
						assetDependencyCache = null;
						Debug.LogWarning( "Couldn't load cache (probably cache format has changed in an update), will regenerate cache.\n" + e.ToString() );
					}
				}
			}

			// Generate cache for all assets for the first time
			if( assetDependencyCache == null )
			{
				assetDependencyCache = new Dictionary<string, CacheEntry>( 1024 * 8 );

				string[] allAssets = AssetDatabase.GetAllAssetPaths();
				if( allAssets.Length > 0 )
				{
					double startTime = EditorApplication.timeSinceStartup;

					try
					{
						for( int i = 0; i < allAssets.Length; i++ )
						{
							if( i % 30 == 0 && EditorUtility.DisplayCancelableProgressBar( "Please wait...", "Generating cache for the first time (optional)", (float) i / allAssets.Length ) )
							{
								EditorUtility.ClearProgressBar();
								Debug.LogWarning( "Initial cache generation cancelled, cache will be generated on the fly as more and more assets are searched." );
								break;
							}

							AssetHasAnyReference( allAssets[i] );
						}

						EditorUtility.ClearProgressBar();

						Debug.Log( "Cache generated in " + ( EditorApplication.timeSinceStartup - startTime ).ToString( "F2" ) + " seconds" );
						Debug.Log( "You can always reset the cache by deleting " + Path.GetFullPath( CachePath ) );

						SaveCache();
					}
					catch( Exception e )
					{
						EditorUtility.ClearProgressBar();
						Debug.LogException( e );
					}
				}
			}
		}
	}
}