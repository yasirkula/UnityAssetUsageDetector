= Asset Usage Detector =

Online documentation available at: https://github.com/yasirkula/UnityAssetUsageDetector
E-mail: yasirkula@gmail.com

1. ABOUT
This tool helps you find usages of the selected asset(s) and/or Object(s) in your Unity project, i.e. lists the objects that refer to them.

2. HOW TO
Open "Window-Asset Usage Detector" window, configure the settings and hit GO! You can also right click an object and select "Search For References".

In the search results page, each row represents a reference to the searched Object(s). Rows can be traversed from left to right to see how the
references are formed. If a node's label contains text inside square brackets (e.g. [Child object]), then that text describes how that node is
connected to the node to its left.

3. KNOWN LIMITATIONS
- static variables are not searched
- GUIText materials are not searched
- Textures in Lens Flare's can not be searched