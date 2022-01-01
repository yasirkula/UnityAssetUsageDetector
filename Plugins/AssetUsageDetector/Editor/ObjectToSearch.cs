using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AssetUsageDetectorNamespace
{
	[Serializable]
	public class ObjectToSearch
	{
		[Serializable]
		public class SubAsset
		{
			public Object subAsset;
			public bool shouldSearch;

			public SubAsset( Object subAsset, bool shouldSearch )
			{
				this.subAsset = subAsset;
				this.shouldSearch = shouldSearch;
			}
		}

		public Object obj;
		public List<SubAsset> subAssets;
		public bool showSubAssetsFoldout;

		private static HashSet<Object> currentSubAssets;

		public ObjectToSearch( Object obj, bool? shouldSearchChildren = null )
		{
			this.obj = obj;
			RefreshSubAssets( shouldSearchChildren );
		}

		public void RefreshSubAssets( bool? shouldSearchChildren = null )
		{
			if( subAssets == null )
				subAssets = new List<SubAsset>();
			else
				subAssets.Clear();

			if( currentSubAssets == null )
				currentSubAssets = new HashSet<Object>();
			else
				currentSubAssets.Clear();

			AddSubAssets( obj, false, shouldSearchChildren );
			currentSubAssets.Clear();
		}

		private void AddSubAssets( Object target, bool includeTarget, bool? shouldSearchChildren )
		{
			if( target == null || target.Equals( null ) )
				return;

			if( !target.IsAsset() )
			{
				GameObject go = target as GameObject;
				if( !go || !go.scene.IsValid() )
					return;

				// If this is a scene object, add its child objects to the sub-assets list
				// but don't include them in the search by default
				Transform goTransform = go.transform;
				Transform[] children = go.GetComponentsInChildren<Transform>( true );
				for( int i = 0; i < children.Length; i++ )
				{
					if( ReferenceEquals( children[i], goTransform ) )
						continue;

					subAssets.Add( new SubAsset( children[i].gameObject, shouldSearchChildren ?? false ) );
				}
			}
			else
			{
				if( !AssetDatabase.IsMainAsset( target ) || target is SceneAsset )
					return;

				if( includeTarget )
				{
					if( currentSubAssets.Add( target ) )
						subAssets.Add( new SubAsset( target, shouldSearchChildren ?? true ) );
				}
				else
				{
					// If asset is a directory, add all of its contents as sub-assets recursively
					if( target.IsFolder() )
					{
						foreach( string filePath in Utilities.EnumerateFolderContents( target ) )
							AddSubAssets( AssetDatabase.LoadAssetAtPath<Object>( filePath ), true, shouldSearchChildren );

						return;
					}
				}

				// Find sub-asset(s) of the asset (if any)
				Object[] assets = AssetDatabase.LoadAllAssetsAtPath( AssetDatabase.GetAssetPath( target ) );
				for( int i = 0; i < assets.Length; i++ )
				{
					Object asset = assets[i];
					if( asset == null || asset.Equals( null ) || asset is Component || asset == target )
						continue;

#if UNITY_2018_3_OR_NEWER
					// Nested prefabs in prefab assets add an additional native object of type 'UnityEngine.PrefabInstance' to the prefab. Managed type of that native type
					// is UnityEngine.Object (i.e. GetType() returns UnityEngine.Object, not UnityEngine.PrefabInstance). There are no possible references to these native
					// objects so skip them (we're checking for UnityEngine.Prefab because it includes other native types like UnityEngine.PrefabCreation, as well)
					if( target is GameObject && asset.GetType() == typeof( Object ) && asset.ToString().Contains( "(UnityEngine.Prefab" ) )
						continue;
#endif

					if( currentSubAssets.Add( asset ) )
						subAssets.Add( new SubAsset( asset, shouldSearchChildren ?? true ) );
				}
			}
		}
	}
}