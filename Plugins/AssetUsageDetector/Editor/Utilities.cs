using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
#if UNITY_2018_1_OR_NEWER
using Unity.Collections;
#endif
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
#if UNITY_2018_3_OR_NEWER && !UNITY_2021_2_OR_NEWER
using PrefabStage = UnityEditor.Experimental.SceneManagement.PrefabStage;
using PrefabStageUtility = UnityEditor.Experimental.SceneManagement.PrefabStageUtility;
#endif

namespace AssetUsageDetectorNamespace
{
	public static class Utilities
	{
		// A set of commonly used Unity types
		private static readonly HashSet<Type> primitiveUnityTypes = new HashSet<Type>()
		{
			typeof( string ), typeof( Vector4 ), typeof( Vector3 ), typeof( Vector2 ), typeof( Rect ),
			typeof( Quaternion ), typeof( Color ), typeof( Color32 ), typeof( LayerMask ), typeof( Bounds ),
			typeof( Matrix4x4 ), typeof( AnimationCurve ), typeof( Gradient ), typeof( RectOffset ),
			typeof( bool[] ), typeof( byte[] ), typeof( sbyte[] ), typeof( char[] ), typeof( decimal[] ),
			typeof( double[] ), typeof( float[] ), typeof( int[] ), typeof( uint[] ), typeof( long[] ),
			typeof( ulong[] ), typeof( short[] ), typeof( ushort[] ), typeof( string[] ),
			typeof( Vector4[] ), typeof( Vector3[] ), typeof( Vector2[] ), typeof( Rect[] ),
			typeof( Quaternion[] ), typeof( Color[] ), typeof( Color32[] ), typeof( LayerMask[] ), typeof( Bounds[] ),
			typeof( Matrix4x4[] ), typeof( AnimationCurve[] ), typeof( Gradient[] ), typeof( RectOffset[] ),
			typeof( List<bool> ), typeof( List<byte> ), typeof( List<sbyte> ), typeof( List<char> ), typeof( List<decimal> ),
			typeof( List<double> ), typeof( List<float> ), typeof( List<int> ), typeof( List<uint> ), typeof( List<long> ),
			typeof( List<ulong> ), typeof( List<short> ), typeof( List<ushort> ), typeof( List<string> ),
			typeof( List<Vector4> ), typeof( List<Vector3> ), typeof( List<Vector2> ), typeof( List<Rect> ),
			typeof( List<Quaternion> ), typeof( List<Color> ), typeof( List<Color32> ), typeof( List<LayerMask> ), typeof( List<Bounds> ),
			typeof( List<Matrix4x4> ), typeof( List<AnimationCurve> ), typeof( List<Gradient> ), typeof( List<RectOffset> ),
#if UNITY_2017_2_OR_NEWER
			typeof( Vector3Int ), typeof( Vector2Int ), typeof( RectInt ), typeof( BoundsInt ),
			typeof( Vector3Int[] ), typeof( Vector2Int[] ), typeof( RectInt[] ), typeof( BoundsInt[] ),
			typeof( List<Vector3Int> ), typeof( List<Vector2Int> ), typeof( List<RectInt> ), typeof( List<BoundsInt> )
#endif
		};

		private static readonly string reflectionNamespace = typeof( Assembly ).Namespace;
#if UNITY_2018_1_OR_NEWER
		private static readonly string nativeCollectionsNamespace = typeof( NativeArray<int> ).Namespace;
#endif

		private static MethodInfo screenFittedRectGetter;

		private static readonly HashSet<string> folderContentsSet = new HashSet<string>();

		internal static readonly StringBuilder stringBuilder = new StringBuilder( 400 );

		public static readonly GUILayoutOption GL_EXPAND_WIDTH = GUILayout.ExpandWidth( true );
		public static readonly GUILayoutOption GL_EXPAND_HEIGHT = GUILayout.ExpandHeight( true );
		public static readonly GUILayoutOption GL_WIDTH_25 = GUILayout.Width( 25 );
		public static readonly GUILayoutOption GL_WIDTH_100 = GUILayout.Width( 100 );
		public static readonly GUILayoutOption GL_WIDTH_250 = GUILayout.Width( 250 );
		public static readonly GUILayoutOption GL_HEIGHT_0 = GUILayout.Height( 0 );
		public static readonly GUILayoutOption GL_HEIGHT_2 = GUILayout.Height( 2 );
		public static readonly GUILayoutOption GL_HEIGHT_30 = GUILayout.Height( 30 );
		public static readonly GUILayoutOption GL_HEIGHT_35 = GUILayout.Height( 35 );
		public static readonly GUILayoutOption GL_HEIGHT_40 = GUILayout.Height( 40 );

		private static GUIStyle m_boxGUIStyle; // GUIStyle used to draw the results of the search
		public static GUIStyle BoxGUIStyle
		{
			get
			{
				if( m_boxGUIStyle == null )
				{
					m_boxGUIStyle = new GUIStyle( EditorStyles.helpBox )
					{
						alignment = TextAnchor.MiddleCenter,
						font = EditorStyles.label.font,
						richText = true
					};

					Color textColor = GUI.skin.button.normal.textColor;
					m_boxGUIStyle.normal.textColor = textColor;
					m_boxGUIStyle.hover.textColor = textColor;
					m_boxGUIStyle.focused.textColor = textColor;
					m_boxGUIStyle.active.textColor = textColor;

#if !UNITY_2019_1_OR_NEWER || UNITY_2019_3_OR_NEWER
					// On 2019.1 and 2019.2 versions, GUI.skin.button.fontSize returns 0 on some devices
					// https://forum.unity.com/threads/asset-usage-detector-find-references-to-an-asset-object-open-source.408134/page-3#post-7285954
					m_boxGUIStyle.fontSize = ( m_boxGUIStyle.fontSize + GUI.skin.button.fontSize ) / 2;
#endif
				}

				return m_boxGUIStyle;
			}
		}

		// Check if object is an asset or a Scene object
		public static bool IsAsset( this object obj )
		{
			return obj is Object && AssetDatabase.Contains( (Object) obj );
		}

		public static bool IsAsset( this Object obj )
		{
			return AssetDatabase.Contains( obj );
		}

		// Check if object is a folder asset
		public static bool IsFolder( this Object obj )
		{
			return obj is DefaultAsset && AssetDatabase.IsValidFolder( AssetDatabase.GetAssetPath( obj ) );
		}

		// Returns an enumerator to iterate through all asset paths in the folder
		public static IEnumerable<string> EnumerateFolderContents( Object folderAsset )
		{
			string[] folderContents = AssetDatabase.FindAssets( "", new string[] { AssetDatabase.GetAssetPath( folderAsset ) } );
			if( folderContents == null )
				return new EmptyEnumerator<string>();

			folderContentsSet.Clear();
			for( int i = 0; i < folderContents.Length; i++ )
			{
				string filePath = AssetDatabase.GUIDToAssetPath( folderContents[i] );
				if( !string.IsNullOrEmpty( filePath ) && !AssetDatabase.IsValidFolder( filePath ) )
					folderContentsSet.Add( filePath );
			}

			return folderContentsSet;
		}

		public static void GetObjectsToSelectAndPing( this Object obj, out Object selection, out Object pingTarget )
		{
			if( obj == null || obj.Equals( null ) )
			{
				selection = pingTarget = null;
				return;
			}

			if( obj is Component )
				obj = ( (Component) obj ).gameObject;

			selection = pingTarget = obj;

			if( obj.IsAsset() )
			{
				if( obj is GameObject )
				{
					// Pinging a prefab only works if the pinged object is the root of the prefab or a direct child of it. Pinging any grandchildren
					// of the prefab doesn't work; in which case, traverse the parent hierarchy until a pingable parent is reached
#if UNITY_2018_3_OR_NEWER
					Transform objTR = ( (GameObject) obj ).transform.root;

					PrefabAssetType prefabAssetType = PrefabUtility.GetPrefabAssetType( objTR.gameObject );
					if( prefabAssetType == PrefabAssetType.Regular || prefabAssetType == PrefabAssetType.Variant )
					{
						string assetPath = AssetDatabase.GetAssetPath( objTR.gameObject );
						PrefabStage openPrefabStage = PrefabStageUtility.GetCurrentPrefabStage();
#if UNITY_2020_1_OR_NEWER
						if( openPrefabStage != null && openPrefabStage.stageHandle.IsValid() && assetPath == openPrefabStage.assetPath )
#else
						if( openPrefabStage != null && openPrefabStage.stageHandle.IsValid() && assetPath == openPrefabStage.prefabAssetPath )
#endif
						{
							GameObject prefabStageGO = FollowSymmetricHierarchy( (GameObject) obj, ( (GameObject) obj ).transform.root.gameObject, openPrefabStage.prefabContentsRoot );
							if( prefabStageGO != null )
							{
								objTR = prefabStageGO.transform;
								selection = objTR.gameObject;
							}
						}
#if UNITY_2019_1_OR_NEWER
						else if( obj != objTR.gameObject )
							selection = objTR.gameObject;
#endif
					}
					else if( prefabAssetType == PrefabAssetType.Model )
					{
						objTR = ( (GameObject) obj ).transform;
						while( objTR.parent != null && objTR.parent.parent != null )
							objTR = objTR.parent;
					}
#else
					Transform objTR = ( (GameObject) obj ).transform;
					while( objTR.parent != null && objTR.parent.parent != null )
						objTR = objTR.parent;
#endif

					pingTarget = objTR.gameObject;
				}
				else if( ( obj.hideFlags & ( HideFlags.HideInInspector | HideFlags.HideInHierarchy ) ) != HideFlags.None )
				{
					// Can't ping assets that are hidden from Project window (e.g. animator states of AnimatorController), ping the main asset at that path instead
					pingTarget = AssetDatabase.LoadMainAssetAtPath( AssetDatabase.GetAssetPath( obj ) );
				}
				else if( !AssetDatabase.IsMainAsset( obj ) && Array.IndexOf( AssetDatabase.LoadAllAssetRepresentationsAtPath( AssetDatabase.GetAssetPath( obj ) ), obj ) < 0 )
				{
					// VFX Graph assets' nodes are serialized as part of the graph but they are invisible in the Project window even though their hideFlags is None (I don't know how)
					pingTarget = AssetDatabase.LoadMainAssetAtPath( AssetDatabase.GetAssetPath( obj ) );
				}
			}
		}

		// We are passing "go"s root Transform to thisRoot parameter. If we use go.transform.root instead, when we are in prefab mode on
		// newer Unity versions, it points to the preview scene at the root of the prefab stage instead of pointing to the actual root of "go"
		public static GameObject FollowSymmetricHierarchy( this GameObject go, GameObject thisRoot, GameObject symmetricRoot )
		{
			Transform target = go.transform;
			Transform root1 = thisRoot.transform;
			Transform root2 = symmetricRoot.transform;
			while( root1 != target )
			{
				Transform temp = target;
				while( temp.parent != root1 )
					temp = temp.parent;

				Transform newRoot2;
				int siblingIndex = temp.GetSiblingIndex();
				if( siblingIndex < root2.childCount )
				{
					newRoot2 = root2.GetChild( siblingIndex );
					if( newRoot2.name != temp.name )
						newRoot2 = root2.Find( temp.name );
				}
				else
					newRoot2 = root2.Find( temp.name );

				if( newRoot2 == null )
					return null;

				root2 = newRoot2;
				root1 = temp;
			}

			return root2.gameObject;
		}

		// Returns -1 if t1 is above t2 in Hierarchy, 1 if t1 is below t2 in Hierarchy and 0 if they are the same object
		public static int CompareHierarchySiblingIndices( Transform t1, Transform t2 )
		{
			Transform parent1 = t1.parent;
			Transform parent2 = t2.parent;

			if( parent1 == parent2 )
				return t1.GetSiblingIndex() - t2.GetSiblingIndex();

			int deltaHierarchyDepth = 0;
			for( ; parent1; parent1 = parent1.parent )
				deltaHierarchyDepth++;
			for( ; parent2; parent2 = parent2.parent )
				deltaHierarchyDepth--;

			for( ; deltaHierarchyDepth > 0; deltaHierarchyDepth-- )
			{
				t1 = t1.parent;
				if( t1 == t2 )
					return 1;
			}
			for( ; deltaHierarchyDepth < 0; deltaHierarchyDepth++ )
			{
				t2 = t2.parent;
				if( t1 == t2 )
					return -1;
			}

			while( t1.parent != t2.parent )
			{
				t1 = t1.parent;
				t2 = t2.parent;
			}

			return t1.GetSiblingIndex() - t2.GetSiblingIndex();
		}

		// Check if the field is serializable
		public static bool IsSerializable( this FieldInfo fieldInfo )
		{
			// See Serialization Rules: https://docs.unity3d.com/Manual/script-Serialization.html
			if( fieldInfo.IsInitOnly )
				return false;

#if UNITY_2019_3_OR_NEWER
			// SerializeReference makes even System.Object fields serializable
			if( Attribute.IsDefined( fieldInfo, typeof( SerializeReference ) ) )
				return true;
#endif

			if( ( !fieldInfo.IsPublic || fieldInfo.IsNotSerialized ) && !Attribute.IsDefined( fieldInfo, typeof( SerializeField ) ) )
				return false;

			return IsTypeSerializable( fieldInfo.FieldType );
		}

		// Check if the property is serializable
		public static bool IsSerializable( this PropertyInfo propertyInfo )
		{
			return IsTypeSerializable( propertyInfo.PropertyType );
		}

		// Check if type is serializable
		private static bool IsTypeSerializable( Type type )
		{
			// see Serialization Rules: https://docs.unity3d.com/Manual/script-Serialization.html
			if( typeof( Object ).IsAssignableFrom( type ) )
				return true;

			if( type.IsArray )
			{
				if( type.GetArrayRank() != 1 )
					return false;

				type = type.GetElementType();

				if( typeof( Object ).IsAssignableFrom( type ) )
					return true;
			}
			else if( type.IsGenericType )
			{
				// Generic types are allowed on 2020.1 and later
#if UNITY_2020_1_OR_NEWER
				if( type.GetGenericTypeDefinition() == typeof( List<> ) )
				{
					type = type.GetGenericArguments()[0];

					if( typeof( Object ).IsAssignableFrom( type ) )
						return true;
				}
#else
				if( type.GetGenericTypeDefinition() != typeof( List<> ) )
					return false;

				type = type.GetGenericArguments()[0];

				if( typeof( Object ).IsAssignableFrom( type ) )
					return true;
#endif
			}

#if !UNITY_2020_1_OR_NEWER
			if( type.IsGenericType )
				return false;
#endif

			return Attribute.IsDefined( type, typeof( SerializableAttribute ), false );
		}

		// Check if instances of this type should be searched for references
		public static bool IsIgnoredUnityType( this Type type )
		{
			if( type.IsPrimitive || primitiveUnityTypes.Contains( type ) || type.IsEnum )
				return true;

#if UNITY_2018_1_OR_NEWER
			// Searching NativeArrays for reference can throw InvalidOperationException if the collection is disposed
			if( type.Namespace == nativeCollectionsNamespace )
				return true;
#endif

			// Searching assembly variables for reference throws InvalidCastException on .NET 4.0 runtime
			if( typeof( Type ).IsAssignableFrom( type ) || type.Namespace == reflectionNamespace )
				return true;

			// Searching pointers or ref variables for reference throws ArgumentException
			if( type.IsPointer || type.IsByRef )
				return true;

			return false;
		}

		// Get <get> function for a field
		public static VariableGetVal CreateGetter( this FieldInfo fieldInfo, Type type )
		{
			// Commented the IL generator code below because it might actually be slower than simply using reflection
			// Credit: https://www.codeproject.com/Articles/14560/Fast-Dynamic-Property-Field-Accessors
			//DynamicMethod dm = new DynamicMethod( "Get" + fieldInfo.Name, fieldInfo.FieldType, new Type[] { typeof( object ) }, type );
			//ILGenerator il = dm.GetILGenerator();
			//// Load the instance of the object (argument 0) onto the stack
			//il.Emit( OpCodes.Ldarg_0 );
			//// Load the value of the object's field (fi) onto the stack
			//il.Emit( OpCodes.Ldfld, fieldInfo );
			//// return the value on the top of the stack
			//il.Emit( OpCodes.Ret );

			//return (VariableGetVal) dm.CreateDelegate( typeof( VariableGetVal ) );

			return fieldInfo.GetValue;
		}

		// Get <get> function for a property
		public static VariableGetVal CreateGetter( this PropertyInfo propertyInfo )
		{
			// Can't use PropertyWrapper (which uses CreateDelegate) for property getters of structs
			if( propertyInfo.DeclaringType.IsValueType )
			{
				return !propertyInfo.CanRead ? (VariableGetVal) null : ( obj ) =>
				{
					try
					{
						return propertyInfo.GetValue( obj, null );
					}
					catch
					{
						// Property getters may return various kinds of exceptions if their backing fields are not initialized (yet)
						return null;
					}
				};
			}

			Type GenType = typeof( PropertyWrapper<,> ).MakeGenericType( propertyInfo.DeclaringType, propertyInfo.PropertyType );
			return ( (IPropertyAccessor) Activator.CreateInstance( GenType, propertyInfo.GetGetMethod( true ) ) ).GetValue;
		}

		// Check if all open scenes are saved (not dirty)
		public static bool AreScenesSaved()
		{
			for( int i = 0; i < SceneManager.sceneCount; i++ )
			{
				Scene scene = SceneManager.GetSceneAt( i );
				if( scene.isDirty || string.IsNullOrEmpty( scene.path ) )
					return false;
			}

			return true;
		}

		// Returns file extension in lowercase (period not included)
		public static string GetFileExtension( string path )
		{
			int extensionIndex = path.LastIndexOf( '.' );
			if( extensionIndex < 0 || extensionIndex >= path.Length - 1 )
				return "";

			stringBuilder.Length = 0;
			for( extensionIndex++; extensionIndex < path.Length; extensionIndex++ )
			{
				char ch = path[extensionIndex];
				if( ch >= 65 && ch <= 90 ) // A-Z
					ch += (char) 32; // Converted to a-z

				stringBuilder.Append( ch );
			}

			return stringBuilder.ToString();
		}

		// Draw horizontal line inside OnGUI
		public static void DrawSeparatorLine()
		{
			GUILayout.Space( 4f );
			GUILayout.Box( "", GL_HEIGHT_2, GL_EXPAND_WIDTH );
			GUILayout.Space( 4f );
		}

		// Restricts the given Rect within the screen's bounds
		public static Rect GetScreenFittedRect( Rect originalRect )
		{
			if( screenFittedRectGetter == null )
				screenFittedRectGetter = typeof( EditorWindow ).Assembly.GetType( "UnityEditor.ContainerWindow" ).GetMethod( "FitRectToScreen", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static );

			return (Rect) screenFittedRectGetter.Invoke( null, new object[3] { originalRect, true, true } );
		}

		// Check if all the objects inside the list are null
		public static bool IsEmpty( this List<ObjectToSearch> objectsToSearch )
		{
			if( objectsToSearch == null )
				return true;

			for( int i = 0; i < objectsToSearch.Count; i++ )
			{
				if( objectsToSearch[i].obj != null && !objectsToSearch[i].obj.Equals( null ) )
					return false;
			}

			return true;
		}

		// Check if all the objects inside the list are null
		public static bool IsEmpty( this List<Object> objects )
		{
			if( objects == null )
				return true;

			for( int i = 0; i < objects.Count; i++ )
			{
				if( objects[i] != null && !objects[i].Equals( null ) )
					return false;
			}

			return true;
		}

		// Check if all the objects that are enumerated are null
		public static bool IsEmpty( this IEnumerable<Object> objects )
		{
			if( objects == null )
				return true;

			using( IEnumerator<Object> enumerator = objects.GetEnumerator() )
			{
				while( enumerator.MoveNext() )
				{
					if( enumerator.Current != null && !enumerator.Current.Equals( null ) )
						return false;
				}
			}

			return true;
		}

		// Returns true is str starts with prefix
		public static bool StartsWithFast( this string str, string prefix )
		{
			int aLen = str.Length;
			int bLen = prefix.Length;
			int ap = 0; int bp = 0;
			while( ap < aLen && bp < bLen && str[ap] == prefix[bp] )
			{
				ap++;
				bp++;
			}

			return bp == bLen;
		}

		// Returns true is str ends with postfix
		public static bool EndsWithFast( this string str, string postfix )
		{
			int ap = str.Length - 1;
			int bp = postfix.Length - 1;
			while( ap >= 0 && bp >= 0 && str[ap] == postfix[bp] )
			{
				ap--;
				bp--;
			}

			return bp < 0;
		}

		public static bool ContainsFast<T>( this List<T> list, T element )
		{
			if( !( element is ValueType ) )
			{
				for( int i = list.Count - 1; i >= 0; i-- )
				{
					if( ReferenceEquals( list[i], element ) )
						return true;
				}
			}
			else
			{
				for( int i = list.Count - 1; i >= 0; i-- )
				{
					if( element.Equals( list[i] ) )
						return true;
				}
			}

			return false;
		}
	}
}