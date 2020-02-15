using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace AssetUsageDetectorNamespace
{
	[Serializable]
	public class SearchResultDrawParameters
	{
		public SearchResult searchResult;
		public PathDrawingMode pathDrawingMode;
		public bool showTooltips;
		public bool noAssetDatabaseChanges;
		public Rect guiRect;
		public bool shouldRefreshEditorWindow;
		public string tooltip;

		public SearchResultDrawParameters( PathDrawingMode pathDrawingMode, bool showTooltips, bool noAssetDatabaseChanges )
		{
			this.pathDrawingMode = pathDrawingMode;
			this.showTooltips = showTooltips;
			this.noAssetDatabaseChanges = noAssetDatabaseChanges;
		}
	}

	// Custom class to hold search results
	[Serializable]
	public class SearchResult : ISerializationCallbackReceiver
	{
		// Credit: https://docs.unity3d.com/Manual/script-Serialization-Custom.html
		[Serializable]
		public class SerializableResultGroup
		{
			public string title;
			public SearchResultGroup.GroupType type;
			public bool isExpanded;
			public bool pendingSearch;

			public List<int> initialSerializedNodes;
		}

		[Serializable]
		public class SerializableNode
		{
			public int instanceId;
			public bool isUnityObject;
			public string description;

			public List<int> links;
			public List<string> linkDescriptions;
		}

#pragma warning disable 1692
#pragma warning disable IDE0044
		private bool success; // This is not readonly so that it can be serialized
#pragma warning restore IDE0044
#pragma warning restore 1692

		private List<SearchResultGroup> result;
		private SceneSetup[] initialSceneSetup;

		private AssetUsageDetector searchHandler;
		private AssetUsageDetector.Parameters m_searchParameters;

		private float guiHeight; // Prevents scroll view's position from getting reset at domain reload

		private List<SerializableNode> serializedNodes;
		private List<SerializableResultGroup> serializedGroups;

		public int NumberOfGroups { get { return result.Count; } }
		public SearchResultGroup this[int index] { get { return result[index]; } }

		public bool SearchCompletedSuccessfully { get { return success; } }
		public bool InitialSceneSetupConfigured { get { return initialSceneSetup != null && initialSceneSetup.Length > 0; } }
		public AssetUsageDetector.Parameters SearchParameters { get { return m_searchParameters; } }

		public SearchResult( bool success, List<SearchResultGroup> result, SceneSetup[] initialSceneSetup, AssetUsageDetector searchHandler, AssetUsageDetector.Parameters searchParameters )
		{
			if( result == null )
				result = new List<SearchResultGroup>( 0 );

			this.success = success;
			this.result = result;
			this.initialSceneSetup = initialSceneSetup;
			this.searchHandler = searchHandler;
			this.m_searchParameters = searchParameters;
		}

		public void RefreshSearchResultGroup( SearchResultGroup searchResultGroup, bool noAssetDatabaseChanges )
		{
			if( searchResultGroup == null )
			{
				Debug.LogError( "SearchResultGroup is null!" );
				return;
			}

			int searchResultGroupIndex = -1;
			for( int i = 0; i < result.Count; i++ )
			{
				if( result[i] == searchResultGroup )
				{
					searchResultGroupIndex = i;
					break;
				}
			}

			if( searchResultGroupIndex < 0 )
			{
				Debug.LogError( "SearchResultGroup is not a part of SearchResult!" );
				return;
			}

			if( searchResultGroup.Type == SearchResultGroup.GroupType.Scene && EditorApplication.isPlaying && !EditorSceneManager.GetSceneByPath( searchResultGroup.Title ).isLoaded )
			{
				Debug.LogError( "Can't search unloaded scene while in Play Mode!" );
				return;
			}

			if( searchHandler == null )
				searchHandler = new AssetUsageDetector();

			SceneSearchMode searchInScenes = m_searchParameters.searchInScenes;
			Object[] searchInScenesSubset = m_searchParameters.searchInScenesSubset;
			bool searchInAssetsFolder = m_searchParameters.searchInAssetsFolder;
			Object[] searchInAssetsSubset = m_searchParameters.searchInAssetsSubset;

			try
			{
				if( searchResultGroup.Type == SearchResultGroup.GroupType.Assets )
				{
					m_searchParameters.searchInScenes = SceneSearchMode.None;
					m_searchParameters.searchInScenesSubset = null;
				}
				else if( searchResultGroup.Type == SearchResultGroup.GroupType.Scene )
				{
					m_searchParameters.searchInScenes = SceneSearchMode.None;
					m_searchParameters.searchInScenesSubset = new Object[1] { AssetDatabase.LoadAssetAtPath<SceneAsset>( searchResultGroup.Title ) };
					m_searchParameters.searchInAssetsFolder = false;
					m_searchParameters.searchInAssetsSubset = null;
				}
				else
				{
					m_searchParameters.searchInScenes = (SceneSearchMode) 1024; // A unique value to search only the DontDestroyOnLoad scene
					m_searchParameters.searchInScenesSubset = null;
					m_searchParameters.searchInAssetsFolder = false;
					m_searchParameters.searchInAssetsSubset = null;
				}

				m_searchParameters.lazySceneSearch = false;
				m_searchParameters.noAssetDatabaseChanges = noAssetDatabaseChanges;

				// Make sure the AssetDatabase is up-to-date
				AssetDatabase.SaveAssets();

				SearchResult searchResult = searchHandler.Run( m_searchParameters );
				if( !searchResult.success )
				{
					EditorUtility.DisplayDialog( "Error", "Couldn't refresh, check console for more info.", "OK" );
					return;
				}

				List<SearchResultGroup> searchResultGroups = searchResult.result;
				if( searchResultGroups != null )
				{
					int newSearchResultGroupIndex = -1;
					for( int i = 0; i < searchResultGroups.Count; i++ )
					{
						if( searchResultGroup.Title == searchResultGroups[i].Title )
						{
							newSearchResultGroupIndex = i;
							break;
						}
					}

					if( newSearchResultGroupIndex >= 0 )
						result[searchResultGroupIndex] = searchResultGroups[newSearchResultGroupIndex];
					else
						searchResultGroup.Clear();
				}
			}
			finally
			{
				m_searchParameters.searchInScenes = searchInScenes;
				m_searchParameters.searchInScenesSubset = searchInScenesSubset;
				m_searchParameters.searchInAssetsFolder = searchInAssetsFolder;
				m_searchParameters.searchInAssetsSubset = searchInAssetsSubset;
			}
		}

		public void DrawOnGUI( SearchResultDrawParameters parameters )
		{
			parameters.searchResult = this;
			parameters.tooltip = null;
			parameters.guiRect = EditorGUILayout.GetControlRect( Utilities.GL_EXPAND_WIDTH, Utilities.GL_HEIGHT_0 );

			float guiInitialY = parameters.guiRect.y;

			for( int i = 0; i < result.Count; i++ )
			{
				result[i].DrawOnGUI( parameters );

				if( i < result.Count - 1 )
				{
					Rect rect = parameters.guiRect;
					rect.y += 10f;
					parameters.guiRect = rect;
				}
			}

			if( Event.current.type == EventType.Repaint )
				guiHeight = parameters.guiRect.y - guiInitialY;

			// To work nicely with GUILayout and scroll view
			EditorGUILayout.GetControlRect( Utilities.GL_EXPAND_WIDTH, GUILayout.Height( guiHeight ) );

			if( parameters.tooltip != null )
			{
				// Show tooltip at mouse position
				Vector2 mousePos = Event.current.mousePosition;
				Vector2 size = Utilities.TooltipGUIStyle.CalcSize( new GUIContent( parameters.tooltip ) );
				size.x += 10f;

				Rect tooltipRect = new Rect( new Vector2( mousePos.x - size.x * 0.5f, mousePos.y - size.y ), size );
				if( tooltipRect.xMin < 0f )
					tooltipRect.x -= tooltipRect.xMin;
				else if( tooltipRect.xMax > parameters.guiRect.width )
					tooltipRect.x -= tooltipRect.xMax - parameters.guiRect.width;

				GUI.Box( tooltipRect, parameters.tooltip, Utilities.TooltipGUIStyle );
			}

			if( parameters.shouldRefreshEditorWindow )
			{
				parameters.shouldRefreshEditorWindow = false;

				EditorWindow focusedWindow = EditorWindow.focusedWindow;
				if( focusedWindow != null )
					focusedWindow.Repaint();

				EditorWindow mouseOverWindow = EditorWindow.mouseOverWindow;
				if( mouseOverWindow != null && mouseOverWindow != focusedWindow )
					mouseOverWindow.Repaint();
			}
		}

		public Object[] SelectAllAsObjects()
		{
			HashSet<Object> uniqueObjects = new HashSet<Object>();
			for( int i = 0; i < result.Count; i++ )
				result[i].AddObjectsTo( uniqueObjects );

			if( uniqueObjects.Count > 0 )
			{
				Object[] objects = new Object[uniqueObjects.Count];
				uniqueObjects.CopyTo( objects );

				return objects;
			}

			return null;
		}

		public GameObject[] SelectAllAsGameObjects()
		{
			HashSet<GameObject> uniqueGameObjects = new HashSet<GameObject>();
			for( int i = 0; i < result.Count; i++ )
				result[i].AddGameObjectsTo( uniqueGameObjects );

			if( uniqueGameObjects.Count > 0 )
			{
				GameObject[] objects = new GameObject[uniqueGameObjects.Count];
				uniqueGameObjects.CopyTo( objects );

				return objects;
			}

			return null;
		}

		// Returns if RestoreInitialSceneSetup will have any effect on the current scene setup
		public bool IsSceneSetupDifferentThanCurrentSetup()
		{
			if( initialSceneSetup == null )
				return false;

			SceneSetup[] sceneFinalSetup = EditorSceneManager.GetSceneManagerSetup();
			if( initialSceneSetup.Length != sceneFinalSetup.Length )
				return true;

			for( int i = 0; i < sceneFinalSetup.Length; i++ )
			{
				bool sceneIsOneOfInitials = false;
				for( int j = 0; j < initialSceneSetup.Length; j++ )
				{
					if( sceneFinalSetup[i].path == initialSceneSetup[j].path )
					{
						if( sceneFinalSetup[i].isLoaded != initialSceneSetup[j].isLoaded )
							return true;

						sceneIsOneOfInitials = true;
						break;
					}
				}

				if( !sceneIsOneOfInitials )
					return true;
			}

			return false;
		}

		// Close the scenes that were not part of the initial scene setup
		// Returns true if initial scene setup is restored successfully
		public bool RestoreInitialSceneSetup()
		{
			if( initialSceneSetup == null || initialSceneSetup.Length == 0 )
				return true;

			if( EditorApplication.isPlaying || !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo() )
				return false;

			for( int i = 0; i < initialSceneSetup.Length; i++ )
			{
				Scene scene = EditorSceneManager.GetSceneByPath( initialSceneSetup[i].path );
				if( !scene.isLoaded )
					scene = EditorSceneManager.OpenScene( initialSceneSetup[i].path, initialSceneSetup[i].isLoaded ? OpenSceneMode.Additive : OpenSceneMode.AdditiveWithoutLoading );

				if( initialSceneSetup[i].isActive )
					EditorSceneManager.SetActiveScene( scene );
			}

			SceneSetup[] sceneFinalSetup = EditorSceneManager.GetSceneManagerSetup();
			for( int i = 0; i < sceneFinalSetup.Length; i++ )
			{
				bool sceneIsOneOfInitials = false;
				for( int j = 0; j < initialSceneSetup.Length; j++ )
				{
					if( sceneFinalSetup[i].path == initialSceneSetup[j].path )
					{
						sceneIsOneOfInitials = true;
						break;
					}
				}

				if( !sceneIsOneOfInitials )
					EditorSceneManager.CloseScene( EditorSceneManager.GetSceneByPath( sceneFinalSetup[i].path ), true );
			}

			for( int i = 0; i < initialSceneSetup.Length; i++ )
			{
				if( !initialSceneSetup[i].isLoaded )
					EditorSceneManager.CloseScene( EditorSceneManager.GetSceneByPath( initialSceneSetup[i].path ), false );
			}

			initialSceneSetup = null;
			return true;
		}

		// Assembly reloading; serialize nodes in a way that Unity can serialize
		void ISerializationCallbackReceiver.OnBeforeSerialize()
		{
			if( result == null )
				return;

			if( serializedGroups == null )
				serializedGroups = new List<SerializableResultGroup>( result.Count );
			else
				serializedGroups.Clear();

			if( serializedNodes == null )
				serializedNodes = new List<SerializableNode>( result.Count * 16 );
			else
				serializedNodes.Clear();

			Dictionary<ReferenceNode, int> nodeToIndex = new Dictionary<ReferenceNode, int>( result.Count * 16 );
			for( int i = 0; i < result.Count; i++ )
				serializedGroups.Add( result[i].Serialize( nodeToIndex, serializedNodes ) );
		}

		// Assembly reloaded; deserialize nodes to construct the original graph
		void ISerializationCallbackReceiver.OnAfterDeserialize()
		{
			if( serializedGroups == null || serializedNodes == null )
				return;

			if( result == null )
				result = new List<SearchResultGroup>( serializedGroups.Count );
			else
				result.Clear();

			List<ReferenceNode> allNodes = new List<ReferenceNode>( serializedNodes.Count );
			for( int i = 0; i < serializedNodes.Count; i++ )
				allNodes.Add( new ReferenceNode() );

			for( int i = 0; i < serializedNodes.Count; i++ )
				allNodes[i].Deserialize( serializedNodes[i], allNodes );

			for( int i = 0; i < serializedGroups.Count; i++ )
			{
				result.Add( new SearchResultGroup( serializedGroups[i].title, serializedGroups[i].type, serializedGroups[i].isExpanded, serializedGroups[i].pendingSearch ) );
				result[i].Deserialize( serializedGroups[i], allNodes );
			}

			serializedNodes.Clear();
			serializedGroups.Clear();
		}
	}

	// Custom class to hold the results for a single scene or Assets folder
	public class SearchResultGroup
	{
		public enum GroupType { Assets = 0, Scene = 1, DontDestroyOnLoad = 2 };

		// Custom struct to hold a single path to a reference
		public struct ReferencePath
		{
			public readonly ReferenceNode startNode;
			public readonly int[] pathLinksToFollow;

			public ReferencePath( ReferenceNode startNode, int[] pathIndices )
			{
				this.startNode = startNode;
				pathLinksToFollow = pathIndices;
			}
		}

		private const float HEADER_HEIGHT = 40f;

		public string Title { get; private set; }
		public GroupType Type { get; private set; }
		public bool IsExpanded { get; private set; }
		public bool PendingSearch { get; private set; }

		private readonly List<ReferenceNode> references;
		private List<ReferencePath> referencePathsShortUnique;
		private List<ReferencePath> referencePathsShortest;

		private float calculatedGUIWidth;
		private float calculatedGUIHeight;
		private PathDrawingMode calculatedGUIMode;
		private ReferenceNodeGUI[] guiNodes;

		public int NumberOfReferences { get { return references.Count; } }
		public ReferenceNode this[int index] { get { return references[index]; } }

		public SearchResultGroup( string title, GroupType type, bool isExpanded = true, bool pendingSearch = false )
		{
			Title = title;
			Type = type;
			IsExpanded = isExpanded;
			PendingSearch = pendingSearch;

			references = new List<ReferenceNode>();
			referencePathsShortUnique = null;
			referencePathsShortest = null;

			calculatedGUIWidth = 0f;
			guiNodes = null;
		}

		// Add a reference to the list
		public void AddReference( ReferenceNode node )
		{
			references.Add( node );
		}

		// Removes all nodes
		public void Clear()
		{
			references.Clear();

			if( referencePathsShortUnique != null )
				referencePathsShortUnique.Clear();
			if( referencePathsShortest != null )
				referencePathsShortest.Clear();

			PendingSearch = false;
			calculatedGUIWidth = 0f;
			guiNodes = null;
		}

		// Initializes commonly used variables of the nodes
		public void InitializeNodes( Func<object, ReferenceNode> nodeGetter )
		{
			// Remove root nodes that don't have any outgoing links or have null node objects (somehow)
			for( int i = references.Count - 1; i >= 0; i-- )
			{
				if( references[i].NumberOfOutgoingLinks == 0 )
					references.RemoveAtFast( i );
				else
				{
					object nodeObject = references[i].nodeObject;
					if( nodeObject == null || nodeObject.Equals( null ) )
						references.RemoveAtFast( i );
				}
			}

			for( int i = references.Count - 1; i >= 0; i-- )
			{
				references[i].VerifyLinksRecursively();

				// Some links might be removed during verification
				if( references[i].NumberOfOutgoingLinks == 0 )
					references.RemoveAtFast( i );
			}

			if( references.Count == 0 )
				return;

			List<ReferenceNode> callStack = new List<ReferenceNode>( 8 );

			// For simplicity's sake, get rid of root nodes that are already part of another node's hierarchy
			for( int i = references.Count - 1; i >= 0; i-- )
			{
				if( IsRootNodePartOfAnotherRootNode( i, callStack ) )
					references.RemoveAtFast( i );
			}

			// For clarity, a reference path shouldn't start with a sub-asset but instead with its corresponding main asset
			for( int i = references.Count - 1; i >= 0; i-- )
			{
				object nodeObject = references[i].nodeObject;
				if( nodeObject.IsAsset() && !AssetDatabase.IsMainAsset( (Object) nodeObject ) )
				{
					string assetPath = AssetDatabase.GetAssetPath( (Object) nodeObject );
					if( string.IsNullOrEmpty( assetPath ) )
						continue;

					Object mainAsset = AssetDatabase.LoadMainAssetAtPath( assetPath );
					if( mainAsset == null || mainAsset.Equals( null ) )
						continue;

					// We're already calling AssetDatabase.IsMainAsset but just in case...
					if( ReferenceEquals( nodeObject, mainAsset ) )
						continue;

					if( nodeObject is Component && ( (Component) nodeObject ).gameObject == mainAsset )
						continue;

					// Get a ReferenceNode for the main asset, add a link to the sub-asset's node and change the root node
					ReferenceNode newRootNode = nodeGetter( mainAsset );
					newRootNode.AddLinkTo( references[i], ( nodeObject is Component || nodeObject is GameObject ) ? "Child object" : "Sub-asset" );
					references[i] = newRootNode;

					// Make sure that the new root node isn't already a part of another node's hierarchy
					if( IsRootNodePartOfAnotherRootNode( i, callStack ) )
						references.RemoveAtFast( i );
				}
			}

			for( int i = 0; i < references.Count; i++ )
				references[i].InitializeRecursively();
		}

		// Check if node exists in this results set
		public bool Contains( ReferenceNode node )
		{
			for( int i = 0; i < references.Count; i++ )
			{
				if( references[i] == node )
					return true;
			}

			return false;
		}

		private bool IsRootNodePartOfAnotherRootNode( int index, List<ReferenceNode> callStack )
		{
			ReferenceNode node = references[index];
			for( int i = references.Count - 1; i >= 0; i-- )
			{
				if( index == i )
					continue;

				if( references[i].nodeObject == node.nodeObject || references[i].NodeExistsInChildrenRecursive( node, callStack ) )
					return true;
			}

			return false;
		}

		// Add all the Object's in this container to the set
		public void AddObjectsTo( HashSet<Object> objectsSet )
		{
			if( PendingSearch )
				return;

			CalculateShortestPathsToReferences();

			for( int i = 0; i < referencePathsShortUnique.Count; i++ )
			{
				Object obj = referencePathsShortUnique[i].startNode.UnityObject;
				if( obj != null && !obj.Equals( null ) )
					objectsSet.Add( obj );
			}
		}

		// Add all the GameObject's in this container to the set
		public void AddGameObjectsTo( HashSet<GameObject> gameObjectsSet )
		{
			if( PendingSearch )
				return;

			CalculateShortestPathsToReferences();

			for( int i = 0; i < referencePathsShortUnique.Count; i++ )
			{
				Object obj = referencePathsShortUnique[i].startNode.UnityObject;
				if( obj != null && !obj.Equals( null ) )
				{
					if( obj is GameObject )
						gameObjectsSet.Add( (GameObject) obj );
					else if( obj is Component )
						gameObjectsSet.Add( ( (Component) obj ).gameObject );
				}
			}
		}

		// Calculate short unique paths to the references
		private void CalculateShortestPathsToReferences()
		{
			if( referencePathsShortUnique != null )
				return;

			referencePathsShortUnique = new List<ReferencePath>( 32 );
			for( int i = 0; i < references.Count; i++ )
				references[i].CalculateShortUniquePaths( referencePathsShortUnique, Type == GroupType.Scene || Type == GroupType.DontDestroyOnLoad );

			referencePathsShortest = new List<ReferencePath>( referencePathsShortUnique.Count );
			for( int i = 0; i < referencePathsShortUnique.Count; i++ )
			{
				int[] linksToFollow = referencePathsShortUnique[i].pathLinksToFollow;

				// Find the last two nodes in this path
				ReferenceNode nodeBeforeLast = referencePathsShortUnique[i].startNode;
				for( int j = 0; j < linksToFollow.Length - 1; j++ )
					nodeBeforeLast = nodeBeforeLast[linksToFollow[j]].targetNode;

				// Check if these two nodes are unique
				bool isUnique = true;
				for( int j = 0; j < referencePathsShortest.Count; j++ )
				{
					ReferencePath path = referencePathsShortest[j];
					if( path.startNode == nodeBeforeLast && path.pathLinksToFollow[0] == linksToFollow[linksToFollow.Length - 1] )
					{
						isUnique = false;
						break;
					}
				}

				if( isUnique )
					referencePathsShortest.Add( new ReferencePath( nodeBeforeLast, new int[1] { linksToFollow[linksToFollow.Length - 1] } ) );
			}
		}

		// Draw the results found for this container
		public void DrawOnGUI( SearchResultDrawParameters parameters )
		{
			if( Event.current.type == EventType.Repaint && ( guiNodes == null || parameters.pathDrawingMode != calculatedGUIMode || parameters.guiRect.width != calculatedGUIWidth ) )
			{
				GenerateGUINodes( parameters );
				parameters.shouldRefreshEditorWindow = true; // A repaint is needed to work nicely with GUILayout
			}

			Color c = GUI.color;
			GUI.color = Color.cyan;

			Rect rect = parameters.guiRect;
			float width = rect.width;
			rect.width = 40f;
			rect.height = HEADER_HEIGHT;
			if( GUI.Button( rect, IsExpanded ? "v" : ">" ) )
			{
				IsExpanded = !IsExpanded;

				parameters.shouldRefreshEditorWindow = true;
				GUIUtility.ExitGUI();
			}

			rect.x += 40f;
			rect.width = width - ( parameters.searchResult != null ? 140f : 40f );
			if( GUI.Button( rect, Title, Utilities.BoxGUIStyle ) && Type == GroupType.Scene )
			{
				if( Event.current.button != 1 )
				{
					// If the container (scene, usually) is left clicked, highlight it on Project view
					AssetDatabase.LoadAssetAtPath<SceneAsset>( Title ).SelectInEditor();
				}
				else if( !EditorApplication.isPlaying && EditorSceneManager.loadedSceneCount > 1 )
				{
					// Show context menu when SearchResultGroup's header is right clicked
					Scene scene = EditorSceneManager.GetSceneByPath( Title );
					if( scene.isLoaded )
					{
						GenericMenu contextMenu = new GenericMenu();
						contextMenu.AddItem( new GUIContent( "Close Scene" ), false, () =>
						{
							if( !scene.isDirty || EditorSceneManager.SaveModifiedScenesIfUserWantsTo( new Scene[1] { scene } ) )
								EditorSceneManager.CloseScene( scene, true );
						} );
						contextMenu.ShowAsContext();
					}
				}
			}

			if( parameters.searchResult != null )
			{
				rect.x += width - 140f;
				rect.width = 100f;
				if( GUI.Button( rect, "Refresh" ) )
				{
					if( Utilities.AreScenesSaved() || ( EditorUtility.DisplayDialog( "Refresh", "Some scene(s) have unsaved changes, they must be saved before the refresh.", "Save Scenes", "Cancel" ) && EditorSceneManager.SaveOpenScenes() ) )
					{
						parameters.searchResult.RefreshSearchResultGroup( this, parameters.noAssetDatabaseChanges );
						GUIUtility.ExitGUI();
					}
				}
			}

			rect = parameters.guiRect;
			rect.y += HEADER_HEIGHT + 5f;

			if( IsExpanded )
			{
				GUI.color = Color.yellow;

				if( PendingSearch )
				{
					rect.height = 25f;
					GUI.Box( rect, "Lazy Search: this scene potentially has some references, hit Refresh to find them", Utilities.BoxGUIStyle );
				}
				else if( references.Count == 0 )
				{
					rect.height = 25f;
					GUI.Box( rect, "No references found...", Utilities.BoxGUIStyle );
				}
				else if( guiNodes != null )
				{
					rect.height = calculatedGUIHeight;
					parameters.guiRect = rect;

					for( int i = 0; i < guiNodes.Length; i++ )
						guiNodes[i].DrawOnGUI( parameters );
				}

				rect.y += rect.height;
			}

			parameters.guiRect = rect;
			GUI.color = c;
		}

		private void GenerateGUINodes( SearchResultDrawParameters parameters )
		{
			float guiRectWidth = parameters.guiRect.width;

			if( guiNodes == null || parameters.pathDrawingMode != calculatedGUIMode )
			{
				if( parameters.pathDrawingMode == PathDrawingMode.Full )
				{
					List<ReferenceNode> stack = new List<ReferenceNode>( 8 );
					guiNodes = new ReferenceNodeGUI[references.Count];

					for( int i = 0; i < guiNodes.Length; i++ )
						guiNodes[i] = references[i].GenerateGUINodeRecursive( stack, null );
				}
				else
				{
					if( referencePathsShortUnique == null )
						CalculateShortestPathsToReferences();

					List<ReferencePath> pathsToDraw;
					if( parameters.pathDrawingMode == PathDrawingMode.ShortRelevantParts )
						pathsToDraw = referencePathsShortUnique;
					else
						pathsToDraw = referencePathsShortest;

					guiNodes = new ReferenceNodeGUI[pathsToDraw.Count];

					for( int i = 0; i < guiNodes.Length; i++ )
					{
						ReferencePath path = pathsToDraw[i];
						ReferenceNode currentNode = path.startNode;

						ReferenceNodeGUI currentNodeGUI = currentNode.GenerateGUINode( null );
						guiNodes[i] = currentNodeGUI;

						for( int j = 0; j < path.pathLinksToFollow.Length; j++ )
						{
							ReferenceNode.Link link = currentNode[path.pathLinksToFollow[j]];
							currentNodeGUI.links = new ReferenceNodeGUI[1] { link.targetNode.GenerateGUINode( link.description ) };

							currentNode = link.targetNode;
							currentNodeGUI = currentNodeGUI.links[0];
						}

						currentNodeGUI.links = new ReferenceNodeGUI[0];
					}
				}

				for( int i = 0; i < guiNodes.Length; i++ )
					guiNodes[i].CalculateDepth();
			}

			calculatedGUIMode = parameters.pathDrawingMode;
			calculatedGUIWidth = guiRectWidth;

			calculatedGUIHeight = -5f; // will start from -5f + 5f = 0f
			for( int i = 0; i < guiNodes.Length; i++ )
			{
				calculatedGUIHeight += 5f;
				guiNodes[i].CalculateHeight( guiRectWidth );
				guiNodes[i].CalculateOffset( new Vector2( 0f, calculatedGUIHeight ) );
				calculatedGUIHeight += guiNodes[i].size.y;
			}
		}

		// Serialize this result group
		public SearchResult.SerializableResultGroup Serialize( Dictionary<ReferenceNode, int> nodeToIndex, List<SearchResult.SerializableNode> serializedNodes )
		{
			SearchResult.SerializableResultGroup serializedResultGroup = new SearchResult.SerializableResultGroup()
			{
				title = Title,
				type = Type,
				isExpanded = IsExpanded,
				pendingSearch = PendingSearch
			};

			if( references != null )
			{
				serializedResultGroup.initialSerializedNodes = new List<int>( references.Count );

				for( int i = 0; i < references.Count; i++ )
					serializedResultGroup.initialSerializedNodes.Add( references[i].SerializeRecursively( nodeToIndex, serializedNodes ) );
			}

			return serializedResultGroup;
		}

		// Deserialize this result group from the serialized data
		public void Deserialize( SearchResult.SerializableResultGroup serializedResultGroup, List<ReferenceNode> allNodes )
		{
			if( serializedResultGroup.initialSerializedNodes != null )
			{
				for( int i = 0; i < serializedResultGroup.initialSerializedNodes.Count; i++ )
					references.Add( allNodes[serializedResultGroup.initialSerializedNodes[i]] );
			}

			referencePathsShortUnique = null;
			referencePathsShortest = null;
		}
	}

	// Custom class to hold an object in the path to a reference as a node
	public class ReferenceNode
	{
		public struct Link
		{
			public readonly ReferenceNode targetNode;
			public readonly string description;

			public Link( ReferenceNode targetNode, string description )
			{
				this.targetNode = targetNode;
				this.description = description;
			}
		}

		// Unique identifier is used while serializing the node
		private static int uid_last = 0;
		private readonly int uid;

		internal object nodeObject;
		private int? instanceId; // instanceId of the nodeObject if it is a Unity object, null otherwise
		private string description; // String to print on this node

		private readonly List<Link> links;

		public Object UnityObject { get { return instanceId.HasValue ? EditorUtility.InstanceIDToObject( instanceId.Value ) : null; } }

		public int NumberOfOutgoingLinks { get { return links.Count; } }
		public Link this[int index] { get { return links[index]; } }

		public ReferenceNode()
		{
			links = new List<Link>( 2 );
			uid = uid_last++;
		}

		// Add a one-way connection to another node
		public void AddLinkTo( ReferenceNode nextNode, string description = null )
		{
			if( nextNode != null && nextNode != this )
			{
				if( !string.IsNullOrEmpty( description ) )
					description = "[" + description + "]";

				// Avoid duplicate links
				for( int i = 0; i < links.Count; i++ )
				{
					if( links[i].targetNode == nextNode )
					{
						if( !string.IsNullOrEmpty( description ) )
						{
							if( !string.IsNullOrEmpty( links[i].description ) )
								links[i] = new Link( links[i].targetNode, string.Concat( links[i].description, Environment.NewLine, description ) );
							else
								links[i] = new Link( links[i].targetNode, description );
						}

						return;
					}
				}

				links.Add( new Link( nextNode, description ) );
			}
		}

		public void CopyReferencesTo( ReferenceNode other )
		{
			other.links.Clear();
			other.links.AddRange( links );
		}

		// Remove any redundant connections that this node has
		public void VerifyLinksRecursively()
		{
			// Make sure that this functions isn't called multiple times per node (avoids StackOverflowException)
			if( description != null )
				return;

			description = "";

			for( int i = links.Count - 1; i >= 0; i-- )
			{
				// Links from a GameObject to its components should be omitted if the component has no references (these are redundant links)
				if( links[i].targetNode.links.Count == 0 && nodeObject is GameObject )
				{
					Component component = links[i].targetNode.nodeObject as Component;
					if( component != null && !component.Equals( null ) && component.gameObject == (GameObject) nodeObject )
					{
						links.RemoveAtFast( i );
						continue;
					}
				}

				links[i].targetNode.VerifyLinksRecursively();
			}

			description = null;
		}

		// Initialize node's commonly used variables
		public void InitializeRecursively()
		{
			if( description != null ) // Already initialized
				return;

			Object unityObject = nodeObject as Object;
			if( unityObject != null )
			{
				instanceId = unityObject.GetInstanceID();
				description = unityObject.name + " (" + unityObject.GetType() + ")";
			}
			else if( nodeObject != null )
			{
				instanceId = null;
				description = nodeObject.GetType() + " object";
			}
			else
			{
				instanceId = null;
				description = "<<destroyed>>";
			}

			nodeObject = null; // don't hold Object reference, allow Unity to GC used memory

			for( int i = 0; i < links.Count; i++ )
				links[i].targetNode.InitializeRecursively();
		}

		// Returns whether or not specified node is part of this node's siblings
		public bool NodeExistsInChildrenRecursive( ReferenceNode node, List<ReferenceNode> callStack )
		{
			if( callStack.ContainsFast( this ) )
				return false;

			for( int i = 0; i < links.Count; i++ )
			{
				if( links[i].targetNode == node )
					return true;
			}

			callStack.Add( this );

			for( int i = 0; i < links.Count; i++ )
			{
				if( links[i].targetNode.NodeExistsInChildrenRecursive( node, callStack ) )
				{
					callStack.RemoveAt( callStack.Count - 1 );
					return true;
				}
			}

			callStack.RemoveAt( callStack.Count - 1 );
			return false;
		}

		// Clear this node so that it can be reused later
		public void Clear()
		{
			nodeObject = null;
			links.Clear();
		}

		// Calculate short unique paths that start with this node
		public void CalculateShortUniquePaths( List<SearchResultGroup.ReferencePath> currentPaths, bool startPathsWithSceneObjects )
		{
			CalculateShortUniquePaths( currentPaths, startPathsWithSceneObjects, new List<ReferenceNode>( 8 ), new List<int>( 8 ) { -1 }, 0, new List<ReferenceNode>( 8 ) );
		}

		// Just some boring calculations to find the short unique paths recursively
		private void CalculateShortUniquePaths( List<SearchResultGroup.ReferencePath> shortestPaths, bool startPathsWithSceneObjects, List<ReferenceNode> currentPath, List<int> currentPathIndices, int latestObjectIndexInPath, List<ReferenceNode> stack )
		{
			int currentIndex = currentPath.Count;
			currentPath.Add( this );

			if( links.Count == 0 || stack.ContainsFast( this ) )
			{
				// Check if the path to the reference is unique (not discovered so far)
				bool isUnique = true;
				for( int i = 0; i < shortestPaths.Count; i++ )
				{
					if( shortestPaths[i].startNode == currentPath[latestObjectIndexInPath] && shortestPaths[i].pathLinksToFollow.Length == currentPathIndices.Count - latestObjectIndexInPath - 1 )
					{
						int j = latestObjectIndexInPath + 1;
						for( int k = 0; j < currentPathIndices.Count; j++, k++ )
						{
							if( shortestPaths[i].pathLinksToFollow[k] != currentPathIndices[j] )
								break;
						}

						if( j == currentPathIndices.Count )
						{
							isUnique = false;
							break;
						}
					}
				}

				// Don't allow duplicate short paths
				if( isUnique )
				{
					int[] pathIndices = new int[currentPathIndices.Count - latestObjectIndexInPath - 1];
					for( int i = latestObjectIndexInPath + 1, j = 0; i < currentPathIndices.Count; i++, j++ )
						pathIndices[j] = currentPathIndices[i];

					shortestPaths.Add( new SearchResultGroup.ReferencePath( currentPath[latestObjectIndexInPath], pathIndices ) );
				}
			}
			else
			{
				if( instanceId.HasValue ) // nodeObject is Unity object
				{
					if( !startPathsWithSceneObjects || !AssetDatabase.Contains( instanceId.Value ) )
						latestObjectIndexInPath = currentIndex;
				}

				for( int i = 0; i < links.Count; i++ )
				{
					currentPathIndices.Add( i );
					stack.Add( this );

					links[i].targetNode.CalculateShortUniquePaths( shortestPaths, startPathsWithSceneObjects, currentPath, currentPathIndices, latestObjectIndexInPath, stack );

					stack.RemoveAt( stack.Count - 1 );
					currentPathIndices.RemoveAt( currentIndex + 1 );
				}
			}

			currentPath.RemoveAt( currentIndex );
		}

		public ReferenceNodeGUI GenerateGUINode( string linkToPrevNodeDescription )
		{
			return new ReferenceNodeGUI()
			{
				label = new GUIContent( string.IsNullOrEmpty( linkToPrevNodeDescription ) ? description : ( linkToPrevNodeDescription + "\n" + description ) ),
				instanceId = instanceId
			};
		}

		public ReferenceNodeGUI GenerateGUINodeRecursive( List<ReferenceNode> stack, string linkToPrevNodeDescription )
		{
			ReferenceNodeGUI guiNode = GenerateGUINode( linkToPrevNodeDescription );

			if( stack.ContainsFast( this ) )
				guiNode.links = new ReferenceNodeGUI[0];
			else
			{
				stack.Add( this );

				guiNode.links = new ReferenceNodeGUI[links.Count];
				for( int i = 0; i < links.Count; i++ )
					guiNode.links[i] = links[i].targetNode.GenerateGUINodeRecursive( stack, links[i].description );

				stack.RemoveAt( stack.Count - 1 );
			}

			return guiNode;
		}

		// Serialize this node and its connected nodes recursively
		public int SerializeRecursively( Dictionary<ReferenceNode, int> nodeToIndex, List<SearchResult.SerializableNode> serializedNodes )
		{
			int index;
			if( nodeToIndex.TryGetValue( this, out index ) )
				return index;

			SearchResult.SerializableNode serializedNode = new SearchResult.SerializableNode()
			{
				instanceId = instanceId ?? 0,
				isUnityObject = instanceId.HasValue,
				description = description
			};

			index = serializedNodes.Count;
			nodeToIndex[this] = index;
			serializedNodes.Add( serializedNode );

			if( links.Count > 0 )
			{
				serializedNode.links = new List<int>( links.Count );
				serializedNode.linkDescriptions = new List<string>( links.Count );

				for( int i = 0; i < links.Count; i++ )
				{
					serializedNode.links.Add( links[i].targetNode.SerializeRecursively( nodeToIndex, serializedNodes ) );
					serializedNode.linkDescriptions.Add( links[i].description );
				}
			}

			return index;
		}

		// Deserialize this node and its links from the serialized data
		public void Deserialize( SearchResult.SerializableNode serializedNode, List<ReferenceNode> allNodes )
		{
			if( serializedNode.isUnityObject )
				instanceId = serializedNode.instanceId;
			else
				instanceId = null;

			description = serializedNode.description;

			if( serializedNode.links != null )
			{
				for( int i = 0; i < serializedNode.links.Count; i++ )
					links.Add( new Link( allNodes[serializedNode.links[i]], serializedNode.linkDescriptions[i] ) );
			}
		}

		public override int GetHashCode()
		{
			return uid;
		}
	}

	public class ReferenceNodeGUI
	{
		public GUIContent label;
		public ReferenceNodeGUI[] links;
		public int? instanceId;

		private int depth;

		private Vector2 offset;
		public Vector2 size;

		public void CalculateDepth()
		{
			depth = 0;

			for( int i = 0; i < links.Length; i++ )
			{
				links[i].CalculateDepth();
				if( links[i].depth > depth )
					depth = links[i].depth;
			}

			depth++;
		}

		public void CalculateHeight( float totalWidth )
		{
			size.x = totalWidth / depth;
			totalWidth -= size.x;
			size.y = Utilities.BoxGUIStyle.CalcHeight( label, size.x );
			float connectedNodesHeight = 0f;
			for( int i = 0; i < links.Length; i++ )
			{
				links[i].CalculateHeight( totalWidth );
				connectedNodesHeight += links[i].size.y;
			}

			if( size.y < connectedNodesHeight )
				size.y = connectedNodesHeight;
			else if( size.y > connectedNodesHeight )
				ExpandHeightLeftToRight();
		}

		private void ExpandHeightLeftToRight()
		{
			float connectedNodesHeight = 0f;
			for( int i = 0; i < links.Length; i++ )
				connectedNodesHeight += links[i].size.y;

			if( size.y > connectedNodesHeight )
			{
				for( int i = 0; i < links.Length; i++ )
				{
					links[i].size.y = size.y * links[i].size.y / connectedNodesHeight;
					links[i].ExpandHeightLeftToRight();
				}
			}
		}

		public void CalculateOffset( Vector2 baseOffset )
		{
			offset = baseOffset;
			baseOffset.x += size.x;

			for( int i = 0; i < links.Length; i++ )
			{
				links[i].CalculateOffset( baseOffset );
				baseOffset.y += links[i].size.y;
			}
		}

		public void DrawOnGUI( SearchResultDrawParameters parameters )
		{
			Rect rect = parameters.guiRect;
			rect.position += offset;
			rect.size = size;

			if( GUI.Button( rect, label, Utilities.BoxGUIStyle ) && instanceId.HasValue )
			{
				// If a reference is clicked, highlight it (either on Hierarchy view or Project view)
				EditorUtility.InstanceIDToObject( instanceId.Value ).SelectInEditor();
			}

			for( int i = 0; i < links.Length; i++ )
				links[i].DrawOnGUI( parameters );

			if( parameters.showTooltips && Event.current.type == EventType.Repaint && rect.Contains( Event.current.mousePosition ) )
				parameters.tooltip = label.text;
		}
	}
}