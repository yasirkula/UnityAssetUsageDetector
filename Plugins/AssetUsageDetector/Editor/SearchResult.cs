using AssetUsageDetectorNamespace.Extras;
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AssetUsageDetectorNamespace
{
	[Serializable]
	public class SearchResultDrawParameters
	{
		public PathDrawingMode pathDrawingMode;
		public bool showTooltips;
		public string tooltip;

		public SearchResultDrawParameters( PathDrawingMode pathDrawingMode, bool showTooltips )
		{
			this.pathDrawingMode = pathDrawingMode;
			this.showTooltips = showTooltips;
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
			public bool clickable;

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

		private bool success;
		private List<SearchResultGroup> result;
		private SceneSetup[] initialSceneSetup;

		private List<SerializableNode> serializedNodes;
		private List<SerializableResultGroup> serializedGroups;

		public int NumberOfGroups { get { return result.Count; } }
		public SearchResultGroup this[int index] { get { return result[index]; } }

		public bool SearchCompletedSuccessfully { get { return success; } }
		public bool InitialSceneSetupConfigured { get { return initialSceneSetup != null; } }

		public SearchResult( bool success, List<SearchResultGroup> result, SceneSetup[] initialSceneSetup )
		{
			if( result == null )
				result = new List<SearchResultGroup>( 0 );

			this.success = success;
			this.result = result;
			this.initialSceneSetup = initialSceneSetup;
		}

		public void DrawOnGUI( SearchResultDrawParameters parameters )
		{
			parameters.tooltip = null;

			for( int i = 0; i < result.Count; i++ )
				result[i].DrawOnGUI( parameters );

			if( parameters.tooltip != null )
			{
				// Show tooltip at mouse position
				Vector2 mousePos = Event.current.mousePosition;
				Vector2 size = Utilities.TooltipGUIStyle.CalcSize( new GUIContent( parameters.tooltip ) );
				size.x += 10f;

				GUI.Box( new Rect( new Vector2( mousePos.x - size.x * 0.5f, mousePos.y - size.y ), size ), parameters.tooltip, Utilities.TooltipGUIStyle );
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
			if( initialSceneSetup == null )
				return true;

			if( EditorApplication.isPlaying || !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo() )
				return false;

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
				result.Add( new SearchResultGroup( serializedGroups[i].title, serializedGroups[i].clickable ) );
				result[i].Deserialize( serializedGroups[i], allNodes );
			}

			serializedNodes.Clear();
			serializedGroups.Clear();
		}
	}

	// Custom class to hold the results for a single scene or Assets folder
	public class SearchResultGroup
	{
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

		private readonly string title;
		private readonly bool clickable;

		private readonly List<ReferenceNode> references;
		private List<ReferencePath> referencePathsShortUnique;
		private List<ReferencePath> referencePathsShortest;

		public int NumberOfReferences { get { return references.Count; } }
		public ReferenceNode this[int index] { get { return references[index]; } }

		public SearchResultGroup( string title, bool clickable )
		{
			this.title = title;
			this.clickable = clickable;

			references = new List<ReferenceNode>();
			referencePathsShortUnique = null;
			referencePathsShortest = null;
		}

		// Add a reference to the list
		public void AddReference( ReferenceNode node )
		{
			references.Add( node );
		}

		// Initializes commonly used variables of the nodes
		public void InitializeNodes()
		{
			for( int i = references.Count - 1; i >= 0; i-- )
			{
				ReferenceNode node = references[i];
				if( node.NumberOfOutgoingLinks == 0 )
					references.RemoveAt( i );
				else
				{
					// For simplicity's sake, get rid of root nodes that are
					// already part of another node's hierarchy
					bool isRootNodeChildOfOtherNode = false;
					for( int j = references.Count - 1; j >= 0; j-- )
					{
						if( i == j )
							continue;

						if( references[j].NodeExistsInChildrenRecursive( node ) )
						{
							isRootNodeChildOfOtherNode = true;
							break;
						}
					}

					if( isRootNodeChildOfOtherNode )
						references.RemoveAt( i );
					else
						node.InitializeRecursively();
				}
			}
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

		// Add all the Object's in this container to the set
		public void AddObjectsTo( HashSet<Object> objectsSet )
		{
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
		public void CalculateShortestPathsToReferences()
		{
			if( referencePathsShortUnique != null )
				return;

			referencePathsShortUnique = new List<ReferencePath>( 32 );
			for( int i = 0; i < references.Count; i++ )
				references[i].CalculateShortUniquePaths( referencePathsShortUnique );

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
			Color c = GUI.color;
			GUI.color = Color.cyan;

			if( GUILayout.Button( title, Utilities.BoxGUIStyle, Utilities.GL_EXPAND_WIDTH, Utilities.GL_HEIGHT_40 ) && clickable )
			{
				// If the container (scene, usually) is clicked, highlight it on Project view
				AssetDatabase.LoadAssetAtPath<SceneAsset>( title ).SelectInEditor();
			}

			GUI.color = Color.yellow;

			if( parameters.pathDrawingMode == PathDrawingMode.Full )
			{
				for( int i = 0; i < references.Count; i++ )
				{
					GUILayout.Space( 5 );
					references[i].DrawOnGUIRecursively( parameters, null );
				}
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

				for( int i = 0; i < pathsToDraw.Count; i++ )
				{
					GUILayout.Space( 5 );

					GUILayout.BeginHorizontal();

					ReferencePath path = pathsToDraw[i];
					path.startNode.DrawOnGUI( parameters, null );

					ReferenceNode currentNode = path.startNode;
					for( int j = 0; j < path.pathLinksToFollow.Length; j++ )
					{
						ReferenceNode.Link link = currentNode[path.pathLinksToFollow[j]];
						link.targetNode.DrawOnGUI( parameters, link.description );
						currentNode = link.targetNode;
					}

					GUILayout.EndHorizontal();
				}
			}

			GUI.color = c;

			GUILayout.Space( 10 );
		}

		// Serialize this result group
		public SearchResult.SerializableResultGroup Serialize( Dictionary<ReferenceNode, int> nodeToIndex, List<SearchResult.SerializableNode> serializedNodes )
		{
			SearchResult.SerializableResultGroup serializedResultGroup = new SearchResult.SerializableResultGroup()
			{
				title = title,
				clickable = clickable
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

		public object nodeObject;
		private readonly List<Link> links;

		private int? instanceId; // instanceId of the nodeObject if it is a Unity object, null otherwise
		private string description; // String to print on this node

		public Object UnityObject { get { return instanceId.HasValue ? EditorUtility.InstanceIDToObject( instanceId.Value ) : null; } }

		public int NumberOfOutgoingLinks { get { return links.Count; } }
		public Link this[int index] { get { return links[index]; } }

		public ReferenceNode()
		{
			links = new List<Link>( 2 );
			uid = uid_last++;
		}

		public ReferenceNode( object obj ) : this()
		{
			nodeObject = obj;
		}

		// Add a one-way connection to another node
		public void AddLinkTo( ReferenceNode nextNode, string description = null )
		{
			if( nextNode != null )
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
		public bool NodeExistsInChildrenRecursive( ReferenceNode node )
		{
			for( int i = 0; i < links.Count; i++ )
			{
				if( links[i].targetNode == node )
					return true;
			}

			for( int i = 0; i < links.Count; i++ )
			{
				if( links[i].targetNode.NodeExistsInChildrenRecursive( node ) )
					return true;
			}

			return false;
		}

		// Clear this node so that it can be reused later
		public void Clear()
		{
			nodeObject = null;
			links.Clear();
		}

		// Calculate short unique paths that start with this node
		public void CalculateShortUniquePaths( List<SearchResultGroup.ReferencePath> currentPaths )
		{
			CalculateShortUniquePaths( currentPaths, new List<ReferenceNode>( 8 ), new List<int>( 8 ) { -1 }, 0 );
		}

		// Just some boring calculations to find the short unique paths recursively
		private void CalculateShortUniquePaths( List<SearchResultGroup.ReferencePath> shortestPaths, List<ReferenceNode> currentPath, List<int> currentPathIndices, int latestObjectIndexInPath )
		{
			int currentIndex = currentPath.Count;
			currentPath.Add( this );

			if( links.Count == 0 )
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
					latestObjectIndexInPath = currentIndex;

				for( int i = 0; i < links.Count; i++ )
				{
					currentPathIndices.Add( i );
					links[i].targetNode.CalculateShortUniquePaths( shortestPaths, currentPath, currentPathIndices, latestObjectIndexInPath );
					currentPathIndices.RemoveAt( currentIndex + 1 );
				}
			}

			currentPath.RemoveAt( currentIndex );
		}

		// Draw all the paths that start with this node on GUI recursively
		public void DrawOnGUIRecursively( SearchResultDrawParameters parameters, string linkToPrevNodeDescription )
		{
			GUILayout.BeginHorizontal();

			DrawOnGUI( parameters, linkToPrevNodeDescription );

			if( links.Count > 0 )
			{
				GUILayout.BeginVertical();

				for( int i = 0; i < links.Count; i++ )
				{
					ReferenceNode next = links[i].targetNode;
					next.DrawOnGUIRecursively( parameters, links[i].description );
				}

				GUILayout.EndVertical();
			}

			GUILayout.EndHorizontal();
		}

		// Draw only this node on GUI
		public void DrawOnGUI( SearchResultDrawParameters parameters, string linkToPrevNodeDescription )
		{
			string label = string.IsNullOrEmpty( linkToPrevNodeDescription ) ? description : ( linkToPrevNodeDescription + "\n" + description );
			if( GUILayout.Button( label, Utilities.BoxGUIStyle, Utilities.GL_EXPAND_HEIGHT ) )
			{
				// If a reference is clicked, highlight it (either on Hierarchy view or Project view)
				UnityObject.SelectInEditor();
			}

			if( parameters.showTooltips && Event.current.type == EventType.Repaint && GUILayoutUtility.GetLastRect().Contains( Event.current.mousePosition ) )
				parameters.tooltip = label;
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
}