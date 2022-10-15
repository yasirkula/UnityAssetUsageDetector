= Asset Usage Detector (v2.3.0) =

Online documentation available at: https://github.com/yasirkula/UnityAssetUsageDetector
E-mail: yasirkula@gmail.com


### ABOUT
This tool helps you find usages of the selected asset(s) and/or scene object(s), i.e. lists the objects that refer to them.


### ADDRESSABLES SUPPORT
This plugin has experimental support for Addressables package. However, the search may take significantly longer to finish when Addressables are searched. Some manual modifications are needed to enable Addressables support:

- The plugin mustn't be installed as a package, i.e. it must reside inside the Assets folder and not the Packages folder (it can reside inside a subfolder of Assets like Assets/Plugins)
- Add ASSET_USAGE_ADDRESSABLES compiler directive to "Player Settings/Scripting Define Symbols" (these symbols are platform specific, so if you change the active platform later, you'll have to add the compiler directive again)
- Add "Unity.Addressables" assembly to "AssetUsageDetector.Editor" Assembly Definition File's "Assembly Definition References" list
- Enable the "Addressables support" option in Asset Usage Detector window


### HOW TO
- Open "Window-Asset Usage Detector" window, configure the settings and hit "GO!". You can also right click an object and select "Search For References"

- Asset Usage Detector window can be locked via the "Lock" button in its context menu (or via the lock icon at the top-right corner). If all the
  open windows are locked, "Search For References" opens a new window

- Seach results are split into different groups (scenes, assets, Project Settings) and each group displays its search results in its own tree view.
  Tree views support multi-selection and keyboard navigation. It's possible to hide groups or rows via their context menu (right click)

- Each root row in a group's tree view represents a different searched object and is drawn with an outline. Usage(s) of a searched object are listed under it as
  child rows (with indentation). If a child row's label contains bold text inside square brackets (e.g. "[Variable: m_Shader]"), then that text describes how
  its parent row is connected to that row (i.e. the value assigned to the m_Shader variable of that row is its parent row). Child rows can have their own children
  as some references involve multiple steps (e.g. when a Texture reference is found in a material which is used in a prefab which is referenced by a scene object)

- Rows with green tint to their left are the main references. For example, main references in a scene are the GameObjects that belong to that scene

- Rows with yellow tint are the parent rows of the selected row(s). To see a row's connection to the searched object, the yellow tinted rows can be traversed
  from bottom to top (or, you can hover the cursor over a row and see its connection to the searched object in a tooltip)

- Rows with gradient blue tint are the other occurrences of the selected row(s) (a row can appear multiple times in the tree view, right clicking the row and
  selecting "Expand All Occurrences" will reveal them all)

- When "Hide duplicate rows" option is enabled and a row's label starts with "[D]", then it means that the row has appeared in the tree view before and its children
  were omitted to simplify the search results (because otherwise the same child rows would be displayed for each occurrence of that row). Only the first occurrence
  of a row will display its children when "Hide duplicate rows" is enabled. To find a row's first occurrence, you can right click it and select "Select First Occurrence"

- Inside "Unused Objects" group, if a row's label starts with "[!]", then it means that some of its children are used in the project and it isn't safe to delete
  that object. Right clicking the row and selecting "Show Used Children" will reveal the used children

- When a prefab's row is double clicked, that prefab is automatically opened in Prefab Mode

- Selections in tree views are completely independent from each other, it isn't possible to multi-select objects in different tree views. Likewise, selected rows in
  a tree view won't be highlighted in other tree views. "Hide duplicate rows" also works independently for each tree view

- You can tweak most settings/colors via "Project Settings/yasirkula/Asset Usage Detector" page (on older versions, this menu is located at "Preferences" window)