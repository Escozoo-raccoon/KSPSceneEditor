using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using KSP.UI.Screens;

namespace KSPSceneEditor
{
    [KSPAddon(KSPAddon.Startup.MainMenu, false)]
    public sealed class SceneEditorRuntime : MonoBehaviour
    {
        internal sealed class SpawnedCraft { internal string FileName; internal bool ForceStowed; internal GameObject Root; internal List<CraftVisualLoader.CraftControl> Controls=new List<CraftVisualLoader.CraftControl>(); }
        internal sealed class SpawnedLight { internal GameObject Root; }
        internal sealed class SpawnedPlanet { internal string BodyName; internal GameObject Root; internal List<Texture2D> TextureCandidates=new List<Texture2D>(); internal int TextureIndex=0; internal bool UsesLiveScaledMaterials=false; }
        internal sealed class SpawnedText { internal string Text; internal GameObject Root; }
        internal sealed class SpawnedImage { internal string FileName; internal GameObject Root; }
        internal sealed class SceneEntry
        {
            internal Transform Transform;
            internal string Name;
            internal string Path;
            internal string Category;
            internal string FriendlyName;
            internal string Role;
            internal string Components;
            internal bool Essential;
            internal string Kind;
            internal string ParentName;
            internal string Display;
        }

        internal static SceneEditorRuntime Instance { get; private set; }
        private static bool sessionWorkspaceInitialized=false;
        private static string sessionWorkspaceToken=null;
        private ApplicationLauncherButton toolbarButton;
        private Texture2D toolbarIcon;
        internal Transform Selected { get; private set; }
        internal bool BaselineReady { get; private set; }
        internal string Status { get; private set; }
        internal GameObject EditorRoot { get; private set; }
        internal IReadOnlyList<SceneEntry> SceneEntries { get { return sceneEntries; } }
        internal IEnumerable<Transform> EditedObjects { get { return edited; } }
        internal IEnumerable<SpawnedCraft> SpawnedCrafts { get { return spawnedCrafts; } }
        internal IEnumerable<SpawnedLight> SpawnedLights { get { return spawnedLights; } }
        internal int UndoCount { get { return history.UndoCount; } }
        internal int RedoCount { get { return history.RedoCount; } }

        private readonly List<SceneEntry> sceneEntries = new List<SceneEntry>();
        private readonly HashSet<Transform> edited = new HashSet<Transform>();
        private readonly Dictionary<Transform,string> editedContextOwners=new Dictionary<Transform,string>();
        private readonly List<GameObject> created = new List<GameObject>();
        private readonly Dictionary<GameObject,string> createdContextOwners=new Dictionary<GameObject,string>();
        private readonly Dictionary<Transform,string> visualImageOverrides=new Dictionary<Transform,string>();
        private readonly List<SpawnedCraft> spawnedCrafts = new List<SpawnedCraft>();
        private readonly List<SpawnedLight> spawnedLights = new List<SpawnedLight>();
        private readonly List<SpawnedPlanet> spawnedPlanets = new List<SpawnedPlanet>();
        private readonly List<SpawnedText> spawnedTexts = new List<SpawnedText>();
        private readonly List<SpawnedImage> spawnedImages = new List<SpawnedImage>();
        private SceneSnapshot baseline = new SceneSnapshot();
        private SceneEditorWindow window;
        private SceneEditorSettings settings;
        private readonly EditorHistory history = new EditorHistory();
        private readonly HashSet<string> favourites = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> locked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private bool pickMode;
        private readonly Dictionary<Transform, Transform> kerbalActorToPivot = new Dictionary<Transform, Transform>();
        private readonly Dictionary<Transform, Transform> kerbalPivotToActor = new Dictionary<Transform, Transform>();
        private readonly HashSet<Transform> activeKerbalProxies = new HashSet<Transform>();
        private sealed class KerbalOriginalState
        {
            internal Transform Parent;
            internal int SiblingIndex;
            internal Vector3 LocalPosition;
            internal Quaternion LocalRotation;
            internal Vector3 LocalScale;
        }
        private readonly Dictionary<Transform,KerbalOriginalState> kerbalOriginalStates = new Dictionary<Transform,KerbalOriginalState>();
        private sealed class KerbalLiveOffsetState
        {
            internal Vector3 OriginalLocalPosition;
            internal Quaternion OriginalLocalRotation;
            internal Vector3 OriginalLocalScale;

            internal Vector3 PositionOffsetWorld=Vector3.zero;
            internal Quaternion RotationOffset=Quaternion.identity;
            internal Vector3 ScaleMultiplier=Vector3.one;

            internal Vector3 LastAppliedPositionOffset=Vector3.zero;
            internal Quaternion LastAppliedRotationOffset=Quaternion.identity;
            internal Vector3 LastAppliedScaleMultiplier=Vector3.one;
            internal Vector3 LastComposedPosition;
            internal Quaternion LastComposedRotation=Quaternion.identity;
            internal Vector3 LastComposedScale=Vector3.one;
            internal bool HasLastCompose=false;

            internal Vector3 DragStartPositionOffset=Vector3.zero;
            internal Quaternion DragStartRotationOffset=Quaternion.identity;
            internal Vector3 DragStartScaleMultiplier=Vector3.one;

            internal bool OverridePosition,OverrideRotation,OverrideScale;
        }
        private readonly Dictionary<Transform,KerbalLiveOffsetState> kerbalLiveOffsets = new Dictionary<Transform,KerbalLiveOffsetState>();
        // Stable click anchors: computed once from the initial visible Kerbal,
        // then kept in actor-local space. Animated renderer bounds no longer change the target.
        private readonly Dictionary<Transform,Vector3> kerbalPickLocalAnchors = new Dictionary<Transform,Vector3>();
        private readonly List<Transform> kerbalRegistry = new List<Transform>();
        private readonly Dictionary<Transform,int> kerbalRegistryIds = new Dictionary<Transform,int>();
        private Transform guiDragKerbalRuntime;
        private Camera guiDragCameraRuntime;
        private Vector3 guiDragStartWorldRuntime;
        private Vector3 guiDragStartPositionRuntime;
        private Vector3 guiDragStartOffsetRuntime;
        private Quaternion guiDragStartRotationOffsetRuntime=Quaternion.identity;
        private Vector3 guiDragStartScaleMultiplierRuntime=Vector3.one;
        private Vector2 guiDragStartMouseRuntime;
        private int guiDragToolRuntime=0;
        private float guiDragScreenDepthRuntime;
        private bool guiDragActiveRuntime;
        private int nextKerbalRegistryId=1;
        private string kerbalRegistryWorkspace="";
        private int kerbalRegistryFrame=-1;
        private readonly List<Transform> discoveredVisuals = new List<Transform>();
        private string workspaceRootName="OrbitScene";
        private string[] nativeAreaNames=new string[0];
        private int nativeAreaIndex=-1;
        private int nativeStageIndex=-1;
        private string nativeSceneState="Natif KSP non analysé";
        private object nativeEnvInstanceCache=null;
        private Type nativeEnvTypeCache=null;
        private float nativeStateNextRefresh=0f;
        private bool nativeSandcastleActive=false;
        private bool applyingContextProfile=false;
        private Color baselineAmbientLight=Color.white;
        private bool contextTransitionInProgress=false;
        private string persistentObservedContextKey=null;
        private string persistentCandidateContextKey=null;
        private float persistentCandidateSince=0f;
        private float nextSessionDraftAutosave=0f;
        private const float ContextStableDelay=0.45f;
        private const float SessionDraftAutosaveInterval=0.75f;
        private static readonly HashSet<string> sessionWorkspaceKeys=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string,string> contextProfiles=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        private string ContextProfilesPath { get { return Path.Combine(KSPUtil.ApplicationRootPath,"GameData","KSPSceneEditor","PluginData","ContextProfiles.cfg"); } }
        internal string CurrentContextKey { get { ForceRefreshNativeMainMenuState(); return BuildContextKey(nativeAreaIndex,nativeStageIndex,nativeSandcastleActive); } }
        internal string CurrentContextLabel { get { ForceRefreshNativeMainMenuState(); return BuildContextLabel(nativeAreaIndex,nativeStageIndex,nativeSandcastleActive); } }
        private readonly Dictionary<GameObject,bool> scenePreviewOriginalStates=new Dictionary<GameObject,bool>();
        private readonly Dictionary<Transform,VariantPose> scenePreviewOriginalPoses=new Dictionary<Transform,VariantPose>();
        private bool scenePreviewActive=false;
        private string nativeSceneCatalogSummary="Catalogue natif non analysé";
        private int nativeSceneCatalogTypeCount=0;
        private int nativeSceneCatalogInstanceCount=0;
        internal string NativeSceneCatalogSummary { get { return nativeSceneCatalogSummary; } }
        private bool editorShieldActive=false;
        private readonly Dictionary<Collider,bool> shieldColliderStates=new Dictionary<Collider,bool>();
        private readonly Dictionary<CanvasGroup,bool> shieldCanvasStates=new Dictionary<CanvasGroup,bool>();
        private readonly Dictionary<Renderer,Texture[]> originalRendererTextures=new Dictionary<Renderer,Texture[]>();
        private sealed class TextMeshState { internal string Text; internal float CharacterSize,LineSpacing; internal Color Color; internal TextAlignment Alignment; internal TextAnchor Anchor; internal FontStyle FontStyle; internal Font Font; }
        private sealed class UiTextState { internal string Text; internal int FontSize; internal float LineSpacing; internal Color Color; internal TextAnchor Alignment; internal FontStyle FontStyle; internal Font Font; }
        private readonly Dictionary<TextMesh,TextMeshState> originalTextMeshStates=new Dictionary<TextMesh,TextMeshState>();
        private readonly Dictionary<Text,UiTextState> originalUiTextStates=new Dictionary<Text,UiTextState>();
        private Material originalSkyboxMaterial;
        private readonly Dictionary<Material,Texture> originalGalaxyMaterialTextures=new Dictionary<Material,Texture>();
        private readonly List<Texture2D> loadedSkyboxTextures=new List<Texture2D>();
        private string activeSkyboxPack="";
        private string lastImportedImage="";
        internal string LastImportedImage { get { return lastImportedImage; } }
        private Vector2 lastPickGuiPoint=new Vector2(-9999f,-9999f);
        private int overlapPickIndex=0;
        private string[] cachedFonts=new string[0],cachedBodies=new string[0],cachedScenes=new string[0],cachedCrafts=new string[0],cachedLogos=new string[0],cachedSkyboxes=new string[0],cachedCompositions=new string[0];
        private readonly Dictionary<string,Font> cachedFontObjects=new Dictionary<string,Font>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string,CelestialBody> cachedBodyObjects=new Dictionary<string,CelestialBody>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string,string> cachedSceneProfiles=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string,Transform> cachedSceneObjects=new Dictionary<string,Transform>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string,Camera> cachedSceneCameras=new Dictionary<string,Camera>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> capturedSceneKeys=new List<string>();
        private sealed class VariantPose
        {
            internal bool Active;
            internal Vector3 LocalPosition;
            internal Quaternion LocalRotation;
            internal Vector3 LocalScale;
        }

        private sealed class MenuVariant
        {
            internal string Key;
            internal string RootKey;
            internal readonly Dictionary<GameObject,bool> States=new Dictionary<GameObject,bool>();
            internal readonly Dictionary<string,bool> SavedStates=new Dictionary<string,bool>(StringComparer.OrdinalIgnoreCase);
            internal readonly Dictionary<GameObject,VariantPose> Poses=new Dictionary<GameObject,VariantPose>();
            internal readonly Dictionary<string,VariantPose> SavedPoses=new Dictionary<string,VariantPose>(StringComparer.OrdinalIgnoreCase);
        }
        private readonly Dictionary<string,MenuVariant> menuVariants=new Dictionary<string,MenuVariant>(StringComparer.OrdinalIgnoreCase);
        private int variantSequence=1;
        private readonly Dictionary<string,Texture2D> cachedPlanetTextures=new Dictionary<string,Texture2D>(StringComparer.OrdinalIgnoreCase);
        private Texture2D[] cachedAllTextures=new Texture2D[0];

        // V0.2.2 direct manipulation state. The normal workflow is now: click -> drag -> adjust.
        private bool directManipulationEnabled = true;
        private int directTool = 0; // 0 Move, 1 Rotate, 2 Scale
        private bool directDragging;
        private Transform directDragTarget;
        private Vector3 directMouseStart;
        private Vector3 directStartPosition;
        private Quaternion directStartRotation;
        private Vector3 directStartScale;
        private Vector3 directStartAnchor;
        private Vector3 directStartScreen;

        internal bool DirectManipulationEnabled { get { return directManipulationEnabled; } }
        internal bool DirectDragging { get { return directDragging; } }
        internal int DirectTool { get { return directTool; } }
        internal string DirectToolName { get { return directTool == 1 ? "ROTATE" : directTool == 2 ? "SCALE" : directTool == 3 ? "DEPTH" : "MOVE"; } }
        internal float GetSelectedCameraDepth()
        {
            if(Selected==null)return 0f;Camera cam=FindCameraForTarget(Selected);
            if(cam==null)return 0f;return cam.WorldToScreenPoint(GetVisualAnchor(Selected)).z;
        }

        internal float GetSelectedScaleAverage()
        {
            if(Selected==null)return 0f;Vector3 s=Selected.localScale;
            return (Mathf.Abs(s.x)+Mathf.Abs(s.y)+Mathf.Abs(s.z))/3f;
        }

        internal string ToggleShortcutLabel { get { return settings != null ? settings.ShortcutLabel : "Ctrl+Alt+F10"; } }
        internal string WorkspaceRootName { get { return workspaceRootName; } }
        internal string NativeSceneState { get { RefreshNativeMainMenuState(); return nativeSceneState; } }
        internal string[] NativeAreaNames { get { RefreshNativeMainMenuState(); return nativeAreaNames; } }
        internal int NativeAreaIndex { get { RefreshNativeMainMenuState(); return nativeAreaIndex; } }
        internal int NativeStageIndex { get { RefreshNativeMainMenuState(); return nativeStageIndex; } }
        internal bool NativeSandcastleActive { get { return nativeSandcastleActive; } }
        internal string NativeContextShort
        {
            get
            {
                RefreshNativeMainMenuState();
                string scene=nativeAreaIndex==0?"MUN":nativeAreaIndex==1?"ORBIT":"?";
                return scene+" / S"+nativeStageIndex;
            }
        }
        internal bool IsNativeOrbit { get { RefreshNativeMainMenuState(); return nativeAreaIndex==1; } }
        internal bool IsNativeMun { get { RefreshNativeMainMenuState(); return nativeAreaIndex==0; } }
        internal string CurrentActiveProfile
        {
            get
            {
                LoadContextProfiles();
                string n;
                return contextProfiles.TryGetValue(CurrentContextKey,out n)&&!string.IsNullOrEmpty(n)?n:"KSP ORIGINAL";
            }
        }
        internal bool ScenePreviewActive { get { return scenePreviewActive; } }

        private void Awake()
        {
            Instance = this;
            settings = SceneEditorSettings.Load();
            InitializeSessionWorkspaces();
            EditorRoot = new GameObject("KSPSceneEditor_RuntimeObjects");
            DontDestroyOnLoad(EditorRoot);
            window = gameObject.AddComponent<SceneEditorWindow>();
            GameEvents.onGUIApplicationLauncherReady.Add(OnApplicationLauncherReady);
            GameEvents.onGUIApplicationLauncherDestroyed.Add(OnApplicationLauncherDestroyed);
            GameEvents.onGameSceneLoadRequested.Add(OnGameSceneLoadRequested);
            Status = "Initializing...";
            StartCoroutine(CaptureWhenStable());
        }

        private string UserImagesPath { get { return Path.Combine(KSPUtil.ApplicationRootPath,"GameData","KSPSceneEditor","PluginData","Images"); } }
        private string UserCraftsPath { get { return Path.Combine(KSPUtil.ApplicationRootPath,"GameData","KSPSceneEditor","Crafts"); } }
        private string UserScenesPath { get { return Path.Combine(KSPUtil.ApplicationRootPath,"GameData","KSPSceneEditor","Scenes"); } }
        private string SessionWorkspaceRoot { get { return Path.Combine(KSPUtil.ApplicationRootPath,"GameData","KSPSceneEditor","PluginData","SessionWorkspaces"); } }
        private string SessionWorkspacePath(string key)
        {
            string safe=(key??"UNKNOWN").Replace("/","_").Replace("\\","_").Replace(":","_");
            return Path.Combine(SessionWorkspaceRoot,(sessionWorkspaceToken??"session")+"_"+safe+".cfg");
        }

        private void InitializeSessionWorkspaces()
        {
            if(sessionWorkspaceInitialized)return;
            sessionWorkspaceInitialized=true;
            sessionWorkspaceToken=Guid.NewGuid().ToString("N");
            sessionWorkspaceKeys.Clear();
            try
            {
                if(Directory.Exists(SessionWorkspaceRoot))Directory.Delete(SessionWorkspaceRoot,true);
                Directory.CreateDirectory(SessionWorkspaceRoot);
            }
            catch(Exception ex){SceneEditorLog.Warn("Session workspace init: "+ex.Message);}
        }

        private void EnsureUserFolders()
        {
            try
            {
                Directory.CreateDirectory(UserImagesPath);
                Directory.CreateDirectory(UserCraftsPath);
                Directory.CreateDirectory(UserScenesPath);
                Directory.CreateDirectory(SkyboxRootPath);
            }catch{}
        }

        internal void RefreshLibraries()
        {
            EnsureUserFolders();
            BuildRuntimeCaches();
            RefreshUserFileCaches();
            Status="Bibliothèques actualisées";
        }

        private void BuildFontCache()
        {
            cachedFontObjects.Clear();
            try
            {
                Font[] fonts=Resources.FindObjectsOfTypeAll<Font>();
                List<string> names=new List<string>();
                for(int i=0;i<fonts.Length;i++)
                {
                    Font f=fonts[i];if(f==null||string.IsNullOrEmpty(f.name)||cachedFontObjects.ContainsKey(f.name))continue;
                    cachedFontObjects[f.name]=f;names.Add(f.name);
                }
                names.Sort(StringComparer.OrdinalIgnoreCase);cachedFonts=names.ToArray();
            }catch{cachedFonts=new string[0];}
        }

        private void BuildBodyCache()
        {
            cachedBodyObjects.Clear();
            try
            {
                CelestialBody[] bodies=Resources.FindObjectsOfTypeAll<CelestialBody>();
                List<string> names=new List<string>();
                for(int i=0;i<bodies.Length;i++)
                {
                    CelestialBody b=bodies[i];if(b==null)continue;string n=b.bodyName??b.name??string.Empty;
                    if(string.IsNullOrEmpty(n)||cachedBodyObjects.ContainsKey(n))continue;
                    cachedBodyObjects[n]=b;names.Add(n);
                }
                if(names.Count==0)
                {
                    string[] stock={"Kerbin","Mun","Minmus","Moho","Eve","Gilly","Duna","Ike","Dres","Jool","Laythe","Vall","Tylo","Bop","Pol","Eeloo"};
                    names.AddRange(stock);
                }
                names.Sort(StringComparer.OrdinalIgnoreCase);cachedBodies=names.ToArray();
            }catch{cachedBodies=new string[0];}
        }

        private void CollectControllerSceneReferences(List<GameObject> output)
        {
            if(output==null)return;
            try
            {
                GameObject main=GameObject.Find("MainMenu");
                if(main==null)
                {
                    GameObject[] all=Resources.FindObjectsOfTypeAll<GameObject>();
                    for(int i=0;i<all.Length;i++)if(all[i]!=null&&string.Equals(all[i].name,"MainMenu",StringComparison.OrdinalIgnoreCase)&&ScenePath.InLoadedScene(all[i].transform)){main=all[i];break;}
                }
                if(main==null)return;

                MonoBehaviour[] behaviours=main.GetComponentsInChildren<MonoBehaviour>(true);
                BindingFlags flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
                for(int b=0;b<behaviours.Length;b++)
                {
                    MonoBehaviour mb=behaviours[b];if(mb==null)continue;
                    Type type=mb.GetType();string tn=(type.Name??string.Empty).ToLowerInvariant();
                    if(!tn.Contains("menu")&&!tn.Contains("scene")&&!tn.Contains("scenery"))continue;
                    FieldInfo[] fields=type.GetFields(flags);
                    for(int f=0;f<fields.Length;f++)
                    {
                        FieldInfo fi=fields[f];string fn=(fi.Name??string.Empty).ToLowerInvariant();
                        if(!fn.Contains("scene")&&!fn.Contains("scenery")&&!fn.Contains("landscape"))continue;
                        object value=null;try{value=fi.GetValue(mb);}catch{continue;}
                        AddSceneReferenceValue(value,output);
                    }
                }
            }catch(Exception ex){SceneEditorLog.Warn("MainMenu controller scene discovery: "+ex.Message);}
        }

        private void AddSceneReferenceValue(object value,List<GameObject> output)
        {
            AddSceneReferenceValue(value,output,0);
        }

        private void AddSceneReferenceValue(object value,List<GameObject> output,int depth)
        {
            if(value==null||output==null||depth>2)return;
            GameObject go=value as GameObject;
            if(go!=null){if(ScenePath.InLoadedScene(go.transform)&&!output.Contains(go))output.Add(go);return;}
            Transform tr=value as Transform;
            if(tr!=null){if(ScenePath.InLoadedScene(tr)&&!output.Contains(tr.gameObject))output.Add(tr.gameObject);return;}
            Component cp=value as Component;
            if(cp!=null){if(ScenePath.InLoadedScene(cp.transform)&&!output.Contains(cp.gameObject))output.Add(cp.gameObject);return;}
            System.Collections.IEnumerable enumerable=value as System.Collections.IEnumerable;
            if(enumerable!=null&&!(value is string))
            {
                foreach(object item in enumerable)AddSceneReferenceValue(item,output,depth+1);
                return;
            }

            Type t=value.GetType();
            if(t.IsPrimitive||t.IsEnum||t==typeof(string))return;
            string tn=(t.Name??string.Empty).ToLowerInvariant();
            if(!tn.Contains("scene")&&!tn.Contains("scenery")&&!tn.Contains("menu")&&!tn.Contains("landscape"))return;
            try
            {
                FieldInfo[] ff=t.GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
                for(int i=0;i<ff.Length;i++)
                {
                    string fn=(ff[i].Name??string.Empty).ToLowerInvariant();
                    if(!fn.Contains("scene")&&!fn.Contains("root")&&!fn.Contains("object")&&!fn.Contains("scenery")&&!fn.Contains("landscape"))continue;
                    object child=null;try{child=ff[i].GetValue(value);}catch{continue;}
                    AddSceneReferenceValue(child,output,depth+1);
                }
            }catch{}
        }

        private void BuildSceneCache()
        {
            cachedSceneProfiles.Clear();cachedSceneObjects.Clear();cachedSceneCameras.Clear();
            List<string> names=new List<string>();
            try
            {
                GameObject[] all=Resources.FindObjectsOfTypeAll<GameObject>();
                GameObject orbit=null,mun=null;
                for(int i=0;i<all.Length;i++)
                {
                    GameObject g=all[i];if(g==null||!ScenePath.InLoadedScene(g.transform))continue;
                    if(orbit==null&&string.Equals(g.name,"OrbitScene",StringComparison.OrdinalIgnoreCase))orbit=g;
                    if(mun==null&&string.Equals(g.name,"MunScene",StringComparison.OrdinalIgnoreCase))mun=g;
                }

                if(orbit!=null){names.Add("OrbitScene");cachedSceneObjects["OrbitScene"]=orbit.transform;}
                if(mun!=null){names.Add("MunScene");cachedSceneObjects["MunScene"]=mun.transform;}

                LoadMenuVariantsFromDisk();

                // Preserve only variants explicitly captured from a scene KSP actually displayed.
                foreach(KeyValuePair<string,MenuVariant> kv in menuVariants)
                {
                    MenuVariant v=kv.Value;if(v==null)continue;
                    Transform root;
                    if(cachedSceneObjects.TryGetValue(v.RootKey,out root)&&root!=null)
                    {
                        names.Add(v.Key);cachedSceneObjects[v.Key]=root;
                    }
                }

                cachedScenes=names.ToArray();
                for(int i=0;i<cachedScenes.Length;i++)
                {
                    cachedSceneProfiles[cachedScenes[i]]=menuVariants.ContainsKey(cachedScenes[i])
                        ? BuildVariantProfile(menuVariants[cachedScenes[i]])
                        : BuildSceneProfileForTransform(cachedSceneObjects[cachedScenes[i]]);

                    Transform sr=cachedSceneObjects[cachedScenes[i]];Camera chosen=null;
                    Camera[] cc=sr.GetComponentsInChildren<Camera>(true);
                    for(int c=0;c<cc.Length;c++)if(cc[c]!=null&&cc[c].enabled){chosen=cc[c];break;}
                    if(chosen==null&&cc.Length>0)chosen=cc[0];
                    if(chosen!=null)cachedSceneCameras[cachedScenes[i]]=chosen;
                }
            }
            catch(Exception ex){cachedScenes=new string[0];SceneEditorLog.Warn("Canonical scene cache: "+ex.Message);}
        }

        private void BuildPlanetTextureCache()
        {
            try{cachedAllTextures=Resources.FindObjectsOfTypeAll<Texture2D>();}
            catch{cachedAllTextures=new Texture2D[0];}
        }

        private void BuildRuntimeCaches()
        {
            BuildFontCache();BuildBodyCache();BuildPlanetTextureCache();BuildSceneCache();
        }

        private IEnumerator BuildInitialCachesCoroutine()
        {
            EnsureUserFolders();
            Status="Cache polices...";
            BuildFontCache();yield return null;
            Status="Cache astres...";
            BuildBodyCache();yield return null;
            Status="Cache textures planétaires...";
            BuildPlanetTextureCache();yield return null;
            Status="Cache scènes...";
            BuildSceneCache();yield return null;
            RefreshUserFileCaches();
        }

        private void RefreshUserFileCaches()
        {
            try
            {
                string[] f=Directory.Exists(UserCraftsPath)?Directory.GetFiles(UserCraftsPath,"*.craft"):new string[0];
                for(int i=0;i<f.Length;i++)f[i]=Path.GetFileName(f[i]);Array.Sort(f,StringComparer.OrdinalIgnoreCase);cachedCrafts=f;
            }catch{cachedCrafts=new string[0];}
            try
            {
                List<string> files=new List<string>();
                if(Directory.Exists(UserImagesPath))
                {
                    string[] patterns={"*.png","*.jpg","*.jpeg"};
                    for(int p=0;p<patterns.Length;p++)files.AddRange(Directory.GetFiles(UserImagesPath,patterns[p]));
                }
                for(int i=0;i<files.Count;i++)files[i]=Path.GetFileName(files[i]);
                files.Sort(StringComparer.OrdinalIgnoreCase);cachedLogos=files.ToArray();
            }catch{cachedLogos=new string[0];}
            try
            {
                string root=SkyboxRootPath;string[] dirs=Directory.Exists(root)?Directory.GetDirectories(root):new string[0];
                for(int i=0;i<dirs.Length;i++)dirs[i]=Path.GetFileName(dirs[i]);Array.Sort(dirs,StringComparer.OrdinalIgnoreCase);cachedSkyboxes=dirs;
            }catch{cachedSkyboxes=new string[0];}
            try
            {
                string[] f=Directory.Exists(UserScenesPath)?Directory.GetFiles(UserScenesPath,"*.cfg"):new string[0];
                for(int i=0;i<f.Length;i++)f[i]=Path.GetFileNameWithoutExtension(f[i]);Array.Sort(f,StringComparer.OrdinalIgnoreCase);cachedCompositions=f;
            }catch{cachedCompositions=new string[0];}
        }

        internal void RefreshUserContent()
        {
            EnsureUserFolders();RefreshUserFileCaches();
            Status="Contenu utilisateur actualisé : "+cachedCrafts.Length+" craft(s) • "+cachedLogos.Length+" image(s) • "+cachedSkyboxes.Length+" skybox(es)";
        }

        internal bool DeleteUserImage(string imageFile)
        {
            try
            {
                if(string.IsNullOrEmpty(imageFile)){Status="Aucune image sélectionnée";return false;}
                string path=Path.Combine(UserImagesPath,imageFile);
                if(!File.Exists(path)){Status="Image déjà absente";RefreshUserFileCaches();return false;}

                // Remove placed instances using this source so a saved composition cannot
                // silently reference a file that no longer exists.
                for(int i=spawnedImages.Count-1;i>=0;i--)
                {
                    SpawnedImage si=spawnedImages[i];
                    if(si==null||!string.Equals(si.FileName,imageFile,StringComparison.OrdinalIgnoreCase))continue;
                    if(si.Root!=null){created.Remove(si.Root);UnityEngine.Object.Destroy(si.Root);}
                    spawnedImages.RemoveAt(i);
                }

                File.Delete(path);
                if(string.Equals(lastImportedImage,imageFile,StringComparison.OrdinalIgnoreCase))lastImportedImage="";
                RefreshUserFileCaches();
                Status="Image supprimée : "+imageFile;
                return true;
            }
            catch(Exception ex){Status="Suppression image impossible : "+ex.Message;return false;}
        }

        internal bool DeleteUserCraft(string craftFile)
        {
            try
            {
                if(string.IsNullOrEmpty(craftFile)){Status="Aucun craft sélectionné";return false;}
                string path=Path.Combine(UserCraftsPath,craftFile);
                if(!File.Exists(path)){Status="Craft déjà absent";RefreshUserFileCaches();return false;}

                for(int i=spawnedCrafts.Count-1;i>=0;i--)
                {
                    SpawnedCraft sc=spawnedCrafts[i];
                    if(sc==null||!string.Equals(sc.FileName,craftFile,StringComparison.OrdinalIgnoreCase))continue;
                    if(sc.Root!=null){created.Remove(sc.Root);UnityEngine.Object.Destroy(sc.Root);}
                    spawnedCrafts.RemoveAt(i);
                }

                File.Delete(path);
                RefreshUserFileCaches();
                Status="Craft supprimé : "+craftFile;
                return true;
            }
            catch(Exception ex){Status="Suppression craft impossible : "+ex.Message;return false;}
        }

        internal bool DeleteUserSkybox(string pack)
        {
            try
            {
                if(string.IsNullOrEmpty(pack)){Status="Aucune skybox sélectionnée";return false;}
                if(string.Equals(activeSkyboxPack,pack,StringComparison.OrdinalIgnoreCase))RestoreOriginalSkybox();
                string path=Path.Combine(SkyboxRootPath,pack);
                if(!Directory.Exists(path)){Status="Skybox déjà absente";RefreshUserFileCaches();return false;}
                Directory.Delete(path,true);
                RefreshUserFileCaches();
                Status="Skybox supprimée : "+pack;
                return true;
            }
            catch(Exception ex){Status="Suppression skybox impossible : "+ex.Message;return false;}
        }

        internal bool ImportImageFromPath(string sourcePath)
        {
            try
            {
                if(string.IsNullOrWhiteSpace(sourcePath)){Status="Indiquez le chemin d'une image";return false;}
                string src=sourcePath.Trim().Trim('"');
                if(!File.Exists(src)){Status="Image introuvable";return false;}
                string ext=Path.GetExtension(src).ToLowerInvariant();
                if(ext!=".png"&&ext!=".jpg"&&ext!=".jpeg"){Status="Format accepté : PNG / JPG / JPEG";return false;}
                EnsureUserFolders();
                string name=Path.GetFileName(src);string dest=Path.Combine(UserImagesPath,name);
                if(File.Exists(dest))
                {
                    string stem=Path.GetFileNameWithoutExtension(name);int n=2;
                    do{dest=Path.Combine(UserImagesPath,stem+"_"+n+ext);n++;}while(File.Exists(dest));
                }
                File.Copy(src,dest,false);RefreshUserFileCaches();
                lastImportedImage=Path.GetFileName(dest);
                Status="Image importée : "+lastImportedImage;return true;
            }catch(Exception ex){Status="Import image impossible : "+ex.Message;return false;}
        }

        private Texture2D LoadToolbarIcon()
        {
            try
            {
                string path=Path.Combine(KSPUtil.ApplicationRootPath,"GameData","KSPSceneEditor","PluginData","UI","toolbar_icon.png");
                if(!File.Exists(path))return null;Texture2D tex=new Texture2D(38,38,TextureFormat.ARGB32,false);
                if(!ImageConversion.LoadImage(tex,File.ReadAllBytes(path))){UnityEngine.Object.Destroy(tex);return null;}
                return tex;
            }catch{return null;}
        }

        private void OnApplicationLauncherReady()
        {
            if(toolbarButton!=null||ApplicationLauncher.Instance==null)return;
            toolbarIcon=LoadToolbarIcon();
            if(toolbarIcon==null){toolbarIcon=new Texture2D(38,38);Color[] px=new Color[38*38];for(int i=0;i<px.Length;i++)px[i]=new Color(0.12f,0.55f,0.32f,1f);toolbarIcon.SetPixels(px);toolbarIcon.Apply();}
            toolbarButton=ApplicationLauncher.Instance.AddModApplication(
                delegate{OpenEditorWindow();},
                delegate{CloseEditorWindow();},
                null,null,null,null,
                ApplicationLauncher.AppScenes.ALWAYS,
                toolbarIcon);
        }

        private void OnApplicationLauncherDestroyed()
        {
            toolbarButton=null;
        }

        private void OnDestroy()
        {
            try{if(BaselineReady)CaptureCurrentSessionWorkspace();}catch{}
            try
            {
                GameEvents.onGUIApplicationLauncherReady.Remove(OnApplicationLauncherReady);
                GameEvents.onGUIApplicationLauncherDestroyed.Remove(OnApplicationLauncherDestroyed);
                GameEvents.onGameSceneLoadRequested.Remove(OnGameSceneLoadRequested);
                if(toolbarButton!=null&&ApplicationLauncher.Instance!=null)ApplicationLauncher.Instance.RemoveModApplication(toolbarButton);
            }catch{}
            toolbarButton=null;
            if(toolbarIcon!=null)UnityEngine.Object.Destroy(toolbarIcon);
            if(EditorRoot!=null)UnityEngine.Object.Destroy(EditorRoot);
            if(Instance==this)Instance=null;
        }

        private IEnumerator CaptureWhenStable()
        {
            yield return null;
            yield return new WaitForSecondsRealtime(10.0f);
            if (HighLogic.LoadedScene != GameScenes.MAINMENU) yield break;
            Status = "Préparation de l’éditeur...";
            SceneEditorLog.Info("Safe baseline capture starting after 10s stabilization delay");
            baseline.Capture(EditorRoot.transform);
            baselineAmbientLight=RenderSettings.ambientLight;
            CaptureSkyboxOriginal();
            BaselineReady = true;
            yield return StartCoroutine(BuildInitialCachesCoroutine());
            RefreshObjects();
            LoadContextProfiles();
            yield return StartCoroutine(RestorePreferredCurrentWorkspaceDelayed(0.35f));
            ForceRefreshNativeMainMenuState();
            persistentObservedContextKey=BuildContextKey(nativeAreaIndex,nativeStageIndex,nativeSandcastleActive);
            persistentCandidateContextKey=null;
            Status = "Scene Editor prêt";
            SceneEditorLog.Info(Status);
        }

        private void UpdateCurrentWorkspaceFromKsp()
        {
            if(scenePreviewActive)return;
            string detected=ActiveCanonicalRootKey();
            if(!string.IsNullOrEmpty(detected)&&!string.Equals(workspaceRootName,detected,StringComparison.OrdinalIgnoreCase))
            {
                workspaceRootName=detected;Selected=null;RefreshObjects();
            }
        }

        internal void SetEditorWindowInteraction(bool active){SetEditorInteractionShield(active);}
        internal bool NativeToolbarVisible
        {
            get
            {
                try{return toolbarButton!=null&&ApplicationLauncher.Instance!=null&&ApplicationLauncher.Instance.DetermineVisibility(toolbarButton);}
                catch{return false;}
            }
        }
        internal void OpenEditorWindow()
        {
            if(window!=null)window.Visible=true;SetEditorInteractionShield(true);
            try{if(toolbarButton!=null)toolbarButton.SetTrue(false);}catch{}
        }
        internal void CloseEditorWindow()
        {
            if(window!=null)window.Visible=false;
            SetEditorInteractionShield(false);
            try{if(toolbarButton!=null)toolbarButton.SetFalse(false);}catch{}

            if(HighLogic.LoadedScene==GameScenes.MAINMENU&&BaselineReady)
                StartCoroutine(CloseEditorReturnHomeRoutine());
        }

        private IEnumerator CloseEditorReturnHomeRoutine()
        {
            contextTransitionInProgress=true;
            ForceRefreshNativeMainMenuState();
            bool alreadyHome=nativeAreaIndex==1&&nativeStageIndex==0;
            if(alreadyHome){contextTransitionInProgress=false;yield break;}

            CaptureCurrentSessionWorkspace();
            applyingContextProfile=true;
            RestoreCurrentNativeContext(false);
            yield return new WaitForSecondsRealtime(0.85f);

            SelectNativeMainMenuArea(1);
            yield return new WaitForSecondsRealtime(0.40f);
            SelectNativeMainMenuStage(0);
            yield return new WaitForSecondsRealtime(0.75f);

            applyingContextProfile=false;
            string homeKey=BuildContextKey(1,0,false);
            string homeLabel=BuildContextLabel(1,0,false);
            yield return StartCoroutine(RestorePreferredWorkspaceByKeyDelayed(homeKey,homeLabel,0.15f));
            persistentObservedContextKey=homeKey;
            persistentCandidateContextKey=null;
            contextTransitionInProgress=false;
            Status="Scène principale";
        }

        private void SetEditorInteractionShield(bool active)
        {
            if(editorShieldActive==active)return;
            editorShieldActive=active;
            GameObject main=GameObject.Find("MainMenu");if(main==null)return;
            if(active)
            {
                shieldColliderStates.Clear();shieldCanvasStates.Clear();
                Collider[] cc=main.GetComponentsInChildren<Collider>(true);
                for(int i=0;i<cc.Length;i++)if(cc[i]!=null){shieldColliderStates[cc[i]]=cc[i].enabled;cc[i].enabled=false;}
                CanvasGroup[] cg=main.GetComponentsInChildren<CanvasGroup>(true);
                for(int i=0;i<cg.Length;i++)if(cg[i]!=null){shieldCanvasStates[cg[i]]=cg[i].blocksRaycasts;cg[i].blocksRaycasts=false;}
            }
            else
            {
                foreach(KeyValuePair<Collider,bool> kv in shieldColliderStates)if(kv.Key!=null)kv.Key.enabled=kv.Value;
                foreach(KeyValuePair<CanvasGroup,bool> kv in shieldCanvasStates)if(kv.Key!=null)kv.Key.blocksRaycasts=kv.Value;
                shieldColliderStates.Clear();shieldCanvasStates.Clear();
            }
        }

        private void PrepareKerbalPivotForEditing(Transform pivot)
        {
            if(pivot==null||!IsKerbalPivot(pivot))return;
            // Proxy is already isolated from KSP. Nothing above it can restore the stock position.
        }

        private void FreezeKerbalForEditing(Transform pivot)
        {
            // V0.20: intentionally disabled. The live KSP actor must keep animating.
        }

        private void OnGameSceneLoadRequested(GameScenes target)
        {
            // This event fires before KSP destroys MAINMENU. It is the last reliable moment
            // to persist unsaved work when the player starts/loads a game.
            if(HighLogic.LoadedScene!=GameScenes.MAINMENU||!BaselineReady)return;
            try
            {
                ForceRefreshNativeMainMenuState();
                CaptureSessionWorkspaceForContext(nativeAreaIndex,nativeStageIndex,nativeSandcastleActive,
                    BuildContextKey(nativeAreaIndex,nativeStageIndex,nativeSandcastleActive));
                SceneEditorLog.Info("Workspace flushed before scene request: "+target);
            }
            catch(Exception ex){SceneEditorLog.Warn("Pre-scene workspace flush: "+ex.Message);}
        }

        private void UpdatePersistentContextEngine(bool editorOpen)
        {
            if(!BaselineReady||contextTransitionInProgress||applyingContextProfile||scenePreviewActive)return;

            ForceRefreshNativeMainMenuState();
            string key=BuildContextKey(nativeAreaIndex,nativeStageIndex,nativeSandcastleActive);
            if(string.IsNullOrEmpty(key)||key.StartsWith("UNKNOWN",StringComparison.OrdinalIgnoreCase))return;

            // While editing, continuously checkpoint the live workspace. This makes KSP-driven
            // stage changes safe even when they happen without passing through our own buttons.
            if(editorOpen&&Time.realtimeSinceStartup>=nextSessionDraftAutosave)
            {
                nextSessionDraftAutosave=Time.realtimeSinceStartup+SessionDraftAutosaveInterval;
                CaptureSessionWorkspaceForContext(nativeAreaIndex,nativeStageIndex,nativeSandcastleActive,key);
            }

            if(string.IsNullOrEmpty(persistentObservedContextKey))
            {
                persistentObservedContextKey=key;
                persistentCandidateContextKey=null;
                return;
            }

            if(string.Equals(key,persistentObservedContextKey,StringComparison.OrdinalIgnoreCase))
            {
                persistentCandidateContextKey=null;
                return;
            }

            if(!string.Equals(key,persistentCandidateContextKey,StringComparison.OrdinalIgnoreCase))
            {
                persistentCandidateContextKey=key;
                persistentCandidateSince=Time.realtimeSinceStartup;
                return;
            }

            if(Time.realtimeSinceStartup-persistentCandidateSince<ContextStableDelay)return;

            string targetKey=persistentCandidateContextKey;
            persistentCandidateContextKey=null;
            StartCoroutine(ApplyDetectedKspContextRoutine(targetKey));
        }

        private IEnumerator ApplyDetectedKspContextRoutine(string targetKey)
        {
            if(contextTransitionInProgress)yield break;
            contextTransitionInProgress=true;

            // Give MainMenuEnvLogic one additional frame after its own transition.
            yield return null;
            ForceRefreshNativeMainMenuState();
            string liveKey=BuildContextKey(nativeAreaIndex,nativeStageIndex,nativeSandcastleActive);
            if(!string.Equals(liveKey,targetKey,StringComparison.OrdinalIgnoreCase))
            {
                contextTransitionInProgress=false;
                yield break;
            }

            string targetLabel=BuildContextLabel(nativeAreaIndex,nativeStageIndex,nativeSandcastleActive);

            // KSP initiated this switch, not Scene Editor. Remove the previous workspace's
            // live modifications from shared menu objects, then rebuild the target workspace.
            CleanLiveWorkspaceObjectsWithoutChangingNativeState();
            yield return null;
            RebuildKerbalRegistry(true);
            RefreshObjects();
            yield return StartCoroutine(RestorePreferredWorkspaceByKeyDelayed(targetKey,targetLabel,0f));

            persistentObservedContextKey=targetKey;
            contextTransitionInProgress=false;
            SceneEditorLog.Info("Persistent context applied: "+targetKey);
        }

        private void Update()
        {
            if(HighLogic.LoadedScene!=GameScenes.MAINMENU){SetEditorInteractionShield(false);return;}
            UpdateCurrentWorkspaceFromKsp();
            bool editorOpen=window!=null&&window.Visible;
            UpdatePersistentContextEngine(editorOpen);
            SetEditorInteractionShield(editorOpen);
            if(editorOpen)
            {
                bool ctrl=Input.GetKey(KeyCode.LeftControl)||Input.GetKey(KeyCode.RightControl);
                if(ctrl&&Input.GetKeyDown(KeyCode.S))SaveScene(window.SceneName);
                if(ctrl&&Input.GetKeyDown(KeyCode.R))ResetCurrentWorkToActiveComposition();
            }
            if(pickMode)TryScenePick();
            else if(editorOpen)HandleDirectManipulation();
        }

        private void LateUpdate()
        {
            if(HighLogic.LoadedScene!=GameScenes.MAINMENU)return;

            // V0.23: never replace the animated Kerbal pose with an absolute transform.
            // We compose a user offset over whatever pose KSP produced this frame.
            if(kerbalLiveOffsets.Count>0)
            {
                List<Transform> dead=null;
                foreach(KeyValuePair<Transform,KerbalLiveOffsetState> kv in kerbalLiveOffsets)
                {
                    Transform actor=kv.Key;KerbalLiveOffsetState state=kv.Value;
                    if(actor==null){if(dead==null)dead=new List<Transform>();dead.Add(actor);continue;}

                    Vector3 basePos=actor.position;
                    Quaternion baseRot=actor.rotation;
                    Vector3 baseScale=actor.localScale;

                    if(state.HasLastCompose)
                    {
                        // If KSP did not rewrite an axis this frame, remove our previous
                        // offset first. If KSP DID rewrite it, current is already the fresh base.
                        if(Vector3.Distance(actor.position,state.LastComposedPosition)<0.0005f)
                            basePos=actor.position-state.LastAppliedPositionOffset;

                        if(Quaternion.Angle(actor.rotation,state.LastComposedRotation)<0.02f)
                            baseRot=Quaternion.Inverse(state.LastAppliedRotationOffset)*actor.rotation;

                        if(Vector3.Distance(actor.localScale,state.LastComposedScale)<0.0005f)
                        {
                            Vector3 m=state.LastAppliedScaleMultiplier;
                            baseScale=new Vector3(
                                Mathf.Abs(m.x)>0.00001f?actor.localScale.x/m.x:actor.localScale.x,
                                Mathf.Abs(m.y)>0.00001f?actor.localScale.y/m.y:actor.localScale.y,
                                Mathf.Abs(m.z)>0.00001f?actor.localScale.z/m.z:actor.localScale.z);
                        }
                    }

                    if(state.OverridePosition)actor.position=basePos+state.PositionOffsetWorld;
                    if(state.OverrideRotation)actor.rotation=state.RotationOffset*baseRot;
                    if(state.OverrideScale)actor.localScale=Vector3.Scale(baseScale,state.ScaleMultiplier);

                    state.LastAppliedPositionOffset=state.OverridePosition?state.PositionOffsetWorld:Vector3.zero;
                    state.LastAppliedRotationOffset=state.OverrideRotation?state.RotationOffset:Quaternion.identity;
                    state.LastAppliedScaleMultiplier=state.OverrideScale?state.ScaleMultiplier:Vector3.one;
                    state.LastComposedPosition=actor.position;
                    state.LastComposedRotation=actor.rotation;
                    state.LastComposedScale=actor.localScale;
                    state.HasLastCompose=true;
                }
                if(dead!=null)for(int i=0;i<dead.Count;i++)kerbalLiveOffsets.Remove(dead[i]);
            }



            float dt=Time.unscaledDeltaTime;
            for(int i=0;i<spawnedPlanets.Count;i++)
            {
                SpawnedPlanet p=spawnedPlanets[i];
                if(p!=null&&p.Root!=null&&p.Root.activeInHierarchy)p.Root.transform.Rotate(Vector3.up,6f*dt,Space.Self);
            }
        }

        private Transform EnsureKerbalPivot(Transform actor)
        {
            if(actor==null)return null;
            Transform existing;
            if(kerbalActorToPivot.TryGetValue(actor,out existing)&&existing!=null)return existing;

            Transform parent=actor.parent;if(parent==null)return null;
            KerbalOriginalState state=new KerbalOriginalState
            {
                Parent=parent,
                SiblingIndex=actor.GetSiblingIndex(),
                LocalPosition=actor.localPosition,
                LocalRotation=actor.localRotation,
                LocalScale=actor.localScale
            };
            kerbalOriginalStates[actor]=state;

            // Identity offset pivot under the SAME stock parent.
            // Reparenting with worldPositionStays=true preserves the actor's original
            // local pose because the new pivot starts as identity.
            GameObject pivotGo=new GameObject("KSE_KERBAL_PIVOT_"+actor.name);
            Transform pivot=pivotGo.transform;
            pivot.SetParent(parent,false);
            pivot.localPosition=Vector3.zero;
            pivot.localRotation=Quaternion.identity;
            pivot.localScale=Vector3.one;
            pivot.SetSiblingIndex(state.SiblingIndex);

            actor.SetParent(pivot,true);

            // IMPORTANT: this is still the original KSP actor.
            // No MonoBehaviour / Animation / Animator is disabled or cloned.
            Animation[] aa=actor.GetComponentsInChildren<Animation>(true);
            for(int i=0;i<aa.Length;i++)
            {
                if(aa[i]==null)continue;aa[i].enabled=true;
                try{if(!aa[i].isPlaying)aa[i].Play();}catch{}
            }
            Animator[] ar=actor.GetComponentsInChildren<Animator>(true);
            for(int i=0;i<ar.Length;i++)if(ar[i]!=null)ar[i].enabled=true;

            kerbalActorToPivot[actor]=pivot;
            kerbalPivotToActor[pivot]=actor;
            activeKerbalProxies.Add(pivot);
            Status="Kerbal en édition : acteur KSP vivant";
            return pivot;
        }

        private bool IsKerbalPivot(Transform t)
        {
            return t!=null && kerbalPivotToActor.ContainsKey(t);
        }

        private Transform KerbalActorFromPivot(Transform pivot)
        {
            Transform actor;return pivot!=null&&kerbalPivotToActor.TryGetValue(pivot,out actor)?actor:null;
        }

        private void RestoreKerbalActorFromPivot(Transform actor,Transform pivot)
        {
            if(actor==null)return;
            KerbalOriginalState state;
            if(kerbalOriginalStates.TryGetValue(actor,out state)&&state!=null&&state.Parent!=null)
            {
                actor.SetParent(state.Parent,false);
                actor.localPosition=state.LocalPosition;
                actor.localRotation=state.LocalRotation;
                actor.localScale=state.LocalScale;
                actor.SetSiblingIndex(Mathf.Clamp(state.SiblingIndex,0,Mathf.Max(0,state.Parent.childCount-1)));
            }
            else if(pivot!=null&&pivot.parent!=null)actor.SetParent(pivot.parent,true);
            actor.gameObject.SetActive(true);
            kerbalOriginalStates.Remove(actor);
        }

        private void ClearKerbalPivotsAfterBaselineRestore()
        {
            List<KeyValuePair<Transform,Transform>> pairs=new List<KeyValuePair<Transform,Transform>>(kerbalActorToPivot);
            kerbalActorToPivot.Clear();kerbalPivotToActor.Clear();activeKerbalProxies.Clear();
            for(int i=0;i<pairs.Count;i++)
            {
                Transform actor=pairs[i].Key;Transform proxy=pairs[i].Value;
                RestoreKerbalActorFromPivot(actor,proxy);
                if(proxy!=null)UnityEngine.Object.Destroy(proxy.gameObject);
            }
        }

        private KerbalLiveOffsetState EnsureKerbalLiveOffset(Transform actor)
        {
            if(actor==null)return null;
            KerbalLiveOffsetState state;
            if(kerbalLiveOffsets.TryGetValue(actor,out state)&&state!=null)return state;
            state=new KerbalLiveOffsetState
            {
                OriginalLocalPosition=actor.localPosition,
                OriginalLocalRotation=actor.localRotation,
                OriginalLocalScale=actor.localScale,
                LastComposedPosition=actor.position,
                LastComposedRotation=actor.rotation,
                LastComposedScale=actor.localScale
            };
            kerbalLiveOffsets[actor]=state;
            BeginEdit(actor);
            return state;
        }

        private void RememberKerbalDirectTransform(Transform actor,int tool)
        {
            if(actor==null||!IsKnownKerbal(actor))return;
            KerbalLiveOffsetState state=EnsureKerbalLiveOffset(actor);if(state==null)return;
            if(tool==0||tool==3)
            {
                Vector3 delta=actor.position-directStartPosition;
                state.PositionOffsetWorld=state.DragStartPositionOffset+delta;
                state.OverridePosition=true;
            }
            else if(tool==1)
            {
                Quaternion delta=actor.rotation*Quaternion.Inverse(directStartRotation);
                state.RotationOffset=delta*state.DragStartRotationOffset;
                state.OverrideRotation=true;
            }
            else if(tool==2)
            {
                Vector3 ratio=new Vector3(
                    Mathf.Abs(directStartScale.x)>0.00001f?actor.localScale.x/directStartScale.x:1f,
                    Mathf.Abs(directStartScale.y)>0.00001f?actor.localScale.y/directStartScale.y:1f,
                    Mathf.Abs(directStartScale.z)>0.00001f?actor.localScale.z/directStartScale.z:1f);
                state.ScaleMultiplier=Vector3.Scale(state.DragStartScaleMultiplier,ratio);
                state.OverrideScale=true;
            }
            state.LastComposedPosition=actor.position;
            state.LastComposedRotation=actor.rotation;
            state.LastComposedScale=actor.localScale;
            state.HasLastCompose=true;
            edited.Add(actor);
        }

        private bool RestoreKerbalLiveOffset(Transform actor)
        {
            if(actor==null)return false;KerbalLiveOffsetState state;
            if(!kerbalLiveOffsets.TryGetValue(actor,out state)||state==null)return false;
            actor.localPosition=state.OriginalLocalPosition;
            actor.localRotation=state.OriginalLocalRotation;
            actor.localScale=state.OriginalLocalScale;
            kerbalLiveOffsets.Remove(actor);edited.Remove(actor);editedContextOwners.Remove(actor);
            return true;
        }

        internal void WriteKerbalOffsets(ConfigNode root)
        {
            if(root==null)return;
            try
            {
                foreach(KeyValuePair<Transform,KerbalLiveOffsetState> kv in kerbalLiveOffsets)
                {
                    Transform actor=kv.Key;
                    KerbalLiveOffsetState state=kv.Value;
                    if(actor==null||state==null||!IsKnownKerbal(actor)||!IsInsideCurrentWorkspace(actor))continue;

                    ConfigNode n=root.AddNode("KERBAL_OFFSET");
                    n.AddValue("path",ScenePath.Get(actor));
                    n.AddValue("positionOffset",SerializeVector3(state.PositionOffsetWorld));
                    n.AddValue("rotationOffset",SerializeQuaternion(state.RotationOffset));
                    n.AddValue("scaleMultiplier",SerializeVector3(state.ScaleMultiplier));
                    n.AddValue("overridePosition",state.OverridePosition);
                    n.AddValue("overrideRotation",state.OverrideRotation);
                    n.AddValue("overrideScale",state.OverrideScale);
                }
            }
            catch(Exception ex){SceneEditorLog.Warn("Kerbal offset save: "+ex.Message);}
        }

        internal void ReadKerbalOffsets(ConfigNode root)
        {
            if(root==null)return;
            try
            {
                ConfigNode[] nodes=root.GetNodes("KERBAL_OFFSET");
                for(int i=0;i<nodes.Length;i++)
                {
                    string path=nodes[i].GetValue("path");
                    Transform actor=ScenePath.Find(path);
                    if(actor==null||!IsKnownKerbal(actor))continue;

                    KerbalLiveOffsetState state=EnsureKerbalLiveOffset(actor);
                    if(state==null)continue;

                    Vector3 v;
                    Quaternion q;
                    if(TryDeserializeVector3(nodes[i].GetValue("positionOffset"),out v))state.PositionOffsetWorld=v;
                    if(TryDeserializeQuaternion(nodes[i].GetValue("rotationOffset"),out q))state.RotationOffset=q;
                    if(TryDeserializeVector3(nodes[i].GetValue("scaleMultiplier"),out v))state.ScaleMultiplier=v;

                    bool b;
                    state.OverridePosition=bool.TryParse(nodes[i].GetValue("overridePosition"),out b)?b:true;
                    state.OverrideRotation=bool.TryParse(nodes[i].GetValue("overrideRotation"),out b)?b:false;
                    state.OverrideScale=bool.TryParse(nodes[i].GetValue("overrideScale"),out b)?b:false;

                    // Do not restore an absolute animated pose. Force LateUpdate to take the
                    // next KSP animation pose as the new base and compose our saved offsets over it.
                    state.LastAppliedPositionOffset=Vector3.zero;
                    state.LastAppliedRotationOffset=Quaternion.identity;
                    state.LastAppliedScaleMultiplier=Vector3.one;
                    state.HasLastCompose=false;
                    edited.Add(actor);
                }
            }
            catch(Exception ex){SceneEditorLog.Warn("Kerbal offset load: "+ex.Message);}
        }

        private static string SerializeVector3(Vector3 v)
        {
            return v.x.ToString("R",CultureInfo.InvariantCulture)+","+
                   v.y.ToString("R",CultureInfo.InvariantCulture)+","+
                   v.z.ToString("R",CultureInfo.InvariantCulture);
        }

        private static string SerializeQuaternion(Quaternion q)
        {
            return q.x.ToString("R",CultureInfo.InvariantCulture)+","+
                   q.y.ToString("R",CultureInfo.InvariantCulture)+","+
                   q.z.ToString("R",CultureInfo.InvariantCulture)+","+
                   q.w.ToString("R",CultureInfo.InvariantCulture);
        }

        private static bool TryDeserializeVector3(string s,out Vector3 v)
        {
            v=Vector3.zero;
            if(string.IsNullOrEmpty(s))return false;
            string[] p=s.Split(',');
            float x,y,z;
            if(p.Length<3||
               !float.TryParse(p[0],NumberStyles.Float,CultureInfo.InvariantCulture,out x)||
               !float.TryParse(p[1],NumberStyles.Float,CultureInfo.InvariantCulture,out y)||
               !float.TryParse(p[2],NumberStyles.Float,CultureInfo.InvariantCulture,out z))return false;
            v=new Vector3(x,y,z);return true;
        }

        private static bool TryDeserializeQuaternion(string s,out Quaternion q)
        {
            q=Quaternion.identity;
            if(string.IsNullOrEmpty(s))return false;
            string[] p=s.Split(',');
            float x,y,z,w;
            if(p.Length<4||
               !float.TryParse(p[0],NumberStyles.Float,CultureInfo.InvariantCulture,out x)||
               !float.TryParse(p[1],NumberStyles.Float,CultureInfo.InvariantCulture,out y)||
               !float.TryParse(p[2],NumberStyles.Float,CultureInfo.InvariantCulture,out z)||
               !float.TryParse(p[3],NumberStyles.Float,CultureInfo.InvariantCulture,out w))return false;
            q=new Quaternion(x,y,z,w);return true;
        }

        internal bool IsKnownKerbal(Transform t)
        {
            if(t==null)return false;
            string n=t.name??string.Empty;
            bool actor=string.Equals(n,"maleEVA_inverted",StringComparison.OrdinalIgnoreCase)
                    || string.Equals(n,"maleEVA_side",StringComparison.OrdinalIgnoreCase)
                    || string.Equals(n,"maleEVA_center",StringComparison.OrdinalIgnoreCase)
                    || string.Equals(n,"femaleEVA",StringComparison.OrdinalIgnoreCase)
                    || string.Equals(n,"maleEVA",StringComparison.OrdinalIgnoreCase);
            if(!actor)return false;

            Transform p=t.parent;if(p==null||!string.Equals(p.name,"Kerbals",StringComparison.OrdinalIgnoreCase))return false;
            Transform root=p.parent;
            return root!=null&&(string.Equals(root.name,"OrbitScene",StringComparison.OrdinalIgnoreCase)
                              ||string.Equals(root.name,"MunScene",StringComparison.OrdinalIgnoreCase));
        }

        internal void ApplyWorldPosition(Transform t, Vector3 position)
        {
            if(t==null)return;
            if(IsKnownKerbal(t))
            {
                KerbalLiveOffsetState state=EnsureKerbalLiveOffset(t);if(state==null)return;
                state.PositionOffsetWorld+=position-t.position;
                state.OverridePosition=true;
                t.position=position;state.LastComposedPosition=t.position;state.HasLastCompose=true;
                Selected=t;edited.Add(t);return;
            }
            if(IsKerbalPivot(t))PrepareKerbalPivotForEditing(t);
            t.position=position;
        }

        private Transform FindSceneRoot(string rootName)
        {
            if(string.IsNullOrEmpty(rootName))return null;
            Transform cached;
            if(cachedSceneObjects.TryGetValue(rootName,out cached)&&cached!=null)return cached;
            Scene scene=SceneManager.GetActiveScene();if(!scene.IsValid())return null;
            GameObject[] roots=scene.GetRootGameObjects();
            for(int i=0;i<roots.Length;i++)if(roots[i]!=null&&string.Equals(roots[i].name,rootName,StringComparison.OrdinalIgnoreCase))return roots[i].transform;
            GameObject go=GameObject.Find(rootName);return go!=null?go.transform:null;
        }

        private int SceneCandidateScore(GameObject g)
        {
            if(g==null||!ScenePath.InLoadedScene(g.transform))return -1000;
            string n=g.name??string.Empty;string low=n.ToLowerInvariant();
            if(low.StartsWith("kse_")||low.Contains("eventsystem")||low.Contains("canvas")||low.Contains("loading")||low.Contains("editor"))return -1000;
            if(low=="sceneui"||low.EndsWith("ui"))return -1000;

            Renderer[] rr=g.GetComponentsInChildren<Renderer>(true);
            Camera[] cc=g.GetComponentsInChildren<Camera>(true);
            Animation[] aa=g.GetComponentsInChildren<Animation>(true);
            Animator[] ar=g.GetComponentsInChildren<Animator>(true);
            int visual=rr!=null?rr.Length:0;
            int cams=cc!=null?cc.Length:0;
            int anim=(aa!=null?aa.Length:0)+(ar!=null?ar.Length:0);

            if(string.Equals(n,"OrbitScene",StringComparison.OrdinalIgnoreCase))return 10000;
            if(string.Equals(n,"MunScene",StringComparison.OrdinalIgnoreCase))return 9000;

            // A true menu composition normally owns a substantial visual subtree,
            // or several animations/cameras. Tiny technical containers such as the
            // V0.18 "Scenery" false positive are deliberately rejected.
            int score=visual*2+anim*18+cams*12;
            if(low.Contains("scene")||low.Contains("menu")||low.Contains("landscape")||low.Contains("space")||low.Contains("mun")||low.Contains("orbit"))score+=25;
            if(g.activeInHierarchy)score+=8;
            if(visual<8&&anim==0&&cams<=1)return -1000;
            if(score<35)return -1000;
            return score;
        }

        private bool IsUsefulMenuSceneRoot(GameObject g)
        {
            return SceneCandidateScore(g)>=35;
        }

        private string BuildSceneProfile(string rootName)
        {
            Transform t=FindSceneRoot(rootName);return BuildSceneProfileForTransform(t);
        }

        internal string GetSceneProfile(string rootName)
        {
            string value;
            if(cachedSceneProfiles.TryGetValue(rootName??string.Empty,out value))return value;
            return "profil non actualisé";
        }

        private void ResumeSceneAnimations(Transform root)
        {
            if(root==null)return;
            Animation[] aa=root.GetComponentsInChildren<Animation>(true);
            for(int i=0;i<aa.Length;i++)
            {
                Animation a=aa[i];if(a==null||!a.enabled||a.isPlaying)continue;
                try
                {
                    if(a.clip!=null)a.Play(a.clip.name);
                    else a.Play();
                }catch{}
            }
            Animator[] ar=root.GetComponentsInChildren<Animator>(true);
            for(int i=0;i<ar.Length;i++)if(ar[i]!=null&&ar[i].gameObject.activeInHierarchy)ar[i].enabled=true;
        }

        internal string[] GetKnownMenuSceneRoots()
        {
            return cachedScenes??new string[0];
        }

        private string ActiveCanonicalRootKey()
        {
            Transform mun; if(cachedSceneObjects.TryGetValue("MunScene",out mun)&&mun!=null&&mun.gameObject.activeInHierarchy)return "MunScene";
            Transform orbit;if(cachedSceneObjects.TryGetValue("OrbitScene",out orbit)&&orbit!=null&&orbit.gameObject.activeInHierarchy)return "OrbitScene";
            return cachedSceneObjects.ContainsKey("OrbitScene")?"OrbitScene":cachedSceneObjects.ContainsKey("MunScene")?"MunScene":null;
        }

        private string VariantFilePath
        {
            get { return Path.Combine(KSPUtil.ApplicationRootPath,"GameData","KSPSceneEditor","PluginData","CapturedMenuVariants.cfg"); }
        }

        private string RelativeIndexPath(Transform root,Transform target)
        {
            if(root==null||target==null)return "";
            if(root==target)return ".";
            List<int> indices=new List<int>();
            Transform t=target;
            while(t!=null&&t!=root){indices.Add(t.GetSiblingIndex());t=t.parent;}
            if(t!=root)return "";
            indices.Reverse();
            return string.Join("/",indices.ConvertAll(delegate(int x){return x.ToString();}).ToArray());
        }

        private Transform FindRelativeIndexPath(Transform root,string path)
        {
            if(root==null||string.IsNullOrEmpty(path))return null;
            if(path==".")return root;
            string[] parts=path.Split('/');Transform t=root;
            for(int i=0;i<parts.Length;i++)
            {
                int idx;if(!int.TryParse(parts[i],out idx)||idx<0||idx>=t.childCount)return null;
                t=t.GetChild(idx);
            }
            return t;
        }

        private Transform ResolveVariantPath(Transform root,string path)
        {
            if(string.IsNullOrEmpty(path))return null;
            if(path.StartsWith("ABS:",StringComparison.Ordinal))return ScenePath.Find(path.Substring(4));
            return FindRelativeIndexPath(root,path);
        }

        private string VecString(Vector3 v)
        {
            return v.x.ToString("R",CultureInfo.InvariantCulture)+","+v.y.ToString("R",CultureInfo.InvariantCulture)+","+v.z.ToString("R",CultureInfo.InvariantCulture);
        }

        private string QuatString(Quaternion q)
        {
            return q.x.ToString("R",CultureInfo.InvariantCulture)+","+q.y.ToString("R",CultureInfo.InvariantCulture)+","+q.z.ToString("R",CultureInfo.InvariantCulture)+","+q.w.ToString("R",CultureInfo.InvariantCulture);
        }

        private bool TryParseVecString(string s,out Vector3 v)
        {
            v=Vector3.zero;if(string.IsNullOrEmpty(s))return false;
            string[] p=s.Split(',');if(p.Length!=3)return false;
            float x,y,z;
            if(!float.TryParse(p[0],NumberStyles.Float,CultureInfo.InvariantCulture,out x))return false;
            if(!float.TryParse(p[1],NumberStyles.Float,CultureInfo.InvariantCulture,out y))return false;
            if(!float.TryParse(p[2],NumberStyles.Float,CultureInfo.InvariantCulture,out z))return false;
            v=new Vector3(x,y,z);return true;
        }

        private bool TryParseQuatString(string s,out Quaternion q)
        {
            q=Quaternion.identity;if(string.IsNullOrEmpty(s))return false;
            string[] p=s.Split(',');if(p.Length!=4)return false;
            float x,y,z,w;
            if(!float.TryParse(p[0],NumberStyles.Float,CultureInfo.InvariantCulture,out x))return false;
            if(!float.TryParse(p[1],NumberStyles.Float,CultureInfo.InvariantCulture,out y))return false;
            if(!float.TryParse(p[2],NumberStyles.Float,CultureInfo.InvariantCulture,out z))return false;
            if(!float.TryParse(p[3],NumberStyles.Float,CultureInfo.InvariantCulture,out w))return false;
            q=new Quaternion(x,y,z,w);return true;
        }

        private void SaveMenuVariantsToDisk()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(VariantFilePath));
                ConfigNode root=new ConfigNode("KSP_SCENE_EDITOR_VARIANTS");
                root.AddValue("version","2");
                foreach(KeyValuePair<string,MenuVariant> kv in menuVariants)
                {
                    MenuVariant v=kv.Value;if(v==null)continue;
                    ConfigNode vn=root.AddNode("VARIANT");
                    vn.AddValue("key",v.Key);vn.AddValue("root",v.RootKey);

                    foreach(KeyValuePair<string,VariantPose> ps in v.SavedPoses)
                    {
                        VariantPose p=ps.Value;if(p==null)continue;
                        ConfigNode pn=vn.AddNode("POSE");
                        pn.AddValue("path",ps.Key);
                        pn.AddValue("active",p.Active);
                        pn.AddValue("position",VecString(p.LocalPosition));
                        pn.AddValue("rotation",QuatString(p.LocalRotation));
                        pn.AddValue("scale",VecString(p.LocalScale));
                    }

                    // Keep STATE for backward readability / manual inspection.
                    foreach(KeyValuePair<string,bool> st in v.SavedStates)
                    {
                        ConfigNode sn=vn.AddNode("STATE");sn.AddValue("path",st.Key);sn.AddValue("active",st.Value);
                    }
                }
                root.Save(VariantFilePath);
            }catch(Exception ex){SceneEditorLog.Warn("Save variants: "+ex.Message);}
        }

        private void LoadMenuVariantsFromDisk()
        {
            if(menuVariants.Count>0||!File.Exists(VariantFilePath))return;
            try
            {
                ConfigNode root=ConfigNode.Load(VariantFilePath);if(root==null)return;
                ConfigNode[] vars=root.GetNodes("VARIANT");
                for(int i=0;i<vars.Length;i++)
                {
                    string key=vars[i].GetValue("key"),rootKey=vars[i].GetValue("root");
                    Transform sceneRoot;
                    if(string.IsNullOrEmpty(key)||string.IsNullOrEmpty(rootKey)||!cachedSceneObjects.TryGetValue(rootKey,out sceneRoot)||sceneRoot==null)continue;
                    MenuVariant v=new MenuVariant{Key=key,RootKey=rootKey};

                    ConfigNode[] poses=vars[i].GetNodes("POSE");
                    for(int p=0;p<poses.Length;p++)
                    {
                        string path=poses[p].GetValue("path");if(string.IsNullOrEmpty(path))continue;
                        bool active=false;bool.TryParse(poses[p].GetValue("active"),out active);
                        Vector3 lp=Vector3.zero,ls=Vector3.one;Quaternion lr=Quaternion.identity;
                        TryParseVecString(poses[p].GetValue("position"),out lp);
                        TryParseQuatString(poses[p].GetValue("rotation"),out lr);
                        if(!TryParseVecString(poses[p].GetValue("scale"),out ls))ls=Vector3.one;
                        VariantPose pose=new VariantPose{Active=active,LocalPosition=lp,LocalRotation=lr,LocalScale=ls};
                        v.SavedPoses[path]=pose;v.SavedStates[path]=active;
                        Transform t=ResolveVariantPath(sceneRoot,path);
                        if(t!=null){v.Poses[t.gameObject]=pose;v.States[t.gameObject]=active;}
                    }

                    // Compatibility with V0.23/V0.24 captures containing STATE only.
                    if(v.Poses.Count==0)
                    {
                        ConfigNode[] states=vars[i].GetNodes("STATE");
                        for(int s=0;s<states.Length;s++)
                        {
                            string path=states[s].GetValue("path");bool active=false;bool.TryParse(states[s].GetValue("active"),out active);
                            if(string.IsNullOrEmpty(path))continue;
                            v.SavedStates[path]=active;
                            Transform t=ResolveVariantPath(sceneRoot,path);
                            if(t!=null)
                            {
                                VariantPose pose=new VariantPose{Active=active,LocalPosition=t.localPosition,LocalRotation=t.localRotation,LocalScale=t.localScale};
                                v.States[t.gameObject]=active;v.Poses[t.gameObject]=pose;v.SavedPoses[path]=pose;
                            }
                        }
                    }

                    if(v.States.Count>0)
                    {
                        menuVariants[key]=v;capturedSceneKeys.Add(key);
                        Match mm=Regex.Match(key,@"(\d+)$");int n;if(mm.Success&&int.TryParse(mm.Groups[1].Value,out n))variantSequence=Mathf.Max(variantSequence,n+1);
                    }
                }
            }catch(Exception ex){SceneEditorLog.Warn("Load variants: "+ex.Message);}
        }

        private void CapturePoseAbsolute(Transform t,MenuVariant variant,ref int captured,int limit)
        {
            if(t==null||variant==null||captured>=limit)return;
            string n=t.name??string.Empty;if(n.StartsWith("KSE_",StringComparison.OrdinalIgnoreCase))return;
            string path="ABS:"+ScenePath.Get(t);if(string.IsNullOrEmpty(path))return;
            if(variant.SavedPoses.ContainsKey(path))return;
            VariantPose pose=new VariantPose{Active=t.gameObject.activeSelf,LocalPosition=t.localPosition,LocalRotation=t.localRotation,LocalScale=t.localScale};
            variant.States[t.gameObject]=pose.Active;variant.SavedStates[path]=pose.Active;
            variant.Poses[t.gameObject]=pose;variant.SavedPoses[path]=pose;captured++;
        }

        private void CaptureTreeAbsolute(Transform root,MenuVariant variant,ref int captured,int limit)
        {
            if(root==null)return;
            Transform[] all=root.GetComponentsInChildren<Transform>(true);
            for(int i=0;i<all.Length&&captured<limit;i++)CapturePoseAbsolute(all[i],variant,ref captured,limit);
        }

        private void CaptureVariantStates(Transform root,MenuVariant variant)
        {
            if(root==null||variant==null)return;
            int captured=0;const int limit=3200;

            // The scene geometry itself.
            CaptureTreeAbsolute(root,variant,ref captured,limit);

            // CRITICAL: KSP's visible "other menu scenes" are also controlled by the
            // MainMenu stage hierarchy (stage 1 / stage 2 / mission menu / etc.).
            // Previous builds only captured visual renderers outside OrbitScene and
            // therefore missed the actual stage switch.
            GameObject main=GameObject.Find("MainMenu");
            if(main!=null)CaptureTreeAbsolute(main.transform,variant,ref captured,limit);

            // Capture the other canonical scene too, because KSP can switch which root
            // is active as part of the same composition.
            string other=string.Equals(variant.RootKey,"OrbitScene",StringComparison.OrdinalIgnoreCase)?"MunScene":"OrbitScene";
            Transform otherRoot=FindSceneRoot(other);
            if(otherRoot!=null)CaptureTreeAbsolute(otherRoot,variant,ref captured,limit);

            // Finally capture other top-level scene/scenery objects in the same Unity scene.
            Transform[] loaded=Resources.FindObjectsOfTypeAll<Transform>();
            for(int i=0;i<loaded.Length&&captured<limit;i++)
            {
                Transform t=loaded[i];if(t==null||!ScenePath.InLoadedScene(t)||t.parent!=null)continue;
                if(t.gameObject.scene!=root.gameObject.scene)continue;
                string low=(t.name??string.Empty).ToLowerInvariant();
                if(low.Contains("scene")||low.Contains("scenery")||low.Contains("menu")||low.Contains("landscape"))
                    CaptureTreeAbsolute(t,variant,ref captured,limit);
            }
            SceneEditorLog.Info("SCENE CAPTURE V0.27 | root="+variant.RootKey+" | poses="+variant.SavedPoses.Count+" | mainMenu="+(main!=null));
        }

        private string BuildVariantProfile(MenuVariant v)
        {
            if(v==null)return "variante invalide";
            int active=0;foreach(KeyValuePair<GameObject,bool> kv in v.States)if(kv.Value)active++;
            return "CAPTURE COMPLÈTE • "+active+"/"+v.States.Count+" actifs • "+v.Poses.Count+" poses • "+v.RootKey;
        }

        private void ApplyVariantSnapshot(MenuVariant v,bool rememberOriginal)
        {
            if(v==null)return;
            foreach(KeyValuePair<GameObject,VariantPose> kv in v.Poses)
            {
                GameObject go=kv.Key;VariantPose p=kv.Value;if(go==null||p==null)continue;
                Transform t=go.transform;
                if(rememberOriginal)
                {
                    if(!scenePreviewOriginalStates.ContainsKey(go))scenePreviewOriginalStates[go]=go.activeSelf;
                    if(!scenePreviewOriginalPoses.ContainsKey(t))
                        scenePreviewOriginalPoses[t]=new VariantPose{Active=go.activeSelf,LocalPosition=t.localPosition,LocalRotation=t.localRotation,LocalScale=t.localScale};
                }

                go.SetActive(p.Active);
                t.localPosition=p.LocalPosition;
                t.localRotation=p.LocalRotation;
                t.localScale=p.LocalScale;
            }
            Transform root;if(cachedSceneObjects.TryGetValue(v.RootKey,out root)&&root!=null)root.gameObject.SetActive(true);
        }

        private void ApplyVariant(MenuVariant v)
        {
            ApplyVariantSnapshot(v,true);
        }

        private bool VariantsEquivalent(MenuVariant a,MenuVariant b)
        {
            if(a==null||b==null)return false;
            if(!string.Equals(a.RootKey,b.RootKey,StringComparison.OrdinalIgnoreCase))return false;
            if(a.SavedPoses.Count!=b.SavedPoses.Count)return false;
            foreach(KeyValuePair<string,VariantPose> kv in a.SavedPoses)
            {
                VariantPose p2;if(!b.SavedPoses.TryGetValue(kv.Key,out p2)||p2==null||kv.Value==null)return false;
                VariantPose p1=kv.Value;
                if(p1.Active!=p2.Active)return false;
                if(Vector3.Distance(p1.LocalPosition,p2.LocalPosition)>0.0005f)return false;
                if(Quaternion.Angle(p1.LocalRotation,p2.LocalRotation)>0.05f)return false;
                if(Vector3.Distance(p1.LocalScale,p2.LocalScale)>0.0005f)return false;
            }
            return true;
        }

        internal bool CaptureCurrentKspScene()
        {
            try
            {
                string rootKey=ActiveCanonicalRootKey();
                if(string.IsNullOrEmpty(rootKey)){Status="Aucune racine KSP OrbitScene/MunScene active";return false;}
                Transform root=cachedSceneObjects[rootKey];

                MenuVariant variant=new MenuVariant();
                variant.RootKey=rootKey;
                CaptureVariantStates(root,variant);
                if(variant.States.Count==0){Status="Aucun groupe visuel à capturer";return false;}

                // Duplicate detection includes active states AND local transforms.
                foreach(KeyValuePair<string,MenuVariant> existing in menuVariants)
                {
                    if(!VariantsEquivalent(existing.Value,variant))continue;
                    workspaceRootName=existing.Key;
Selected=null;RefreshObjects();
                    Status="Capture déjà connue : "+existing.Key;return true;
                }

                variant.Key=(rootKey=="MunScene"?"MUN":"ORBITE")+" - VARIANTE "+variantSequence++;
                menuVariants[variant.Key]=variant;
                capturedSceneKeys.Add(variant.Key);
                SaveMenuVariantsToDisk();
                BuildSceneCache();
                workspaceRootName=variant.Key;
Selected=null;RefreshObjects();
                Status="Variante KSP capturée : "+variant.Key;return true;
            }
            catch(Exception ex){Status="Capture variante impossible : "+ex.Message;return false;}
        }

        private string BuildSceneProfileForTransform(Transform t)
        {
            if(t==null)return "indisponible";
            Renderer[] rr=t.GetComponentsInChildren<Renderer>(true);
            Camera[] cc=t.GetComponentsInChildren<Camera>(true);
            Animation[] aa=t.GetComponentsInChildren<Animation>(true);
            Animator[] ar=t.GetComponentsInChildren<Animator>(true);
            int kerbals=0;Transform[] tt=t.GetComponentsInChildren<Transform>(true);
            for(int i=0;i<tt.Length;i++)if(IsKnownKerbal(tt[i]))kerbals++;
            return kerbals+" Kerbal(s) • "+rr.Length+" visuel(s) • "+(aa.Length+ar.Length)+" animation(s) • "+cc.Length+" caméra(s)";
        }

        internal void SetWorkspaceRoot(string rootName)
        {
            if(string.IsNullOrEmpty(rootName)||FindSceneRoot(rootName)==null){Status="Menu scene unavailable: "+rootName;return;}
            workspaceRootName=rootName;
Selected=null;RefreshObjects();Status="Scène d'édition : "+rootName;
        }

        private int TransformDepth(Transform t)
        {
            int d=0;while(t!=null){d++;t=t.parent;}return d;
        }

        private string FindBestActiveWorkspaceKey()
        {
            string best=null;int bestDepth=-1;int bestScore=-1000;
            foreach(KeyValuePair<string,Transform> kv in cachedSceneObjects)
            {
                Transform t=kv.Value;if(t==null||!t.gameObject.activeInHierarchy)continue;
                int depth=TransformDepth(t);int score=SceneCandidateScore(t.gameObject);
                if(depth>bestDepth||(depth==bestDepth&&score>bestScore)){best=kv.Key;bestDepth=depth;bestScore=score;}
            }
            return best;
        }

        private void ActivatePreviewHierarchy(Transform wanted)
        {
            if(wanted==null)return;
            foreach(KeyValuePair<string,Transform> kv in cachedSceneObjects)
            {
                Transform t=kv.Value;if(t==null)continue;
                if(!scenePreviewOriginalStates.ContainsKey(t.gameObject))scenePreviewOriginalStates[t.gameObject]=t.gameObject.activeSelf;

                bool ancestorOfWanted=wanted.IsChildOf(t);
                bool wantedAncestor=t.IsChildOf(wanted);
                if(t==wanted||ancestorOfWanted)t.gameObject.SetActive(true);
                else if(!wantedAncestor)t.gameObject.SetActive(false);
            }

            // A nested composition cannot become visible while one of its non-candidate
            // ancestors is inactive.
            Transform p=wanted;
            while(p!=null)
            {
                if(!scenePreviewOriginalStates.ContainsKey(p.gameObject))scenePreviewOriginalStates[p.gameObject]=p.gameObject.activeSelf;
                p.gameObject.SetActive(true);p=p.parent;
            }
        }

        internal void PreviewWorkspaceRoot()
        {
            Transform wanted=FindSceneRoot(workspaceRootName);
            if(wanted==null){Status="Composition indisponible : "+workspaceRootName;return;}
            if(!scenePreviewActive){scenePreviewOriginalStates.Clear();scenePreviewOriginalPoses.Clear();scenePreviewActive=true;}

            MenuVariant variant;
            if(menuVariants.TryGetValue(workspaceRootName,out variant)&&variant!=null)
            {
                // First ensure the canonical root wins against the other canonical root.
                string other=string.Equals(variant.RootKey,"OrbitScene",StringComparison.OrdinalIgnoreCase)?"MunScene":"OrbitScene";
                Transform otherRoot;if(cachedSceneObjects.TryGetValue(other,out otherRoot)&&otherRoot!=null)
                {
                    if(!scenePreviewOriginalStates.ContainsKey(otherRoot.gameObject))scenePreviewOriginalStates[otherRoot.gameObject]=otherRoot.gameObject.activeSelf;
                    otherRoot.gameObject.SetActive(false);
                }
                ApplyVariant(variant);

                Transform liveRoot;
                if(cachedSceneObjects.TryGetValue(variant.RootKey,out liveRoot)&&liveRoot!=null)
                {
                    ResumeSceneAnimations(liveRoot);

                    // Re-enable animation components that may have been inactive when the
                    // snapshot was captured. Do not freeze their transforms afterwards.
                    Animation[] aa=liveRoot.GetComponentsInChildren<Animation>(true);
                    for(int ai=0;ai<aa.Length;ai++)
                    {
                        if(aa[ai]==null)continue;aa[ai].enabled=true;
                        try{if(!aa[ai].isPlaying)aa[ai].Play();}catch{}
                    }
                    Animator[] ar=liveRoot.GetComponentsInChildren<Animator>(true);
                    for(int ai=0;ai<ar.Length;ai++)if(ar[ai]!=null)ar[ai].enabled=true;

                    MonoBehaviour[] mb=liveRoot.GetComponentsInChildren<MonoBehaviour>(true);
                    for(int mi=0;mi<mb.Length;mi++)
                    {
                        MonoBehaviour b=mb[mi];if(b==null)continue;
                        string tn=b.GetType().Name??string.Empty;
                        if(tn.IndexOf("Anim",StringComparison.OrdinalIgnoreCase)>=0||
                           tn.IndexOf("Menu",StringComparison.OrdinalIgnoreCase)>=0)
                            b.enabled=true;
                    }
                }

                Selected=null;RefreshObjects();Status="APERÇU VIVANT : "+workspaceRootName;return;
            }

            // Canonical root preview only.
            string canonical=workspaceRootName;
            string otherCanonical=string.Equals(canonical,"OrbitScene",StringComparison.OrdinalIgnoreCase)?"MunScene":"OrbitScene";
            Transform otherC;if(cachedSceneObjects.TryGetValue(otherCanonical,out otherC)&&otherC!=null)
            {
                if(!scenePreviewOriginalStates.ContainsKey(otherC.gameObject))scenePreviewOriginalStates[otherC.gameObject]=otherC.gameObject.activeSelf;
                otherC.gameObject.SetActive(false);
            }
            if(!scenePreviewOriginalStates.ContainsKey(wanted.gameObject))scenePreviewOriginalStates[wanted.gameObject]=wanted.gameObject.activeSelf;
            wanted.gameObject.SetActive(true);ResumeSceneAnimations(wanted);Selected=null;RefreshObjects();
            Status="APERÇU : "+workspaceRootName+" • "+GetSceneProfile(workspaceRootName);
        }

        internal void EndScenePreview()
        {
            if(scenePreviewActive)
            {
                foreach(KeyValuePair<Transform,VariantPose> kv in scenePreviewOriginalPoses)
                {
                    Transform t=kv.Key;VariantPose p=kv.Value;if(t==null||p==null)continue;
                    t.localPosition=p.LocalPosition;t.localRotation=p.LocalRotation;t.localScale=p.LocalScale;
                }
                foreach(KeyValuePair<GameObject,bool> kv in scenePreviewOriginalStates)if(kv.Key!=null)kv.Key.SetActive(kv.Value);
                scenePreviewOriginalPoses.Clear();scenePreviewOriginalStates.Clear();scenePreviewActive=false;
                Status="Cycle KSP restauré; scène d'édition conservée : "+workspaceRootName;
            }
        }

        internal void RefreshObjects()
        {
            RebuildKerbalRegistry(true);
            sceneEntries.Clear();
            Transform orbit=FindSceneRoot(workspaceRootName);
            MenuVariant workspaceVariant;string workspaceSemantic=menuVariants.TryGetValue(workspaceRootName,out workspaceVariant)&&workspaceVariant!=null?workspaceVariant.RootKey:workspaceRootName;
            if(orbit==null){Status=workspaceRootName+" not found";return;}

            if(string.Equals(workspaceSemantic,"OrbitScene",StringComparison.OrdinalIgnoreCase))
            {
                AddKerbalActor(orbit,"Kerbals/maleEVA_inverted","Kerbal droite / inversé");
                AddKerbalActor(orbit,"Kerbals/maleEVA_side","Kerbal gauche / côté");
                AddKerbalActor(orbit,"Kerbals/maleEVA_center","Kerbal centre");
                AddKerbalActor(orbit,"Kerbals/femaleEVA","Kerbal féminin");
                AddKnownPath(orbit,"Kerbin","PLANETS","Kerbin","Planète décorative principale du menu","Celestial visual");
                AddKnownPath(orbit,"MunPivot/Mun","PLANETS","Mun","Lune décorative du menu","Celestial visual");
            }
            else if(string.Equals(workspaceSemantic,"MunScene",StringComparison.OrdinalIgnoreCase))
            {
                AddKerbalActor(orbit,"Kerbals/maleEVA","Kerbal solitaire de la Mun");
                AddNamedDescendant(orbit,"Mun","PLANETS","Mun (scène lunaire)","Corps décoratif de la scène Mun","Celestial visual");
            }

            else
            {
                for(int i=0;i<orbit.childCount;i++)
                {
                    Transform child=orbit.GetChild(i);if(child==null)continue;
                    Renderer[] rr=child.GetComponentsInChildren<Renderer>(true);
                    Camera[] cc=child.GetComponentsInChildren<Camera>(true);
                    if((rr!=null&&rr.Length>0)||(cc!=null&&cc.Length>0))
                        AddEntry(child,"DECOR","Décor : "+child.name,"Élément visuel de "+workspaceRootName,"Scene visual");
                }
            }

            // Branding/menu visuals: expose only obvious visual labels/logos, not the whole UI hierarchy.
            AddBrandingAndMenuVisuals();
            AddMainMenuUiTargets();

            // Cameras and lights are inherently editable scene controls; keep only those under OrbitScene.
            Camera[] cams = orbit.GetComponentsInChildren<Camera>(true);
            for (int i=0;i<cams.Length;i++) if (cams[i]!=null) AddEntry(cams[i].transform,"CAMERAS",
                cams[i].name.IndexOf("landscape",StringComparison.OrdinalIgnoreCase)>=0 ? "Caméra principale du décor" : "Caméra : "+cams[i].name,
                cams[i].enabled ? "Caméra active de la scène" : "Caméra secondaire", "Camera");
            Light[] lights = orbit.GetComponentsInChildren<Light>(true);
            for (int i=0;i<lights.Length;i++) if (lights[i]!=null) AddEntry(lights[i].transform,"LIGHTS",
                "Lumière : "+lights[i].name, "Éclairage "+lights[i].type+" | intensité "+lights[i].intensity.ToString("0.00"), "Light/"+lights[i].type);

            // Editor-created content is always safe to expose.
            for (int i=0;i<created.Count;i++) if (created[i]!=null) AddEntry(created[i].transform,"IMPORTS / CREATED",
                created[i].name.StartsWith("KSE_CRAFT_",StringComparison.OrdinalIgnoreCase) ? "Craft importé : "+created[i].name.Substring(10) : created[i].name.StartsWith("KSE_PLANET_",StringComparison.OrdinalIgnoreCase) ? "Planète ajoutée : "+created[i].name.Substring(11) : created[i].name.StartsWith("KSE_TEXT_",StringComparison.OrdinalIgnoreCase) ? "Texte ajouté : "+created[i].name.Substring(9) : created[i].name.StartsWith("KSE_IMAGE_",StringComparison.OrdinalIgnoreCase) ? "Image ajoutée : "+created[i].name.Substring(10) : "Objet ajouté : "+created[i].name,
                "Objet créé par KSP Scene Editor", created[i].name.StartsWith("KSE_CRAFT_",StringComparison.OrdinalIgnoreCase)?"Imported craft":created[i].name.StartsWith("KSE_PLANET_",StringComparison.OrdinalIgnoreCase)?"Added planet":created[i].name.StartsWith("KSE_TEXT_",StringComparison.OrdinalIgnoreCase)?"Added text":created[i].name.StartsWith("KSE_IMAGE_",StringComparison.OrdinalIgnoreCase)?"Added image":"Editor object");

            // A few top-level OrbitScene children are useful decoration groups, but never expose their thousands of descendants.
            for (int i=0;i<orbit.childCount;i++)
            {
                Transform c=orbit.GetChild(i); if(c==null)continue;
                string n=c.name??string.Empty;
                if(string.Equals(n,"Kerbals",StringComparison.OrdinalIgnoreCase))continue;
                if(ContainsEntry(c))continue;
                Renderer direct=c.GetComponent<Renderer>(); Renderer[] rr=c.GetComponentsInChildren<Renderer>(true);
                if(direct!=null || (rr!=null && rr.Length>0 && rr.Length<80))
                    AddEntry(c,"DECOR", "Décor : "+n, "Groupe visuel de premier niveau dans OrbitScene", "Decor group");
            }

            // Keep user-discovered visuals available after Refresh/Undo/Redo.
            for(int i=0;i<discoveredVisuals.Count;i++)
            {
                Transform d=discoveredVisuals[i];if(d==null||ContainsEntry(d))continue;
                string low=(d.name??string.Empty).ToLowerInvariant();
                bool branding=low.Contains("logo")||low.Contains("title")||low.Contains("banner")||low.Contains("emblem")||d.GetComponent<TextMesh>()!=null;
                AddEntry(d,branding?"BRANDING / MENU":"DISCOVERED VISUALS",branding?"Visuel menu : "+d.name:"Visuel découvert : "+d.name,
                    "Objet visuel sélectionné directement dans la scène",branding?"Branding visual":"Discovered visual");
            }

            sceneEntries.Sort(delegate(SceneEntry a, SceneEntry b){ int c=string.Compare(a.Category,b.Category,StringComparison.OrdinalIgnoreCase); return c!=0?c:string.Compare(a.FriendlyName,b.FriendlyName,StringComparison.OrdinalIgnoreCase); });
            Status = "Ready: " + sceneEntries.Count + " editable scene targets";
        }

        private void AddMainMenuUiTargets()
        {
            try
            {
                GameObject main=GameObject.Find("MainMenu");if(main==null)return;
                Transform[] all=main.GetComponentsInChildren<Transform>(true);int added=0;
                for(int i=0;i<all.Length&&added<120;i++)
                {
                    Transform t=all[i];if(t==null||t==main.transform||ContainsEntry(t))continue;
                    Text uiText=t.GetComponent<Text>();
                    TextMesh tm=t.GetComponent<TextMesh>();
                    Renderer rr=t.GetComponent<Renderer>();RectTransform rect=t as RectTransform;
                    if(uiText==null&&tm==null&&rr==null&&rect==null)continue;

                    string n=t.name??string.Empty;
                    if(uiText!=null&&!string.IsNullOrEmpty(uiText.text))
                    {
                        AddEntry(t,"MENU TEXT","Texte menu : "+uiText.text,"Texte UI KSP éditable", "UI Text");added++;continue;
                    }
                    if(tm!=null&&!string.IsNullOrEmpty(tm.text))
                    {
                        AddEntry(t,"MENU TEXT","Texte menu : "+tm.text,"Texte 3D KSP éditable","TextMesh");added++;continue;
                    }

                    bool useful=t.childCount==0||n.IndexOf("stage",StringComparison.OrdinalIgnoreCase)>=0||n.IndexOf("header",StringComparison.OrdinalIgnoreCase)>=0||n.IndexOf("logo",StringComparison.OrdinalIgnoreCase)>=0;
                    if(!useful)continue;
                    AddEntry(t,"MENU UI","Menu : "+n,"Élément du menu KSP modifiable",rect!=null?"RectTransform UI":"Menu renderer");added++;
                }
            }catch(Exception ex){SceneEditorLog.Info("Menu UI scan skipped: "+ex.Message);}
        }

        private void AddBrandingAndMenuVisuals()
        {
            try
            {
                Scene scene=SceneManager.GetActiveScene();
                if(!scene.IsValid())return;
                GameObject[] roots=scene.GetRootGameObjects();
                int added=0;
                for(int r=0;r<roots.Length && added<40;r++)
                {
                    Transform[] all=roots[r].GetComponentsInChildren<Transform>(true);
                    for(int i=0;i<all.Length && added<40;i++)
                    {
                        Transform t=all[i];if(t==null||ContainsEntry(t)||!t.gameObject.activeInHierarchy)continue;
                        string n=(t.name??string.Empty).ToLowerInvariant();
                        bool nameMatch=n.Contains("logo")||n.Contains("title")||n.Contains("branding")||n.Contains("emblem")||n.Contains("banner");
                        TextMesh tm=t.GetComponent<TextMesh>();
                        Renderer rr=t.GetComponent<Renderer>();
                        if(tm==null && !(nameMatch && rr!=null && rr.enabled))continue;
                        string friendly=tm!=null && !string.IsNullOrEmpty(tm.text)?"Texte : "+tm.text:"Visuel : "+(t.name??"branding");
                        AddEntry(t,"BRANDING / MENU",friendly,"Logo, titre ou texte visuel modifiable du menu",tm!=null?"Menu text":"Branding visual");
                        added++;
                    }
                }
            }catch(Exception ex){SceneEditorLog.Info("Branding scan skipped: "+ex.Message);}
        }

        private Transform DiscoverVisualAtMouse(Camera cam, Vector3 mouse)
        {
            try
            {
                Scene scene=SceneManager.GetActiveScene();if(!scene.IsValid())return null;
                Vector2 m=new Vector2(mouse.x,Screen.height-mouse.y);
                GameObject[] roots=scene.GetRootGameObjects();
                Renderer bestRenderer=null;Rect bestRect=new Rect();float bestScore=float.MaxValue;
                for(int r=0;r<roots.Length;r++)
                {
                    Renderer[] rs=roots[r].GetComponentsInChildren<Renderer>(true);
                    for(int i=0;i<rs.Length;i++)
                    {
                        Renderer ren=rs[i];if(ren==null||!ren.enabled||!ren.gameObject.activeInHierarchy)continue;
                        string low=(ren.name??string.Empty).ToLowerInvariant();
                        if(low.Contains("skybox")||low.Contains("stars")||low.Contains("galaxy")||low.Contains("scaledspace"))continue;
                        Rect rect;if(!TryProjectBounds(cam,ren.bounds,out rect)||!rect.Contains(m))continue;
                        float screenArea=rect.width*rect.height;
                        if(screenArea>Screen.width*Screen.height*0.65f)continue; // reject backgrounds / sky
                        float score=Mathf.Max(1f,screenArea)+(rect.center-m).sqrMagnitude*0.20f;
                        if(score<bestScore){bestScore=score;bestRenderer=ren;bestRect=rect;}
                    }
                }
                if(bestRenderer==null)return null;
                Transform t=ChooseEditableVisualRoot(bestRenderer.transform);
                if(t==null)return null;
                if(!ContainsEntry(t))
                {
                    string low=(t.name??string.Empty).ToLowerInvariant();
                    bool branding=low.Contains("logo")||low.Contains("title")||low.Contains("banner")||low.Contains("emblem")||t.GetComponent<TextMesh>()!=null;
                    AddEntry(t,branding?"BRANDING / MENU":"DISCOVERED VISUALS",branding?"Visuel menu : "+t.name:"Visuel découvert : "+t.name,
                        "Objet visuel sélectionné directement dans la scène",branding?"Branding visual":"Discovered visual");
                    if(!discoveredVisuals.Contains(t))discoveredVisuals.Add(t);
                    Status="Discovered editable visual: "+t.name;
                }
                return t;
            }catch(Exception ex){Status="Visual discovery failed: "+ex.Message;return null;}
        }

        private Transform ChooseEditableVisualRoot(Transform hit)
        {
            if(hit==null)return null;
            Transform registered=FindRegisteredTarget(hit);if(registered!=null)return registered;
            Transform cur=hit;Transform candidate=hit;int climb=0;
            while(cur.parent!=null && climb<6)
            {
                Transform p=cur.parent;string pn=(p.name??string.Empty).ToLowerInvariant();
                if(pn=="orbitscene"||pn=="kerbals")break;
                int renderCount=p.GetComponentsInChildren<Renderer>(true).Length;
                if(renderCount>0 && renderCount<=25)candidate=p;else break;
                if(pn.Contains("logo")||pn.Contains("title")||pn.Contains("banner")||pn.Contains("emblem")){candidate=p;break;}
                cur=p;climb++;
            }
            return candidate;
        }

        private bool ContainsEntry(Transform t){for(int i=0;i<sceneEntries.Count;i++)if(sceneEntries[i]!=null&&sceneEntries[i].Transform==t)return true;return false;}
        private void AddKerbalActor(Transform orbit,string relative,string friendly)
        {
            Transform actor=orbit.Find(relative);
            if(actor==null)
            {
                string actorName=relative;int slash=actorName.LastIndexOf('/');if(slash>=0)actorName=actorName.Substring(slash+1);
                Transform[] all=orbit.GetComponentsInChildren<Transform>(true);
                for(int i=0;i<all.Length;i++)if(all[i]!=null&&string.Equals(all[i].name,actorName,StringComparison.OrdinalIgnoreCase)){actor=all[i];break;}
            }
            if(actor==null)return;
            Transform proxy;
            if(kerbalActorToPivot.TryGetValue(actor,out proxy)&&proxy!=null)
                AddEntry(proxy,"KERBALS",friendly,"Personnage édité — proxy indépendant","Kerbal pivot");
            else
                // IMPORTANT: discovery never touches the live KSP actor. It keeps every stock
                // animation running until the player actually starts an edit gesture.
                AddEntry(actor,"KERBALS",friendly,"Personnage KSP stock — animation intact jusqu'au premier edit","Kerbal actor");
        }

        private void AddKnownPath(Transform orbit,string relative,string category,string friendly,string role,string kind){Transform t=orbit.Find(relative);if(t!=null)AddEntry(t,category,friendly,role,kind);}
        private void AddNamedDescendant(Transform orbit,string exact,string category,string friendly,string role,string kind)
        {
            Transform[] all=orbit.GetComponentsInChildren<Transform>(true);for(int i=0;i<all.Length;i++)if(all[i]!=null&&string.Equals(all[i].name,exact,StringComparison.OrdinalIgnoreCase)){AddEntry(all[i],category,friendly,role,kind);return;}
        }
        private void AddEntry(Transform t,string category,string friendly,string role,string kind)
        {
            if(t==null||ContainsEntry(t))return; string path=ScenePath.Get(t); string parent=t.parent!=null?(t.parent.name??"ROOT"):"ROOT";
            sceneEntries.Add(new SceneEntry{Transform=t,Name=t.name??"<unnamed>",Path=path,Category=category,FriendlyName=friendly,Role=role,Components=GetComponentSummary(t),Essential=true,Kind=kind,ParentName=parent,Display=friendly+" <"+kind+">"});
        }

        private string GetComponentSummary(Transform t)
        {
            try { Component[] comps=t.GetComponents<Component>(); List<string> names=new List<string>(); for(int i=0;i<comps.Length;i++){Component c=comps[i];if(c==null)continue;string cn=c.GetType().Name;if(cn=="Transform")continue;if(!names.Contains(cn))names.Add(cn);if(names.Count>=8)break;} return names.Count==0?"Transform":string.Join(", ",names.ToArray()); }
            catch{return "Unavailable";}
        }

        internal void Select(Transform t) { Selected=t; }
        internal void MarkEdited(Transform t)
        {
            if(t==null)return;
            edited.Add(t);
            try
            {
                ForceRefreshNativeMainMenuState();
                string key=BuildContextKey(nativeAreaIndex,nativeStageIndex,nativeSandcastleActive);
                if(!string.IsNullOrEmpty(key)&&!key.StartsWith("UNKNOWN",StringComparison.OrdinalIgnoreCase))
                    editedContextOwners[t]=key;
            }
            catch{}
        }

        internal void MarkEditedForContext(Transform t,string key)
        {
            if(t==null)return;
            edited.Add(t);
            if(!string.IsNullOrEmpty(key))editedContextOwners[t]=key;
        }

        private string SafeCurrentContextKey()
        {
            try
            {
                ForceRefreshNativeMainMenuState();
                return BuildContextKey(nativeAreaIndex,nativeStageIndex,nativeSandcastleActive);
            }
            catch{return string.Empty;}
        }

        private void AssignCreatedContext(GameObject go)
        {
            if(go==null)return;
            string key=SafeCurrentContextKey();
            if(!string.IsNullOrEmpty(key))createdContextOwners[go]=key;
        }

        internal void MarkCreatedForContext(GameObject go,string key)
        {
            if(go==null)return;
            if(!created.Contains(go))created.Add(go);
            if(!string.IsNullOrEmpty(key))createdContextOwners[go]=key;
        }

        private bool CreatedBelongsToContext(GameObject go,string key)
        {
            if(go==null||string.IsNullOrEmpty(key))return false;
            string owner;
            if(createdContextOwners.TryGetValue(go,out owner))
                return string.Equals(owner,key,StringComparison.OrdinalIgnoreCase);
            return InSpecificContext(go.transform,key);
        }

        internal bool IsLocked(Transform t){return t!=null&&locked.Contains(ScenePath.Get(t));}
        internal bool IsFavourite(Transform t){return t!=null&&favourites.Contains(ScenePath.Get(t));}
        internal void ToggleLock(Transform t){if(t==null)return;string p=ScenePath.Get(t);if(locked.Contains(p))locked.Remove(p);else locked.Add(p);Status=IsLocked(t)?"Object locked":"Object unlocked";}
        internal void ToggleFavourite(Transform t){if(t==null)return;string p=ScenePath.Get(t);if(favourites.Contains(p))favourites.Remove(p);else favourites.Add(p);Status=IsFavourite(t)?"Added to favourites":"Removed from favourites";}
        internal void BeginEdit(Transform t){if(t==null)return;if(IsLocked(t)){Status="Objet verrouillé";return;}history.Capture(t);MarkEdited(t);}
        internal bool CanEdit(Transform t){if(t==null)return false;if(IsLocked(t)){Status="Objet verrouillé";return false;}return true;}
        internal void Undo(){if(history.Undo()){SyncForcedKerbalPositions();Status="Undo";RefreshObjects();}else Status="Rien à annuler";}
        internal void Redo(){if(history.Redo()){SyncForcedKerbalPositions();Status="Redo";RefreshObjects();}else Status="Rien à rétablir";}
        private void SyncForcedKerbalPositions()
        {
            // V0.4 pivots are ordinary transforms and history restores them directly.
        }
        internal void RestoreSelected()
        {
            if(Selected==null){Status="No object selected";return;}
            if(IsKnownKerbal(Selected)&&RestoreKerbalLiveOffset(Selected))
            {
                Status="Kerbal restauré — parent et animation KSP inchangés";return;
            }
            if(IsKerbalPivot(Selected))
            {
                Transform proxy=Selected;Transform actor=KerbalActorFromPivot(proxy);
                kerbalPivotToActor.Remove(proxy);
                if(actor!=null){kerbalActorToPivot.Remove(actor);RestoreKerbalActorFromPivot(actor,proxy);}
                activeKerbalProxies.Remove(proxy);
                Selected=null;if(proxy!=null)UnityEngine.Object.Destroy(proxy.gameObject);
                RefreshObjects();Status="Kerbal restauré : acteur KSP vivant";return;
            }
            if(created.Contains(Selected.gameObject)){Status="Created objects have no original baseline";return;}
            history.Capture(Selected);RestoreRendererTextures(Selected);RestoreSelectedTextState();
            if(baseline.RestoreOne(Selected)){edited.Remove(Selected);editedContextOwners.Remove(Selected);Status="Selected object restored";}else Status="Selected object not found in baseline";
        }
        internal void SelectParent(){if(Selected!=null&&Selected.parent!=null){Selected=Selected.parent;Status="Selected parent: "+Selected.name;}}
        internal void SelectFirstChild(){if(Selected!=null&&Selected.childCount>0){Selected=Selected.GetChild(0);Status="Selected first child: "+Selected.name;}}

        internal GameObject SpawnCraft(string fileName, bool forceStowed)
        {
            try
            {
                int mounted,missing;Transform parent=FindPreferredSceneRoot();
                GameObject go=CraftVisualLoader.Load(fileName,parent,forceStowed,out mounted,out missing);
                Camera cam=FindLandscapeCamera();
                if(cam!=null)
                {
                    go.transform.position=cam.transform.position+cam.transform.forward*18f;
                    go.transform.rotation=Quaternion.LookRotation(cam.transform.forward,cam.transform.up);
                    AutoFitVisual(go.transform,cam,0.28f);
                }
                created.Add(go);AssignCreatedContext(go);
                SpawnedCraft sc=new SpawnedCraft{FileName=fileName,ForceStowed=forceStowed,Root=go};
                sc.Controls=CraftVisualLoader.DiscoverControls(go);
                if(forceStowed)CraftVisualLoader.SetAllControls(sc.Controls,false);
                spawnedCrafts.Add(sc);
                Selected=go.transform;RefreshObjects();
                Status="Craft added at 28% screen height. Use DEPTH - / + to place it in front or behind.";
                return go;
            }
            catch(Exception ex){Status="Craft load failed: "+ex.Message;SceneEditorLog.Warn(Status);return null;}
        }

        private SpawnedCraft SelectedCraft()
        {
            if(Selected==null)return null;
            for(int i=0;i<spawnedCrafts.Count;i++)
            {
                SpawnedCraft sc=spawnedCrafts[i];if(sc==null||sc.Root==null)continue;
                if(Selected==sc.Root.transform||Selected.IsChildOf(sc.Root.transform))return sc;
            }
            return null;
        }

        internal bool HasSelectedCraft { get { return SelectedCraft()!=null; } }
        internal int SelectedCraftControlCount { get { SpawnedCraft sc=SelectedCraft();return sc!=null&&sc.Controls!=null?sc.Controls.Count:0; } }
        internal string GetSelectedCraftControlLabel(int index)
        {
            SpawnedCraft sc=SelectedCraft();if(sc==null||sc.Controls==null||index<0||index>=sc.Controls.Count)return string.Empty;
            return sc.Controls[index].Label+(sc.Controls[index].Open?"  [OUVERT]":"  [FERMÉ]");
        }
        internal bool GetSelectedCraftControlOpen(int index)
        {
            SpawnedCraft sc=SelectedCraft();return sc!=null&&sc.Controls!=null&&index>=0&&index<sc.Controls.Count&&sc.Controls[index].Open;
        }
        internal void InvertSelectedCraftControl(int index)
        {
            SpawnedCraft sc=SelectedCraft();if(sc==null||sc.Controls==null||index<0||index>=sc.Controls.Count)return;
            CraftVisualLoader.InvertControl(sc.Controls[index]);
            Status="Sens inversé : "+sc.Controls[index].Label;
        }

        internal void SetSelectedCraftControl(int index,bool open)
        {
            SpawnedCraft sc=SelectedCraft();if(sc==null||sc.Controls==null||index<0||index>=sc.Controls.Count)return;
            CraftVisualLoader.SetControl(sc.Controls[index],open);Status=(open?"Ouvert : ":"Fermé : ")+sc.Controls[index].Label;
        }
        internal void SetSelectedCraftAll(bool open)
        {
            SpawnedCraft sc=SelectedCraft();if(sc==null){Status="Sélectionnez un craft";return;}
            CraftVisualLoader.SetAllControls(sc.Controls,open);Status=open?"Toutes les animations du craft sont ouvertes":"Toutes les animations du craft sont fermées";
        }

        private void AutoFitVisual(Transform target,Camera cam,float screenHeightFraction)
        {
            if(target==null||cam==null)return;
            for(int pass=0;pass<3;pass++)
            {
                Rect sr;if(!TryGetTargetScreenRect(cam,target,out sr)||sr.height<=1f)break;
                float targetPixels=Screen.height*Mathf.Clamp(screenHeightFraction,0.04f,0.70f);
                float factor=Mathf.Clamp(targetPixels/sr.height,0.01f,25f);
                target.localScale*=factor;
            }
        }


        private void SetLayerRecursiveForEditor(GameObject go,int layer)
        {
            if(go==null)return;go.layer=layer;
            for(int i=0;i<go.transform.childCount;i++)SetLayerRecursiveForEditor(go.transform.GetChild(i).gameObject,layer);
        }

        private Transform FindCelestialScaledVisual(string bodyName)
        {
            try
            {
                CelestialBody body;
                if(!cachedBodyObjects.TryGetValue(bodyName??string.Empty,out body)||body==null)return null;
                Type t=body.GetType();object value=null;
                FieldInfo fi=t.GetField("scaledBody",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
                if(fi!=null)value=fi.GetValue(body);
                if(value==null)
                {
                    PropertyInfo pi=t.GetProperty("scaledBody",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
                    if(pi!=null)value=pi.GetValue(body,null);
                }
                GameObject go=value as GameObject;if(go!=null)return go.transform;
                Transform tr=value as Transform;if(tr!=null)return tr;
                Component cp=value as Component;if(cp!=null)return cp.transform;
            }catch(Exception ex){SceneEditorLog.Warn("Scaled visual lookup "+bodyName+": "+ex.Message);}
            return null;
        }

        internal string[] ListAvailableBodies()
        {
            return cachedBodies??new string[0];
        }

        private List<Texture2D> CollectPlanetTextureCandidates(string bodyName)
        {
            List<Texture2D> result=new List<Texture2D>();
            try
            {
                Transform scaled=FindCelestialScaledVisual(bodyName);if(scaled==null)return result;
                Renderer[] rr=scaled.GetComponentsInChildren<Renderer>(true);
                string[] props={"_MainTex","_ColorMap","_Diffuse","_BaseMap","_PlanetTex","_MainTexture"};
                List<KeyValuePair<Texture2D,long>> scored=new List<KeyValuePair<Texture2D,long>>();

                for(int i=0;i<rr.Length;i++)
                {
                    Renderer r=rr[i];if(r==null)continue;
                    string rn=(r.name??string.Empty).ToLowerInvariant();
                    if(rn.Contains("ring")||rn.Contains("cloud")||rn.Contains("atmos")||rn.Contains("halo")||rn.Contains("aurora")||rn.Contains("corona")||rn.Contains("flare"))continue;
                    Material[] mats=r.sharedMaterials;
                    for(int mi=0;mi<mats.Length;mi++)
                    {
                        Material mat=mats[mi];if(mat==null)continue;
                        string mn=(mat.name??string.Empty).ToLowerInvariant();
                        string shader=mat.shader!=null?(mat.shader.name??string.Empty).ToLowerInvariant():string.Empty;
                        if(mn.Contains("ring")||mn.Contains("cloud")||mn.Contains("atmos")||mn.Contains("halo")||mn.Contains("normal")||mn.Contains("bump")||mn.Contains("corona")||mn.Contains("flare"))continue;
                        for(int p=0;p<props.Length;p++)
                        {
                            if(!mat.HasProperty(props[p]))continue;
                            Texture2D tex=mat.GetTexture(props[p]) as Texture2D;
                            if(tex==null||tex.width<64||tex.height<32)continue;
                            string tn=(tex.name??string.Empty).ToLowerInvariant();
                            if(tn.Contains("normal")||tn.Contains("bump")||tn.Contains("height")||tn.Contains("spec")||tn.Contains("mask")||tn.Contains("detail")||tn.Contains("emiss")||tn.Contains("ring")||tn.Contains("cloud")||tn.Contains("corona")||tn.Contains("flare")||tn.Contains("sunspot"))continue;

                            bool exists=false;for(int x=0;x<scored.Count;x++)if(scored[x].Key==tex){exists=true;break;}
                            if(exists)continue;

                            long score=(long)tex.width*(long)tex.height;
                            if(props[p]=="_MainTex")score+=60000000L;
                            if(props[p]=="_ColorMap"||props[p]=="_Diffuse"||props[p]=="_BaseMap")score+=45000000L;
                            string bn=(bodyName??string.Empty).ToLowerInvariant();
                            if(rn.Contains(bn)||mn.Contains(bn)||tn.Contains(bn))score+=30000000L;
                            if(shader.Contains("scaled")||shader.Contains("planet"))score+=12000000L;
                            float ratio=tex.height>0?(float)tex.width/(float)tex.height:0f;
                            if(ratio>1.7f&&ratio<2.3f)score+=18000000L; // common equirectangular color map
                            scored.Add(new KeyValuePair<Texture2D,long>(tex,score));
                        }
                    }
                }

                // Some planet packs keep the color map outside the scaledBody material.
                // Search the already-cached texture table by body name only when needed.
                if(scored.Count<2&&cachedAllTextures!=null)
                {
                    string bn=(bodyName??string.Empty).ToLowerInvariant().Replace(" ","").Replace("-","").Replace("_","");
                    for(int i=0;i<cachedAllTextures.Length;i++)
                    {
                        Texture2D tex=cachedAllTextures[i];if(tex==null||tex.width<128||tex.height<64)continue;
                        string tn=(tex.name??string.Empty).ToLowerInvariant();
                        string norm=tn.Replace(" ","").Replace("-","").Replace("_","");
                        if(!norm.Contains(bn))continue;
                        if(tn.Contains("normal")||tn.Contains("bump")||tn.Contains("height")||tn.Contains("spec")||tn.Contains("mask")||
                           tn.Contains("detail")||tn.Contains("emiss")||tn.Contains("ring")||tn.Contains("cloud")||tn.Contains("corona")||
                           tn.Contains("flare")||tn.Contains("sunspot"))continue;
                        bool exists=false;for(int x=0;x<scored.Count;x++)if(scored[x].Key==tex){exists=true;break;}
                        if(exists)continue;
                        float ratio=tex.height>0?(float)tex.width/(float)tex.height:0f;
                        long score=(long)tex.width*(long)tex.height+35000000L;
                        if(ratio>1.7f&&ratio<2.3f)score+=25000000L;
                        if(tn.Contains("color")||tn.Contains("diff")||tn.Contains("albedo"))score+=20000000L;
                        scored.Add(new KeyValuePair<Texture2D,long>(tex,score));
                    }
                }

                scored.Sort(delegate(KeyValuePair<Texture2D,long> a,KeyValuePair<Texture2D,long> b){return b.Value.CompareTo(a.Value);});
                for(int i=0;i<scored.Count;i++)result.Add(scored[i].Key);
            }catch(Exception ex){SceneEditorLog.Warn("Planet texture candidates "+bodyName+": "+ex.Message);}
            return result;
        }

        private Texture2D FindLoadedPlanetTexture(string bodyName)
        {
            Texture2D cached;
            if(cachedPlanetTextures.TryGetValue(bodyName??string.Empty,out cached)&&cached!=null)return cached;
            List<Texture2D> candidates=CollectPlanetTextureCandidates(bodyName);
            if(candidates.Count>0){cachedPlanetTextures[bodyName]=candidates[0];return candidates[0];}
            return null;
        }

        private Texture2D LoadPlanetAssetTexture(string bodyName)
        {
            try
            {
                string path=Path.Combine(KSPUtil.ApplicationRootPath,"GameData","KSPSceneEditor","PluginData","Planets",bodyName+".png");
                if(!File.Exists(path))return null;
                byte[] bytes=File.ReadAllBytes(path);Texture2D tex=new Texture2D(2,2,TextureFormat.ARGB32,false);
                if(!ImageConversion.LoadImage(tex,bytes)){UnityEngine.Object.Destroy(tex);return null;}
                tex.wrapMode=TextureWrapMode.Repeat;return tex;
            }catch{return null;}
        }

        private Texture2D BuildPlanetPreviewTexture(string bodyName)
        {
            const int W=256,H=128;
            Texture2D tex=new Texture2D(W,H,TextureFormat.ARGB32,false);
            Color baseA=new Color(0.45f,0.45f,0.45f),baseB=new Color(0.25f,0.25f,0.25f);
            if(bodyName=="Kerbin"){baseA=new Color(0.08f,0.33f,0.70f);baseB=new Color(0.16f,0.55f,0.22f);}
            else if(bodyName=="Mun"){baseA=new Color(0.58f,0.58f,0.55f);baseB=new Color(0.32f,0.32f,0.30f);}
            else if(bodyName=="Minmus"){baseA=new Color(0.55f,0.82f,0.72f);baseB=new Color(0.34f,0.58f,0.50f);}
            else if(bodyName=="Moho"){baseA=new Color(0.48f,0.34f,0.23f);baseB=new Color(0.28f,0.20f,0.15f);}
            else if(bodyName=="Eve"){baseA=new Color(0.54f,0.27f,0.64f);baseB=new Color(0.31f,0.12f,0.39f);}
            else if(bodyName=="Gilly"){baseA=new Color(0.48f,0.43f,0.35f);baseB=new Color(0.29f,0.26f,0.21f);}
            else if(bodyName=="Duna"){baseA=new Color(0.76f,0.30f,0.15f);baseB=new Color(0.44f,0.16f,0.08f);}
            else if(bodyName=="Ike"){baseA=new Color(0.42f,0.42f,0.42f);baseB=new Color(0.22f,0.22f,0.22f);}
            else if(bodyName=="Dres"){baseA=new Color(0.50f,0.48f,0.44f);baseB=new Color(0.28f,0.27f,0.25f);}
            else if(bodyName=="Jool"){baseA=new Color(0.46f,0.72f,0.27f);baseB=new Color(0.24f,0.46f,0.14f);}
            else if(bodyName=="Laythe"){baseA=new Color(0.20f,0.48f,0.72f);baseB=new Color(0.72f,0.74f,0.50f);}
            else if(bodyName=="Vall"){baseA=new Color(0.66f,0.80f,0.86f);baseB=new Color(0.38f,0.58f,0.67f);}
            else if(bodyName=="Tylo"){baseA=new Color(0.66f,0.62f,0.55f);baseB=new Color(0.37f,0.35f,0.31f);}
            else if(bodyName=="Bop"){baseA=new Color(0.42f,0.34f,0.25f);baseB=new Color(0.22f,0.18f,0.14f);}
            else if(bodyName=="Pol"){baseA=new Color(0.70f,0.64f,0.30f);baseB=new Color(0.42f,0.36f,0.15f);}
            else if(bodyName=="Eeloo"){baseA=new Color(0.82f,0.84f,0.86f);baseB=new Color(0.50f,0.54f,0.58f);}

            int seed=Mathf.Abs(bodyName.GetHashCode()%1000);
            for(int y=0;y<H;y++)
            {
                float v=(float)y/(H-1);
                for(int x=0;x<W;x++)
                {
                    float u=(float)x/(W-1);
                    float n=Mathf.PerlinNoise(u*5f+seed*0.013f,v*4f+seed*0.017f);
                    if(bodyName=="Jool")n=0.5f+0.5f*Mathf.Sin(v*38f+n*2f);
                    Color c=Color.Lerp(baseB,baseA,Mathf.Clamp01(n));
                    tex.SetPixel(x,y,c);
                }
            }
            tex.Apply();tex.wrapMode=TextureWrapMode.Repeat;return tex;
        }

        private Renderer FindBestScaledPlanetRenderer(string bodyName)
        {
            Transform scaled=FindCelestialScaledVisual(bodyName);if(scaled==null)return null;
            Renderer[] rr=scaled.GetComponentsInChildren<Renderer>(true);
            Renderer best=null;int bestScore=int.MinValue;
            for(int i=0;i<rr.Length;i++)
            {
                Renderer r=rr[i];if(r==null||!(r is MeshRenderer))continue;
                MeshFilter mf=r.GetComponent<MeshFilter>();if(mf==null||mf.sharedMesh==null)continue;
                string rn=(r.name??string.Empty).ToLowerInvariant();
                if(rn.Contains("atmos")||rn.Contains("cloud")||rn.Contains("ring")||rn.Contains("halo")||rn.Contains("corona")||rn.Contains("flare"))continue;
                Material[] mats=r.sharedMaterials;if(mats==null||mats.Length==0)continue;
                int score=0;
                for(int m=0;m<mats.Length;m++)
                {
                    Material mat=mats[m];if(mat==null)continue;
                    string shader=mat.shader!=null?(mat.shader.name??string.Empty):string.Empty;
                    if(shader.IndexOf("Terrain/Scaled Planet",StringComparison.OrdinalIgnoreCase)>=0)score+=1000;
                    if((mat.name??string.Empty).IndexOf(bodyName??string.Empty,StringComparison.OrdinalIgnoreCase)>=0)score+=120;
                    if(mat.mainTexture!=null)score+=80;
                }
                if(string.Equals(r.name,bodyName,StringComparison.OrdinalIgnoreCase))score+=250;
                if(r.transform==scaled)score+=180;
                if(score>bestScore){bestScore=score;best=r;}
            }
            return best;
        }

        private ConfigNode FindKopernicusBodyNode(ConfigNode node,string bodyName)
        {
            if(node==null)return null;
            if(string.Equals(node.name,"Body",StringComparison.OrdinalIgnoreCase))
            {
                string n=node.GetValue("name");
                if(string.Equals(n,bodyName,StringComparison.OrdinalIgnoreCase))return node;
            }
            ConfigNode[] children=node.GetNodes();
            for(int i=0;i<children.Length;i++)
            {
                ConfigNode found=FindKopernicusBodyNode(children[i],bodyName);
                if(found!=null)return found;
            }
            return null;
        }

        private Texture2D LoadDdsTexture(string file)
        {
            try
            {
                byte[] data=File.ReadAllBytes(file);
                if(data.Length<128||data[0]!=(byte)'D'||data[1]!=(byte)'D'||data[2]!=(byte)'S'||data[3]!=(byte)' ')return null;

                int height=BitConverter.ToInt32(data,12);
                int width=BitConverter.ToInt32(data,16);
                int mipCount=Mathf.Max(1,BitConverter.ToInt32(data,28));
                string fourCC=Encoding.ASCII.GetString(data,84,4);
                TextureFormat format;
                if(fourCC=="DXT1")format=TextureFormat.DXT1;
                else if(fourCC=="DXT5")format=TextureFormat.DXT5;
                else return null;

                int payload=data.Length-128;if(payload<=0)return null;
                byte[] raw=new byte[payload];Buffer.BlockCopy(data,128,raw,0,payload);
                Texture2D tex=new Texture2D(width,height,format,mipCount>1);
                tex.LoadRawTextureData(raw);tex.Apply(false,false);
                tex.name="KSE_DDS_"+Path.GetFileNameWithoutExtension(file);
                tex.wrapMode=TextureWrapMode.Repeat;tex.filterMode=FilterMode.Bilinear;
                return tex;
            }catch(Exception ex){SceneEditorLog.Warn("DDS load "+file+": "+ex.Message);return null;}
        }

        private Texture2D TryLoadTextureFileFromGameData(string configPath)
        {
            if(string.IsNullOrEmpty(configPath))return null;
            try
            {
                string rel=configPath.Trim().Replace('/' , Path.DirectorySeparatorChar).Replace('\\',Path.DirectorySeparatorChar);
                string root=Path.Combine(KSPUtil.ApplicationRootPath,"GameData");
                string[] tries;
                if(Path.HasExtension(rel))tries=new string[]{Path.Combine(root,rel)};
                else tries=new string[]{Path.Combine(root,rel+".dds"),Path.Combine(root,rel+".png"),Path.Combine(root,rel+".jpg"),Path.Combine(root,rel+".jpeg")};

                for(int i=0;i<tries.Length;i++)
                {
                    string f=tries[i];if(!File.Exists(f))continue;
                    string ext=Path.GetExtension(f).ToLowerInvariant();
                    Texture2D tex=null;
                    if(ext==".dds")tex=LoadDdsTexture(f);
                    else
                    {
                        byte[] bytes=File.ReadAllBytes(f);
                        tex=new Texture2D(2,2,TextureFormat.ARGB32,false);
                        if(!ImageConversion.LoadImage(tex,bytes)){UnityEngine.Object.Destroy(tex);tex=null;}
                    }
                    if(tex!=null)
                    {
                        SceneEditorLog.Info("DIRECT PLANET TEXTURE | "+configPath+" -> "+f+" | "+tex.width+"x"+tex.height);
                        return tex;
                    }
                }
            }catch(Exception ex){SceneEditorLog.Warn("Direct texture "+configPath+": "+ex.Message);}
            return null;
        }

        private string FindKopernicusTexturePathFromFiles(string bodyName)
        {
            try
            {
                string gd=Path.Combine(KSPUtil.ApplicationRootPath,"GameData");
                string[] files=Directory.GetFiles(gd,"*.cfg",SearchOption.AllDirectories);
                for(int i=0;i<files.Length;i++)
                {
                    ConfigNode root=null;
                    try{root=ConfigNode.Load(files[i]);}catch{continue;}
                    ConfigNode body=FindKopernicusBodyNode(root,bodyName);if(body==null)continue;
                    ConfigNode scaled=body.GetNode("ScaledVersion");if(scaled==null)continue;
                    ConfigNode mat=scaled.GetNode("Material");ConfigNode od=scaled.GetNode("OnDemand");
                    string[] keys={"texture","mainTex","mainTexture","colorMap","albedo"};
                    for(int k=0;k<keys.Length;k++)
                    {
                        string p=mat!=null?mat.GetValue(keys[k]):null;
                        if(string.IsNullOrEmpty(p)&&od!=null)p=od.GetValue(keys[k]);
                        if(!string.IsNullOrEmpty(p))return p.Trim();
                    }
                }
            }catch(Exception ex){SceneEditorLog.Warn("Kopernicus cfg scan "+bodyName+": "+ex.Message);}
            return null;
        }

        private Texture2D TryLoadKopernicusColorTexture(string bodyName)
        {
            try
            {
                GameDatabase db=GameDatabase.Instance;if(db==null)return null;
                MethodInfo getNodes=db.GetType().GetMethod("GetConfigNodes",new Type[]{typeof(string)});
                if(getNodes==null)return null;
                ConfigNode[] roots=getNodes.Invoke(db,new object[]{"Kopernicus"}) as ConfigNode[];
                if(roots==null)return null;

                ConfigNode body=null;
                for(int i=0;i<roots.Length&&body==null;i++)body=FindKopernicusBodyNode(roots[i],bodyName);
                if(body==null)return null;
                ConfigNode scaled=body.GetNode("ScaledVersion");if(scaled==null)return null;
                ConfigNode material=scaled.GetNode("Material");
                ConfigNode ondemand=scaled.GetNode("OnDemand");

                string path=null;
                string[] keys={"texture","mainTex","mainTexture","colorMap","albedo"};
                for(int k=0;k<keys.Length&&string.IsNullOrEmpty(path);k++)
                {
                    if(material!=null)path=material.GetValue(keys[k]);
                    if(string.IsNullOrEmpty(path)&&ondemand!=null)path=ondemand.GetValue(keys[k]);
                }
                if(string.IsNullOrEmpty(path))return null;
                path=path.Trim();

                MethodInfo getTexture=db.GetType().GetMethod("GetTexture",new Type[]{typeof(string),typeof(bool)});
                if(getTexture!=null)
                {
                    Texture2D tex=getTexture.Invoke(db,new object[]{path,false}) as Texture2D;
                    if(tex!=null)
                    {
                        SceneEditorLog.Info("KOPERNICUS CONFIG TEXTURE | body="+bodyName+" | path="+path+" | tex="+tex.name);
                        return tex;
                    }
                }

                // GameDatabase URLs omit file extensions in most configs; try the already
                // cached Unity texture table as a final non-I/O fallback.
                string leaf=Path.GetFileName(path).ToLowerInvariant();
                for(int i=0;i<cachedAllTextures.Length;i++)
                {
                    Texture2D t=cachedAllTextures[i];if(t==null)continue;
                    string tn=(t.name??string.Empty).ToLowerInvariant();
                    if(tn==leaf||tn.StartsWith(leaf,StringComparison.OrdinalIgnoreCase))
                    {
                        SceneEditorLog.Info("KOPERNICUS CACHE TEXTURE | body="+bodyName+" | path="+path+" | tex="+t.name);
                        return t;
                    }
                }

                Texture2D direct=TryLoadTextureFileFromGameData(path);
                if(direct!=null)return direct;
            }
            catch(Exception ex){SceneEditorLog.Warn("Kopernicus config texture "+bodyName+": "+ex.Message);}

            string filePath=FindKopernicusTexturePathFromFiles(bodyName);
            if(!string.IsNullOrEmpty(filePath))
            {
                Texture2D direct=TryLoadTextureFileFromGameData(filePath);
                if(direct!=null)return direct;
            }
            return null;
        }

        private bool MaterialHasColorTexture(Material mat)
        {
            if(mat==null)return false;
            string[] props={"_MainTex","_ColorMap","_Diffuse","_BaseMap","_PlanetTex","_MainTexture"};
            for(int i=0;i<props.Length;i++)if(mat.HasProperty(props[i])&&mat.GetTexture(props[i])!=null)return true;
            return mat.mainTexture!=null;
        }

        private void ApplyRecoveredColorTexture(Material mat,Texture2D tex)
        {
            if(mat==null||tex==null)return;
            string[] props={"_MainTex","_ColorMap","_Diffuse","_BaseMap","_PlanetTex","_MainTexture"};
            bool set=false;
            for(int i=0;i<props.Length;i++)
            {
                if(!mat.HasProperty(props[i]))continue;
                if(mat.GetTexture(props[i])==null){mat.SetTexture(props[i],tex);set=true;}
            }
            if(!set&&mat.mainTexture==null)mat.mainTexture=tex;
        }

        private GameObject CreateNativeScaledPlanetVisual(string bodyName,out float localDiameter)
        {
            localDiameter=0f;
            try
            {
                Renderer source=FindBestScaledPlanetRenderer(bodyName);if(source==null)return null;
                MeshFilter sourceMf=source.GetComponent<MeshFilter>();if(sourceMf==null||sourceMf.sharedMesh==null)return null;

                GameObject go=new GameObject("Visual_"+bodyName+"_Scaled");
                MeshFilter mf=go.AddComponent<MeshFilter>();mf.sharedMesh=sourceMf.sharedMesh;
                MeshRenderer mr=go.AddComponent<MeshRenderer>();

                Material[] src=source.sharedMaterials;
                Material[] finalMats=new Material[src.Length];
                Texture2D recovered=null;

                // If the live ScaledSpace material has no usable color texture, recover the
                // path from Kopernicus and load the actual file (including DDS DXT1/DXT5).
                bool needsRecovery=false;
                for(int i=0;i<src.Length;i++)if(src[i]!=null&&!MaterialHasColorTexture(src[i]))needsRecovery=true;
                if(needsRecovery)recovered=TryLoadKopernicusColorTexture(bodyName);

                for(int i=0;i<src.Length;i++)
                {
                    Material sm=src[i];if(sm==null){finalMats[i]=null;continue;}
                    if(recovered!=null)
                    {
                        Material cm=new Material(sm);
                        ApplyRecoveredColorTexture(cm,recovered);
                        finalMats[i]=cm;
                    }
                    else finalMats[i]=sm; // stock and already-loaded mod bodies keep live material
                }
                mr.sharedMaterials=finalMats;mr.enabled=true;

                try
                {
                    MaterialPropertyBlock block=new MaterialPropertyBlock();
                    source.GetPropertyBlock(block);
                    block.SetFloat("_Opacity",1f);
                    mr.SetPropertyBlock(block);
                }catch{}

                Bounds b=sourceMf.sharedMesh.bounds;
                localDiameter=Mathf.Max(b.size.x,Mathf.Max(b.size.y,b.size.z));
                if(localDiameter<=0.0001f)localDiameter=1f;

                SceneEditorLog.Info("PLANET V0.31 | body="+bodyName+" | source="+ScenePath.Get(source.transform)+
                    " | recovered="+(recovered!=null?recovered.name:"LIVE")+" | mats="+finalMats.Length);
                return go;
            }
            catch(Exception ex){SceneEditorLog.Warn("Native scaled visual "+bodyName+": "+ex.Message);return null;}
        }

        private GameObject CreateProceduralPlanetVisual(string bodyName)
        {
            GameObject sphere=GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name="Visual_"+bodyName;
            Collider c=sphere.GetComponent<Collider>();if(c!=null)UnityEngine.Object.Destroy(c);
            Renderer r=sphere.GetComponent<Renderer>();
            if(r!=null)
            {
                Shader sh=Shader.Find("KSP/Diffuse");if(sh==null)sh=Shader.Find("Diffuse");if(sh==null)sh=Shader.Find("Unlit/Texture");
                Material m=new Material(sh);
                Texture2D asset=FindLoadedPlanetTexture(bodyName);
                string source="ScaledSpace";
                if(asset==null){asset=LoadPlanetAssetTexture(bodyName);source="Internal";}
                if(asset==null){asset=BuildPlanetPreviewTexture(bodyName);source="Generated";}
                m.mainTexture=asset;
                if(m.HasProperty("_Color"))m.color=Color.white;
                r.sharedMaterial=m;r.enabled=true;
                SceneEditorLog.Info("PLANET TEXTURE | "+bodyName+" | "+source+" | "+(asset!=null?asset.name:"<none>"));
            }
            return sphere;
        }

        private int PreferredVisibleLayer(Camera cam)
        {
            if(cam==null)return 0;
            if((cam.cullingMask&1)!=0)return 0;
            for(int layer=0;layer<32;layer++)if((cam.cullingMask&(1<<layer))!=0)return layer;
            return 0;
        }

        internal GameObject AddPlanetClone(string bodyName)
        {
            try
            {
                Transform orbit=FindSceneRoot("OrbitScene");Camera cam=FindLandscapeCamera();
                if(orbit==null||cam==null){Status="OrbitScene ou caméra introuvable";return null;}

                GameObject actor=new GameObject("KSE_PLANET_ACTOR_"+bodyName+"_"+spawnedPlanets.Count);
                actor.transform.SetParent(orbit,false);

                // V0.23.1: use the proven KN/KSP approach at renderer level.
                // We copy the REAL ScaledSpace mesh + complete material/shader into a clean
                // editor GameObject. This preserves Kopernicus/stock texture bindings without
                // cloning the entire scaledBody hierarchy (atmospheres/coronas/scripts).
                float visualLocalDiameter=0f;
                GameObject visual=CreateNativeScaledPlanetVisual(bodyName,out visualLocalDiameter);
                bool nativeVisual=visual!=null;
                if(visual==null)
                {
                    visual=CreateProceduralPlanetVisual(bodyName);
                    visualLocalDiameter=1f;
                }
                visual.name="Visual_"+bodyName;
                visual.transform.SetParent(actor.transform,false);
                visual.transform.localPosition=Vector3.zero;
                visual.transform.localRotation=Quaternion.identity;
                visual.transform.localScale=Vector3.one;
                visual.SetActive(true);

                int layer=PreferredVisibleLayer(cam);
                SetLayerRecursiveForEditor(actor,layer);

                int slot=spawnedPlanets.Count%6;
                float[] xs={0.68f,0.82f,0.62f,0.76f,0.88f,0.70f};
                float[] ys={0.56f,0.46f,0.36f,0.66f,0.60f,0.30f};
                float screenX=xs[slot],screenY=ys[slot];
                float depth=11.5f+0.7f*(spawnedPlanets.Count%4);
                float screenHeight=string.Equals(bodyName,"Jool",StringComparison.OrdinalIgnoreCase)?0.24f:
                                   string.Equals(bodyName,"Kerbin",StringComparison.OrdinalIgnoreCase)?0.20f:0.145f;

                actor.transform.position=cam.ViewportToWorldPoint(new Vector3(screenX,screenY,depth));
                actor.transform.rotation=Quaternion.identity;

                float desiredWorldDiameter=2f*depth*Mathf.Tan(cam.fieldOfView*Mathf.Deg2Rad*0.5f)*screenHeight;
                float scale=desiredWorldDiameter/Mathf.Max(0.0001f,visualLocalDiameter);
                visual.transform.localScale=Vector3.one*scale;

                created.Add(actor);AssignCreatedContext(actor);
                SpawnedPlanet record=new SpawnedPlanet{BodyName=bodyName,Root=actor,UsesLiveScaledMaterials=nativeVisual};
                record.TextureCandidates=nativeVisual?new List<Texture2D>():CollectPlanetTextureCandidates(bodyName);
                Renderer currentRenderer=actor.GetComponentInChildren<Renderer>(true);
                Texture2D currentTex=currentRenderer!=null&&currentRenderer.sharedMaterial!=null?currentRenderer.sharedMaterial.mainTexture as Texture2D:null;
                if(currentTex!=null&&!record.TextureCandidates.Contains(currentTex))record.TextureCandidates.Add(currentTex);
                spawnedPlanets.Add(record);
                Selected=actor.transform;RefreshObjects();
                Status=bodyName+" ajouté • "+(nativeVisual?"rendu ScaledSpace natif":"fallback texturé")+" • slot "+(slot+1);
                SceneEditorLog.Info("PLANET BUILT | body="+bodyName+" | mode="+(nativeVisual?"NATIVE_RENDERER":"FALLBACK")+" | viewport="+screenX.ToString("0.00")+","+screenY.ToString("0.00")+" | depth="+depth.ToString("0.0")+" | localDiameter="+visualLocalDiameter.ToString("0.###")+" | desiredDiameter="+desiredWorldDiameter.ToString("0.###")+" | scale="+scale.ToString("0.######")+" | layer="+layer);
                return actor;
            }
            catch(Exception ex){Status="Ajout planète impossible : "+ex.Message;SceneEditorLog.Warn(Status);return null;}
        }

        private SpawnedPlanet SelectedSpawnedPlanet()
        {
            if(Selected==null)return null;
            for(int i=0;i<spawnedPlanets.Count;i++)
            {
                SpawnedPlanet p=spawnedPlanets[i];if(p==null||p.Root==null)continue;
                if(Selected==p.Root.transform||Selected.IsChildOf(p.Root.transform))return p;
            }
            return null;
        }

        internal bool HasSelectedPlanet { get { return SelectedSpawnedPlanet()!=null; } }

        internal string GetSelectedPlanetNativeMaterialInfo()
        {
            SpawnedPlanet p=SelectedSpawnedPlanet();if(p==null)return "";
            Transform scaled=FindCelestialScaledVisual(p.BodyName);if(scaled==null)return "ScaledSpace absent";
            Renderer r=scaled.GetComponentInChildren<Renderer>(true);if(r==null)return "Renderer absent";
            Material m=r.sharedMaterial;if(m==null)return "Material absent";
            Texture tx=m.mainTexture;
            return "NATIF "+m.name+" | "+(m.shader!=null?m.shader.name:"shader?")+" | "+(tx!=null?tx.name:"texture null");
        }

        internal string GetSelectedPlanetTextureLabel()
        {
            SpawnedPlanet p=SelectedSpawnedPlanet();if(p==null)return "";
            if(p.UsesLiveScaledMaterials)return "SCALEDSPACE / DDS DIRECT";
            if(p.TextureCandidates==null||p.TextureCandidates.Count==0)return "FALLBACK";
            Texture2D t=p.TextureCandidates[Mathf.Clamp(p.TextureIndex,0,p.TextureCandidates.Count-1)];
            return (p.TextureIndex+1)+"/"+p.TextureCandidates.Count+" "+(t!=null?t.name:"<null>");
        }

        internal void CycleSelectedPlanetTexture(int direction)
        {
            SpawnedPlanet p=SelectedSpawnedPlanet();if(p==null)return;
            if(p.UsesLiveScaledMaterials)
            {
                Status="Texture ScaledSpace/DDS de "+p.BodyName+" — sélection manuelle désactivée";
                return;
            }
            if(p.TextureCandidates==null||p.TextureCandidates.Count==0){Status="Aucune autre texture ScaledSpace pour "+p.BodyName;return;}
            p.TextureIndex=(p.TextureIndex+direction+p.TextureCandidates.Count)%p.TextureCandidates.Count;
            Texture2D tex=p.TextureCandidates[p.TextureIndex];
            Renderer r=p.Root.GetComponentInChildren<Renderer>(true);
            if(r!=null&&r.sharedMaterial!=null)r.sharedMaterial.mainTexture=tex;
            Status="Texture "+p.BodyName+" : "+GetSelectedPlanetTextureLabel();
        }

        private TextMesh SelectedTextMesh()
        {
            return Selected!=null?Selected.GetComponentInChildren<TextMesh>(true):null;
        }

        private Text SelectedUiText()
        {
            return Selected!=null?Selected.GetComponentInChildren<Text>(true):null;
        }

        private void CaptureTextStateForTransform(Transform root)
        {
            if(root==null)return;
            TextMesh tm=root.GetComponentInChildren<TextMesh>(true);
            if(tm!=null&&!originalTextMeshStates.ContainsKey(tm))
                originalTextMeshStates[tm]=new TextMeshState{Text=tm.text,CharacterSize=tm.characterSize,LineSpacing=tm.lineSpacing,Color=tm.color,Alignment=tm.alignment,Anchor=tm.anchor,FontStyle=tm.fontStyle,Font=tm.font};
            Text ut=root.GetComponentInChildren<Text>(true);
            if(ut!=null&&!originalUiTextStates.ContainsKey(ut))
                originalUiTextStates[ut]=new UiTextState{Text=ut.text,FontSize=ut.fontSize,LineSpacing=ut.lineSpacing,Color=ut.color,Alignment=ut.alignment,FontStyle=ut.fontStyle,Font=ut.font};
        }

        private void CaptureOriginalTextState()
        {
            CaptureTextStateForTransform(Selected);
        }

        private void RestoreTextStateForTransform(Transform root)
        {
            if(root==null)return;

            TextMesh[] meshes=root.GetComponentsInChildren<TextMesh>(true);
            for(int i=0;i<meshes.Length;i++)
            {
                TextMesh tm=meshes[i];TextMeshState s;
                if(tm==null||!originalTextMeshStates.TryGetValue(tm,out s)||s==null)continue;
                tm.text=s.Text;tm.characterSize=s.CharacterSize;tm.lineSpacing=s.LineSpacing;
                tm.color=s.Color;tm.alignment=s.Alignment;tm.anchor=s.Anchor;tm.fontStyle=s.FontStyle;
                if(s.Font!=null)
                {
                    tm.font=s.Font;Renderer rr=tm.GetComponent<Renderer>();if(rr!=null)rr.sharedMaterial=s.Font.material;
                }
                originalTextMeshStates.Remove(tm);
            }

            Text[] ui=root.GetComponentsInChildren<Text>(true);
            for(int i=0;i<ui.Length;i++)
            {
                Text ut=ui[i];UiTextState s;
                if(ut==null||!originalUiTextStates.TryGetValue(ut,out s)||s==null)continue;
                ut.text=s.Text;ut.fontSize=s.FontSize;ut.lineSpacing=s.LineSpacing;ut.color=s.Color;
                ut.alignment=s.Alignment;ut.fontStyle=s.FontStyle;if(s.Font!=null)ut.font=s.Font;
                originalUiTextStates.Remove(ut);
            }
        }

        private void RestoreSelectedTextState()
        {
            TextMesh tm=SelectedTextMesh();TextMeshState ms;
            if(tm!=null&&originalTextMeshStates.TryGetValue(tm,out ms))
            {
                tm.text=ms.Text;tm.characterSize=ms.CharacterSize;tm.lineSpacing=ms.LineSpacing;tm.color=ms.Color;tm.alignment=ms.Alignment;tm.anchor=ms.Anchor;tm.fontStyle=ms.FontStyle;tm.font=ms.Font;if(tm.font!=null&&tm.GetComponent<Renderer>()!=null)tm.GetComponent<Renderer>().sharedMaterial=tm.font.material;
                originalTextMeshStates.Remove(tm);
            }
            Text ut=SelectedUiText();UiTextState us;
            if(ut!=null&&originalUiTextStates.TryGetValue(ut,out us))
            {
                ut.text=us.Text;ut.fontSize=us.FontSize;ut.lineSpacing=us.LineSpacing;ut.color=us.Color;ut.alignment=us.Alignment;ut.fontStyle=us.FontStyle;ut.font=us.Font;
                originalUiTextStates.Remove(ut);
            }
        }

        private void RestoreAllTextStates()
        {
            foreach(KeyValuePair<TextMesh,TextMeshState> kv in originalTextMeshStates)
            {
                TextMesh tm=kv.Key;TextMeshState s=kv.Value;if(tm==null)continue;
                tm.text=s.Text;tm.characterSize=s.CharacterSize;tm.lineSpacing=s.LineSpacing;tm.color=s.Color;tm.alignment=s.Alignment;tm.anchor=s.Anchor;tm.fontStyle=s.FontStyle;tm.font=s.Font;if(tm.font!=null&&tm.GetComponent<Renderer>()!=null)tm.GetComponent<Renderer>().sharedMaterial=tm.font.material;
            }
            foreach(KeyValuePair<Text,UiTextState> kv in originalUiTextStates)
            {
                Text ut=kv.Key;UiTextState s=kv.Value;if(ut==null)continue;
                ut.text=s.Text;ut.fontSize=s.FontSize;ut.lineSpacing=s.LineSpacing;ut.color=s.Color;ut.alignment=s.Alignment;ut.fontStyle=s.FontStyle;ut.font=s.Font;
            }
            originalTextMeshStates.Clear();originalUiTextStates.Clear();
        }

        private SpawnedText SelectedSpawnedText()
        {
            if(Selected==null)return null;
            for(int i=0;i<spawnedTexts.Count;i++)
            {
                SpawnedText st=spawnedTexts[i];if(st!=null&&st.Root!=null&&(Selected==st.Root.transform||Selected.IsChildOf(st.Root.transform)))return st;
            }
            return null;
        }

        internal bool HasSelectedText
        {
            get { return SelectedTextMesh()!=null||SelectedUiText()!=null; }
        }

        internal string GetSelectedText()
        {
            TextMesh tm=SelectedTextMesh();if(tm!=null)return tm.text;
            Text ut=SelectedUiText();return ut!=null?ut.text:string.Empty;
        }

        internal void SetSelectedText(string value)
        {
            if(Selected==null){Status="Sélectionnez un texte";return;}
            CaptureOriginalTextState();BeginEdit(Selected);
            TextMesh tm=SelectedTextMesh();Text ut=SelectedUiText();
            if(tm!=null)tm.text=value??string.Empty;
            else if(ut!=null)ut.text=value??string.Empty;
            else{Status="L'objet sélectionné n'est pas un texte éditable";return;}
            SpawnedText st=SelectedSpawnedText();if(st!=null)st.Text=value??string.Empty;
            MarkEdited(Selected);RefreshObjects();Status="Texte mis à jour";
        }

        internal float GetSelectedTextSize()
        {
            TextMesh tm=SelectedTextMesh();if(tm!=null)return tm.characterSize;
            Text ut=SelectedUiText();return ut!=null?ut.fontSize:0f;
        }

        internal void SetSelectedTextSize(float size)
        {
            if(Selected==null)return;CaptureOriginalTextState();BeginEdit(Selected);
            TextMesh tm=SelectedTextMesh();Text ut=SelectedUiText();
            if(tm!=null)tm.characterSize=Mathf.Clamp(size,0.01f,2f);
            else if(ut!=null)
            {
                int target=ut.fontSize;
                if(size>ut.fontSize)target++;
                else if(size<ut.fontSize)target--;
                ut.fontSize=Mathf.Clamp(target,8,96);
            }
            MarkEdited(Selected);Status="Taille du texte mise à jour";
        }

        internal Color GetSelectedTextColor()
        {
            TextMesh tm=SelectedTextMesh();if(tm!=null)return tm.color;
            Text ut=SelectedUiText();return ut!=null?ut.color:Color.white;
        }

        internal void SetSelectedTextColor(Color color)
        {
            if(Selected==null)return;CaptureOriginalTextState();BeginEdit(Selected);
            TextMesh tm=SelectedTextMesh();Text ut=SelectedUiText();
            if(tm!=null)tm.color=color;else if(ut!=null)ut.color=color;
            MarkEdited(Selected);Status="Couleur du texte mise à jour";
        }

        internal int GetSelectedTextAlignment()
        {
            TextMesh tm=SelectedTextMesh();if(tm!=null)return tm.alignment==TextAlignment.Left?0:tm.alignment==TextAlignment.Right?2:1;
            Text ut=SelectedUiText();if(ut==null)return 1;
            return (ut.alignment==TextAnchor.UpperLeft||ut.alignment==TextAnchor.MiddleLeft||ut.alignment==TextAnchor.LowerLeft)?0:
                   (ut.alignment==TextAnchor.UpperRight||ut.alignment==TextAnchor.MiddleRight||ut.alignment==TextAnchor.LowerRight)?2:1;
        }

        internal void SetSelectedTextAlignment(int mode)
        {
            if(Selected==null)return;CaptureOriginalTextState();BeginEdit(Selected);
            TextMesh tm=SelectedTextMesh();Text ut=SelectedUiText();
            if(tm!=null){tm.alignment=mode<=0?TextAlignment.Left:mode>=2?TextAlignment.Right:TextAlignment.Center;tm.anchor=mode<=0?TextAnchor.MiddleLeft:mode>=2?TextAnchor.MiddleRight:TextAnchor.MiddleCenter;}
            else if(ut!=null)ut.alignment=mode<=0?TextAnchor.MiddleLeft:mode>=2?TextAnchor.MiddleRight:TextAnchor.MiddleCenter;
            MarkEdited(Selected);Status="Alignement du texte mis à jour";
        }

        internal bool GetSelectedTextBold()
        {
            TextMesh tm=SelectedTextMesh();if(tm!=null)return tm.fontStyle==FontStyle.Bold||tm.fontStyle==FontStyle.BoldAndItalic;
            Text ut=SelectedUiText();return ut!=null&&(ut.fontStyle==FontStyle.Bold||ut.fontStyle==FontStyle.BoldAndItalic);
        }

        internal void SetSelectedTextBold(bool bold)
        {
            if(Selected==null)return;CaptureOriginalTextState();BeginEdit(Selected);
            TextMesh tm=SelectedTextMesh();Text ut=SelectedUiText();
            if(tm!=null)tm.fontStyle=bold?FontStyle.Bold:FontStyle.Normal;else if(ut!=null)ut.fontStyle=bold?FontStyle.Bold:FontStyle.Normal;
            MarkEdited(Selected);Status=bold?"Texte en gras":"Texte normal";
        }

        internal float GetSelectedTextLineSpacing()
        {
            TextMesh tm=SelectedTextMesh();if(tm!=null)return tm.lineSpacing;
            Text ut=SelectedUiText();return ut!=null?ut.lineSpacing:1f;
        }

        internal void SetSelectedTextLineSpacing(float spacing)
        {
            if(Selected==null)return;CaptureOriginalTextState();BeginEdit(Selected);
            TextMesh tm=SelectedTextMesh();Text ut=SelectedUiText();float v=Mathf.Clamp(spacing,0.5f,2f);
            if(tm!=null)tm.lineSpacing=v;else if(ut!=null)ut.lineSpacing=v;
            MarkEdited(Selected);Status="Interligne : "+v.ToString("0.00");
        }

        private string SkyboxRootPath
        {
            get { return Path.Combine(KSPUtil.ApplicationRootPath,"GameData","KSPSceneEditor","PluginData","Skyboxes"); }
        }

        internal string[] ListSkyboxPacks()
        {
            return cachedSkyboxes??new string[0];
        }

        private Texture2D LoadSkyboxFace(string pack,string face)
        {
            try
            {
                string dir=Path.Combine(SkyboxRootPath,pack);
                string[] names={"GalaxyTex_"+face+".png","GalaxyTex_"+face+".jpg","GalaxyTex_"+face+".jpeg",
                                "Skybox_"+face+".png","Skybox_"+face+".jpg","Skybox_"+face+".jpeg"};
                for(int i=0;i<names.Length;i++)
                {
                    string path=Path.Combine(dir,names[i]);if(!File.Exists(path))continue;
                    Texture2D tex=new Texture2D(2,2,TextureFormat.ARGB32,false);
                    if(!ImageConversion.LoadImage(tex,File.ReadAllBytes(path))){UnityEngine.Object.Destroy(tex);continue;}
                    tex.name="KSE_SKYBOX_"+pack+"_"+face;tex.wrapMode=TextureWrapMode.Clamp;
                    loadedSkyboxTextures.Add(tex);return tex;
                }
            }catch{}
            return null;
        }

        private void CaptureSkyboxOriginal()
        {
            if(originalSkyboxMaterial==null&&RenderSettings.skybox!=null)
                originalSkyboxMaterial=new Material(RenderSettings.skybox);
        }

        private bool ApplyUnitySkyboxMaterial(Dictionary<string,Texture2D> faces)
        {
            Material current=RenderSettings.skybox;if(current==null)return false;
            CaptureSkyboxOriginal();
            string[] props={"_FrontTex","_BackTex","_LeftTex","_RightTex","_UpTex","_DownTex"};
            string[] keys={"PositiveZ","NegativeZ","NegativeX","PositiveX","PositiveY","NegativeY"};
            bool any=false;
            for(int i=0;i<props.Length;i++)
            {
                Texture2D tex;if(current.HasProperty(props[i])&&faces.TryGetValue(keys[i],out tex)){current.SetTexture(props[i],tex);any=true;}
            }
            return any;
        }

        private bool ApplyGalaxyMaterials(Dictionary<string,Texture2D> faces)
        {
            bool any=false;Renderer[] rr=Resources.FindObjectsOfTypeAll<Renderer>();
            string[] keys={"PositiveX","NegativeX","PositiveY","NegativeY","PositiveZ","NegativeZ"};
            for(int r=0;r<rr.Length;r++)
            {
                Renderer renderer=rr[r];if(renderer==null||!ScenePath.InLoadedScene(renderer.transform))continue;
                Material[] mats=renderer.materials;
                for(int m=0;m<mats.Length;m++)
                {
                    Material mat=mats[m];if(mat==null)continue;
                    string mn=(mat.name??string.Empty).ToLowerInvariant();
                    string tn=mat.mainTexture!=null?(mat.mainTexture.name??string.Empty).ToLowerInvariant():string.Empty;
                    int face=-1;
                    for(int k=0;k<keys.Length;k++)
                    {
                        string low=keys[k].ToLowerInvariant();
                        if(mn.Contains("galaxytex_"+low)||tn.Contains("galaxytex_"+low)||mn.Contains("skybox_"+low)||tn.Contains("skybox_"+low)){face=k;break;}
                    }
                    if(face<0)continue;
                    if(!originalGalaxyMaterialTextures.ContainsKey(mat))originalGalaxyMaterialTextures[mat]=mat.mainTexture;
                    Texture2D tex;if(faces.TryGetValue(keys[face],out tex)){mat.mainTexture=tex;any=true;}
                }
            }
            return any;
        }

        internal bool ApplySkyboxPack(string pack)
        {
            if(string.IsNullOrEmpty(pack)){Status="Choisissez un pack skybox";return false;}
            string[] keys={"PositiveX","NegativeX","PositiveY","NegativeY","PositiveZ","NegativeZ"};
            Dictionary<string,Texture2D> faces=new Dictionary<string,Texture2D>(StringComparer.OrdinalIgnoreCase);
            for(int i=0;i<keys.Length;i++)
            {
                Texture2D tex=LoadSkyboxFace(pack,keys[i]);
                if(tex==null)
                {
                    Status="Skybox incomplet : face "+keys[i]+" manquante";
                    return false;
                }
                faces[keys[i]]=tex;
            }
            bool material=ApplyUnitySkyboxMaterial(faces);
            bool galaxy=ApplyGalaxyMaterials(faces);
            if(!material&&!galaxy){Status="Aucun renderer/material skybox compatible détecté";return false;}
            activeSkyboxPack=pack;Status="Skybox appliqué : "+pack;return true;
        }

        internal void RestoreOriginalSkybox()
        {
            if(originalSkyboxMaterial!=null)
            {
                RenderSettings.skybox=new Material(originalSkyboxMaterial);
            }
            foreach(KeyValuePair<Material,Texture> kv in originalGalaxyMaterialTextures)if(kv.Key!=null)kv.Key.mainTexture=kv.Value;
            originalGalaxyMaterialTextures.Clear();activeSkyboxPack="";
            for(int i=0;i<loadedSkyboxTextures.Count;i++)if(loadedSkyboxTextures[i]!=null)UnityEngine.Object.Destroy(loadedSkyboxTextures[i]);
            loadedSkyboxTextures.Clear();Status="Skybox original restauré";
        }

        internal string ActiveSkyboxPack { get { return activeSkyboxPack; } }

        internal string[] ListAvailableFonts()
        {
            return cachedFonts??new string[0];
        }

        internal string GetSelectedTextFontName()
        {
            TextMesh tm=SelectedTextMesh();if(tm!=null&&tm.font!=null)return tm.font.name;
            Text ut=SelectedUiText();return ut!=null&&ut.font!=null?ut.font.name:string.Empty;
        }

        internal void SetSelectedTextFont(string fontName)
        {
            if(Selected==null||string.IsNullOrEmpty(fontName))return;
            Font chosen;
            if(!cachedFontObjects.TryGetValue(fontName,out chosen)||chosen==null){Status="Police introuvable dans le cache : "+fontName;return;}

            CaptureOriginalTextState();BeginEdit(Selected);
            TextMesh tm=SelectedTextMesh();Text ut=SelectedUiText();
            if(tm!=null)
            {
                tm.font=chosen;
                Renderer rr=tm.GetComponent<Renderer>();if(rr!=null)rr.sharedMaterial=chosen.material;
            }
            else if(ut!=null)ut.font=chosen;
            else{Status="Aucun texte éditable sélectionné";return;}
            MarkEdited(Selected);Status="Police : "+chosen.name;
        }

        internal GameObject AddTextLabel(string text)
        {
            if(string.IsNullOrEmpty(text))text="KSP Scene";
            try
            {
                string safe=text.Replace("\n"," ").Replace("\r"," ");if(safe.Length>24)safe=safe.Substring(0,24);
                GameObject go=new GameObject("KSE_TEXT_"+safe);
                Transform orbit=FindPreferredSceneRoot();if(orbit!=null)go.transform.SetParent(orbit,true);
                TextMesh tm=go.AddComponent<TextMesh>();tm.text=text;tm.fontSize=64;tm.characterSize=0.08f;tm.anchor=TextAnchor.MiddleCenter;tm.alignment=TextAlignment.Center;tm.color=new Color(0.72f,1f,0.80f,1f);
                Camera cam=FindLandscapeCamera();if(cam!=null){go.transform.position=cam.transform.position+cam.transform.forward*8f;go.transform.rotation=Quaternion.LookRotation(go.transform.position-cam.transform.position,cam.transform.up);}
                created.Add(go);AssignCreatedContext(go);spawnedTexts.Add(new SpawnedText{Text=text,Root=go});Selected=go.transform;RefreshObjects();Status="3D title added";return go;
            }catch(Exception ex){Status="Add text failed: "+ex.Message;return null;}
        }

        private Texture2D LoadUserImageTexture(string imageFile)
        {
            if(string.IsNullOrEmpty(imageFile))return null;
            try
            {
                string path=Path.Combine(KSPUtil.ApplicationRootPath,"GameData","KSPSceneEditor","PluginData","Images",imageFile);
                if(!File.Exists(path))return null;
                byte[] data=File.ReadAllBytes(path);
                Texture2D tex=new Texture2D(2,2,TextureFormat.ARGB32,false);
                if(!ImageConversion.LoadImage(tex,data)){UnityEngine.Object.Destroy(tex);return null;}
                tex.name="KSE_USER_"+Path.GetFileNameWithoutExtension(imageFile);
                tex.wrapMode=TextureWrapMode.Clamp;tex.filterMode=FilterMode.Bilinear;
                return tex;
            }catch{return null;}
        }

        private Mesh CreateDoubleSidedImageQuad()
        {
            Mesh mesh=new Mesh();mesh.name="KSE_DoubleSidedImageQuad";
            mesh.vertices=new Vector3[]
            {
                new Vector3(-0.5f,-0.5f,0f),new Vector3(0.5f,-0.5f,0f),
                new Vector3(0.5f,0.5f,0f),new Vector3(-0.5f,0.5f,0f)
            };
            mesh.uv=new Vector2[]
            {
                new Vector2(0f,0f),new Vector2(1f,0f),new Vector2(1f,1f),new Vector2(0f,1f)
            };
            mesh.triangles=new int[]
            {
                0,1,2,0,2,3, // front
                2,1,0,3,2,0  // back
            };
            mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }

        private Material CreateFreeImageMaterial(Texture2D tex,string label)
        {
            Material mat=null;
            try
            {
                // Reuse the exact material family that already renders the stock KSP logo
                // correctly in this MainMenu camera. This avoids black quads caused by a
                // shader that exists but is not configured for the menu render path.
                Transform logo=FindMainMenuLogo();
                if(logo!=null)
                {
                    Renderer lr=logo.GetComponentInChildren<Renderer>(true);
                    if(lr!=null&&lr.sharedMaterial!=null)mat=new Material(lr.sharedMaterial);
                }
            }catch{}

            if(mat==null)
            {
                Shader sh=Shader.Find("KSP/Scenery/Unlit/Transparent");
                if(sh==null)sh=Shader.Find("Unlit/Transparent");
                if(sh==null)sh=Shader.Find("Unlit/Texture");
                if(sh==null)sh=Shader.Find("KSP/Diffuse");
                if(sh==null)sh=Shader.Find("Diffuse");
                if(sh!=null)mat=new Material(sh);
            }
            if(mat==null)return null;

            mat.name="KSE_IMAGE_MAT_"+label;
            mat.mainTexture=tex;
            string[] textureProps={"_MainTex","_Texture","_BaseMap","_Diffuse","_ColorMap"};
            for(int i=0;i<textureProps.Length;i++)
                if(mat.HasProperty(textureProps[i]))mat.SetTexture(textureProps[i],tex);

            string[] colorProps={"_Color","_TintColor","_EmissiveColor"};
            for(int i=0;i<colorProps.Length;i++)
                if(mat.HasProperty(colorProps[i]))mat.SetColor(colorProps[i],Color.white);

            if(mat.HasProperty("_Cull"))mat.SetInt("_Cull",0);
            if(mat.HasProperty("_ZWrite"))mat.SetInt("_ZWrite",0);
            mat.renderQueue=3001;
            return mat;
        }

        internal GameObject AddFreeImage(string imageFile)
        {
            if(string.IsNullOrEmpty(imageFile)){Status="Choisissez une image";return null;}
            Texture2D tex=LoadUserImageTexture(imageFile);
            if(tex==null){Status="Image impossible à charger : "+imageFile;return null;}
            try
            {
                Camera cam=FindLandscapeCamera();Transform parent=FindPreferredSceneRoot();
                if(parent==null||cam==null){UnityEngine.Object.Destroy(tex);Status="Scène ou caméra introuvable";return null;}

                GameObject go=new GameObject("KSE_IMAGE_"+Path.GetFileNameWithoutExtension(imageFile));
                go.transform.SetParent(parent,true);
                SpriteRenderer sr=go.AddComponent<SpriteRenderer>();
                Sprite sprite=Sprite.Create(tex,new Rect(0f,0f,tex.width,tex.height),new Vector2(0.5f,0.5f),100f,0,SpriteMeshType.FullRect);
                sprite.name="KSE_SPRITE_"+Path.GetFileNameWithoutExtension(imageFile);
                sr.sprite=sprite;sr.color=Color.white;sr.enabled=true;sr.sortingOrder=32000;
                Shader spriteShader=Shader.Find("Sprites/Default");
                if(spriteShader!=null){Material sm=new Material(spriteShader);sm.mainTexture=tex;sm.color=Color.white;sm.renderQueue=3100;sr.sharedMaterial=sm;}

                int layer=PreferredVisibleLayer(cam);SetLayerRecursiveForEditor(go,layer);
                float depth=Mathf.Max(cam.nearClipPlane+0.5f,2.2f);
                go.transform.position=cam.ViewportToWorldPoint(new Vector3(0.66f,0.50f,depth));
                // A SpriteRenderer faces the same direction as the menu camera plane.
                // Using -camera.forward rotated the sprite 180° around Y and mirrored it.
                go.transform.rotation=cam.transform.rotation;

                float desiredHeight=2f*depth*Mathf.Tan(cam.fieldOfView*Mathf.Deg2Rad*0.5f)*0.20f;
                float spriteHeight=Mathf.Max(0.0001f,sprite.bounds.size.y);
                float scale=desiredHeight/spriteHeight;
                go.transform.localScale=Vector3.one*scale;

                created.Add(go);AssignCreatedContext(go);spawnedImages.Add(new SpawnedImage{FileName=imageFile,Root=go});
                Selected=go.transform;RefreshObjects();
                Status="Image libre ajoutée : "+imageFile;
                SceneEditorLog.Info("FREE IMAGE SPRITE | file="+imageFile+" | tex="+tex.width+"x"+tex.height+" | shader="+(sr.sharedMaterial!=null&&sr.sharedMaterial.shader!=null?sr.sharedMaterial.shader.name:"<default>")+" | layer="+layer+" | depth="+depth.ToString("0.00"));
                return go;
            }
            catch(Exception ex){UnityEngine.Object.Destroy(tex);Status="Ajout image impossible : "+ex.Message;return null;}
        }

        internal string[] ListLogoImages()
        {
            return cachedLogos??new string[0];
        }

        private void CaptureOriginalRendererTextures(Renderer rr)
        {
            if(rr==null||originalRendererTextures.ContainsKey(rr))return;
            Material[] mats=rr.materials;Texture[] tex=new Texture[mats.Length];
            for(int i=0;i<mats.Length;i++)tex[i]=(mats[i]!=null&&mats[i].HasProperty("_MainTex"))?mats[i].mainTexture:null;
            originalRendererTextures[rr]=tex;
        }

        private void RestoreRendererTextures(Transform root)
        {
            if(root==null)return;
            visualImageOverrides.Remove(root);
            Renderer[] rs=root.GetComponentsInChildren<Renderer>(true);
            for(int i=0;i<rs.Length;i++)
            {
                Renderer rr=rs[i];Texture[] tex;
                if(rr==null||!originalRendererTextures.TryGetValue(rr,out tex))continue;
                Material[] mats=rr.materials;int n=Mathf.Min(mats.Length,tex.Length);
                for(int m=0;m<n;m++)if(mats[m]!=null&&mats[m].HasProperty("_MainTex"))mats[m].mainTexture=tex[m];
                originalRendererTextures.Remove(rr);
            }
        }

        private void RestoreAllRendererTextures()
        {
            List<KeyValuePair<Renderer,Texture[]>> all=new List<KeyValuePair<Renderer,Texture[]>>(originalRendererTextures);
            originalRendererTextures.Clear();
            for(int i=0;i<all.Count;i++)
            {
                Renderer rr=all[i].Key;Texture[] tex=all[i].Value;if(rr==null)continue;
                Material[] mats=rr.materials;int n=Mathf.Min(mats.Length,tex.Length);
                for(int m=0;m<n;m++)if(mats[m]!=null&&mats[m].HasProperty("_MainTex"))mats[m].mainTexture=tex[m];
            }
        }

        private Transform FindMainMenuLogo()
        {
            try
            {
                GameObject main=GameObject.Find("MainMenu");if(main==null)return null;
                Transform t=main.transform.Find("stage 1/logo");
                if(t!=null)return t;
                Transform[] all=main.GetComponentsInChildren<Transform>(true);
                for(int i=0;i<all.Length;i++)if(all[i]!=null&&string.Equals(all[i].name,"logo",StringComparison.OrdinalIgnoreCase))return all[i];
            }catch{}
            return null;
        }

        internal bool SelectMainMenuLogo()
        {
            Transform logo=FindMainMenuLogo();
            if(logo==null){Status="Logo KSP introuvable";return false;}
            Selected=logo;
            if(!ContainsEntry(logo))AddEntry(logo,"BRANDING / MENU","Visuel : logo","Logo principal KSP","Branding visual");
            Status="Logo KSP sélectionné";return true;
        }

        internal bool IsMainMenuLogoSelected
        {
            get { Transform logo=FindMainMenuLogo();return logo!=null&&Selected==logo; }
        }

        internal bool ApplyImageToMainLogo(string imageFile)
        {
            Transform logo=FindMainMenuLogo();
            if(logo==null){Status="Logo KSP introuvable";return false;}
            Selected=logo;
            return ReplaceSelectedTexture(imageFile);
        }

        internal void ResetMainMenuLogo()
        {
            Transform logo=FindMainMenuLogo();
            if(logo==null){Status="Logo KSP introuvable";return;}
            RestoreRendererTextures(logo);
            RestoreTextStateForTransform(logo);
            baseline.RestoreOne(logo);
            edited.Remove(logo);editedContextOwners.Remove(logo);visualImageOverrides.Remove(logo);Selected=logo;
            Status="Logo KSP original restauré";
        }

        internal bool ReplaceSelectedTexture(string imageFile)
        {
            if(Selected==null){Status="Select the logo or visual first";return false;}
            if(string.IsNullOrEmpty(imageFile)){Status="Choose an image first";return false;}
            try
            {
                string path=Path.Combine(KSPUtil.ApplicationRootPath,"GameData/KSPSceneEditor/PluginData/Images/"+imageFile);
                if(!File.Exists(path)){Status="Image introuvable : "+imageFile;return false;}
                Renderer[] rs=Selected.GetComponentsInChildren<Renderer>(true);if(rs==null||rs.Length==0){Status="Cet élément ne peut pas recevoir d’image";return false;}
                byte[] data=File.ReadAllBytes(path);Texture2D tex=new Texture2D(2,2,TextureFormat.ARGB32,false);
                if(!ImageConversion.LoadImage(tex,data)){Status="Unable to decode image";UnityEngine.Object.Destroy(tex);return false;}
                BeginEdit(Selected);int applied=0;
                for(int i=0;i<rs.Length;i++)
                {
                    Renderer rr=rs[i];if(rr==null)continue;CaptureOriginalRendererTextures(rr);Material[] mats=rr.materials;
                    for(int m=0;m<mats.Length;m++)if(mats[m]!=null && mats[m].HasProperty("_MainTex")){mats[m].mainTexture=tex;applied++;}
                }
                if(applied==0){UnityEngine.Object.Destroy(tex);Status="Ce visuel ne possède pas de texture compatible";return false;}
                MarkEdited(Selected);visualImageOverrides[Selected]=imageFile;Status="Image appliquée : "+imageFile;return true;
            }catch(Exception ex){Status="Application de l’image impossible : "+ex.Message;return false;}
        }
        internal string GetPersistenceHint(Transform t)
        {
            if(t==null)return string.Empty;
            Transform logo=FindMainMenuLogo();
            if(logo!=null&&t==logo)return "MAINMENU_LOGO";

            if(t.GetComponentInChildren<TextMesh>(true)!=null||t.GetComponentInChildren<Text>(true)!=null)
                return "TEXT:"+t.name;

            return "OBJECT:"+t.name;
        }

        internal Transform ResolvePersistedTransform(string path,string hint)
        {
            Transform t=ScenePath.Find(path);
            if(t!=null)return t;

            if(string.Equals(hint,"MAINMENU_LOGO",StringComparison.OrdinalIgnoreCase))
                return FindMainMenuLogo();

            if(!string.IsNullOrEmpty(hint)&&hint.StartsWith("TEXT:",StringComparison.OrdinalIgnoreCase))
            {
                string name=hint.Substring(5);
                GameObject main=GameObject.Find("MainMenu");
                if(main!=null)
                {
                    Transform[] all=main.GetComponentsInChildren<Transform>(true);
                    Transform unique=null;int matches=0;
                    for(int i=0;i<all.Length;i++)
                    {
                        Transform x=all[i];if(x==null||!string.Equals(x.name,name,StringComparison.Ordinal))continue;
                        if(x.GetComponentInChildren<TextMesh>(true)==null&&x.GetComponentInChildren<Text>(true)==null)continue;
                        unique=x;matches++;
                    }
                    if(matches==1)return unique;
                }
            }
            return null;
        }

        internal void WriteVisualOverrides(ConfigNode root,string contextKey)
        {
            if(root==null||string.IsNullOrEmpty(contextKey))return;
            foreach(KeyValuePair<Transform,string> kv in visualImageOverrides)
            {
                Transform t=kv.Key;if(t==null||string.IsNullOrEmpty(kv.Value))continue;
                string owner;
                if(!editedContextOwners.TryGetValue(t,out owner)||!string.Equals(owner,contextKey,StringComparison.OrdinalIgnoreCase))continue;
                ConfigNode n=root.AddNode("VISUAL_OVERRIDE");
                n.AddValue("path",ScenePath.Get(t));
                n.AddValue("hint",GetPersistenceHint(t));
                n.AddValue("file",kv.Value);
            }
        }

        internal void ReadVisualOverrides(ConfigNode root,string contextKey)
        {
            if(root==null)return;
            ConfigNode[] nodes=root.GetNodes("VISUAL_OVERRIDE");
            for(int i=0;i<nodes.Length;i++)
            {
                Transform t=ResolvePersistedTransform(nodes[i].GetValue("path"),nodes[i].GetValue("hint"));
                string file=nodes[i].GetValue("file");
                if(t==null||string.IsNullOrEmpty(file))continue;
                ApplyImageOverrideToTransform(t,file,contextKey);
            }
        }

        private bool ApplyImageOverrideToTransform(Transform target,string imageFile,string contextKey)
        {
            if(target==null||string.IsNullOrEmpty(imageFile))return false;
            try
            {
                string path=Path.Combine(KSPUtil.ApplicationRootPath,"GameData","KSPSceneEditor","PluginData","Images",imageFile);
                if(!File.Exists(path))return false;
                Texture2D tex=new Texture2D(2,2,TextureFormat.ARGB32,false);
                if(!ImageConversion.LoadImage(tex,File.ReadAllBytes(path))){UnityEngine.Object.Destroy(tex);return false;}
                Renderer[] rs=target.GetComponentsInChildren<Renderer>(true);int applied=0;
                for(int i=0;i<rs.Length;i++)
                {
                    Renderer rr=rs[i];if(rr==null)continue;CaptureOriginalRendererTextures(rr);
                    Material[] mats=rr.materials;
                    for(int m=0;m<mats.Length;m++)
                        if(mats[m]!=null&&mats[m].HasProperty("_MainTex")){mats[m].mainTexture=tex;applied++;}
                }
                if(applied<=0){UnityEngine.Object.Destroy(tex);return false;}
                MarkEditedForContext(target,contextKey);
                visualImageOverrides[target]=imageFile;
                return true;
            }catch{return false;}
        }

        internal void WriteTextProperties(ConfigNode node,Transform t)
        {
            if(node==null||t==null)return;
            TextMesh tm=t.GetComponentInChildren<TextMesh>(true);
            Text ut=t.GetComponentInChildren<Text>(true);
            if(tm!=null)
            {
                node.AddValue("textKind","TextMesh");
                node.AddValue("textValue",tm.text??string.Empty);
                node.AddValue("textSize",tm.characterSize.ToString("R",CultureInfo.InvariantCulture));
                node.AddValue("textLineSpacing",tm.lineSpacing.ToString("R",CultureInfo.InvariantCulture));
                node.AddValue("textColor",SerializeColor(tm.color));
                node.AddValue("textAlignment",(int)tm.alignment);
                node.AddValue("textAnchor",(int)tm.anchor);
                node.AddValue("textFontStyle",(int)tm.fontStyle);
                node.AddValue("textFont",tm.font!=null?tm.font.name:string.Empty);
            }
            else if(ut!=null)
            {
                node.AddValue("textKind","UI");
                node.AddValue("textValue",ut.text??string.Empty);
                node.AddValue("textFontSize",ut.fontSize);
                node.AddValue("textLineSpacing",ut.lineSpacing.ToString("R",CultureInfo.InvariantCulture));
                node.AddValue("textColor",SerializeColor(ut.color));
                node.AddValue("textAnchor",(int)ut.alignment);
                node.AddValue("textFontStyle",(int)ut.fontStyle);
                node.AddValue("textFont",ut.font!=null?ut.font.name:string.Empty);
            }
        }

        internal void ReadTextProperties(ConfigNode node,Transform t,string contextKey)
        {
            if(node==null||t==null||string.IsNullOrEmpty(node.GetValue("textKind")))return;
            CaptureTextStateForTransform(t);
            TextMesh tm=t.GetComponentInChildren<TextMesh>(true);
            Text ut=t.GetComponentInChildren<Text>(true);
            float f;int iv;Color c;string fontName=node.GetValue("textFont");

            if(tm!=null)
            {
                string value=node.GetValue("textValue");if(value!=null)tm.text=value;
                if(float.TryParse(node.GetValue("textSize"),NumberStyles.Float,CultureInfo.InvariantCulture,out f))tm.characterSize=f;
                if(float.TryParse(node.GetValue("textLineSpacing"),NumberStyles.Float,CultureInfo.InvariantCulture,out f))tm.lineSpacing=f;
                if(TryDeserializeColor(node.GetValue("textColor"),out c))tm.color=c;
                if(int.TryParse(node.GetValue("textAlignment"),out iv))tm.alignment=(TextAlignment)iv;
                if(int.TryParse(node.GetValue("textAnchor"),out iv))tm.anchor=(TextAnchor)iv;
                if(int.TryParse(node.GetValue("textFontStyle"),out iv))tm.fontStyle=(FontStyle)iv;
                ApplyFontByName(tm,null,fontName);
            }
            else if(ut!=null)
            {
                string value=node.GetValue("textValue");if(value!=null)ut.text=value;
                if(int.TryParse(node.GetValue("textFontSize"),out iv))ut.fontSize=iv;
                if(float.TryParse(node.GetValue("textLineSpacing"),NumberStyles.Float,CultureInfo.InvariantCulture,out f))ut.lineSpacing=f;
                if(TryDeserializeColor(node.GetValue("textColor"),out c))ut.color=c;
                if(int.TryParse(node.GetValue("textAnchor"),out iv))ut.alignment=(TextAnchor)iv;
                if(int.TryParse(node.GetValue("textFontStyle"),out iv))ut.fontStyle=(FontStyle)iv;
                ApplyFontByName(null,ut,fontName);
            }
            MarkEditedForContext(t,contextKey);
        }

        private void ApplyFontByName(TextMesh tm,Text ut,string fontName)
        {
            if(string.IsNullOrEmpty(fontName))return;
            Font font;
            if(!cachedFontObjects.TryGetValue(fontName,out font)||font==null)
            {
                Font[] all=Resources.FindObjectsOfTypeAll<Font>();
                for(int i=0;i<all.Length;i++)if(all[i]!=null&&string.Equals(all[i].name,fontName,StringComparison.OrdinalIgnoreCase)){font=all[i];break;}
            }
            if(font==null)return;
            if(tm!=null){tm.font=font;Renderer rr=tm.GetComponent<Renderer>();if(rr!=null)rr.sharedMaterial=font.material;}
            if(ut!=null)ut.font=font;
        }

        private static string SerializeColor(Color c)
        {
            return c.r.ToString("R",CultureInfo.InvariantCulture)+","+c.g.ToString("R",CultureInfo.InvariantCulture)+","+
                   c.b.ToString("R",CultureInfo.InvariantCulture)+","+c.a.ToString("R",CultureInfo.InvariantCulture);
        }

        private static bool TryDeserializeColor(string s,out Color c)
        {
            c=Color.white;if(string.IsNullOrEmpty(s))return false;
            string[] p=s.Split(',');float r,g,b,a=1f;
            if(p.Length<3||
               !float.TryParse(p[0],NumberStyles.Float,CultureInfo.InvariantCulture,out r)||
               !float.TryParse(p[1],NumberStyles.Float,CultureInfo.InvariantCulture,out g)||
               !float.TryParse(p[2],NumberStyles.Float,CultureInfo.InvariantCulture,out b))return false;
            if(p.Length>3)float.TryParse(p[3],NumberStyles.Float,CultureInfo.InvariantCulture,out a);
            c=new Color(r,g,b,a);return true;
        }

        internal void WriteCraftControlState(ConfigNode craftNode,SpawnedCraft craft)
        {
            if(craftNode==null||craft==null||craft.Controls==null)return;
            for(int i=0;i<craft.Controls.Count;i++)
            {
                CraftVisualLoader.CraftControl cc=craft.Controls[i];if(cc==null)continue;
                ConfigNode n=craftNode.AddNode("CONTROL");
                n.AddValue("animation",cc.AnimationName??string.Empty);
                n.AddValue("open",cc.Open);
                n.AddValue("inverted",cc.Inverted);
            }
        }

        internal void ReadCraftControlState(ConfigNode craftNode,GameObject go)
        {
            if(craftNode==null||go==null)return;
            SpawnedCraft sc=null;
            for(int i=0;i<spawnedCrafts.Count;i++)if(spawnedCrafts[i]!=null&&spawnedCrafts[i].Root==go){sc=spawnedCrafts[i];break;}
            if(sc==null||sc.Controls==null)return;
            ConfigNode[] states=craftNode.GetNodes("CONTROL");
            for(int i=0;i<states.Length;i++)
            {
                string anim=states[i].GetValue("animation");
                for(int j=0;j<sc.Controls.Count;j++)
                {
                    CraftVisualLoader.CraftControl cc=sc.Controls[j];
                    if(cc==null||!string.Equals(cc.AnimationName,anim,StringComparison.OrdinalIgnoreCase))continue;
                    bool inv,open;bool.TryParse(states[i].GetValue("inverted"),out inv);bool.TryParse(states[i].GetValue("open"),out open);
                    if(cc.Inverted!=inv)CraftVisualLoader.InvertControl(cc);
                    CraftVisualLoader.SetControl(cc,open);
                    break;
                }
            }
        }

        internal void RestorePlanetTextureIndex(GameObject go,int index)
        {
            if(go==null)return;
            for(int i=0;i<spawnedPlanets.Count;i++)
            {
                SpawnedPlanet p=spawnedPlanets[i];if(p==null||p.Root!=go)continue;
                if(p.UsesLiveScaledMaterials||p.TextureCandidates==null||p.TextureCandidates.Count==0)return;
                p.TextureIndex=Mathf.Clamp(index,0,p.TextureCandidates.Count-1);
                Renderer r=p.Root.GetComponentInChildren<Renderer>(true);
                Texture2D tex=p.TextureCandidates[p.TextureIndex];
                if(r!=null&&r.sharedMaterial!=null)r.sharedMaterial.mainTexture=tex;
                return;
            }
        }

        internal GameObject AddLight()
        {
            GameObject go=new GameObject("KSE_Light"); Transform parent=FindPreferredSceneRoot(); if(parent!=null)go.transform.SetParent(parent,true);
            Camera cam=FindLandscapeCamera(); if(cam!=null){go.transform.position=cam.transform.position+cam.transform.forward*5f;go.transform.rotation=cam.transform.rotation;}
            Light l=go.AddComponent<Light>();l.type=LightType.Point;l.intensity=1f;l.range=20f;
            created.Add(go);AssignCreatedContext(go);spawnedLights.Add(new SpawnedLight{Root=go});Selected=go.transform;RefreshObjects();Status="Light added";return go;
        }

        internal bool IsSelectedCreated
        {
            get { return Selected!=null&&created.Contains(Selected.gameObject); }
        }

        internal string SelectedCreatedType
        {
            get
            {
                if(Selected==null)return string.Empty;
                for(int i=0;i<spawnedCrafts.Count;i++)if(spawnedCrafts[i]!=null&&spawnedCrafts[i].Root==Selected.gameObject)return "CRAFT";
                for(int i=0;i<spawnedPlanets.Count;i++)if(spawnedPlanets[i]!=null&&spawnedPlanets[i].Root==Selected.gameObject)return "PLANÈTE";
                for(int i=0;i<spawnedLights.Count;i++)if(spawnedLights[i]!=null&&spawnedLights[i].Root==Selected.gameObject)return "LUMIÈRE";
                for(int i=0;i<spawnedTexts.Count;i++)if(spawnedTexts[i]!=null&&spawnedTexts[i].Root==Selected.gameObject)return "TEXTE";
                for(int i=0;i<spawnedImages.Count;i++)if(spawnedImages[i]!=null&&spawnedImages[i].Root==Selected.gameObject)return "IMAGE";
                return created.Contains(Selected.gameObject)?"OBJET":"";
            }
        }

        private void RemoveCreatedTracking(GameObject go)
        {
            if(go==null)return;
            created.Remove(go);
            createdContextOwners.Remove(go);
            for(int i=spawnedCrafts.Count-1;i>=0;i--)if(spawnedCrafts[i]==null||spawnedCrafts[i].Root==null||spawnedCrafts[i].Root==go)spawnedCrafts.RemoveAt(i);
            for(int i=spawnedLights.Count-1;i>=0;i--)if(spawnedLights[i]==null||spawnedLights[i].Root==null||spawnedLights[i].Root==go)spawnedLights.RemoveAt(i);
            for(int i=spawnedPlanets.Count-1;i>=0;i--)if(spawnedPlanets[i]==null||spawnedPlanets[i].Root==null||spawnedPlanets[i].Root==go)spawnedPlanets.RemoveAt(i);
            for(int i=spawnedTexts.Count-1;i>=0;i--)if(spawnedTexts[i]==null||spawnedTexts[i].Root==null||spawnedTexts[i].Root==go)spawnedTexts.RemoveAt(i);
            for(int i=spawnedImages.Count-1;i>=0;i--)if(spawnedImages[i]==null||spawnedImages[i].Root==null||spawnedImages[i].Root==go)spawnedImages.RemoveAt(i);
        }

        internal GameObject DuplicateSelected()
        {
            if(Selected==null)return null;
            try
            {
                GameObject original=Selected.gameObject;
                GameObject clone=(GameObject)UnityEngine.Object.Instantiate(original);
                clone.name="KSE_Copy_"+original.name;
                if(Selected.parent!=null)clone.transform.SetParent(Selected.parent,true);
                created.Add(clone);AssignCreatedContext(clone);

                bool tracked=false;
                for(int i=0;i<spawnedPlanets.Count;i++)if(spawnedPlanets[i]!=null&&spawnedPlanets[i].Root==original)
                {
                    spawnedPlanets.Add(new SpawnedPlanet{BodyName=spawnedPlanets[i].BodyName,Root=clone});tracked=true;break;
                }
                if(!tracked)for(int i=0;i<spawnedCrafts.Count;i++)if(spawnedCrafts[i]!=null&&spawnedCrafts[i].Root==original)
                {
                    SpawnedCraft sc=new SpawnedCraft{FileName=spawnedCrafts[i].FileName,ForceStowed=spawnedCrafts[i].ForceStowed,Root=clone};
                    sc.Controls=CraftVisualLoader.DiscoverControls(clone);spawnedCrafts.Add(sc);tracked=true;break;
                }
                if(!tracked)for(int i=0;i<spawnedLights.Count;i++)if(spawnedLights[i]!=null&&spawnedLights[i].Root==original)
                {spawnedLights.Add(new SpawnedLight{Root=clone});tracked=true;break;}
                if(!tracked)for(int i=0;i<spawnedTexts.Count;i++)if(spawnedTexts[i]!=null&&spawnedTexts[i].Root==original)
                {spawnedTexts.Add(new SpawnedText{Text=spawnedTexts[i].Text,Root=clone});tracked=true;break;}
                if(!tracked)for(int i=0;i<spawnedImages.Count;i++)if(spawnedImages[i]!=null&&spawnedImages[i].Root==original)
                {spawnedImages.Add(new SpawnedImage{FileName=spawnedImages[i].FileName,Root=clone});tracked=true;break;}

                Selected=clone.transform;RefreshObjects();
                Status="Dupliqué : "+clone.name+(tracked?" (comportement conservé)":"");
                return clone;
            }
            catch(Exception ex){Status="Duplication impossible : "+ex.Message;return null;}
        }

        internal void DeleteSelected()
        {
            if(Selected==null)return;
            if(!created.Contains(Selected.gameObject)){Status="Protégé : seuls les objets ajoutés par Scene Editor peuvent être supprimés";return;}
            GameObject go=Selected.gameObject;RemoveCreatedTracking(go);Selected=null;UnityEngine.Object.Destroy(go);RefreshObjects();Status="Objet ajouté supprimé";
        }

        internal void HideSelected()
        {
            if(Selected==null){Status="No object selected";return;} if(!CanEdit(Selected))return;
            BeginEdit(Selected); Selected.gameObject.SetActive(false); MarkEdited(Selected); Status="Object hidden (soft delete). RESTORE SELECTED to bring it back.";
        }

        internal GameObject ImportCraftAtSelected(string fileName,bool forceStowed,bool hideOriginal)
        {
            Transform target=Selected; if(target==null){Status="Select an object first";return null;}
            Vector3 p=target.position; Quaternion q=target.rotation;
            GameObject go=SpawnCraft(fileName,forceStowed); if(go==null)return null;
            go.transform.position=p; go.transform.rotation=q;
            if(hideOriginal && target!=null && !created.Contains(target.gameObject)){history.Capture(target);target.gameObject.SetActive(false);edited.Add(target);}
            Selected=go.transform; Status="Craft imported at selected object"+(hideOriginal?"; original hidden":""); return go;
        }

        internal void LookCameraAtSelected()
        {
            if(Selected==null){Status="No object selected";return;} Camera cam=FindLandscapeCamera(); if(cam==null){Status="No scene camera found";return;}
            history.Capture(cam.transform); cam.transform.LookAt(Selected.position); edited.Add(cam.transform); Status="Camera aimed at selected object";
        }

        internal void SetDirectManipulationEnabled(bool enabled)
        {
            directManipulationEnabled = enabled;
            if (!enabled) EndDirectDrag();
            Status = enabled ? "Direct manipulation ON: click and drag an editable object" : "Direct manipulation OFF";
        }

        internal void SetDirectTool(int tool)
        {
            directTool = Mathf.Clamp(tool, 0, 3);
            EndDirectDrag();
            Status = "Outil : " + DirectToolName + (directTool == 0 ? " | glisser = déplacer" : directTool == 1 ? " | glisser = rotation" : directTool == 2 ? " | glisser = taille" : " | glisser = profondeur caméra");
        }

        private bool IsUnderMainMenu(Transform t)
        {
            Transform cur=t;int guard=0;
            while(cur!=null&&guard++<128){if(string.Equals(cur.name,"MainMenu",StringComparison.OrdinalIgnoreCase))return true;cur=cur.parent;}
            return false;
        }

        private Camera FindCameraForTarget(Transform target)
        {
            if(target==null)return FindLandscapeCamera();
            if(IsUnderMainMenu(target))
            {
                Camera[] cams=Camera.allCameras;Camera best=null;float bestDepth=float.MinValue;
                for(int i=0;i<cams.Length;i++)
                {
                    Camera c=cams[i];if(c==null||!c.enabled)continue;
                    int mask=1<<target.gameObject.layer;if((c.cullingMask&mask)==0)continue;
                    Vector3 sp=c.WorldToScreenPoint(GetVisualAnchor(target));
                    if(sp.z>0f && c.depth>=bestDepth){best=c;bestDepth=c.depth;}
                }
                if(best!=null)return best;
            }
            return FindLandscapeCamera();
        }

        private Transform PickKerbalHandleTarget(Vector3 mouse)
        {
            Vector2 gui=new Vector2(mouse.x,Screen.height-mouse.y);
            List<Transform> actors=GetLiveKerbalActors();
            Transform best=null;float score=float.MaxValue;
            for(int i=0;i<actors.Count;i++)
            {
                Rect h;if(!TryGetKerbalHandleRect(actors[i],out h)||!h.Contains(gui))continue;
                float d=(h.center-gui).sqrMagnitude;
                if(d<score){score=d;best=actors[i];}
            }
            return best;
        }

        private bool MouseIsInsideKerbalGuiZone(Vector2 guiMouse)
        {
            List<Transform> actors=GetLiveKerbalActors();
            for(int i=0;i<actors.Count;i++)
            {
                Rect r;
                if(TryGetKerbalInteractionRect(actors[i],out r)&&r.Contains(guiMouse))
                    return true;
            }
            return false;
        }

        private Transform PickNonKerbalSceneTarget(Vector3 mouse)
        {
            Transform picked=PickEditorInteractionTarget(mouse);
            if(picked!=null&&IsKnownKerbal(picked))return null;
            return picked;
        }

        private Transform PickEditorInteractionTarget(Vector3 mouse)
        {
            Transform handleTarget=PickKerbalHandleTarget(mouse);
            if(handleTarget!=null)return handleTarget;
            Camera primary=FindLandscapeCamera();
            if(primary!=null)
            {
                // Layer 1: editor-owned Kerbal hit areas. Nothing behind them competes.
                Transform k=PickStockKerbalExplicit(primary,mouse);
                if(k!=null)return k;

                // Layer 2: registered/created visual objects.
                Transform p=PickPriorityVisual(primary,mouse);
                if(p!=null)return p;
            }

            Camera[] cams=Camera.allCameras;
            for(int i=0;i<cams.Length;i++)
            {
                Camera c=cams[i];if(c==null||!c.enabled||c==primary)continue;
                Transform k=PickStockKerbalExplicit(c,mouse);if(k!=null)return k;
            }

            // Last resort only: physics/discovery. This prevents giant Kerbin renderers from
            // stealing normal clicks while retaining access to unusual scene objects.
            if(primary!=null)return PickEditableAtMouse(primary,mouse);
            return null;
        }

        private void HandleDirectManipulation()
        {
            if(window==null||!window.Visible)return;

            Vector2 guiMouse=new Vector2(Input.mousePosition.x,Screen.height-Input.mousePosition.y);
            bool overWindow=window.ContainsScreenPoint(guiMouse);
            bool overKerbalGui=MouseIsInsideKerbalGuiZone(guiMouse);

            // V0.37 INPUT OWNERSHIP:
            // Kerbal GUI zones are owned exclusively by SceneEditorWindow.OnGUI.
            // Update must never start a 3D pick/drag under the same MouseDown.
            if(Input.GetMouseButtonDown(0)&&overKerbalGui)
            {
                if(directDragging)EndDirectDrag();
                Status="Zone Kerbal : contrôle GUI prioritaire";
                return;
            }

            // While the GUI Kerbal engine owns the pointer, the generic 3D engine is silent.
            if(guiDragActiveRuntime)
            {
                if(directDragging)EndDirectDrag();
                return;
            }

            if(!overWindow&&!overKerbalGui&&directManipulationEnabled&&Selected!=null&&directTool==0)
            {
                float wheel=Input.mouseScrollDelta.y;
                if(Mathf.Abs(wheel)>0.0001f)
                {
                    float fine=(Input.GetKey(KeyCode.LeftShift)||Input.GetKey(KeyCode.RightShift))?0.25f:1f;
                    MoveSelectedScreenRelative(0f,0f,wheel>0f?-1f:1f,1.5f*fine);
                }
            }

            if(Input.GetMouseButtonDown(0)&&!overWindow&&!overKerbalGui)
            {
                Transform picked=PickNonKerbalSceneTarget(Input.mousePosition);
                if(picked!=null)
                {
                    Selected=picked;
                    Camera targetCam=FindCameraForTarget(picked);
                    Status="Selected: "+FriendlyNameOf(picked);
                    if(directManipulationEnabled&&CanEdit(picked)&&targetCam!=null)
                        BeginDirectDrag(targetCam,picked);
                }
            }

            if(directDragging)
            {
                if(Input.GetMouseButton(0))UpdateDirectDrag();
                if(Input.GetMouseButtonUp(0))EndDirectDrag();
            }
        }

        private void BeginDirectDrag(Camera cam, Transform target)
        {
            if(cam==null||target==null)return;

            directDragging=true;
            directDragTarget=target;
            directMouseStart=Input.mousePosition;
            directStartPosition=target.position;
            directStartRotation=target.rotation;
            directStartScale=target.localScale;

            // V0.27: for a Kerbal, use the actor ROOT itself as the anchor.
            // Do not derive the drag origin from animated Renderer bounds.
            directStartAnchor=IsKnownKerbal(target)?GetStableKerbalPickAnchor(target):GetVisualAnchor(target);
            directStartScreen=cam.WorldToScreenPoint(directStartAnchor);

            if(IsKnownKerbal(target))
            {
                KerbalLiveOffsetState state=EnsureKerbalLiveOffset(target);
                if(state!=null)
                {
                    state.DragStartPositionOffset=state.PositionOffsetWorld;
                    state.DragStartRotationOffset=state.RotationOffset;
                    state.DragStartScaleMultiplier=state.ScaleMultiplier;
                }
            }

            if(directStartScreen.z<=0.0001f)
            {
                directDragging=false;
                directDragTarget=null;
                Status="Objet derrière la caméra";
                return;
            }

            if(!IsKnownKerbal(target))
            {
                if(IsKerbalPivot(target))PrepareKerbalPivotForEditing(target);
                BeginEdit(target);
            }

            Status=IsKnownKerbal(target)
                ?"Kerbal sélectionné — glissez pour déplacer"
                :DirectToolName+" : "+FriendlyNameOf(target);
        }

        private void UpdateDirectDrag()
        {
            Transform target=directDragTarget;
            Camera cam=FindCameraForTarget(target);
            if(target==null||cam==null){EndDirectDrag();return;}

            Vector3 mouseDelta=Input.mousePosition-directMouseStart;
            float sensitivity=1f;
            if(Input.GetKey(KeyCode.LeftShift)||Input.GetKey(KeyCode.RightShift))sensitivity=0.25f;
            else if(Input.GetKey(KeyCode.LeftControl)||Input.GetKey(KeyCode.RightControl))sensitivity=2f;

            if(IsKnownKerbal(target))
            {
                // 8 px dead-zone: a click selects; a deliberate mouse move edits.
                if(mouseDelta.sqrMagnitude<64f)return;

                KerbalLiveOffsetState state=EnsureKerbalLiveOffset(target);
                if(state==null)return;

                if(directTool==0)
                {
                    // One-to-one screen-space drag at the actor ROOT depth.
                    Vector3 screen=new Vector3(
                        directStartScreen.x+mouseDelta.x*sensitivity,
                        directStartScreen.y+mouseDelta.y*sensitivity,
                        directStartScreen.z);

                    Vector3 desired=cam.ScreenToWorldPoint(screen);
                    Vector3 delta=desired-directStartAnchor;

                    state.PositionOffsetWorld=state.DragStartPositionOffset+delta;
                    state.OverridePosition=true;
                    target.position=directStartPosition+delta;
                }
                else if(directTool==3)
                {
                    float worldPerPixel=
                        2f*Mathf.Max(0.25f,directStartScreen.z)*
                        Mathf.Tan(cam.fieldOfView*Mathf.Deg2Rad*0.5f)/
                        Mathf.Max(1f,Screen.height);

                    Vector3 delta=
                        cam.transform.forward*
                        (mouseDelta.y*worldPerPixel*0.55f*sensitivity);

                    state.PositionOffsetWorld=state.DragStartPositionOffset+delta;
                    state.OverridePosition=true;
                    target.position=directStartPosition+delta;
                }
                else if(directTool==1)
                {
                    float yaw=mouseDelta.x*0.12f*sensitivity;
                    float pitch=-mouseDelta.y*0.12f*sensitivity;

                    Quaternion delta=
                        Quaternion.AngleAxis(yaw,cam.transform.up)*
                        Quaternion.AngleAxis(pitch,cam.transform.right);

                    state.RotationOffset=delta*state.DragStartRotationOffset;
                    state.OverrideRotation=true;
                    target.rotation=delta*directStartRotation;
                }
                else
                {
                    float factor=Mathf.Clamp(
                        Mathf.Exp(mouseDelta.y*0.0012f*sensitivity),
                        0.35f,3f);

                    state.ScaleMultiplier=state.DragStartScaleMultiplier*factor;
                    state.OverrideScale=true;
                    target.localScale=directStartScale*factor;
                }

                state.LastComposedPosition=target.position;
                state.LastComposedRotation=target.rotation;
                state.LastComposedScale=target.localScale;
                state.HasLastCompose=true;

                edited.Add(target);
                Status="Kerbal • "+DirectToolName+" • racine directe";
                return;
            }

            if(directTool==0)
            {
                Vector3 screen=new Vector3(
                    directStartScreen.x+mouseDelta.x*sensitivity,
                    directStartScreen.y+mouseDelta.y*sensitivity,
                    directStartScreen.z);

                Vector3 desiredAnchor=cam.ScreenToWorldPoint(screen);
                Vector3 delta=desiredAnchor-directStartAnchor;
                ApplyWorldPosition(target,directStartPosition+delta);
            }
            else if(directTool==3)
            {
                float depthPerPixel=Mathf.Max(
                    0.0025f,
                    Mathf.Abs(directStartScreen.z)*0.0025f);

                float amount=mouseDelta.y*depthPerPixel*sensitivity;
                target.position=directStartPosition+cam.transform.forward*amount;
                target.localScale=directStartScale;
            }
            else if(directTool==1)
            {
                float yaw=mouseDelta.x*0.25f*sensitivity;
                float pitch=-mouseDelta.y*0.25f*sensitivity;

                Quaternion qYaw=Quaternion.AngleAxis(yaw,cam.transform.up);
                Quaternion qPitch=Quaternion.AngleAxis(pitch,cam.transform.right);
                target.rotation=qYaw*qPitch*directStartRotation;
            }
            else
            {
                float d=(mouseDelta.y+mouseDelta.x*0.15f)*0.0025f*sensitivity;
                float factor=Mathf.Clamp(Mathf.Exp(d),0.05f,20f);

                target.position=directStartPosition;
                target.localScale=directStartScale*factor;
            }

            edited.Add(target);
        }

        private void EndDirectDrag()
        {
            if (directDragging && directDragTarget != null)
                Status = DirectToolName + " applied: " + FriendlyNameOf(directDragTarget);
            directDragging = false;
            directDragTarget = null;
        }

        private int PickPriority(Transform t)
        {
            if(t==null)return 1000;
            string n=t.name??string.Empty;
            if(n.StartsWith("KSE_IMAGE_",StringComparison.OrdinalIgnoreCase))return 0;
            if(n.StartsWith("KSE_CRAFT_",StringComparison.OrdinalIgnoreCase))return 1;
            if(n.StartsWith("KSE_PLANET_ACTOR_",StringComparison.OrdinalIgnoreCase))return 2;
            if(n.StartsWith("KSE_TEXT_",StringComparison.OrdinalIgnoreCase))return 3;
            if(created.Contains(t.gameObject))return 4;
            if(IsKnownKerbal(t))return 10;
            string low=n.ToLowerInvariant();
            if(low=="logo"||low.Contains("logo"))return 20;
            if(low=="kerbin"||low=="mun"||low=="minmus"||low.Contains("planet"))return 30;
            return 100;
        }

        private Transform PickPriorityVisual(Camera cam,Vector3 mouse)
        {
            if(cam==null)return null;

            // V0.29: Kerbals ALWAYS win against planets/crafts/images when the cursor is
            // inside a reasonable Kerbal selection radius. This directly fixes the case
            // where clicking a Kerbal selects Kerbin/the planet behind him.
            Transform kerbal=PickStockKerbalExplicit(cam,mouse);
            if(kerbal!=null)return kerbal;

            Vector2 gui=new Vector2(mouse.x,Screen.height-mouse.y);
            Transform best=null;float bestScore=float.MaxValue;

            // Created objects come next.
            for(int i=0;i<created.Count;i++)
            {
                GameObject go=created[i];if(go==null||!go.activeInHierarchy)continue;
                Rect rr;if(!TryGetTargetScreenRect(cam,go.transform,out rr))continue;
                rr.xMin-=8f;rr.xMax+=8f;rr.yMin-=8f;rr.yMax+=8f;
                if(!rr.Contains(gui))continue;
                float area=Mathf.Max(1f,rr.width*rr.height);
                float center=(rr.center-gui).sqrMagnitude;
                float score=PickPriority(go.transform)*1000000f+area+center*0.15f;
                if(score<bestScore){bestScore=score;best=go.transform;}
            }
            if(best!=null)return best;

            for(int i=0;i<sceneEntries.Count;i++)
            {
                SceneEntry e=sceneEntries[i];if(e==null||e.Transform==null||!e.Transform.gameObject.activeInHierarchy)continue;
                if(IsKnownKerbal(e.Transform))continue; // handled above
                Rect rr;if(!TryGetTargetScreenRect(cam,e.Transform,out rr))continue;
                int priority=PickPriority(e.Transform);
                float pad=priority<=30?10f:2f;
                rr.xMin-=pad;rr.xMax+=pad;rr.yMin-=pad;rr.yMax+=pad;
                if(!rr.Contains(gui))continue;
                float area=Mathf.Max(1f,rr.width*rr.height);
                if(priority>=100&&area>Screen.width*Screen.height*0.20f)continue;
                float score=priority*1000000f+area+(rr.center-gui).sqrMagnitude*0.12f;
                if(score<bestScore){bestScore=score;best=e.Transform;}
            }
            return best;
        }

        private Transform PickEditableAtMouse(Camera cam, Vector3 mouse)
        {
            Transform priority=PickPriorityVisual(cam,mouse);
            if(priority!=null)return priority;

            Ray ray=cam.ScreenPointToRay(mouse);RaycastHit hitInfo;
            if(Physics.Raycast(ray,out hitInfo,100000f)&&hitInfo.transform!=null)
            {
                Transform registered=FindRegisteredTarget(hitInfo.transform);
                if(registered!=null)return registered;
            }
            return DiscoverVisualAtMouse(cam,mouse);
        }

        internal void MoveSelectedScreenRelative(float right, float up, float depth, float amount)
        {
            if(Selected==null){Status="Aucun objet sélectionné";return;}
            if(IsKnownKerbal(Selected))EnsureKerbalLiveOffset(Selected);
            if(!CanEdit(Selected))return;
            Camera cam=FindCameraForTarget(Selected);
            if(cam==null){Status="No suitable camera found";return;}

            BeginEdit(Selected);

            // Horizontal / vertical movement is performed in TRUE screen space.
            // This is intentionally not based on camera.transform.right/up because the
            // KSP main-menu camera hierarchy can contain rotated parents and non-obvious
            // axes. Projecting to pixels guarantees LEFT/RIGHT/UP/DOWN visually mean
            // exactly that on screen.
            if(Mathf.Abs(right)>0.0001f || Mathf.Abs(up)>0.0001f)
            {
                Vector3 anchor=GetVisualAnchor(Selected);
                Vector3 screen=cam.WorldToScreenPoint(anchor);
                if(screen.z<=0.0001f)
                {
                    Status="Selected object is behind the scene camera";
                    return;
                }

                Vector3 shiftedScreen=new Vector3(
                    screen.x + right * amount,
                    screen.y + up * amount,
                    screen.z);

                Vector3 shiftedAnchor=cam.ScreenToWorldPoint(shiftedScreen);
                Vector3 worldDelta=shiftedAnchor-anchor;
                ApplyWorldPosition(Selected,Selected.position+worldDelta);
                Status="Moved on screen: "+(right<0f?"LEFT ":right>0f?"RIGHT ":"")+(up<0f?"DOWN":up>0f?"UP":"")+" ("+amount.ToString("0.#")+" px)";
                return;
            }

            // Depth remains a camera-forward translation. Scale the UI step down so a
            // value such as 10 gives a useful adjustment instead of throwing actors far
            // through the scene.
            if(Mathf.Abs(depth)>0.0001f)
            {
                float depthAmount=amount*0.10f;
                Vector3 delta=cam.transform.forward*depth*depthAmount;
                ApplyWorldPosition(Selected,Selected.position+delta);
                Status=depth<0f?"Moved closer to camera":"Moved farther from camera";
            }
        }

        private Vector3 GetVisualAnchor(Transform target)
        {
            if(target==null)return Vector3.zero;
            Renderer[] rr=target.GetComponentsInChildren<Renderer>(true);
            bool any=false; Bounds b=new Bounds(target.position,Vector3.zero);
            for(int i=0;i<rr.Length;i++)
            {
                Renderer r=rr[i];
                if(r==null||!r.enabled||!r.gameObject.activeInHierarchy)continue;
                if(!any){b=r.bounds;any=true;}else b.Encapsulate(r.bounds);
            }
            return any?b.center:target.position;
        }

        internal void MoveSelectedLayer(bool towardCamera)
        {
            if(Selected==null)return;MoveSelectedScreenRelative(0f,0f,towardCamera?-1f:1f,18f);
        }

        internal void FrameSelected()
        {
            if(Selected==null){Status="No object selected";return;} Camera cam=FindLandscapeCamera(); if(cam==null){Status="No scene camera found";return;}
            history.Capture(cam.transform);
            Renderer[] rr=Selected.GetComponentsInChildren<Renderer>(true);
            Bounds b=new Bounds(Selected.position,Vector3.one); bool any=false;
            for(int i=0;i<rr.Length;i++){if(rr[i]==null||!rr[i].enabled)continue;if(!any){b=rr[i].bounds;any=true;}else b.Encapsulate(rr[i].bounds);}
            float radius=any?Mathf.Max(0.5f,b.extents.magnitude):1f;
            Vector3 dir=(cam.transform.position-(any?b.center:Selected.position)).normalized; if(dir.sqrMagnitude<0.1f)dir=-cam.transform.forward;
            Vector3 target=any?b.center:Selected.position; cam.transform.position=target+dir*(radius*3.2f); cam.transform.LookAt(target); edited.Add(cam.transform);
            Status="Camera framed selected object";
        }

        internal bool SelectedHasTextMesh()
        {
            return Selected!=null && Selected.GetComponentInChildren<TextMesh>(true)!=null;
        }

        internal string[] GetSelectedAnimationNames()
        {
            if(Selected==null)return new string[0]; List<string> names=new List<string>(); Animation[] aa=Selected.GetComponentsInChildren<Animation>(true);
            for(int i=0;i<aa.Length;i++){Animation a=aa[i];if(a==null)continue;foreach(AnimationState s in a)if(s!=null&&!names.Contains(s.name))names.Add(s.name);} return names.ToArray();
        }
        internal void PlaySelectedAnimation(string clip)
        {
            if(Selected==null||string.IsNullOrEmpty(clip))return; Animation[] aa=Selected.GetComponentsInChildren<Animation>(true);int hits=0;
            for(int i=0;i<aa.Length;i++){Animation a=aa[i];if(a!=null&&a[clip]!=null){a.Play(clip);hits++;}} Status=hits>0?"Playing animation: "+clip:"Animation not found";
        }

        internal void StopSelectedAnimations()
        {
            if(Selected==null){Status="No object selected";return;}
            Animation[] aa=Selected.GetComponentsInChildren<Animation>(true);Animator[] ar=Selected.GetComponentsInChildren<Animator>(true);int hits=0;
            for(int i=0;i<aa.Length;i++){if(aa[i]!=null){aa[i].Stop();hits++;}}
            for(int i=0;i<ar.Length;i++){if(ar[i]!=null&&ar[i].enabled){ar[i].enabled=false;hits++;}}
            Status=hits>0?"Animations stopped / pose frozen":"No animation controller found";
        }

        internal void ResumeSelectedAnimations()
        {
            if(Selected==null){Status="No object selected";return;}
            Animator[] ar=Selected.GetComponentsInChildren<Animator>(true);int hits=0;
            for(int i=0;i<ar.Length;i++){if(ar[i]!=null&&!ar[i].enabled){ar[i].enabled=true;hits++;}}
            Status=hits>0?"Animator resumed. Choose a clip and PLAY if needed.":"Animator already enabled";
        }

        private bool IsInsideCurrentWorkspace(Transform t)
        {
            if(t==null)return false;
            Transform root=FindSceneRoot(workspaceRootName);
            return root!=null&&(t==root||t.IsChildOf(root));
        }

        internal void RestoreCurrentNativeContext(){RestoreCurrentNativeContext(true);}
        private void RestoreCurrentNativeContext(bool clearProfile)
        {
            if(!BaselineReady){Status="Baseline indisponible";return;}

            ForceRefreshNativeMainMenuState();
            int keepArea=nativeAreaIndex;
            int keepStage=nativeStageIndex;
            bool keepRare=nativeSandcastleActive;
            string keepWorkspace=workspaceRootName;
            string key=BuildContextKey(keepArea,keepStage,keepRare);

            try
            {
                RestoreOriginalSkybox();
                RenderSettings.ambientLight=baselineAmbientLight;

                List<Transform> editedCopy=new List<Transform>(edited);
                int restored=0;
                for(int i=0;i<editedCopy.Count;i++)
                {
                    Transform t=editedCopy[i];
                    if(t==null||!InSpecificContext(t,key))continue;
                    RestoreRendererTextures(t);
                    RestoreTextStateForTransform(t);
                    RestoreKerbalLiveOffset(t);
                    if(baseline.RestoreOne(t))
                    {
                        edited.Remove(t);
                        editedContextOwners.Remove(t);
                        visualImageOverrides.Remove(t);
                        restored++;
                    }
                }

                List<GameObject> createdCopy=new List<GameObject>(created);
                for(int i=0;i<createdCopy.Count;i++)
                {
                    GameObject go=createdCopy[i];
                    if(go==null||!CreatedBelongsToContext(go,key))continue;
                    RemoveCreatedTracking(go);
                    UnityEngine.Object.Destroy(go);
                }

                ClearKerbalPivotsAfterBaselineRestore();
                Selected=null;
                history.Clear();
                workspaceRootName=keepWorkspace;
StartCoroutine(RestoreCurrentNativeContextRoutine(keepArea,keepStage,keepRare));
                if(clearProfile)
                {
                    LoadContextProfiles();
                    contextProfiles.Remove(key);
                    SaveContextProfiles();
                    DeleteSessionWorkspace(key);
                }
                Status="État restauré";
            }
            catch(Exception ex)
            {
                Status="Restauration contexte impossible : "+ex.Message;
                SceneEditorLog.Warn(Status);
            }
        }

        private IEnumerator RestoreCurrentNativeContextRoutine(int areaIndex,int stageIndex,bool rare)
        {
            yield return null;
            Type t;object env=FindMainMenuEnvLogicInstance(out t);
            if(env!=null&&t!=null)
            {
                GameObject[] areas=GetNativeAreas(env,t);
                if(areaIndex>=0&&areaIndex<areas.Length&&areas[areaIndex]!=null)
                {
                    for(int i=0;i<areas.Length;i++)
                        if(areas[i]!=null)areas[i].SetActive(i==areaIndex);

                    FieldInfo sf=t.GetField("startingArea",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
                    if(sf!=null)sf.SetValue(env,areas[areaIndex]);
                    ReactivateNativeAnimations(areas[areaIndex]);
                }

                MethodInfo go=t.GetMethod("GoToStage",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,new Type[]{typeof(int)},null);
                if(go!=null&&stageIndex>=0)go.Invoke(env,new object[]{stageIndex});
            }

            yield return new WaitForSecondsRealtime(0.75f);

            nativeSandcastleActive=false;
            if(areaIndex==0&&rare)SetNativeSandcastleVariant(true);

            ForceRefreshNativeMainMenuState();
            RebuildKerbalRegistry(true);
            RefreshObjects();
}

        internal void RestoreOriginal()
        {
            if(!BaselineReady){Status="Baseline indisponible";return;}
            bool keepPreview=scenePreviewActive;
            string keepWorkspace=workspaceRootName;

            RestoreAllRendererTextures();RestoreAllTextStates();RestoreOriginalSkybox();
            if(kerbalLiveOffsets.Count>0)
            {
                List<Transform> live=new List<Transform>(kerbalLiveOffsets.Keys);
                for(int i=0;i<live.Count;i++)RestoreKerbalLiveOffset(live[i]);
            }

            int count=0;
            if(keepPreview)
            {
                // In preview mode, never restore the entire Unity main-menu snapshot:
                // doing so rewrites active scene roots and can break MunScene's runtime state.
                List<Transform> editedCopy=new List<Transform>(edited);
                for(int i=0;i<editedCopy.Count;i++)if(editedCopy[i]!=null&&baseline.RestoreOne(editedCopy[i]))count++;
                ClearKerbalPivotsAfterBaselineRestore();
            }
            else
            {
                ClearKerbalPivotsAfterBaselineRestore();
                count=baseline.Restore();
            }

            for(int i=created.Count-1;i>=0;i--)if(created[i]!=null)UnityEngine.Object.Destroy(created[i]);
            created.Clear();createdContextOwners.Clear();spawnedCrafts.Clear();spawnedLights.Clear();spawnedPlanets.Clear();spawnedTexts.Clear();spawnedImages.Clear();
            edited.Clear();editedContextOwners.Clear();visualImageOverrides.Clear();history.Clear();Selected=null;

            if(keepPreview)
            {
                workspaceRootName=keepWorkspace;
Transform wanted=FindSceneRoot(keepWorkspace);
                if(wanted!=null){wanted.gameObject.SetActive(true);ResumeSceneAnimations(wanted);}
                RefreshObjects();
                Status="RESET local effectué — aperçu conservé : "+keepWorkspace;
            }
            else
            {
                RefreshObjects();Status="ORIGINAL RESTAURÉ : "+count+" transforms";
            }
            SceneEditorLog.Info(Status);
        }

        internal void HardReloadMainMenu(){Status="Reloading MAINMENU...";HighLogic.LoadScene(GameScenes.MAINMENU);}

        private static string BuildContextKey(int area,int stage,bool rare)
        {
            string scene=area==0?"MUN":area==1?"ORBIT":"UNKNOWN";
            return scene+"_S"+Mathf.Max(0,stage)+(area==0&&rare?"_CASTLE":"_NORMAL");
        }
        private static string BuildContextLabel(int area,int stage,bool rare)
        {
            string scene=area==0?"Mun":area==1?"Principale / Orbit":"Inconnue";
            string state=stage==0?"État 1":"État 2";
            return scene+" — "+state+(area==0&&rare?" — Château":"");
        }
        private void LoadContextProfiles()
        {
            contextProfiles.Clear();
            try{if(!File.Exists(ContextProfilesPath))return;ConfigNode f=ConfigNode.Load(ContextProfilesPath);if(f==null)return;ConfigNode r=f.GetNode("KSP_SCENE_EDITOR_CONTEXT_PROFILES")??f;ConfigNode[] p=r.GetNodes("PROFILE");for(int i=0;i<p.Length;i++){string k=p[i].GetValue("context");string n=p[i].GetValue("scene");if(!string.IsNullOrEmpty(k)&&!string.IsNullOrEmpty(n))contextProfiles[k]=n;}}catch(Exception ex){SceneEditorLog.Warn("Context profiles load: "+ex.Message);}
        }
        private void SaveContextProfiles()
        {
            try{ConfigNode f=new ConfigNode();ConfigNode r=f.AddNode("KSP_SCENE_EDITOR_CONTEXT_PROFILES");r.AddValue("version","1.0");foreach(KeyValuePair<string,string> kv in contextProfiles){ConfigNode p=r.AddNode("PROFILE");p.AddValue("context",kv.Key);p.AddValue("scene",kv.Value);}Directory.CreateDirectory(Path.GetDirectoryName(ContextProfilesPath));f.Save(ContextProfilesPath);}catch(Exception ex){SceneEditorLog.Warn("Context profiles save: "+ex.Message);}
        }
        private bool InCurrentContext(Transform t)
        {
            if(t==null)return false;
            string key=CurrentContextKey;
            string owner;
            if(editedContextOwners.TryGetValue(t,out owner))
                return string.Equals(owner,key,StringComparison.OrdinalIgnoreCase);
            return IsInsideCurrentWorkspace(t);
        }

        private bool InSpecificContext(Transform t,string key)
        {
            if(t==null||string.IsNullOrEmpty(key))return false;
            string owner;
            if(editedContextOwners.TryGetValue(t,out owner))
                return string.Equals(owner,key,StringComparison.OrdinalIgnoreCase);

            // Unowned legacy edits are accepted only when their native root matches the requested area.
            if(key.StartsWith("ORBIT_",StringComparison.OrdinalIgnoreCase))
            {
                Transform root=FindSceneRoot("OrbitScene");
                return root!=null&&(t==root||t.IsChildOf(root));
            }
            if(key.StartsWith("MUN_",StringComparison.OrdinalIgnoreCase))
            {
                Transform root=FindSceneRoot("MunScene");
                return root!=null&&(t==root||t.IsChildOf(root));
            }
            return false;
        }

        private IEnumerable<Transform> CurrentEdited()
        {
            string key=CurrentContextKey;
            foreach(Transform t in edited)if(InSpecificContext(t,key))yield return t;
        }

        private IEnumerable<Transform> EditedForContext(string key)
        {
            foreach(Transform t in edited)if(InSpecificContext(t,key))yield return t;
        }
        private IEnumerable<SpawnedCraft> CraftsForContext(string key){foreach(SpawnedCraft x in spawnedCrafts)if(x!=null&&x.Root!=null&&CreatedBelongsToContext(x.Root,key))yield return x;}
        private IEnumerable<SpawnedLight> LightsForContext(string key){foreach(SpawnedLight x in spawnedLights)if(x!=null&&x.Root!=null&&CreatedBelongsToContext(x.Root,key))yield return x;}
        private IEnumerable<SpawnedPlanet> PlanetsForContext(string key){foreach(SpawnedPlanet x in spawnedPlanets)if(x!=null&&x.Root!=null&&CreatedBelongsToContext(x.Root,key))yield return x;}
        private IEnumerable<SpawnedText> TextsForContext(string key){foreach(SpawnedText x in spawnedTexts)if(x!=null&&x.Root!=null&&CreatedBelongsToContext(x.Root,key))yield return x;}
        private IEnumerable<SpawnedImage> ImagesForContext(string key){foreach(SpawnedImage x in spawnedImages)if(x!=null&&x.Root!=null&&CreatedBelongsToContext(x.Root,key))yield return x;}

        private IEnumerable<SpawnedCraft> CurrentCrafts(){return CraftsForContext(CurrentContextKey);}
        private IEnumerable<SpawnedLight> CurrentLights(){return LightsForContext(CurrentContextKey);}
        private IEnumerable<SpawnedPlanet> CurrentPlanets(){return PlanetsForContext(CurrentContextKey);}
        private IEnumerable<SpawnedText> CurrentTexts(){return TextsForContext(CurrentContextKey);}
        private IEnumerable<SpawnedImage> CurrentImages(){return ImagesForContext(CurrentContextKey);}

        private bool HasSessionWorkspace(string key)
        {
            if(string.IsNullOrEmpty(key))return false;
            string path=SessionWorkspacePath(key);
            bool exists=File.Exists(path);
            if(exists)sessionWorkspaceKeys.Add(key);
            else sessionWorkspaceKeys.Remove(key);
            return exists;
        }

        private void DeleteSessionWorkspace(string key)
        {
            if(string.IsNullOrEmpty(key))return;
            sessionWorkspaceKeys.Remove(key);
            try
            {
                string path=SessionWorkspacePath(key);
                if(File.Exists(path))File.Delete(path);
            }catch{}
        }

        private void CaptureSessionWorkspaceForContext(int area,int stage,bool rare,string key)
        {
            if(!BaselineReady||HighLogic.LoadedScene!=GameScenes.MAINMENU||string.IsNullOrEmpty(key))return;
            try
            {
                string path=SessionWorkspacePath(key);
                ScenePersistence.SaveToPath(path,"__SESSION__"+key,EditedForContext(key),CraftsForContext(key),LightsForContext(key),PlanetsForContext(key),TextsForContext(key),ImagesForContext(key),RenderSettings.ambientLight,area,stage,rare,key,this);
                sessionWorkspaceKeys.Add(key);
                SceneEditorLog.Info("Session draft captured explicit: "+key);
            }
            catch(Exception ex){SceneEditorLog.Warn("Session draft save explicit: "+ex.Message);}
        }

        private void CaptureCurrentSessionWorkspace()
        {
            if(!BaselineReady||HighLogic.LoadedScene!=GameScenes.MAINMENU)return;
            ForceRefreshNativeMainMenuState();
            string key=CurrentContextKey;
            if(string.IsNullOrEmpty(key)||key.StartsWith("UNKNOWN",StringComparison.OrdinalIgnoreCase))return;
            try
            {
                string path=SessionWorkspacePath(key);
                ScenePersistence.SaveToPath(path,"__SESSION__"+key,CurrentEdited(),CurrentCrafts(),CurrentLights(),CurrentPlanets(),CurrentTexts(),CurrentImages(),RenderSettings.ambientLight,nativeAreaIndex,nativeStageIndex,nativeSandcastleActive,key,this);
                sessionWorkspaceKeys.Add(key);
                SceneEditorLog.Info("Session draft captured: "+key);
            }
            catch(Exception ex){SceneEditorLog.Warn("Session draft save: "+ex.Message);}
        }

        private void LoadSessionWorkspaceByKey(string key,string label)
        {
            if(string.IsNullOrEmpty(key)||!HasSessionWorkspace(key))return;
            try
            {
                ScenePersistence.LoadFromPath(SessionWorkspacePath(key),this);
                RefreshObjects();
                Status="Travail en cours restauré : "+label;
            }
            catch(Exception ex){SceneEditorLog.Warn("Session draft load: "+ex.Message);}
        }

        private IEnumerator RestorePreferredWorkspaceByKeyDelayed(string key,string label,float delay)
        {
            if(delay>0f)yield return new WaitForSecondsRealtime(delay);

            if(HasSessionWorkspace(key))
            {
                applyingContextProfile=true;
                LoadSessionWorkspaceByKey(key,label);
                applyingContextProfile=false;
                yield break;
            }

            LoadContextProfiles();
            string profile;
            if(contextProfiles.TryGetValue(key,out profile)&&!string.IsNullOrEmpty(profile))
            {
                applyingContextProfile=true;
                try
                {
                    ScenePersistence.Load(profile,this);
                    RefreshObjects();
                    Status="Composition restaurée : "+profile;
                }
                catch(Exception ex){SceneEditorLog.Warn("Profile restore: "+ex.Message);}
                applyingContextProfile=false;
            }
        }

        private IEnumerator RestorePreferredCurrentWorkspaceDelayed(float delay)
        {
            if(delay>0f)yield return new WaitForSecondsRealtime(delay);
            ForceRefreshNativeMainMenuState();
            string key=CurrentContextKey;
            string label=CurrentContextLabel;
            yield return StartCoroutine(RestorePreferredWorkspaceByKeyDelayed(key,label,0f));
        }

        internal void ResetCurrentWorkToActiveComposition()
        {
            StartCoroutine(ResetCurrentWorkRoutine());
        }

        private IEnumerator ResetCurrentWorkRoutine()
        {
            ForceRefreshNativeMainMenuState();
            string key=CurrentContextKey;
            DeleteSessionWorkspace(key);

            applyingContextProfile=true;
            RestoreCurrentNativeContext(false);
            yield return new WaitForSecondsRealtime(0.85f);

            LoadContextProfiles();
            string name;
            if(contextProfiles.TryGetValue(key,out name)&&!string.IsNullOrEmpty(name))
            {
                try
                {
                    ScenePersistence.Load(name,this);
                    RefreshObjects();
                    Status="État réinitialisé sur : "+name;
                }
                catch(Exception ex){Status="Réinitialisation impossible : "+ex.Message;}
            }
            else
            {
                Status="État réinitialisé sur l'original KSP";
            }
            applyingContextProfile=false;
        }

        internal void SaveScene(string name)
        {
            try
            {
                ForceRefreshNativeMainMenuState();
                string key=CurrentContextKey;
                ScenePersistence.Save(name,CurrentEdited(),CurrentCrafts(),CurrentLights(),CurrentPlanets(),CurrentTexts(),CurrentImages(),RenderSettings.ambientLight,nativeAreaIndex,nativeStageIndex,nativeSandcastleActive,key,this);
                LoadContextProfiles();
                contextProfiles[key]=name;
                SaveContextProfiles();
                DeleteSessionWorkspace(key);
                RefreshUserFileCaches();
                Status="Composition sauvegardée et active : "+name;
            }
            catch(Exception ex){Status="Sauvegarde impossible : "+ex.Message;}
        }
        internal void LoadScene(string name)
        {
            try{StartCoroutine(LoadSceneForItsContext(name,true));}catch(Exception ex){Status="Load failed: "+ex.Message;}
        }
        private IEnumerator LoadSceneForItsContext(string name,bool bind)
        {
            applyingContextProfile=true;
            int a,s;bool rare;string key;

            if(!ScenePersistence.TryGetContext(name,out a,out s,out rare,out key))
            {
                Status="Composition invalide ou ancienne : "+name;
                applyingContextProfile=false;
                yield break;
            }

            ForceRefreshNativeMainMenuState();
            bool changing=a!=nativeAreaIndex||s!=nativeStageIndex||(a==0&&rare!=nativeSandcastleActive);

            if(changing&&BaselineReady)
            {
                RestoreCurrentNativeContext(false);
                yield return new WaitForSecondsRealtime(0.90f);
            }

            if(a!=nativeAreaIndex||s!=nativeStageIndex)
            {
                SelectNativeMainMenuArea(a);
                yield return new WaitForSecondsRealtime(0.40f);
                SelectNativeMainMenuStage(s);
                yield return new WaitForSecondsRealtime(0.75f);
            }

            if(a==0&&rare!=nativeSandcastleActive)
            {
                SetNativeSandcastleVariant(rare);
                yield return new WaitForSecondsRealtime(0.25f);
            }

            ForceRefreshNativeMainMenuState();
            RestoreCurrentNativeContext(false);
            yield return new WaitForSecondsRealtime(0.85f);

            try
            {
                ScenePersistence.Load(name,this);
                RefreshObjects();
                if(bind)
                {
                    LoadContextProfiles();
                    contextProfiles[CurrentContextKey]=name;
                    SaveContextProfiles();
                    DeleteSessionWorkspace(CurrentContextKey);
                }
                Status="Composition active : "+name+" / "+CurrentContextLabel;
            }
            catch(Exception ex)
            {
                Status="Load failed: "+ex.Message;
                SceneEditorLog.Warn(Status);
            }

            applyingContextProfile=false;
        }
        private IEnumerator ApplyCurrentContextProfileDelayed(float delay)
        {
            if(applyingContextProfile)yield break;
            if(delay>0)yield return new WaitForSecondsRealtime(delay);

            ForceRefreshNativeMainMenuState();
            LoadContextProfiles();

            string name;
            if(!contextProfiles.TryGetValue(CurrentContextKey,out name)||string.IsNullOrEmpty(name))
                yield break;

            applyingContextProfile=true;

            if(BaselineReady)
            {
                RestoreCurrentNativeContext(false);
                yield return new WaitForSecondsRealtime(0.85f);
            }

            try
            {
                ScenePersistence.Load(name,this);
                RefreshObjects();
                Status="Composition restaurée : "+name;
            }
            catch(Exception ex)
            {
                SceneEditorLog.Warn("Auto profile: "+ex.Message);
            }

            applyingContextProfile=false;
        }
        internal void ClearCurrentContextProfile(){LoadContextProfiles();if(contextProfiles.Remove(CurrentContextKey)){SaveContextProfiles();Status="Auto-composition désactivée : "+CurrentContextLabel;}else Status="Aucune auto-composition pour ce contexte";}
        internal void UseStockForCurrentContext()
        {
            ForceRefreshNativeMainMenuState();
            string key=CurrentContextKey;
            DeleteSessionWorkspace(key);
            RestoreCurrentNativeContext(false);
            LoadContextProfiles();
            contextProfiles.Remove(key);
            SaveContextProfiles();
            Status="Original KSP actif : "+CurrentContextLabel;
        }

        internal bool RenameScene(string oldName,string newName)
        {
            try
            {
                oldName=(oldName??string.Empty).Trim();
                newName=(newName??string.Empty).Trim();
                if(string.IsNullOrEmpty(oldName)){Status="Sélectionnez une composition à renommer";return false;}
                if(string.IsNullOrEmpty(newName)){Status="Indiquez un nouveau nom";return false;}
                if(string.Equals(oldName,newName,StringComparison.OrdinalIgnoreCase)){Status="Le nom est déjà identique";return false;}

                string oldPath=ScenePersistence.SceneFilePath(oldName);
                string newPath=ScenePersistence.SceneFilePath(newName);
                if(!File.Exists(oldPath)){Status="Composition introuvable : "+oldName;return false;}
                if(File.Exists(newPath)){Status="Ce nom existe déjà : "+newName;return false;}

                ConfigNode file=ConfigNode.Load(oldPath);
                if(file==null){Status="Composition illisible : "+oldName;return false;}
                ConfigNode root=file.GetNode("KSP_SCENE_EDITOR_SCENE")??file;
                root.SetValue("name",newName,true);
                file.Save(newPath);
                File.Delete(oldPath);

                LoadContextProfiles();
                bool changed=false;
                List<string> keys=new List<string>(contextProfiles.Keys);
                for(int i=0;i<keys.Count;i++)
                {
                    string key=keys[i];
                    string value;
                    if(contextProfiles.TryGetValue(key,out value)&&string.Equals(value,oldName,StringComparison.OrdinalIgnoreCase))
                    {
                        contextProfiles[key]=newName;
                        changed=true;
                    }
                }
                if(changed)SaveContextProfiles();

                RefreshUserFileCaches();
                Status="Composition renommée : "+newName;
                return true;
            }
            catch(Exception ex)
            {
                Status="Renommage impossible : "+ex.Message;
                SceneEditorLog.Warn(Status);
                return false;
            }
        }

        internal bool DeleteScene(string name)
        {
            try
            {
                name=(name??string.Empty).Trim();
                if(string.IsNullOrEmpty(name)){Status="Sélectionnez une composition à supprimer";return false;}
                string path=ScenePersistence.SceneFilePath(name);
                if(!File.Exists(path)){Status="Composition déjà absente";RefreshUserFileCaches();return false;}

                File.Delete(path);

                LoadContextProfiles();
                bool changed=false;
                List<string> keys=new List<string>(contextProfiles.Keys);
                for(int i=0;i<keys.Count;i++)
                {
                    string key=keys[i];
                    string value;
                    if(contextProfiles.TryGetValue(key,out value)&&string.Equals(value,name,StringComparison.OrdinalIgnoreCase))
                    {
                        contextProfiles.Remove(key);
                        changed=true;
                    }
                }
                if(changed)SaveContextProfiles();

                RefreshUserFileCaches();
                Status="Composition supprimée : "+name;
                return true;
            }
            catch(Exception ex)
            {
                Status="Suppression impossible : "+ex.Message;
                SceneEditorLog.Warn(Status);
                return false;
            }
        }

        internal string[] ListSceneFilesForCurrentContext()
        {
            ForceRefreshNativeMainMenuState();
            string[] all=cachedCompositions??new string[0];
            List<string> result=new List<string>();
            for(int i=0;i<all.Length;i++)
            {
                int a,s;bool rare;string key;
                if(ScenePersistence.TryGetContext(all[i],out a,out s,out rare,out key) &&
                   a==nativeAreaIndex && s==nativeStageIndex &&
                   (a!=0 || rare==nativeSandcastleActive))
                    result.Add(all[i]);
            }
            return result.ToArray();
        }

        internal string[] ListSceneFiles()
        {
            return cachedCompositions??new string[0];
        }
        internal string[] ListCraftFiles()
        {
            return cachedCrafts??new string[0];
        }
        private bool LooksLikeMainMenuSceneType(Type type)
        {
            if(type==null)return false;
            string n=(type.FullName??type.Name??string.Empty);
            string l=n.ToLowerInvariant();
            if(l.Contains("mainmenu"))return true;
            if(l.Contains("menuscene"))return true;
            if(l.Contains("menu")&&(l.Contains("scene")||l.Contains("stage")||l.Contains("landscape")||l.Contains("kerbal")))return true;
            return false;
        }

        private string SafeMemberValue(object obj,FieldInfo f)
        {
            try
            {
                object v=f.GetValue(obj);
                if(v==null)return "<null>";
                Type t=v.GetType();
                if(t.IsEnum||t.IsPrimitive||v is string)return v.ToString();
                UnityEngine.Object u=v as UnityEngine.Object;
                if(u!=null)return u.name+" ["+t.FullName+"]";
                Array arr=v as Array;
                if(arr!=null)return "Array("+arr.Length+") "+t.GetElementType();
                return t.FullName;
            }
            catch(Exception ex){return "<"+ex.GetType().Name+">";}
        }

        private string SafePropertyValue(object obj,PropertyInfo p)
        {
            try
            {
                if(!p.CanRead||p.GetIndexParameters().Length!=0)return "<non lisible>";
                MethodInfo getter=p.GetGetMethod(true);
                if(getter==null)return "<sans getter>";
                object v=p.GetValue(obj,null);
                if(v==null)return "<null>";
                Type t=v.GetType();
                if(t.IsEnum||t.IsPrimitive||v is string)return v.ToString();
                UnityEngine.Object u=v as UnityEngine.Object;
                if(u!=null)return u.name+" ["+t.FullName+"]";
                Array arr=v as Array;
                if(arr!=null)return "Array("+arr.Length+") "+t.GetElementType();
                return t.FullName;
            }
            catch(Exception ex){return "<"+ex.GetType().Name+">";}
        }

        private void DumpTypeDeep(StringBuilder sb,Type t,object instance,string label)
        {
            if(t==null)return;
            sb.AppendLine("=== "+label+" ===");
            sb.AppendLine("TYPE "+t.FullName+" | assembly="+t.Assembly.GetName().Name+" | base="+(t.BaseType!=null?t.BaseType.FullName:"<none>"));
            FieldInfo[] fs=t.GetFields(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance);
            for(int i=0;i<fs.Length;i++)
            {
                FieldInfo f=fs[i];
                string value="<instance unavailable>";
                try
                {
                    object target=f.IsStatic?null:instance;
                    if(f.IsStatic||instance!=null)value=SafeMemberValue(target,f);
                }catch{}
                sb.AppendLine("FIELD "+f.FieldType.FullName+" "+f.Name+" | static="+f.IsStatic+" | value="+value);
            }
            PropertyInfo[] ps=t.GetProperties(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance);
            for(int i=0;i<ps.Length;i++)
            {
                PropertyInfo p=ps[i];
                string value="<instance unavailable>";
                try
                {
                    MethodInfo g=p.GetGetMethod(true);
                    if(g!=null&&(g.IsStatic||instance!=null))value=SafePropertyValue(g.IsStatic?null:instance,p);
                }catch{}
                sb.AppendLine("PROP "+p.PropertyType.FullName+" "+p.Name+" | value="+value);
            }
            MethodInfo[] ms=t.GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance|BindingFlags.DeclaredOnly);
            for(int i=0;i<ms.Length;i++)
            {
                MethodInfo m=ms[i];
                ParameterInfo[] pp=m.GetParameters();
                StringBuilder sig=new StringBuilder();
                for(int j=0;j<pp.Length;j++)
                {
                    if(j>0)sig.Append(", ");
                    sig.Append(pp[j].ParameterType.FullName+" "+pp[j].Name);
                }
                sb.AppendLine("METHOD "+m.ReturnType.FullName+" "+m.Name+"("+sig+") | static="+m.IsStatic);
            }
            Type[] nested=t.GetNestedTypes(BindingFlags.Public|BindingFlags.NonPublic);
            for(int i=0;i<nested.Length;i++)sb.AppendLine("NESTED "+nested[i].FullName+" | enum="+nested[i].IsEnum);
            sb.AppendLine();
        }

        private Transform FindDirectChildByName(Transform root,string name)
        {
            if(root==null)return null;
            for(int i=0;i<root.childCount;i++)
            {
                Transform c=root.GetChild(i);
                if(c!=null&&string.Equals(c.name,name,StringComparison.OrdinalIgnoreCase))return c;
            }
            return null;
        }

        private Transform ChooseSandcastleVariant(Transform munRoot)
        {
            if(munRoot==null)return null;
            Transform terrainHigh=FindDirectChildByName(munRoot,"TerrainHigh");
            Transform terrainMed=FindDirectChildByName(munRoot,"TerrainMedium");

            if(terrainHigh!=null&&terrainHigh.gameObject.activeSelf)
            {
                Transform h=FindDirectChildByName(munRoot,"sandcastle_v2_High");
                if(h!=null)return h;
            }
            if(terrainMed!=null&&terrainMed.gameObject.activeSelf)
            {
                Transform m=FindDirectChildByName(munRoot,"sandcastle_v2_Medium");
                if(m!=null)return m;
            }

            Transform low=FindDirectChildByName(munRoot,"sandcastle_v2_low");
            if(low!=null)return low;
            Transform high=FindDirectChildByName(munRoot,"sandcastle_v2_High");
            if(high!=null)return high;
            return FindDirectChildByName(munRoot,"sandcastle");
        }

        internal bool HasNativeSandcastleVariant()
        {
            Transform mun=FindSceneRoot("MunScene");
            if(mun==null)return false;
            return FindDirectChildByName(mun,"sandcastle")!=null ||
                   FindDirectChildByName(mun,"sandcastle_v2_low")!=null ||
                   FindDirectChildByName(mun,"sandcastle_v2_Medium")!=null ||
                   FindDirectChildByName(mun,"sandcastle_v2_High")!=null;
        }

        internal void SetNativeSandcastleVariant(bool enabled)
        {
            Transform mun=FindSceneRoot("MunScene");
            if(mun==null){Status="MunScene introuvable";return;}

            string[] names={"sandcastle","sandcastle_v2_low","sandcastle_v2_Medium","sandcastle_v2_High"};
            for(int i=0;i<names.Length;i++)
            {
                Transform t=FindDirectChildByName(mun,names[i]);
                if(t==null)continue;

                MonoBehaviour[] scripts=t.GetComponents<MonoBehaviour>();
                for(int s=0;s<scripts.Length;s++)
                    if(scripts[s]!=null&&string.Equals(scripts[s].GetType().Name,"SandCastleLogic",StringComparison.OrdinalIgnoreCase))
                        scripts[s].enabled=false;

                t.gameObject.SetActive(false);
            }

            if(enabled)
            {
                Transform chosen=ChooseSandcastleVariant(mun);
                if(chosen==null){Status="Variante château introuvable";return;}
                chosen.gameObject.SetActive(true);
                nativeSandcastleActive=true;
                Status="VARIANTE RARE : "+chosen.name;
            }
            else
            {
                nativeSandcastleActive=false;
                Status="Variante rare désactivée";
            }

            StartCoroutine(RefreshNativeContextDelayed(0.15f));
        }

        internal void ExportMainMenuStageLab()
        {
            try
            {
                string dir=Path.Combine(KSPUtil.ApplicationRootPath,"GameData","KSPSceneEditor","PluginData","Diagnostics");
                Directory.CreateDirectory(dir);
                string path=Path.Combine(dir,"MainMenuStageLab_"+DateTime.Now.ToString("yyyyMMdd_HHmmss")+".txt");
                StringBuilder sb=new StringBuilder();
                sb.AppendLine("KSP SCENE EDITOR - MAIN MENU STAGE LAB");
                sb.AppendLine("Generated UTC: "+DateTime.UtcNow.ToString("o"));
                sb.AppendLine("LoadedScene: "+HighLogic.LoadedScene);
                sb.AppendLine();

                Assembly ac=null;
                Assembly[] aa=AppDomain.CurrentDomain.GetAssemblies();
                for(int i=0;i<aa.Length;i++)if(string.Equals(aa[i].GetName().Name,"Assembly-CSharp",StringComparison.OrdinalIgnoreCase)){ac=aa[i];break;}
                if(ac==null){sb.AppendLine("Assembly-CSharp introuvable.");File.WriteAllText(path,sb.ToString());Status="Stage Lab : Assembly-CSharp introuvable";return;}

                Type env=ac.GetType("MainMenuEnvLogic",false);
                Type menu=ac.GetType("MainMenu",false);
                Type terrain=ac.GetType("MainMenuTerrainSelector",false);
                Type random=ac.GetType("MenuRandomKerbalAnims",false);
                Type expr=ac.GetType("MainMenuExpressionManager",false);
                Type stage=ac.GetType("MainMenuEnvLogic+MenuStage",false);

                object envObj=null,menuObj=null,terrainObj=null;
                MonoBehaviour[] all=Resources.FindObjectsOfTypeAll<MonoBehaviour>();
                for(int i=0;i<all.Length;i++)
                {
                    MonoBehaviour b=all[i];if(b==null)continue;Type bt=b.GetType();
                    if(env!=null&&bt==env)envObj=b;
                    else if(menu!=null&&bt==menu)menuObj=b;
                    else if(terrain!=null&&bt==terrain)terrainObj=b;
                }

                DumpTypeDeep(sb,env,envObj,"MainMenuEnvLogic COMPLET");
                DumpTypeDeep(sb,stage,null,"MainMenuEnvLogic.MenuStage COMPLET");
                DumpTypeDeep(sb,terrain,terrainObj,"MainMenuTerrainSelector COMPLET");
                DumpTypeDeep(sb,menu,menuObj,"MainMenu COMPLET");
                DumpTypeDeep(sb,random,null,"MenuRandomKerbalAnims COMPLET");
                DumpTypeDeep(sb,expr,null,"MainMenuExpressionManager COMPLET");

                sb.AppendLine("=== HIÉRARCHIE ORBITSCENE ===");
                DumpHierarchyForStageLab(sb,"OrbitScene");
                sb.AppendLine("=== HIÉRARCHIE MUNSCENE ===");
                DumpHierarchyForStageLab(sb,"MunScene");

                File.WriteAllText(path,sb.ToString());
                Status="Stage Lab écrit : "+Path.GetFileName(path);
                SceneEditorLog.Info(Status);
            }
            catch(Exception ex)
            {
                Status="Stage Lab impossible : "+ex.Message;
                SceneEditorLog.Warn(Status);
            }
        }

        private void DumpHierarchyForStageLab(StringBuilder sb,string rootName)
        {
            GameObject[] all=Resources.FindObjectsOfTypeAll<GameObject>();
            GameObject root=null;
            for(int i=0;i<all.Length;i++)if(all[i]!=null&&all[i].transform.parent==null&&string.Equals(all[i].name,rootName,StringComparison.OrdinalIgnoreCase)){root=all[i];break;}
            if(root==null){sb.AppendLine(rootName+" introuvable");return;}
            Transform[] tt=root.GetComponentsInChildren<Transform>(true);
            sb.AppendLine("ROOT "+root.name+" activeSelf="+root.activeSelf+" activeHierarchy="+root.activeInHierarchy+" transforms="+tt.Length);
            for(int i=0;i<tt.Length;i++)
            {
                Transform t=tt[i];if(t==null)continue;
                Animation an=t.GetComponent<Animation>();
                Animator ar=t.GetComponent<Animator>();
                MonoBehaviour[] mb=t.GetComponents<MonoBehaviour>();
                if(an==null&&ar==null&&mb.Length==0&&t.childCount>0)continue;
                sb.Append("NODE "+ScenePath.Get(t)+" | active="+t.gameObject.activeSelf);
                if(an!=null)
                {
                    sb.Append(" | Animation enabled="+an.enabled+" playing="+an.isPlaying);
                    try
                    {
                        List<string> clips=new List<string>();
                        foreach(AnimationState st in an)if(st!=null)clips.Add(st.name+"(playing="+an.IsPlaying(st.name)+")");
                        if(clips.Count>0)sb.Append(" clips=["+string.Join(",",clips.ToArray())+"]");
                    }catch{}
                }
                if(ar!=null)sb.Append(" | Animator enabled="+ar.enabled+" speed="+ar.speed);
                if(mb.Length>0)
                {
                    List<string> names=new List<string>();
                    for(int j=0;j<mb.Length;j++)if(mb[j]!=null)names.Add(mb[j].GetType().FullName+"(enabled="+mb[j].enabled+")");
                    if(names.Count>0)sb.Append(" | MB=["+string.Join(",",names.ToArray())+"]");
                }
                sb.AppendLine();
            }
            sb.AppendLine();
        }

        private object FindMainMenuEnvLogicInstance(out Type envType)
        {
            envType=nativeEnvTypeCache;
            try
            {
                MonoBehaviour cached=nativeEnvInstanceCache as MonoBehaviour;
                if(cached!=null&&nativeEnvTypeCache!=null)
                {
                    envType=nativeEnvTypeCache;
                    return cached;
                }

                Assembly ac=null;
                Assembly[] aa=AppDomain.CurrentDomain.GetAssemblies();
                for(int i=0;i<aa.Length;i++)
                    if(string.Equals(aa[i].GetName().Name,"Assembly-CSharp",StringComparison.OrdinalIgnoreCase)){ac=aa[i];break;}
                if(ac==null)return null;

                envType=ac.GetType("MainMenuEnvLogic",false);
                if(envType==null)return null;

                MonoBehaviour[] all=Resources.FindObjectsOfTypeAll<MonoBehaviour>();
                for(int i=0;i<all.Length;i++)
                {
                    MonoBehaviour b=all[i];
                    if(b!=null&&b.GetType()==envType)
                    {
                        nativeEnvInstanceCache=b;
                        nativeEnvTypeCache=envType;
                        return b;
                    }
                }
            }catch{}
            return null;
        }

        private GameObject[] GetNativeAreas(object env,Type envType)
        {
            if(env==null||envType==null)return new GameObject[0];
            try
            {
                FieldInfo f=envType.GetField("areas",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
                return (f!=null?f.GetValue(env) as GameObject[]:null)??new GameObject[0];
            }catch{return new GameObject[0];}
        }

        private int GetNativeCurrentStage(object env,Type envType)
        {
            if(env==null||envType==null)return -1;
            try
            {
                FieldInfo f=envType.GetField("currentStage",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
                return f!=null?(int)f.GetValue(env):-1;
            }catch{return -1;}
        }

        private void RefreshNativeMainMenuState()
        {
            if(Time.realtimeSinceStartup<nativeStateNextRefresh)return;
            nativeStateNextRefresh=Time.realtimeSinceStartup+0.50f;
            try
            {
                Type t;object env=FindMainMenuEnvLogicInstance(out t);
                if(env==null||t==null)
                {
                    nativeAreaNames=new string[0];nativeAreaIndex=-1;nativeStageIndex=-1;
                    nativeSceneState="MainMenuEnvLogic introuvable";return;
                }

                GameObject[] areas=GetNativeAreas(env,t);
                nativeAreaNames=new string[areas.Length];
                nativeAreaIndex=-1;
                string active="";
                for(int i=0;i<areas.Length;i++)
                {
                    GameObject go=areas[i];
                    nativeAreaNames[i]=go!=null?go.name:"AREA "+i+" <null>";
                    if(go!=null&&go.activeInHierarchy){nativeAreaIndex=i;active=go.name;}
                }

                nativeStageIndex=GetNativeCurrentStage(env,t);

                if(nativeAreaIndex<0)
                {
                    FieldInfo sf=t.GetField("startingArea",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
                    GameObject start=sf!=null?sf.GetValue(env) as GameObject:null;
                    if(start!=null)
                    {
                        for(int i=0;i<areas.Length;i++)
                            if(areas[i]==start){nativeAreaIndex=i;active=start.name;break;}
                    }
                }

                nativeSceneState=(nativeAreaIndex>=0?("AREA "+nativeAreaIndex+" "+active):"AREA ?")+" / STAGE "+nativeStageIndex;
            }
            catch(Exception ex){nativeSceneState="Erreur natif : "+ex.GetType().Name;}
        }

        private void ForceRefreshNativeMainMenuState()
        {
            nativeStateNextRefresh=0f;
            RefreshNativeMainMenuState();
        }

        private void ReactivateNativeAnimations(GameObject area)
        {
            if(area==null)return;
            try
            {
                Animation[] aa=area.GetComponentsInChildren<Animation>(true);
                for(int i=0;i<aa.Length;i++)if(aa[i]!=null)aa[i].enabled=true;

                Animator[] ar=area.GetComponentsInChildren<Animator>(true);
                for(int i=0;i<ar.Length;i++)if(ar[i]!=null)ar[i].enabled=true;

                MonoBehaviour[] mb=area.GetComponentsInChildren<MonoBehaviour>(true);
                for(int i=0;i<mb.Length;i++)
                {
                    MonoBehaviour b=mb[i];if(b==null)continue;
                    string n=b.GetType().FullName??b.GetType().Name;
                    if(n=="MainMenuTerrainSelector"||n=="MenuRandomKerbalAnims"||n=="MainMenuExpressionManager")
                        b.enabled=true;
                }
            }catch{}
        }

        private IEnumerator RefreshNativeContextDelayed(float delay)
        {
            if(delay>0f)yield return new WaitForSecondsRealtime(delay);
            ForceRefreshNativeMainMenuState();
            RebuildKerbalRegistry(true);
            RefreshObjects();
        }

        internal void SelectNativeContext(int areaIndex,int stageIndex)
        {
            StartCoroutine(SelectNativeContextRoutine(areaIndex,stageIndex));
        }

        private void CleanLiveWorkspaceObjectsWithoutChangingNativeState()
        {
            if(!BaselineReady)return;

            try
            {
                RestoreOriginalSkybox();
                RenderSettings.ambientLight=baselineAmbientLight;
                List<Transform> editedCopy=new List<Transform>(edited);
                for(int i=0;i<editedCopy.Count;i++)
                {
                    Transform t=editedCopy[i];
                    if(t==null)continue;
                    RestoreRendererTextures(t);
                    RestoreTextStateForTransform(t);
                    RestoreKerbalLiveOffset(t);
                    baseline.RestoreOne(t);
                    edited.Remove(t);
                    editedContextOwners.Remove(t);
                }

                List<GameObject> createdCopy=new List<GameObject>(created);
                for(int i=0;i<createdCopy.Count;i++)
                {
                    GameObject go=createdCopy[i];
                    if(go==null)continue;
                    RemoveCreatedTracking(go);
                    UnityEngine.Object.Destroy(go);
                }

                created.Clear();createdContextOwners.Clear();
                spawnedCrafts.Clear();spawnedLights.Clear();spawnedPlanets.Clear();spawnedTexts.Clear();spawnedImages.Clear();
                ClearKerbalPivotsAfterBaselineRestore();
                Selected=null;
                history.Clear();
            }
            catch(Exception ex){SceneEditorLog.Warn("Workspace clean: "+ex.Message);}
        }

        private IEnumerator SelectNativeContextRoutine(int areaIndex,int stageIndex)
        {
            contextTransitionInProgress=true;
            ForceRefreshNativeMainMenuState();

            int sourceArea=nativeAreaIndex;
            int sourceStage=nativeStageIndex;
            bool sourceRare=nativeSandcastleActive;
            bool targetRare=(areaIndex==0)&&nativeSandcastleActive;

            string sourceKey=BuildContextKey(sourceArea,sourceStage,sourceRare);
            string targetKey=BuildContextKey(areaIndex,stageIndex,targetRare);
            string targetLabel=BuildContextLabel(areaIndex,stageIndex,targetRare);

            bool changing=sourceArea!=areaIndex||sourceStage!=stageIndex;
            if(!changing)
            {
                RebuildKerbalRegistry(true);
                RefreshObjects();
                persistentObservedContextKey=sourceKey;
                contextTransitionInProgress=false;
                yield break;
            }

            if(BaselineReady)
                CaptureSessionWorkspaceForContext(sourceArea,sourceStage,sourceRare,sourceKey);

            bool sameArea=sourceArea==areaIndex;

            if(sameArea)
            {
                SelectNativeMainMenuStage(stageIndex);
                yield return new WaitForSecondsRealtime(0.80f);
            }
            else
            {
                SelectNativeMainMenuArea(areaIndex);
                yield return new WaitForSecondsRealtime(0.45f);
                SelectNativeMainMenuStage(stageIndex);
                yield return new WaitForSecondsRealtime(0.75f);
            }

            if(BaselineReady)
            {
                CleanLiveWorkspaceObjectsWithoutChangingNativeState();
                yield return null;
            }

            ForceRefreshNativeMainMenuState();
            RebuildKerbalRegistry(true);
            RefreshObjects();

            if(!applyingContextProfile)
                yield return StartCoroutine(RestorePreferredWorkspaceByKeyDelayed(targetKey,targetLabel,0f));

            persistentObservedContextKey=targetKey;
            persistentCandidateContextKey=null;
            contextTransitionInProgress=false;
        }

        internal void ResetNativeContext(int areaIndex,int stageIndex,bool rare)
        {
            StartCoroutine(ResetNativeContextRoutine(areaIndex,stageIndex,rare));
        }
        private IEnumerator ResetNativeContextRoutine(int areaIndex,int stageIndex,bool rare)
        {
            applyingContextProfile=true;
            yield return StartCoroutine(SelectNativeContextRoutine(areaIndex,stageIndex));
            if(areaIndex==0&&rare!=nativeSandcastleActive){SetNativeSandcastleVariant(rare);yield return new WaitForSecondsRealtime(0.25f);}
            RestoreCurrentNativeContext();yield return new WaitForSecondsRealtime(0.85f);
            LoadContextProfiles();contextProfiles.Remove(BuildContextKey(areaIndex,stageIndex,rare));SaveContextProfiles();
            applyingContextProfile=false;Status="État restauré à KSP original : "+BuildContextLabel(areaIndex,stageIndex,rare);
        }

        internal void CycleNativeContext(int direction)
        {
            ForceRefreshNativeMainMenuState();
            int slot=0;
            if(nativeAreaIndex==1&&nativeStageIndex==0)slot=0;
            else if(nativeAreaIndex==1&&nativeStageIndex==1)slot=1;
            else if(nativeAreaIndex==0&&nativeStageIndex==0)slot=2;
            else if(nativeAreaIndex==0&&nativeStageIndex==1)slot=3;
            slot=(slot+(direction>=0?1:-1)+4)%4;
            if(slot==0)SelectNativeContext(1,0);
            else if(slot==1)SelectNativeContext(1,1);
            else if(slot==2)SelectNativeContext(0,0);
            else SelectNativeContext(0,1);
        }

        internal void SelectNativeMainMenuArea(int targetIndex)
        {
            try
            {
                Type t;object env=FindMainMenuEnvLogicInstance(out t);
                if(env==null||t==null){Status="MainMenuEnvLogic introuvable";return;}

                GameObject[] areas=GetNativeAreas(env,t);
                if(targetIndex<0||targetIndex>=areas.Length||areas[targetIndex]==null)
                {Status="AREA KSP invalide : "+targetIndex;return;}

                for(int i=0;i<areas.Length;i++)
                    if(areas[i]!=null)areas[i].SetActive(i==targetIndex);

                FieldInfo sf=t.GetField("startingArea",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
                if(sf!=null)sf.SetValue(env,areas[targetIndex]);

                ReactivateNativeAnimations(areas[targetIndex]);

                MethodInfo cur=t.GetMethod("GoCurrentStage",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
                if(cur!=null)cur.Invoke(env,null);

                workspaceRootName=areas[targetIndex].name;
ForceRefreshNativeMainMenuState();
                StartCoroutine(RefreshNativeContextDelayed(0.35f));
                Status="Scène sélectionnée : "+nativeSceneState;
            }
            catch(Exception ex){Status="Changement AREA impossible : "+ex.Message;SceneEditorLog.Warn(Status);}
        }

        internal void SelectNativeMainMenuStage(int stageIndex)
        {
            try
            {
                Type t;object env=FindMainMenuEnvLogicInstance(out t);
                if(env==null||t==null){Status="MainMenuEnvLogic introuvable";return;}

                FieldInfo piv=t.GetField("camPivots",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
                Array arr=piv!=null?piv.GetValue(env) as Array:null;
                int count=arr!=null?arr.Length:0;
                if(stageIndex<0||stageIndex>=count)
                {Status="STAGE KSP invalide : "+stageIndex+" / "+count;return;}

                MethodInfo go=t.GetMethod("GoToStage",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,new Type[]{typeof(int)},null);
                if(go==null){Status="GoToStage(int) introuvable";return;}
                go.Invoke(env,new object[]{stageIndex});

                ForceRefreshNativeMainMenuState();
                StartCoroutine(RefreshNativeContextDelayed(0.70f));
                Status="État sélectionné : "+nativeSceneState;
            }
            catch(Exception ex){Status="Changement STAGE impossible : "+ex.Message;SceneEditorLog.Warn(Status);}
        }

        internal void ExportNativeMainMenuCatalog()
        {
            try
            {
                string dir=Path.Combine(KSPUtil.ApplicationRootPath,"GameData","KSPSceneEditor","PluginData","Diagnostics");
                Directory.CreateDirectory(dir);
                string path=Path.Combine(dir,"NativeMainMenuCatalog_"+DateTime.Now.ToString("yyyyMMdd_HHmmss")+".txt");
                StringBuilder sb=new StringBuilder();
                sb.AppendLine("KSP SCENE EDITOR - NATIVE MAIN MENU CATALOG");
                sb.AppendLine("Generated UTC: "+DateTime.UtcNow.ToString("o"));
                sb.AppendLine("HighLogic.LoadedScene: "+HighLogic.LoadedScene);
                sb.AppendLine();

                sb.AppendLine("=== PREUVES RUNTIME / RACINES UNITY ===");
                GameObject[] gos=Resources.FindObjectsOfTypeAll<GameObject>();
                List<GameObject> roots=new List<GameObject>();
                for(int i=0;i<gos.Length;i++)
                {
                    GameObject go=gos[i];if(go==null||go.transform.parent!=null)continue;
                    string n=(go.name??string.Empty).ToLowerInvariant();
                    if(n.Contains("orbit")||n.Contains("mun")||n.Contains("menu")||n.Contains("scene")||n.Contains("scenery")||n.Contains("landscape"))
                        roots.Add(go);
                }
                roots.Sort(delegate(GameObject a,GameObject b){return string.Compare(a.name,b.name,StringComparison.OrdinalIgnoreCase);});
                for(int i=0;i<roots.Count;i++)
                {
                    GameObject go=roots[i];
                    Renderer[] rr=go.GetComponentsInChildren<Renderer>(true);
                    Animation[] aa=go.GetComponentsInChildren<Animation>(true);
                    Animator[] ar=go.GetComponentsInChildren<Animator>(true);
                    MonoBehaviour[] mb=go.GetComponentsInChildren<MonoBehaviour>(true);
                    sb.AppendLine("ROOT "+go.name+" | active="+go.activeInHierarchy+" | renderers="+rr.Length+
                        " | Animation="+aa.Length+" | Animator="+ar.Length+" | MonoBehaviour="+mb.Length);
                }
                sb.AppendLine();

                sb.AppendLine("=== TYPES KSP / MENU / SCENE ===");
                Assembly[] assemblies=AppDomain.CurrentDomain.GetAssemblies();
                int typeCount=0;
                for(int ai=0;ai<assemblies.Length;ai++)
                {
                    Assembly ass=assemblies[ai];
                    string an=ass.GetName().Name??string.Empty;
                    // Concentrate on KSP/Assembly-CSharp and addon types that actually mention menu scenes.
                    Type[] types;
                    try{types=ass.GetTypes();}catch(ReflectionTypeLoadException ex){types=ex.Types;}catch{continue;}
                    if(types==null)continue;
                    for(int ti=0;ti<types.Length;ti++)
                    {
                        Type t=types[ti];if(t==null||!LooksLikeMainMenuSceneType(t))continue;
                        typeCount++;
                        sb.AppendLine("TYPE "+(t.FullName??t.Name)+" | assembly="+an+
                            " | base="+(t.BaseType!=null?t.BaseType.FullName:"<none>")+
                            " | enum="+t.IsEnum);

                        if(t.IsEnum)
                        {
                            string[] names=Enum.GetNames(t);
                            Array vals=Enum.GetValues(t);
                            for(int ei=0;ei<names.Length;ei++)sb.AppendLine("  ENUM "+names[ei]+" = "+Convert.ToInt64(vals.GetValue(ei)));
                        }

                        FieldInfo[] fs=t.GetFields(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance);
                        for(int fi=0;fi<fs.Length;fi++)
                        {
                            FieldInfo f=fs[fi];
                            string fn=f.Name.ToLowerInvariant();
                            if(fn.Contains("scene")||fn.Contains("menu")||fn.Contains("stage")||fn.Contains("orbit")||fn.Contains("mun")||
                               fn.Contains("kerbal")||fn.Contains("landscape")||fn.Contains("camera"))
                                sb.AppendLine("  FIELD "+f.FieldType.FullName+" "+f.Name+" | static="+f.IsStatic);
                        }

                        PropertyInfo[] ps=t.GetProperties(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance);
                        for(int pi=0;pi<ps.Length;pi++)
                        {
                            PropertyInfo p=ps[pi];
                            string pn=p.Name.ToLowerInvariant();
                            if(pn.Contains("scene")||pn.Contains("menu")||pn.Contains("stage")||pn.Contains("orbit")||pn.Contains("mun"))
                                sb.AppendLine("  PROP "+p.PropertyType.FullName+" "+p.Name);
                        }
                    }
                }
                nativeSceneCatalogTypeCount=typeCount;
                sb.AppendLine();

                sb.AppendLine("=== INSTANCES ACTIVES DES TYPES CANDIDATS ===");
                MonoBehaviour[] allMb=Resources.FindObjectsOfTypeAll<MonoBehaviour>();
                int instanceCount=0;
                for(int i=0;i<allMb.Length;i++)
                {
                    MonoBehaviour b=allMb[i];if(b==null)continue;
                    Type t=b.GetType();if(!LooksLikeMainMenuSceneType(t))continue;
                    instanceCount++;
                    sb.AppendLine("INSTANCE "+t.FullName+" | enabled="+b.enabled+" | active="+b.gameObject.activeInHierarchy+
                        " | path="+ScenePath.Get(b.transform));

                    FieldInfo[] fs=t.GetFields(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance);
                    for(int fi=0;fi<fs.Length;fi++)
                    {
                        FieldInfo f=fs[fi];
                        string fn=f.Name.ToLowerInvariant();
                        if(fn.Contains("scene")||fn.Contains("menu")||fn.Contains("stage")||fn.Contains("orbit")||fn.Contains("mun")||
                           fn.Contains("kerbal")||fn.Contains("landscape")||fn.Contains("camera")||fn.Contains("current")||fn.Contains("active"))
                            sb.AppendLine("  "+f.Name+" = "+SafeMemberValue(b,f));
                    }

                    PropertyInfo[] ps=t.GetProperties(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance);
                    for(int pi=0;pi<ps.Length;pi++)
                    {
                        PropertyInfo p=ps[pi];
                        string pn=p.Name.ToLowerInvariant();
                        if(pn.Contains("scene")||pn.Contains("menu")||pn.Contains("stage")||pn.Contains("current")||pn.Contains("active"))
                            sb.AppendLine("  PROP "+p.Name+" = "+SafePropertyValue(b,p));
                    }
                }
                nativeSceneCatalogInstanceCount=instanceCount;

                sb.AppendLine();
                sb.AppendLine("=== RACINES CONNUES SCENE EDITOR ===");
                string[] known=GetKnownMenuSceneRoots();
                for(int i=0;i<known.Length;i++)sb.AppendLine(known[i]+" | "+GetSceneProfile(known[i]));

                File.WriteAllText(path,sb.ToString());
                nativeSceneCatalogSummary=typeCount+" type(s) / "+instanceCount+" contrôleur(s) candidat(s)";
                Status="Catalogue natif écrit : "+Path.GetFileName(path);
                SceneEditorLog.Info(Status);
            }
            catch(Exception ex)
            {
                nativeSceneCatalogSummary="Erreur catalogue : "+ex.GetType().Name;
                Status="Catalogue natif impossible : "+ex.Message;
                SceneEditorLog.Warn(Status);
            }
        }

        internal void ExportDiagnosticLab()
        {
            try
            {
                string dir=Path.Combine(KSPUtil.ApplicationRootPath,"GameData","KSPSceneEditor","PluginData","Diagnostics");
                Directory.CreateDirectory(dir);
                string path=Path.Combine(dir,"DiagnosticLab_"+DateTime.Now.ToString("yyyyMMdd_HHmmss")+".txt");
                StringBuilder sb=new StringBuilder();
                sb.AppendLine("KSP SCENE EDITOR V0.28 DIAGNOSTIC LAB");
                sb.AppendLine("UTC="+DateTime.UtcNow.ToString("o"));
                sb.AppendLine("Workspace="+workspaceRootName+" | Preview="+scenePreviewActive);
                sb.AppendLine();

                sb.AppendLine("=== KERBALS ===");
                Transform[] all=Resources.FindObjectsOfTypeAll<Transform>();
                int kc=0;
                for(int i=0;i<all.Length;i++)
                {
                    Transform t=all[i];if(t==null||!IsKnownKerbal(t))continue;
                    // Only logical roots: ignore children which resolve to the same known Kerbal.
                    Transform p=t.parent;if(p!=null&&IsKnownKerbal(p))continue;
                    kc++;
                    Camera cam=FindCameraForTarget(t);
                    Vector3 sp=cam!=null?cam.WorldToScreenPoint(t.position):Vector3.zero;
                    sb.AppendLine("KERBAL "+kc+" name="+t.name+" path="+ScenePath.Get(t));
                    sb.AppendLine(" rootPos="+t.position+" local="+t.localPosition+" screen="+sp+" active="+t.gameObject.activeInHierarchy);
                    Animation[] aa=t.GetComponentsInChildren<Animation>(true);
                    Animator[] ar=t.GetComponentsInChildren<Animator>(true);
                    Renderer[] rr=t.GetComponentsInChildren<Renderer>(true);
                    sb.AppendLine(" Animation="+aa.Length+" Animator="+ar.Length+" Renderer="+rr.Length);
                    Component[] cc=t.GetComponentsInChildren<Component>(true);
                    for(int c=0;c<cc.Length;c++)
                    {
                        Component cp=cc[c];if(cp==null)continue;
                        string n=cp.GetType().FullName??cp.GetType().Name;
                        if(n.IndexOf("Kerbal",StringComparison.OrdinalIgnoreCase)>=0||
                           n.IndexOf("Anim",StringComparison.OrdinalIgnoreCase)>=0||
                           n.IndexOf("Menu",StringComparison.OrdinalIgnoreCase)>=0)
                            sb.AppendLine("  component="+n+" @ "+ScenePath.Get(cp.transform));
                    }
                }
                sb.AppendLine("Kerbal roots="+kc);
                sb.AppendLine();

                sb.AppendLine("=== MENU / ANIMATION CONTROLLERS ===");
                MonoBehaviour[] mb=Resources.FindObjectsOfTypeAll<MonoBehaviour>();
                int mc=0;
                for(int i=0;i<mb.Length&&mc<300;i++)
                {
                    MonoBehaviour b=mb[i];if(b==null)continue;
                    string n=b.GetType().FullName??b.GetType().Name;
                    if(n.IndexOf("Menu",StringComparison.OrdinalIgnoreCase)<0&&
                       n.IndexOf("Anim",StringComparison.OrdinalIgnoreCase)<0&&
                       n.IndexOf("Scene",StringComparison.OrdinalIgnoreCase)<0)continue;
                    sb.AppendLine(n+" | enabled="+b.enabled+" | active="+b.gameObject.activeInHierarchy+" | "+ScenePath.Get(b.transform));
                    mc++;
                }
                sb.AppendLine();

                sb.AppendLine("=== CELESTIAL BODIES / SCALED SPACE ===");
                if(FlightGlobals.Bodies!=null)
                for(int bi=0;bi<FlightGlobals.Bodies.Count;bi++)
                {
                    CelestialBody body=FlightGlobals.Bodies[bi];if(body==null)continue;
                    Transform scaled=FindCelestialScaledVisual(body.bodyName);
                    sb.AppendLine("BODY "+body.bodyName+" scaled="+(scaled!=null?ScenePath.Get(scaled):"<null>"));
                    if(scaled==null)continue;
                    Renderer[] rr=scaled.GetComponentsInChildren<Renderer>(true);
                    for(int ri=0;ri<rr.Length;ri++)
                    {
                        Renderer r=rr[ri];if(r==null)continue;
                        sb.AppendLine(" renderer="+r.name+" type="+r.GetType().FullName+" enabled="+r.enabled+" materials="+r.sharedMaterials.Length);
                        Material[] mats=r.sharedMaterials;
                        for(int mi=0;mi<mats.Length;mi++)
                        {
                            Material mat=mats[mi];if(mat==null){sb.AppendLine("  mat["+mi+"]=<null>");continue;}
                            sb.AppendLine("  mat["+mi+"]="+mat.name+" shader="+(mat.shader!=null?mat.shader.name:"<null>")+" id="+mat.GetInstanceID());
                            string[] props={"_MainTex","_ColorMap","_Diffuse","_BaseMap","_PlanetTex","_BumpMap","_NormalMap","_Opacity"};
                            for(int pi=0;pi<props.Length;pi++)
                            {
                                string prop=props[pi];if(!mat.HasProperty(prop))continue;
                                if(prop=="_Opacity")sb.AppendLine("   "+prop+"="+mat.GetFloat(prop));
                                else
                                {
                                    Texture tx=mat.GetTexture(prop);
                                    sb.AppendLine("   "+prop+"="+(tx!=null?(tx.name+" "+tx.width+"x"+tx.height+" id="+tx.GetInstanceID()):"<null>"));
                                }
                            }
                        }
                    }
                }

                File.WriteAllText(path,sb.ToString());
                Status="DIAGNOSTIC LAB écrit : "+Path.GetFileName(path);
                SceneEditorLog.Info(Status+" | "+path);
            }
            catch(Exception ex){Status="Diagnostic Lab impossible : "+ex.Message;SceneEditorLog.Warn(Status);}
        }

        internal void ExportDiagnostics()
        {
            try
            {
                string d=Path.Combine(KSPUtil.ApplicationRootPath,"GameData","KSPSceneEditor","Diagnostics");
                Directory.CreateDirectory(d);
                string path=Path.Combine(d,"scene_"+DateTime.Now.ToString("yyyyMMdd_HHmmss")+".txt");
                using(StreamWriter writer=new StreamWriter(path,false))
                {
                    writer.WriteLine("KSP Scene Editor diagnostic V0.20");
                    writer.WriteLine("Workspace: "+workspaceRootName);
                    writer.WriteLine("Preview: "+scenePreviewActive);
                    writer.WriteLine("Scene entries: "+sceneEntries.Count);
                    writer.WriteLine("Baseline: "+baseline.Count);
                    writer.WriteLine();
                    writer.WriteLine("[CACHED COMPOSITIONS]");
                    for(int i=0;i<cachedScenes.Length;i++)
                    {
                        Transform t;cachedSceneObjects.TryGetValue(cachedScenes[i],out t);
                        writer.WriteLine(cachedScenes[i]+" | active="+(t!=null&&t.gameObject.activeInHierarchy)+" | "+GetSceneProfile(cachedScenes[i])+" | "+(t!=null?ScenePath.Get(t):"<missing>"));
                    }
                    writer.WriteLine();
                    writer.WriteLine("[CELESTIAL BODIES]");
                    for(int i=0;i<cachedBodies.Length;i++)
                    {
                        Transform scaled=FindCelestialScaledVisual(cachedBodies[i]);
                        Texture2D tex=FindLoadedPlanetTexture(cachedBodies[i]);
                        writer.WriteLine(cachedBodies[i]+" | scaled="+(scaled!=null?ScenePath.Get(scaled):"<none>")+" | texture="+(tex!=null?(tex.name+" "+tex.width+"x"+tex.height):"<fallback>"));
                    }
                    writer.WriteLine();
                    writer.WriteLine("[SPAWNED PLANETS]");
                    for(int i=0;i<spawnedPlanets.Count;i++)
                    {
                        SpawnedPlanet p=spawnedPlanets[i];
                        writer.WriteLine((p!=null?p.BodyName:"<null>")+" | root="+(p!=null&&p.Root!=null?ScenePath.Get(p.Root.transform):"<missing>"));
                    }
                    writer.WriteLine();
                    writer.WriteLine("[SCENE ENTRIES]");
                    for(int i=0;i<sceneEntries.Count;i++)
                    {
                        SceneEntry e=sceneEntries[i];
                        writer.WriteLine(e.Category+" | "+e.FriendlyName+" | "+e.Kind+" | "+e.Path+" | "+e.Components);
                    }

                    writer.WriteLine();
                    writer.WriteLine("[MAIN MENU VISUAL HIERARCHY - DIAGNOSTIC ONLY]");
                    GameObject[] all=Resources.FindObjectsOfTypeAll<GameObject>();
                    List<GameObject> diag=new List<GameObject>();
                    for(int i=0;i<all.Length;i++)
                    {
                        GameObject g=all[i];if(g==null||!ScenePath.InLoadedScene(g.transform))continue;
                        string n=(g.name??string.Empty).ToLowerInvariant();
                        string pn=g.transform.parent!=null?(g.transform.parent.name??string.Empty).ToLowerInvariant():string.Empty;
                        if(n.Contains("scene")||n.Contains("menu")||n.Contains("scenery")||n.Contains("landscape")||n.Contains("orbit")||n.Contains("mun")||
                           pn.Contains("scene")||pn.Contains("menu")||pn.Contains("scenery"))
                            diag.Add(g);
                    }
                    diag.Sort(delegate(GameObject a,GameObject b){return TransformDepth(a.transform).CompareTo(TransformDepth(b.transform));});
                    for(int i=0;i<Mathf.Min(180,diag.Count);i++)
                    {
                        GameObject g=diag[i];
                        Renderer[] r=g.GetComponentsInChildren<Renderer>(true);
                        Animation[] a=g.GetComponentsInChildren<Animation>(true);
                        Animator[] ar=g.GetComponentsInChildren<Animator>(true);
                        Camera[] c=g.GetComponentsInChildren<Camera>(true);
                        writer.WriteLine((g.activeInHierarchy?"ACTIVE ":"OFF    ")+" | d="+TransformDepth(g.transform)+" | R="+r.Length+" A="+(a.Length+ar.Length)+" C="+c.Length+" | "+ScenePath.Get(g.transform));
                    }
                }
                Status="Diagnostic exporté : "+Path.GetFileName(path);
            }
            catch(Exception ex){Status="Diagnostic impossible : "+ex.Message;}
        }

        internal Camera FindLandscapeCamera()
        {
            Camera cached;
            if(cachedSceneCameras.TryGetValue(workspaceRootName,out cached)&&cached!=null)return cached;
            GameObject go=GameObject.Find("Landscape Camera");if(go!=null){Camera c=go.GetComponent<Camera>();if(c!=null)return c;}
            Camera[] all=Camera.allCameras;for(int i=0;i<all.Length;i++)if(all[i]!=null&&all[i].enabled&&ScenePath.InLoadedScene(all[i].transform))return all[i];
            return Camera.main;
        }
        private Transform FindPreferredSceneRoot()
        {
            Transform t=FindSceneRoot(workspaceRootName);
            if(t!=null&&t.gameObject.activeInHierarchy)return t;
            string active=ActiveCanonicalRootKey();
            if(!string.IsNullOrEmpty(active)){Transform a=FindSceneRoot(active);if(a!=null)return a;}
            if(t!=null)return t;
            GameObject orbit=GameObject.Find("OrbitScene");return orbit!=null?orbit.transform:null;
        }

        internal void BeginPickMode()
        {
            if(window==null)return; pickMode=true; window.Visible=false; Status="PICK 3D: click one visible object";
        }

        private void EndPickMode()
        {
            if(!pickMode)return; pickMode=false; if(window!=null)window.Visible=true;
        }

        private void TryScenePick()
        {
            if(window==null||!Input.GetMouseButtonDown(0))return;
            Vector2 guiMouse=new Vector2(Input.mousePosition.x,Screen.height-Input.mousePosition.y);
            if(!pickMode && window.ContainsScreenPoint(guiMouse))return;
            Camera cam=FindLandscapeCamera();if(cam==null)return;

            Transform picked = PickEditableAtMouse(cam, Input.mousePosition);
            if(picked!=null){Selected=picked;Status="Picked editable target: "+FriendlyNameOf(picked);EndPickMode();}
            else {Status="No editable target under cursor";EndPickMode();}
        }

        private string FriendlyNameOf(Transform t)
        {
            for(int i=0;i<sceneEntries.Count;i++)if(sceneEntries[i]!=null&&sceneEntries[i].Transform==t)return sceneEntries[i].FriendlyName;
            return t!=null?t.name:"<none>";
        }

        private Transform FindRegisteredTarget(Transform hit)
        {
            if(hit==null)return null;
            // A click hits the live stock Kerbal before editing. Route that hit to its invisible
            // editor proxy; the proxy becomes visible only when the drag actually begins.
            Transform curActor=hit;int actorGuard=0;
            while(curActor!=null&&actorGuard++<64)
            {
                Transform proxy;
                if(kerbalActorToPivot.TryGetValue(curActor,out proxy)&&proxy!=null)return proxy;
                curActor=curActor.parent;
            }
            Transform best=null;int bestDepth=int.MaxValue;
            for(int i=0;i<sceneEntries.Count;i++)
            {
                SceneEntry e=sceneEntries[i];if(e==null||e.Transform==null)continue;
                Transform cur=hit;int depth=0;
                while(cur!=null&&depth<128){if(cur==e.Transform){if(depth<bestDepth){best=e.Transform;bestDepth=depth;}break;}cur=cur.parent;depth++;}
            }
            return best;
        }

        private Vector3 GetStableKerbalPickAnchor(Transform actor)
        {
            if(actor==null)return Vector3.zero;
            Vector3 local;
            if(kerbalPickLocalAnchors.TryGetValue(actor,out local))return actor.TransformPoint(local);

            Vector3 world=actor.position;
            Renderer[] rr=actor.GetComponentsInChildren<Renderer>(true);
            bool any=false;Bounds b=new Bounds(actor.position,Vector3.zero);
            for(int i=0;i<rr.Length;i++)
            {
                Renderer r=rr[i];if(r==null||!r.enabled||!r.gameObject.activeInHierarchy)continue;
                if(!any){b=r.bounds;any=true;}else b.Encapsulate(r.bounds);
            }
            if(any)world=b.center;

            local=actor.InverseTransformPoint(world);
            kerbalPickLocalAnchors[actor]=local;
            return actor.TransformPoint(local);
        }

        private bool TryGetStableKerbalScreenRect(Camera cam,Transform actor,out Rect rect)
        {
            rect=new Rect();if(cam==null||actor==null)return false;
            Vector3 anchor=GetStableKerbalPickAnchor(actor);
            Vector3 sp=cam.WorldToScreenPoint(anchor);if(sp.z<=0f)return false;

            // Editor-owned 2D capsule approximation. Its dimensions depend only on screen
            // resolution and actor scale, never on animated renderer bounds.
            float scale=Mathf.Clamp((Mathf.Abs(actor.lossyScale.x)+Mathf.Abs(actor.lossyScale.y)+Mathf.Abs(actor.lossyScale.z))/3f,0.35f,3f);
            float h=Mathf.Clamp(Screen.height*0.115f*scale,72f,150f);
            float w=Mathf.Clamp(h*0.42f,34f,72f);
            float gx=sp.x-w*0.5f;
            float gy=Screen.height-sp.y-h*0.50f;
            rect=new Rect(gx,gy,w,h);
            return true;
        }

        private void RebuildKerbalRegistry(bool force)
        {
            if(!force&&kerbalRegistryWorkspace==workspaceRootName&&Time.frameCount-kerbalRegistryFrame<300)
            {
                bool valid=true;
                for(int i=0;i<kerbalRegistry.Count;i++)
                    if(kerbalRegistry[i]==null){valid=false;break;}
                if(valid)return;
            }

            kerbalRegistryWorkspace=workspaceRootName;
            kerbalRegistryFrame=Time.frameCount;

            List<Transform> found=new List<Transform>();
            Transform root=FindSceneRoot(workspaceRootName);
            Camera cam=FindLandscapeCamera();

            if(root!=null&&root.gameObject.activeInHierarchy)
            {
                Transform group=root.Find("Kerbals");
                if(group!=null)
                {
                    for(int i=0;i<group.childCount;i++)
                    {
                        Transform t=group.GetChild(i);
                        if(t==null||!t.gameObject.activeInHierarchy||!IsKnownKerbal(t))continue;

                        bool visible=true;
                        if(cam!=null)
                        {
                            Rect r;
                            visible=TryGetStableKerbalScreenRect(cam,t,out r) &&
                                r.xMax>=-20f && r.xMin<=Screen.width+20f &&
                                r.yMax>=-20f && r.yMin<=Screen.height+20f;
                        }
                        if(visible&&!found.Contains(t))found.Add(t);
                    }
                }
            }

            if(cam!=null)
            {
                found.Sort(delegate(Transform x,Transform y)
                {
                    float ax=cam.WorldToScreenPoint(GetStableKerbalPickAnchor(x)).x;
                    float bx=cam.WorldToScreenPoint(GetStableKerbalPickAnchor(y)).x;
                    return ax.CompareTo(bx);
                });
            }

            for(int i=0;i<found.Count;i++)
                if(!kerbalRegistryIds.ContainsKey(found[i]))
                    kerbalRegistryIds[found[i]]=nextKerbalRegistryId++;

            kerbalRegistry.Clear();
            kerbalRegistry.AddRange(found);

            List<Transform> stale=new List<Transform>();
            foreach(KeyValuePair<Transform,int> kv in kerbalRegistryIds)
                if(kv.Key==null)stale.Add(kv.Key);
            for(int i=0;i<stale.Count;i++)
            {
                kerbalRegistryIds.Remove(stale[i]);
                kerbalPickLocalAnchors.Remove(stale[i]);
            }

            SceneEditorLog.Info("KERBAL REGISTRY | workspace="+workspaceRootName+
                " | stage="+nativeStageIndex+" | visible="+kerbalRegistry.Count);
        }

        internal List<Transform> GetLiveKerbalActors()
        {
            RebuildKerbalRegistry(false);
            return new List<Transform>(kerbalRegistry);
        }

        internal int GetKerbalRegistryId(Transform actor)
        {
            if(actor==null)return -1;
            RebuildKerbalRegistry(false);
            int id;return kerbalRegistryIds.TryGetValue(actor,out id)?id:-1;
        }

        internal string FriendlyKerbalName(Transform actor)
        {
            if(actor==null)return "KERBAL";
            string n=(actor.name??string.Empty).ToLowerInvariant();
            if(n.Contains("center"))return "CENTRE";
            if(n.Contains("side"))return "GAUCHE";
            if(n.Contains("inverted"))return "DROITE";
            if(n.Contains("female"))return "FEMME";
            return "KERBAL";
        }

        internal string GetKerbalRegistryStatus()
        {
            RebuildKerbalRegistry(false);
            return kerbalRegistry.Count+" KERBAL(S) ENREGISTRÉ(S)";
        }

        internal bool BeginGuiKerbalMove(Transform actor,Vector2 guiMouse)
        {
            if(actor==null||!IsKnownKerbal(actor))return false;
            if(directDragging)EndDirectDrag();
            Camera cam=FindLandscapeCamera();if(cam==null)return false;

            SelectKerbalActor(actor);
            KerbalLiveOffsetState state=EnsureKerbalLiveOffset(actor);
            if(state==null)return false;

            Vector3 anchor=GetStableKerbalPickAnchor(actor);
            Vector3 projected=cam.WorldToScreenPoint(anchor);
            if(projected.z<=0f)return false;

            guiDragKerbalRuntime=actor;
            guiDragCameraRuntime=cam;
            guiDragScreenDepthRuntime=projected.z;
            guiDragStartMouseRuntime=guiMouse;
            guiDragToolRuntime=directTool;

            Vector3 screen=new Vector3(guiMouse.x,Screen.height-guiMouse.y,guiDragScreenDepthRuntime);
            guiDragStartWorldRuntime=cam.ScreenToWorldPoint(screen);
            guiDragStartPositionRuntime=actor.position;
            guiDragStartOffsetRuntime=state.PositionOffsetWorld;
            guiDragStartRotationOffsetRuntime=state.RotationOffset;
            guiDragStartScaleMultiplierRuntime=state.ScaleMultiplier;

            guiDragActiveRuntime=true;
            Status=DirectToolName+" Kerbal verrouillé : "+FriendlyKerbalName(actor);
            return true;
        }

        internal void UpdateGuiKerbalMove(Vector2 guiMouse)
        {
            if(!guiDragActiveRuntime||guiDragKerbalRuntime==null||guiDragCameraRuntime==null)return;
            KerbalLiveOffsetState state=EnsureKerbalLiveOffset(guiDragKerbalRuntime);
            if(state==null)return;

            Vector2 mouseDelta=guiMouse-guiDragStartMouseRuntime;

            if(guiDragToolRuntime==0)
            {
                // MOVE: true screen-space displacement at the original camera depth.
                Vector3 screen=new Vector3(guiMouse.x,Screen.height-guiMouse.y,guiDragScreenDepthRuntime);
                Vector3 world=guiDragCameraRuntime.ScreenToWorldPoint(screen);
                Vector3 delta=world-guiDragStartWorldRuntime;
                state.PositionOffsetWorld=guiDragStartOffsetRuntime+delta;
                state.OverridePosition=true;
            }
            else if(guiDragToolRuntime==3)
            {
                // DEPTH: vertical mouse movement only, along camera forward.
                float worldPerPixel=
                    2f*Mathf.Max(0.25f,guiDragScreenDepthRuntime)*
                    Mathf.Tan(guiDragCameraRuntime.fieldOfView*Mathf.Deg2Rad*0.5f)/
                    Mathf.Max(1f,Screen.height);
                Vector3 delta=guiDragCameraRuntime.transform.forward*
                    (-mouseDelta.y*worldPerPixel*0.65f);
                state.PositionOffsetWorld=guiDragStartOffsetRuntime+delta;
                state.OverridePosition=true;
            }
            else if(guiDragToolRuntime==1)
            {
                // ROTATION: X = yaw, Y = pitch around the camera axes.
                float yaw=mouseDelta.x*0.18f;
                float pitch=-mouseDelta.y*0.18f;
                Quaternion delta=
                    Quaternion.AngleAxis(yaw,guiDragCameraRuntime.transform.up)*
                    Quaternion.AngleAxis(pitch,guiDragCameraRuntime.transform.right);
                state.RotationOffset=delta*guiDragStartRotationOffsetRuntime;
                state.OverrideRotation=true;
            }
            else if(guiDragToolRuntime==2)
            {
                // SCALE: vertical drag, exponential response with safe bounds.
                float factor=Mathf.Clamp(Mathf.Exp(-mouseDelta.y*0.0040f),0.20f,5f);
                state.ScaleMultiplier=guiDragStartScaleMultiplierRuntime*factor;
                state.OverrideScale=true;
            }

            state.LastComposedPosition=guiDragKerbalRuntime.position;
            state.LastComposedRotation=guiDragKerbalRuntime.rotation;
            state.LastComposedScale=guiDragKerbalRuntime.localScale;
            state.HasLastCompose=true;
            edited.Add(guiDragKerbalRuntime);
        }

        internal void EndGuiKerbalMove()
        {
            if(!guiDragActiveRuntime)return;
            Transform was=guiDragKerbalRuntime;
            string tool=DirectToolName;
            guiDragActiveRuntime=false;
            guiDragKerbalRuntime=null;
            guiDragCameraRuntime=null;
            if(was!=null)Status=tool+" Kerbal terminé : "+FriendlyKerbalName(was);
        }

        internal bool IsGuiMovingKerbal(Transform actor)
        {
            return guiDragActiveRuntime&&guiDragKerbalRuntime==actor;
        }

        internal int GuiKerbalActiveTool { get { return guiDragToolRuntime; } }

        internal void SelectKerbalActor(Transform actor)
        {
            if(actor==null||!IsKnownKerbal(actor)){Status="Kerbal indisponible";return;}
            if(directDragging)EndDirectDrag();
            Selected=actor;
            EnsureKerbalLiveOffset(actor);
            Status="Kerbal sélectionné : "+FriendlyKerbalName(actor);
        }

        internal bool TryGetKerbalInteractionRect(Transform actor,out Rect handle)
        {
            handle=new Rect();
            Camera cam=FindLandscapeCamera();Rect r;
            if(cam==null||actor==null||!TryGetStableKerbalScreenRect(cam,actor,out r))return false;
            float padX=Mathf.Clamp(r.width*0.18f,12f,32f);
            float padY=Mathf.Clamp(r.height*0.12f,10f,28f);
            handle=new Rect(r.xMin-padX,r.yMin-padY,r.width+padX*2f,r.height+padY*2f);
            return true;
        }

        internal bool TryGetKerbalHandleRect(Transform actor,out Rect handle)
        {
            handle=new Rect();
            Camera cam=FindLandscapeCamera();Rect r;
            if(cam==null||actor==null||!TryGetStableKerbalScreenRect(cam,actor,out r))return false;
            handle=new Rect(r.center.x-18f,r.yMin-12f,36f,24f);
            return true;
        }

        private bool EllipseContains(Rect rect,Vector2 point)
        {
            if(rect.width<=0f||rect.height<=0f)return false;
            float nx=(point.x-rect.center.x)/(rect.width*0.5f);
            float ny=(point.y-rect.center.y)/(rect.height*0.5f);
            return nx*nx+ny*ny<=1f;
        }

        private Transform PickStockKerbalExplicit(Camera cam,Vector3 mouse)
        {
            if(cam==null)return null;
            Vector2 gui=new Vector2(mouse.x,Screen.height-mouse.y);
            RebuildKerbalRegistry(false);

            Transform best=null;float bestScore=float.MaxValue;
            for(int i=0;i<kerbalRegistry.Count;i++)
            {
                Transform actor=kerbalRegistry[i];
                if(actor==null||!actor.gameObject.activeInHierarchy)continue;

                Rect r;if(!TryGetStableKerbalScreenRect(cam,actor,out r)||!EllipseContains(r,gui))continue;
                float dx=(gui.x-r.center.x)/(r.width*0.5f);
                float dy=(gui.y-r.center.y)/(r.height*0.5f);
                float score=dx*dx+dy*dy;

                // Tiny deterministic tie-break using permanent registry ID.
                int id;kerbalRegistryIds.TryGetValue(actor,out id);
                score+=id*0.000001f;
                if(score<bestScore){bestScore=score;best=actor;}
            }
            return best;
        }

        private Transform PickRegisteredVisual(Camera cam, Vector3 mouse)
        {
            Vector2 m=new Vector2(mouse.x,Screen.height-mouse.y);
            List<KeyValuePair<Transform,float>> hits=new List<KeyValuePair<Transform,float>>();
            for(int i=0;i<sceneEntries.Count;i++)
            {
                SceneEntry e=sceneEntries[i];if(e==null||e.Transform==null)continue;
                Transform boundsTarget=e.Transform;bool kerbal=IsKerbalPivot(e.Transform);
                if(kerbal&&!activeKerbalProxies.Contains(e.Transform))
                {
                    Transform actor=KerbalActorFromPivot(e.Transform);if(actor!=null)boundsTarget=actor;
                }
                Rect rect;if(!TryGetTargetScreenRect(cam,boundsTarget,out rect)||!rect.Contains(m))continue;
                float area=Mathf.Max(1f,rect.width*rect.height);
                float score=area+(rect.center-m).sqrMagnitude*0.10f;
                if(kerbal)score-=100000000f;
                hits.Add(new KeyValuePair<Transform,float>(e.Transform,score));
            }
            if(hits.Count==0)return null;
            hits.Sort(delegate(KeyValuePair<Transform,float>a,KeyValuePair<Transform,float>b){return a.Value.CompareTo(b.Value);});
            if((m-lastPickGuiPoint).sqrMagnitude<144f)overlapPickIndex=(overlapPickIndex+1)%hits.Count;
            else overlapPickIndex=0;
            lastPickGuiPoint=m;
            return hits[Mathf.Clamp(overlapPickIndex,0,hits.Count-1)].Key;
        }

        private bool TryGetTargetScreenRect(Camera cam, Transform target, out Rect rect)
        {
            rect=new Rect();if(target==null)return false;Renderer[] rs=target.GetComponentsInChildren<Renderer>(true);
            bool any=false;float xmin=float.MaxValue,ymin=float.MaxValue,xmax=float.MinValue,ymax=float.MinValue;
            for(int i=0;i<rs.Length;i++){Renderer r=rs[i];Rect rr;if(r==null||!r.enabled||!r.gameObject.activeInHierarchy||!TryProjectBounds(cam,r.bounds,out rr))continue;any=true;xmin=Mathf.Min(xmin,rr.xMin);ymin=Mathf.Min(ymin,rr.yMin);xmax=Mathf.Max(xmax,rr.xMax);ymax=Mathf.Max(ymax,rr.yMax);}
            if(!any)return false;rect=Rect.MinMaxRect(xmin,ymin,xmax,ymax);return rect.width>2f&&rect.height>2f;
        }

        internal bool TryGetSelectedScreenRect(out Rect rect)
        {
            rect=new Rect(); if(Selected==null)return false; Camera cam=FindLandscapeCamera();if(cam==null)return false;
            Renderer[] rs=Selected.GetComponentsInChildren<Renderer>(true); bool any=false; float xmin=float.MaxValue,ymin=float.MaxValue,xmax=float.MinValue,ymax=float.MinValue;
            for(int i=0;i<rs.Length;i++){Rect rr;if(rs[i]!=null&&rs[i].enabled&&TryProjectBounds(cam,rs[i].bounds,out rr)){any=true;xmin=Mathf.Min(xmin,rr.xMin);ymin=Mathf.Min(ymin,rr.yMin);xmax=Mathf.Max(xmax,rr.xMax);ymax=Mathf.Max(ymax,rr.yMax);}}
            if(!any)return false; rect=Rect.MinMaxRect(xmin,ymin,xmax,ymax);return rect.width>2f&&rect.height>2f;
        }

        private bool TryProjectBounds(Camera cam, Bounds b, out Rect rect)
        {
            rect=new Rect(); Vector3 c=b.center,e=b.extents;
            Vector3[] pts={new Vector3(c.x-e.x,c.y-e.y,c.z-e.z),new Vector3(c.x+e.x,c.y-e.y,c.z-e.z),new Vector3(c.x-e.x,c.y+e.y,c.z-e.z),new Vector3(c.x+e.x,c.y+e.y,c.z-e.z),new Vector3(c.x-e.x,c.y-e.y,c.z+e.z),new Vector3(c.x+e.x,c.y-e.y,c.z+e.z),new Vector3(c.x-e.x,c.y+e.y,c.z+e.z),new Vector3(c.x+e.x,c.y+e.y,c.z+e.z)};
            float xmin=float.MaxValue,ymin=float.MaxValue,xmax=float.MinValue,ymax=float.MinValue; bool any=false;
            for(int i=0;i<pts.Length;i++){Vector3 s=cam.WorldToScreenPoint(pts[i]);if(s.z<=0f)continue;any=true;float gy=Screen.height-s.y;xmin=Mathf.Min(xmin,s.x);xmax=Mathf.Max(xmax,s.x);ymin=Mathf.Min(ymin,gy);ymax=Mathf.Max(ymax,gy);}
            if(!any)return false;rect=Rect.MinMaxRect(xmin,ymin,xmax,ymax);return rect.width>1f&&rect.height>1f;
        }

        private bool IsCharacterLike(Transform t)
        {
            if(t==null)return false;
            if(t.GetComponent<Animator>()!=null||t.GetComponent<Animation>()!=null)return true;
            if(t.GetComponent<SkinnedMeshRenderer>()!=null)
            {
                string p=ScenePath.Get(t).ToLowerInvariant();
                if(p.Contains("orbit")||p.Contains("menu")||p.Contains("kerbal")||p.Contains("eva"))return true;
            }
            return false;
        }

        private Transform ChooseEditableAncestor(Transform t)
        {
            if(t==null)return null;
            Transform characterCandidate=null; Transform cur=t;
            while(cur!=null)
            {
                string n=cur.name??string.Empty;
                if(n.StartsWith("KSE_",StringComparison.OrdinalIgnoreCase)||n.StartsWith("KSPSceneEditor",StringComparison.OrdinalIgnoreCase))return cur;
                if(IsCharacterLike(cur))characterCandidate=cur;
                string nl=n.ToLowerInvariant();
                if(nl=="kerbin"||nl=="mun"||nl=="minmus"||nl.Contains("planet"))return cur;
                if(cur.parent!=null&&(cur.parent.name=="OrbitScene"||cur.parent.name=="MainMenu"||cur.parent.name=="stage 1"||cur.parent.name=="stage 2"))return characterCandidate??cur;
                cur=cur.parent;
            }
            return characterCandidate??t;
        }
    }
}
