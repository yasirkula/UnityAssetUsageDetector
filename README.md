# Asset Usage Detector for Unity 3D

![screenshot1](screenshots/img1.png)

**Available on Asset Store:** https://www.assetstore.unity3d.com/en/#!/content/112837

**Forum Thread:** https://forum.unity.com/threads/asset-usage-detector-find-references-to-an-asset-object-open-source.408134/

### A. ABOUT

This editor extension helps you figure out at which places an asset or GameObject is used, i.e. lists the objects that refer to the asset. It is possible to search for references in the Assets folder (Project view) and/or in the scene(s) of your project. You can also search for references while in Play mode!

### B. HOW TO USE

- Import **AssetUsageDetector.unitypackage** to your project
- Open **Window - Asset Usage Detector** window, configure the settings and hit **GO!**

**NOTE:** If your project uses an older version of AssetUsageDetector, delete the older version before updating the plugin.

### C. FEATURES

- You can search for references of any object that extends *UnityEngine.Object*
- Seaches every corner of your project with its *reflection* based search algorithm (even non-Unity objects, structs and data types like dictionaries are searched)
- Can search in multiple scenes at once
- Can show complete paths to the references or only the most relevant parts of the paths (see the demonstration below)

![screenshot2](screenshots/img2.gif)

### D. KNOWN LIMITATIONS

- *static* variables are not searched
- GUIText materials are not searched
- Textures in Lens Flare's can not be searched
