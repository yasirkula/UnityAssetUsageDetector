using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AssetUsageDetectorNamespace
{
	public partial class AssetUsageDetector
	{
		#region Helper Classes
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

				int length = dependencies.Length;
				for( int i = 0; i < length; i++ )
				{
					if( !string.IsNullOrEmpty( dependencies[i] ) )
					{
						FileInfo assetFile = new FileInfo( dependencies[i] );
						fileSizes[i] = assetFile.Exists ? assetFile.Length : 0L;
					}
					else
					{
						// This dependency is empty which causes issues when passed to FileInfo constructor
						// Find a non-empty dependency and move it to this index
						for( int j = length - 1; j > i; j--, length-- )
						{
							if( !string.IsNullOrEmpty( dependencies[j] ) )
							{
								dependencies[i--] = dependencies[j];
								break;
							}
						}

						length--;
					}
				}

				if( length != fileSizes.Length )
				{
					Array.Resize( ref dependencies, length );
					Array.Resize( ref fileSizes, length );
				}
			}
		}
		#endregion

		// An optimization to fetch the dependencies of an asset only once (key is the path of the asset)
		private Dictionary<string, CacheEntry> assetDependencyCache;
		private CacheEntry lastRefreshedCacheEntry;

		private string CachePath { get { return Application.dataPath + "/../Library/AssetUsageDetector.cache"; } } // Path of the cache file

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

							assetDependencyCache[allAssets[i]] = new CacheEntry( allAssets[i] );
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