# Asset Usage Detector for Unity 3D

![Before search](https://yasirkula.files.wordpress.com/2016/05/assetdetectorbefore.png) 
![After search](https://yasirkula.files.wordpress.com/2016/05/assetdetectorafter.png)

### A. ABOUT

This editor extension helps you figure out at which places an asset or GameObject is used, i.e. lists the objects that refer to the asset. It is possible to search for references in Project view and/or Hierarchy view (searching in multiple scenes is also possible).

### B. FEATURES

- Pretty much anything that extend *UnityEngine.Object* can be searched
- The fields and properties of components are also searched using *reflection*
- Supports 1-dimensional arrays, ArrayList's and generic List's
- Searching in multiple scenes is possible thanks to multi-scene editing feature of Unity 5

### C. SEARCH ALGORITHM

#### C.1. Project view (Assets folder):
- each GameObject asset in the project is searched in detail recursively (see **C.3**)
- if asset is a shader or a texture, each Material asset in the project is searched
- each AnimationClip and AnimatorController assets in the project are searched (including keyframes of the animation clips)

#### C.2. Scenes:
- each GameObject at the root of the scene is searched in detail recursively (see **C.3**)

#### C.3. GameObjects:
- check if the asset is prefab of this GameObject (for scene objects only)
- *(for each component)* if asset is a script (MonoScript), check if this component is an instance of it
- *(for each Renderer)* if asset is a Shader, Texture or Material, search through all the materials attached to this Renderer
- *(for each Animation and Animator)* search through all the animation clips attached to the component (including keyframes of the animation clips)
- *(for each component)* search through filtered fields and properties of this component using *reflection* (see **C.4**)
- search through the children of this game object recursively

#### C.4. Fields and Properties:
- public, protected and private non-static fields are fetched from the component
- public non-static properties are fetched from the component
- fields and properties are filtered (by their types) such that only the variables that the asset can be stored in are kept (arrays, ArrayList's and generic List's are also supported)
- *(for each field and property)* check if value is equal to the asset
- *(for each field and property of type Component)* check if asset is the gameObject of the component stored in the variable
- *(for each field and property that implement IEnumerable)* iterate through the IEnumerator of the variable

### D. LIMITATIONS
- GUIText materials can not be searched (could not find a property that does not leak material)
- Textures in Flare's can not be searched (no property to access the texture?)
- *static* variables are not searched
- also *transform*, *rectTransform*, *gameObject* and *attachedRigidbody* properties in components are not searched (to yield more relevant results)

### E. HOW TO USE
- Simply put the **AssetUsageDetector.cs** script into the *Editor* folder of your project (if "Editor" folder does not exist, create it manually).
- Now open **Util - Asset Usage Detector** window and you are good to go!
