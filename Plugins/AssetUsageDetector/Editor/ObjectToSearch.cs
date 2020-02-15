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

		private static MonoScript[] monoScriptsInProject;
		private static HashSet<Object> currentSubAssets;

		public ObjectToSearch( Object obj )
		{
			this.obj = obj;
			RefreshSubAssets();
		}

		public void RefreshSubAssets()
		{
			if( subAssets == null )
				subAssets = new List<SubAsset>();
			else
				subAssets.Clear();

			if( currentSubAssets == null )
				currentSubAssets = new HashSet<Object>();
			else
				currentSubAssets.Clear();

			AddSubAssets( obj, false );
			currentSubAssets.Clear();
		}

		private void AddSubAssets( Object target, bool includeTarget )
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
				Transform[] children = go.GetComponentsInChildren<Transform>();
				for( int i = 0; i < children.Length; i++ )
				{
					if( ReferenceEquals( children[i], goTransform ) )
						continue;

					subAssets.Add( new SubAsset( children[i].gameObject, false ) );
				}
			}
			else
			{
				if( !AssetDatabase.IsMainAsset( target ) || target is SceneAsset )
					return;

				if( includeTarget )
				{
					if( !currentSubAssets.Contains( target ) )
					{
						subAssets.Add( new SubAsset( target, true ) );
						currentSubAssets.Add( target );
					}
				}
				else
				{
					// If asset is a directory, add all of its contents as sub-assets recursively
					if( target.IsFolder() )
					{
						foreach( string filePath in Utilities.EnumerateFolderContents( target ) )
							AddSubAssets( AssetDatabase.LoadAssetAtPath<Object>( filePath ), true );

						return;
					}
				}

				// Find sub-asset(s) of the asset (if any)
				Object[] assets = AssetDatabase.LoadAllAssetsAtPath( AssetDatabase.GetAssetPath( target ) );
				for( int i = 0; i < assets.Length; i++ )
				{
					Object asset = assets[i];
					if( asset == null || asset.Equals( null ) || asset is Component )
						continue;

					if( currentSubAssets.Contains( asset ) )
						continue;

					if( asset != target )
					{
						subAssets.Add( new SubAsset( asset, true ) );
						currentSubAssets.Add( asset );
					}

					// MonoScripts are a special case such that other MonoScript objects
					// that extend this MonoScript are also considered a sub-asset
					if( asset is MonoScript )
					{
						Type monoScriptType = ( (MonoScript) asset ).GetClass();
						if( monoScriptType == null || ( !monoScriptType.IsInterface && !typeof( Component ).IsAssignableFrom( monoScriptType ) ) )
							continue;

						// Find all MonoScript objects in the project
						if( monoScriptsInProject == null )
						{
							string[] monoScriptGuids = AssetDatabase.FindAssets( "t:MonoScript" );
							monoScriptsInProject = new MonoScript[monoScriptGuids.Length];
							for( int k = 0; k < monoScriptGuids.Length; k++ )
								monoScriptsInProject[k] = AssetDatabase.LoadAssetAtPath<MonoScript>( AssetDatabase.GUIDToAssetPath( monoScriptGuids[k] ) );
						}

						// Add any MonoScript objects that extend this MonoScript as a sub-asset
						for( int j = 0; j < monoScriptsInProject.Length; j++ )
						{
							Type otherMonoScriptType = monoScriptsInProject[j].GetClass();
							if( otherMonoScriptType == null || monoScriptType == otherMonoScriptType || !monoScriptType.IsAssignableFrom( otherMonoScriptType ) )
								continue;

							if( !currentSubAssets.Contains( monoScriptsInProject[j] ) )
							{
								subAssets.Add( new SubAsset( monoScriptsInProject[j], true ) );
								currentSubAssets.Add( monoScriptsInProject[j] );
							}
						}
					}
				}
			}
		}
	}
}