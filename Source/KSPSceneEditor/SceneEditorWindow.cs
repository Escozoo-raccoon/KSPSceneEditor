using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace KSPSceneEditor
{
    internal sealed class SceneEditorWindow : MonoBehaviour
    {
        internal bool Visible=false;
        internal string SceneName { get { return sceneName; } }
        private Rect windowRect=new Rect(40,40,820,620);
        private Rect fullRect=new Rect(40,40,820,620);
        private Rect compactRect=new Rect(40,40,520,190);
        private bool uiLocked=false;
        private const float MinFullWidth=820f;
        private const float MinFullHeight=620f;
        private const float SnapDistance=16f;
        private string filter="",sceneName="Ma composition",renameSceneName="",craftFile="",logoImage="",newText="MON TEXTE",textEditDraft="",skyboxPack="",imageImportPath="";
        private bool forceStowed=true;
        private int rightTab=0,addSubTab=0,objectPage=0,craftPage=0,craftControlPage=0,logoPage=0,bodyPage=0,skyboxPage=0,compositionPage=0;
        private Transform guiMoveKerbal;
        private int guiMoveControlId;
        private string px="",py="",pz="";
        private Transform last;
        private float step=0.25f,rotStep=5f,screenStep=10f;
        private bool snapMove=true,snapRot=true;
        private bool compactMode=false,showObjectList=false,expertMode=false;
        private Transform textColorLast,textEditLast;
        private float textR=0.72f,textG=1f,textB=0.80f;

        private GUIStyle titleStyle,sectionStyle,categoryStyle,categoryActiveStyle,itemStyle,itemActiveStyle,smallStyle,statusStyle,dangerStyle,primaryStyle,subtleStyle;
        private Texture2D darkTex,panelTex,accentTex,selectedTex,dangerTex,softTex,avionicsFrameTex,buttonNormalTex,buttonHoverTex,buttonActiveTex,buttonDangerTex,headerPlateTex,sectionPlateTex;
        private Texture2D consoleFullTex,consoleMiniTex,rowNormalTex,rowHoverTex,rowActiveTex,toolbarFallbackTex;
        private Texture2D tabEditNormal,tabEditActive,tabAddNormal,tabAddActive,tabSaveNormal,tabSaveActive,tabAdvancedNormal,tabAdvancedActive;
        private Texture2D toolMoveNormal,toolMoveActive,toolDepthNormal,toolDepthActive,toolRotateNormal,toolRotateActive,toolScaleNormal,toolScaleActive;
        private readonly Dictionary<string,Texture2D> logoThumbs=new Dictionary<string,Texture2D>(StringComparer.OrdinalIgnoreCase);
        private Texture2D actionFrontNormal,actionFrontActive,actionBackNormal,actionBackActive,actionDuplicateNormal,actionDuplicateActive,actionResetNormal,actionResetActive,actionCloseAllNormal,actionCloseAllActive,actionOpenAllNormal,actionOpenAllActive,actionFullscreenNormal,actionFullscreenActive,actionUndoNormal,actionUndoActive;

        private void Awake(){LoadUiState();ApplyModeRect();}
        private void OnDestroy(){SaveUiState();}

        private string UiStatePath()
        {
            return Path.Combine(KSPUtil.ApplicationRootPath,"GameData","KSPSceneEditor","PluginData","ui_state.cfg");
        }

        private void LoadUiState()
        {
            try
            {
                string path=UiStatePath();if(!File.Exists(path))return;
                string[] lines=File.ReadAllLines(path);
                for(int i=0;i<lines.Length;i++)
                {
                    string line=(lines[i]??string.Empty).Trim();if(line.Length==0||line.StartsWith("#")||line.StartsWith("//"))continue;
                    int eq=line.IndexOf('=');if(eq<=0)continue;string k=line.Substring(0,eq).Trim();string v=line.Substring(eq+1).Trim();
                    float f;
                    if(k=="fullX"&&float.TryParse(v,NumberStyles.Float,CultureInfo.InvariantCulture,out f))fullRect.x=f;
                    else if(k=="fullY"&&float.TryParse(v,NumberStyles.Float,CultureInfo.InvariantCulture,out f))fullRect.y=f;
                    else if(k=="fullW"&&float.TryParse(v,NumberStyles.Float,CultureInfo.InvariantCulture,out f))fullRect.width=f;
                    else if(k=="fullH"&&float.TryParse(v,NumberStyles.Float,CultureInfo.InvariantCulture,out f))fullRect.height=f;
                    else if(k=="compactX"&&float.TryParse(v,NumberStyles.Float,CultureInfo.InvariantCulture,out f))compactRect.x=f;
                    else if(k=="compactY"&&float.TryParse(v,NumberStyles.Float,CultureInfo.InvariantCulture,out f))compactRect.y=f;
                    else if(k=="uiLocked")bool.TryParse(v,out uiLocked);
                }
            }catch(Exception ex){Debug.LogWarning("[KSPSceneEditor] UI state load skipped: "+ex.Message);}
            fullRect.width=Mathf.Max(MinFullWidth,fullRect.width);fullRect.height=Mathf.Max(MinFullHeight,fullRect.height);
            compactRect.width=520;compactRect.height=190;
        }

        private void SaveUiState()
        {
            try
            {
                if(compactMode)compactRect=windowRect;else fullRect=windowRect;
                string path=UiStatePath();Directory.CreateDirectory(Path.GetDirectoryName(path));
                using(StreamWriter sw=new StreamWriter(path,false))
                {
                    sw.WriteLine("fullX="+fullRect.x.ToString(CultureInfo.InvariantCulture));
                    sw.WriteLine("fullY="+fullRect.y.ToString(CultureInfo.InvariantCulture));
                    sw.WriteLine("fullW="+fullRect.width.ToString(CultureInfo.InvariantCulture));
                    sw.WriteLine("fullH="+fullRect.height.ToString(CultureInfo.InvariantCulture));
                    sw.WriteLine("compactX="+compactRect.x.ToString(CultureInfo.InvariantCulture));
                    sw.WriteLine("compactY="+compactRect.y.ToString(CultureInfo.InvariantCulture));
                    sw.WriteLine("uiLocked="+uiLocked);
                }
            }catch(Exception ex){Debug.LogWarning("[KSPSceneEditor] UI state save skipped: "+ex.Message);}
        }

        private void ApplyModeRect()
        {
            windowRect=compactMode?compactRect:fullRect;
            if(compactMode){windowRect.width=520;windowRect.height=190;}
            else{windowRect.width=820;windowRect.height=620;}
            ClampToScreen();
        }

        private void SetCompact(bool compact)
        {
            if(compact==compactMode)return;
            if(compactMode)compactRect=windowRect;else fullRect=windowRect;
            compactMode=compact;ApplyModeRect();SaveUiState();
        }

        private void ClampToScreen()
        {
            float maxX=Mathf.Max(0f,Screen.width-windowRect.width);
            float maxY=Mathf.Max(0f,Screen.height-windowRect.height);
            windowRect.x=Mathf.Clamp(windowRect.x,0f,maxX);
            windowRect.y=Mathf.Clamp(windowRect.y,0f,maxY);
        }

        private void SnapToScreenEdges()
        {
            if(windowRect.x<SnapDistance)windowRect.x=0;
            if(windowRect.y<SnapDistance)windowRect.y=0;
            float rx=Screen.width-windowRect.width;float by=Screen.height-windowRect.height;
            if(Mathf.Abs(windowRect.x-rx)<SnapDistance)windowRect.x=Mathf.Max(0,rx);
            if(Mathf.Abs(windowRect.y-by)<SnapDistance)windowRect.y=Mathf.Max(0,by);
            ClampToScreen();
        }

        private void ResetUiLayout()
        {
            fullRect=new Rect(22,22,Mathf.Min(900,Mathf.Max(MinFullWidth,Screen.width-44)),Mathf.Min(680,Mathf.Max(MinFullHeight,Screen.height-44)));
            compactRect=new Rect(22,22,390,258);uiLocked=false;ApplyModeRect();SaveUiState();
        }

        private void DockLeft(){windowRect.x=8;windowRect.y=Mathf.Max(8,(Screen.height-windowRect.height)*0.5f);SnapToScreenEdges();SaveUiState();}
        private void DockRight(){windowRect.x=Mathf.Max(0,Screen.width-windowRect.width-8);windowRect.y=Mathf.Max(8,(Screen.height-windowRect.height)*0.5f);SnapToScreenEdges();SaveUiState();}

        internal bool ContainsScreenPoint(Vector2 p){return Visible&&windowRect.Contains(p);}

        private Texture2D Solid(Color c){Texture2D t=new Texture2D(1,1);t.SetPixel(0,0,c);t.Apply();return t;}
        private Texture2D LoadUiTexture(string relative)
        {
            try
            {
                string path=Path.Combine(KSPUtil.ApplicationRootPath,"GameData","KSPSceneEditor","PluginData","UI",relative);
                if(!File.Exists(path))return null;byte[] data=File.ReadAllBytes(path);
                Texture2D t=new Texture2D(2,2,TextureFormat.ARGB32,false);
                if(!ImageConversion.LoadImage(t,data)){UnityEngine.Object.Destroy(t);return null;}
                return t;
            }catch{return null;}
        }

        private void EnsureStyles()
        {
            if(titleStyle!=null)return;
            avionicsFrameTex=LoadUiTexture("avionics_frame.png");
            buttonNormalTex=LoadUiTexture("button_normal.png");buttonHoverTex=LoadUiTexture("button_hover.png");
            buttonActiveTex=LoadUiTexture("button_active.png");buttonDangerTex=LoadUiTexture("button_danger.png");
            headerPlateTex=LoadUiTexture("header_plate.png");sectionPlateTex=LoadUiTexture("section_plate.png");
            consoleFullTex=LoadUiTexture("console_full.png");consoleMiniTex=LoadUiTexture("console_mini.png");toolbarFallbackTex=LoadUiTexture("toolbar_icon.png");
            rowNormalTex=LoadUiTexture("row_normal.png");rowHoverTex=LoadUiTexture("row_hover.png");rowActiveTex=LoadUiTexture("row_active.png");
            tabEditNormal=LoadUiTexture("tab_edit_normal.png");tabEditActive=LoadUiTexture("tab_edit_active.png");
            tabAddNormal=LoadUiTexture("tab_add_normal.png");tabAddActive=LoadUiTexture("tab_add_active.png");
            tabSaveNormal=LoadUiTexture("tab_save_normal.png");tabSaveActive=LoadUiTexture("tab_save_active.png");
            tabAdvancedNormal=LoadUiTexture("tab_advanced_normal.png");tabAdvancedActive=LoadUiTexture("tab_advanced_active.png");
            toolMoveNormal=LoadUiTexture("tool_move_normal.png");toolMoveActive=LoadUiTexture("tool_move_active.png");
            toolDepthNormal=LoadUiTexture("tool_depth_normal.png");toolDepthActive=LoadUiTexture("tool_depth_active.png");
            toolRotateNormal=LoadUiTexture("tool_rotate_normal.png");toolRotateActive=LoadUiTexture("tool_rotate_active.png");
            toolScaleNormal=LoadUiTexture("tool_scale_normal.png");toolScaleActive=LoadUiTexture("tool_scale_active.png");
            actionFrontNormal=LoadUiTexture("action_front_normal.png");actionFrontActive=LoadUiTexture("action_front_active.png");
            actionBackNormal=LoadUiTexture("action_back_normal.png");actionBackActive=LoadUiTexture("action_back_active.png");
            actionDuplicateNormal=LoadUiTexture("action_duplicate_normal.png");actionDuplicateActive=LoadUiTexture("action_duplicate_active.png");
            actionResetNormal=LoadUiTexture("action_reset_normal.png");actionResetActive=LoadUiTexture("action_reset_active.png");
            actionCloseAllNormal=LoadUiTexture("action_closeall_normal.png");actionCloseAllActive=LoadUiTexture("action_closeall_active.png");
            actionOpenAllNormal=LoadUiTexture("action_openall_normal.png");actionOpenAllActive=LoadUiTexture("action_openall_active.png");
            actionFullscreenNormal=LoadUiTexture("action_fullscreen_normal.png");actionFullscreenActive=LoadUiTexture("action_fullscreen_active.png");
            actionUndoNormal=LoadUiTexture("action_undo_normal.png");actionUndoActive=LoadUiTexture("action_undo_active.png");
            darkTex=Solid(new Color(0.055f,0.065f,0.065f,0.985f));
            panelTex=Solid(new Color(0.11f,0.13f,0.13f,0.94f));
            accentTex=Solid(new Color(0.18f,0.58f,0.36f,0.98f));
            selectedTex=Solid(new Color(0.32f,0.88f,0.55f,0.98f));
            dangerTex=Solid(new Color(0.48f,0.13f,0.13f,0.98f));
            softTex=Solid(new Color(0.16f,0.19f,0.18f,0.96f));

            titleStyle=new GUIStyle(GUI.skin.label);titleStyle.fontSize=18;titleStyle.fontStyle=FontStyle.Bold;titleStyle.alignment=TextAnchor.MiddleLeft;titleStyle.normal.textColor=Color.white;
            sectionStyle=new GUIStyle(GUI.skin.label);sectionStyle.fontSize=12;sectionStyle.fontStyle=FontStyle.Bold;sectionStyle.normal.textColor=new Color(0.42f,0.96f,0.62f);
            categoryStyle=new GUIStyle(GUI.skin.button);categoryStyle.fontSize=11;categoryStyle.alignment=TextAnchor.MiddleLeft;categoryStyle.padding=new RectOffset(12,8,5,5);categoryStyle.normal.background=softTex;categoryStyle.normal.textColor=new Color(0.88f,0.92f,0.89f);categoryStyle.hover.background=accentTex;
            categoryActiveStyle=new GUIStyle(categoryStyle);categoryActiveStyle.normal.background=selectedTex;categoryActiveStyle.normal.textColor=Color.white;
            itemStyle=new GUIStyle(GUI.skin.button);itemStyle.alignment=TextAnchor.MiddleLeft;itemStyle.fontSize=11;itemStyle.wordWrap=true;itemStyle.padding=new RectOffset(10,8,5,5);itemStyle.normal.background=softTex;itemStyle.normal.textColor=new Color(0.9f,0.92f,0.95f);
            itemActiveStyle=new GUIStyle(itemStyle);itemActiveStyle.normal.background=selectedTex;itemActiveStyle.normal.textColor=Color.white;
            smallStyle=new GUIStyle(GUI.skin.label);smallStyle.fontSize=10;smallStyle.normal.textColor=new Color(0.70f,0.74f,0.8f);
            statusStyle=new GUIStyle(GUI.skin.label);statusStyle.fontSize=11;statusStyle.normal.textColor=new Color(0.72f,0.9f,0.75f);
            dangerStyle=new GUIStyle(GUI.skin.button);dangerStyle.normal.background=dangerTex;dangerStyle.hover.background=dangerTex;dangerStyle.normal.textColor=Color.white;dangerStyle.fontStyle=FontStyle.Bold;
            primaryStyle=new GUIStyle(GUI.skin.button);primaryStyle.normal.background=accentTex;primaryStyle.hover.background=selectedTex;primaryStyle.active.background=selectedTex;primaryStyle.focused.background=accentTex;primaryStyle.normal.textColor=Color.white;primaryStyle.hover.textColor=Color.white;primaryStyle.active.textColor=Color.white;primaryStyle.focused.textColor=Color.white;primaryStyle.fontStyle=FontStyle.Bold;
            subtleStyle=new GUIStyle(GUI.skin.button);subtleStyle.normal.background=softTex;subtleStyle.hover.background=accentTex;subtleStyle.active.background=selectedTex;subtleStyle.focused.background=softTex;subtleStyle.normal.textColor=new Color(0.9f,0.92f,0.95f);subtleStyle.hover.textColor=Color.white;subtleStyle.active.textColor=Color.white;subtleStyle.focused.textColor=new Color(0.9f,0.92f,0.95f);
        }

        private void OnGUI()
        {
            if(HighLogic.LoadedScene!=GameScenes.MAINMENU)return;EnsureStyles();
            SceneEditorRuntime rt=SceneEditorRuntime.Instance;
            if(!Visible)
            {
                if(rt!=null&&!rt.NativeToolbarVisible)
                {
                    Rect barButton=new Rect(Screen.width-54,14,40,40);
                    if(toolbarFallbackTex!=null)GUI.DrawTexture(barButton,toolbarFallbackTex,ScaleMode.ScaleToFit,true);
                    if(GUI.Button(barButton,GUIContent.none,GUIStyle.none))rt.OpenEditorWindow();
                }
                return;
            }
            GUI.backgroundColor=Color.white;
            if(compactMode){windowRect.width=520;windowRect.height=190;}
            else{windowRect.width=820;windowRect.height=620;}
            ClampToScreen();
            Rect before=windowRect;
            windowRect=GUI.Window(GetInstanceID(),windowRect,DrawWindow,string.Empty,new GUIStyle(GUI.skin.window){normal={background=darkTex}});
            ClampToScreen();
            DrawKerbalHandlesOverlay(rt);
            if(Event.current.type==EventType.MouseUp){SnapToScreenEdges();if(before!=windowRect)SaveUiState();}
            Rect sr;if(rt!=null&&rt.TryGetSelectedScreenRect(out sr))
            {
                GUI.Box(sr,GUIContent.none);
                float ly=Mathf.Max(2f,sr.y-24f);
                GUI.Label(new Rect(sr.x,ly,230,22),"SELECTED  •  "+rt.DirectToolName+"  •  DRAG",statusStyle);
            }
        }

        private bool ImageButton(Rect r,Texture2D normal,Texture2D active,bool selected,string fallback)
        {
            Vector2 mp=Event.current.mousePosition;bool hover=r.Contains(mp);
            bool baked=string.IsNullOrEmpty(fallback);
            Texture2D tex=selected&&active!=null?active:normal;
            if(!baked&&hover&&buttonHoverTex!=null)tex=buttonHoverTex;
            if(tex!=null)GUI.DrawTexture(r,tex,ScaleMode.StretchToFill,true);
            if(hover&&baked)
            {
                Color old=GUI.color;GUI.color=new Color(0.55f,1f,0.70f,0.28f);
                GUI.DrawTexture(new Rect(r.x+2,r.y+2,r.width-4,2),Texture2D.whiteTexture);
                GUI.color=old;
            }
            bool clicked=GUI.Button(r,GUIContent.none,GUIStyle.none);
            if(!string.IsNullOrEmpty(fallback))
            {
                GUIStyle st=new GUIStyle(GUI.skin.label);st.alignment=TextAnchor.MiddleCenter;st.fontStyle=FontStyle.Bold;st.fontSize=10;st.normal.textColor=new Color(0.86f,0.94f,0.88f);
                GUI.Label(r,fallback,st);
            }
            return clicked;
        }

        private Texture2D GetLogoPreview(string file)
        {
            if(string.IsNullOrEmpty(file))return null;Texture2D cached;
            if(logoThumbs.TryGetValue(file,out cached)&&cached!=null)return cached;
            try
            {
                string path=Path.Combine(KSPUtil.ApplicationRootPath,"GameData","KSPSceneEditor","PluginData","Images",file);
                if(!File.Exists(path))return null;Texture2D tex=new Texture2D(2,2,TextureFormat.ARGB32,false);
                if(!ImageConversion.LoadImage(tex,File.ReadAllBytes(path))){UnityEngine.Object.Destroy(tex);return null;}
                logoThumbs[file]=tex;return tex;
            }catch{return null;}
        }

        private void DrawKerbalHandlesOverlay(SceneEditorRuntime rt)
        {
            if(rt==null||!Visible)return;
            List<Transform> actors=rt.GetLiveKerbalActors();
            Event e=Event.current;

            // Draw labels first. The actual interactive zone is the full Kerbal screen rect,
            // not the tiny marker and not a physics collider.
            Transform hovered=null;
            Rect hoveredRect=new Rect();
            float best=999999f;
            for(int i=0;i<actors.Count;i++)
            {
                Transform actor=actors[i];Rect r;
                if(!rt.TryGetKerbalInteractionRect(actor,out r))continue;
                int rid=rt.GetKerbalRegistryId(actor);
                bool selected=rt.Selected==actor;
                GUI.Label(new Rect(r.center.x-48f,r.yMin-18f,96f,18f),
                    "K"+rid+" "+rt.FriendlyKerbalName(actor)+(selected?"  ["+rt.DirectToolName+"]":""),smallStyle);

                if(r.Contains(e.mousePosition))
                {
                    float d=(e.mousePosition-r.center).sqrMagnitude;
                    if(d<best){best=d;hovered=actor;hoveredRect=r;}
                }
            }

            // A single exclusive IMGUI control owns Kerbal interaction.
            // Therefore overlapping Kerbal/planet geometry cannot change the target mid-drag.
            int controlId=GUIUtility.GetControlID(0x4B534536,FocusType.Passive);
            EventType type=e.GetTypeForControl(controlId);
            if(type==EventType.MouseDown&&e.button==0&&hovered!=null&&GUIUtility.hotControl==0)
            {
                GUIUtility.hotControl=controlId;
                guiMoveControlId=controlId;
                guiMoveKerbal=hovered;
                rt.SelectKerbalActor(hovered);
                craftControlPage=0;
                GUI.FocusControl(null);
                if(rt.BeginGuiKerbalMove(hovered,e.mousePosition))e.Use();
                else
                {
                    GUIUtility.hotControl=0;
                    guiMoveControlId=0;
                    guiMoveKerbal=null;
                }
            }
            else if(type==EventType.MouseDrag&&GUIUtility.hotControl==controlId&&guiMoveKerbal!=null)
            {
                rt.UpdateGuiKerbalMove(e.mousePosition);
                e.Use();
            }
            else if(type==EventType.MouseUp&&GUIUtility.hotControl==controlId)
            {
                rt.EndGuiKerbalMove();
                GUIUtility.hotControl=0;
                guiMoveControlId=0;
                guiMoveKerbal=null;
                e.Use();
            }

            if(guiMoveControlId!=0&&guiMoveKerbal==null)
            {
                rt.EndGuiKerbalMove();
                if(GUIUtility.hotControl==guiMoveControlId)GUIUtility.hotControl=0;
                guiMoveControlId=0;
            }
        }

        private void DrawWindow(int id)
        {
            SceneEditorRuntime rt=SceneEditorRuntime.Instance;if(rt==null)return;
            if(compactMode){DrawCompactWindow(id,rt);return;}

            if(consoleFullTex!=null)GUI.DrawTexture(new Rect(0,0,820,620),consoleFullTex,ScaleMode.StretchToFill,true);
            // Header kept clean: no version label.
            if(ImageButton(new Rect(636,18,78,30),buttonNormalTex,buttonActiveTex,false,"MINI"))SetCompact(true);
            if(ImageButton(new Rect(720,18,78,30),buttonDangerTex,buttonDangerTex,false,"FERMER"))rt.CloseEditorWindow();

            // Native scene/state selector.
            GUI.Label(new Rect(228,27,44,16),"SCÈNE",smallStyle);
            if(ImageButton(new Rect(274,20,30,28),buttonNormalTex,buttonActiveTex,false,"<"))rt.CycleNativeContext(-1);
            ImageButton(new Rect(308,20,208,28),buttonNormalTex,buttonActiveTex,true,rt.NativeContextShort);
            if(ImageButton(new Rect(520,20,30,28),buttonNormalTex,buttonActiveTex,false,">"))rt.CycleNativeContext(1);

            DrawObjectPanel(rt);
            DrawControlPanel(rt);

            if(!uiLocked)GUI.DragWindow(new Rect(0,0,620,48));
        }

        private void DrawCompactWindow(int id,SceneEditorRuntime rt)
        {
            if(consoleMiniTex!=null)GUI.DrawTexture(new Rect(0,0,520,190),consoleMiniTex,ScaleMode.StretchToFill,true);
            GUI.Label(new Rect(196,12,300,20),FriendlySelected(rt),sectionStyle);
            GUI.Label(new Rect(330,35,164,18),rt.WorkspaceRootName.ToUpperInvariant(),smallStyle);

            Texture2D[] n={toolMoveNormal,toolDepthNormal,toolRotateNormal,toolScaleNormal};
            Texture2D[] a={toolMoveActive,toolDepthActive,toolRotateActive,toolScaleActive};
            int[] tools={0,3,1,2};
            for(int i=0;i<4;i++)
            {
                if(ImageButton(new Rect(12+i*126,58,120,54),n[i],a[i],rt.DirectTool==tools[i],string.Empty))rt.SetDirectTool(tools[i]);
            }
            GUI.Label(new Rect(18,118,175,18),"PROFONDEUR  "+rt.GetSelectedCameraDepth().ToString("0.00")+" m",smallStyle);
            GUI.Label(new Rect(202,118,165,18),"TAILLE  "+rt.GetSelectedScaleAverage().ToString("0.000")+" x",smallStyle);

            if(rt.HasSelectedPlanet)
            {
                GUI.Label(new Rect(306,360,460,22),"PLANÈTE // TEXTURE",sectionStyle);
                GUI.Label(new Rect(306,388,320,22),rt.GetSelectedPlanetTextureLabel(),smallStyle);
                if(ImageButton(new Rect(636,382,68,30),buttonNormalTex,buttonActiveTex,false,"< TEX"))rt.CycleSelectedPlanetTexture(-1);
                if(ImageButton(new Rect(712,382,76,30),buttonNormalTex,buttonActiveTex,false,"TEX >"))rt.CycleSelectedPlanetTexture(1);
                GUI.Label(new Rect(306,424,470,42),"Si l'auto-détection choisit une mauvaise carte, parcourez les textures ScaledSpace candidates.",smallStyle);
                return;
            }

            if(rt.HasSelectedCraft)
            {
                if(ImageButton(new Rect(12,144,120,30),actionCloseAllNormal,actionCloseAllActive,false,string.Empty))rt.SetSelectedCraftAll(false);
                if(ImageButton(new Rect(138,144,120,30),actionOpenAllNormal,actionOpenAllActive,false,string.Empty))rt.SetSelectedCraftAll(true);
            }
            else
            {
                if(ImageButton(new Rect(12,144,120,30),actionUndoNormal,actionUndoActive,false,string.Empty))rt.Undo();
            }
            if(ImageButton(new Rect(264,144,120,30),actionResetNormal,actionResetActive,false,string.Empty))rt.RestoreSelected();
            if(ImageButton(new Rect(390,144,118,30),actionFullscreenNormal,actionFullscreenActive,false,string.Empty))SetCompact(false);
            if(!uiLocked)GUI.DragWindow(new Rect(0,0,330,45));
        }

        private List<SceneEditorRuntime.SceneEntry> FilteredEntries(SceneEditorRuntime rt)
        {
            List<SceneEditorRuntime.SceneEntry> result=new List<SceneEditorRuntime.SceneEntry>();
            string f=(filter??string.Empty).Trim().ToLowerInvariant();
            for(int i=0;i<rt.SceneEntries.Count;i++)
            {
                SceneEditorRuntime.SceneEntry e=rt.SceneEntries[i];if(e==null||e.Transform==null)continue;
                if(f.Length>0)
                {
                    string hay=(e.FriendlyName+" "+e.Category+" "+e.Kind).ToLowerInvariant();
                    if(hay.IndexOf(f,StringComparison.Ordinal)<0)continue;
                }
                result.Add(e);
            }
            return result;
        }

        private void DrawObjectPanel(SceneEditorRuntime rt)
        {
            if(!showObjectList)
            {
                GUI.Label(new Rect(24,146,220,54),"Cliquez directement dans la scène.\nUtilisez la liste seulement si un élément est difficile à atteindre.",smallStyle);
                GUI.Label(new Rect(24,202,220,16),"KERBALS",smallStyle);
                GUI.Label(new Rect(24,216,220,16),rt.GetKerbalRegistryStatus(),smallStyle);
                List<Transform> liveKerbals=rt.GetLiveKerbalActors();
                int kb=Mathf.Min(4,liveKerbals.Count);
                for(int i=0;i<kb;i++)
                {
                    Transform k=liveKerbals[i];
                    if(ImageButton(new Rect(24+i*54,234,50,26),buttonNormalTex,buttonActiveTex,rt.Selected==k,(i+1).ToString()))
                        rt.SelectKerbalActor(k);
                }
                GUI.Label(new Rect(24,260,220,18),"OBJET ACTUEL",smallStyle);
                GUI.Label(new Rect(24,282,220,34),FriendlySelected(rt),sectionStyle);

                if(rt.Selected!=null)
                {
                    string source=rt.IsSelectedCreated?"AJOUTÉ":"KSP ORIGINAL";
                    string type=rt.IsSelectedCreated?rt.SelectedCreatedType:(rt.HasSelectedText?"TEXTE":rt.HasSelectedCraft?"CRAFT":rt.IsKnownKerbal(rt.Selected)?"KERBAL":"VISUEL");
                    GUI.Label(new Rect(24,320,220,18),"ORIGINE "+source,smallStyle);
                    GUI.Label(new Rect(24,342,220,18),"TYPE    "+type,smallStyle);
                    GUI.Label(new Rect(24,364,220,18),"VUE     "+rt.CurrentContextLabel.ToUpperInvariant(),smallStyle);
                    bool locked=rt.IsLocked(rt.Selected);
                    if(ImageButton(new Rect(24,390,106,30),buttonNormalTex,buttonActiveTex,locked,locked?"DÉVERROUILLER":"VERROUILLER"))rt.ToggleLock(rt.Selected);
                    if(rt.IsSelectedCreated&&ImageButton(new Rect(136,390,108,30),buttonDangerTex,buttonDangerTex,false,"SUPPRIMER"))rt.DeleteSelected();
                }
                else
                {
                    GUI.Label(new Rect(24,320,220,38),"Aucune sélection.",smallStyle);
                }

                if(ImageButton(new Rect(24,430,220,32),buttonNormalTex,buttonActiveTex,false,"AFFICHER LA LISTE"))showObjectList=true;
                GUI.Label(new Rect(24,466,220,28),"Liste de sélection de secours.",smallStyle);
                GUI.Label(new Rect(24,500,220,18),"ÉTAT",sectionStyle);
                GUI.Label(new Rect(24,520,220,42),rt.Status??"PRÊT",smallStyle);
                return;
            }

            if(ImageButton(new Rect(24,136,220,28),buttonNormalTex,buttonActiveTex,false,"MASQUER LA LISTE")){showObjectList=false;return;}
            filter=GUI.TextField(new Rect(24,172,220,24),filter);
            List<SceneEditorRuntime.SceneEntry> entries=FilteredEntries(rt);
            const int perPage=9;int pageCount=Mathf.Max(1,Mathf.CeilToInt(entries.Count/(float)perPage));
            objectPage=Mathf.Clamp(objectPage,0,pageCount-1);
            int first=objectPage*perPage;
            for(int row=0;row<perPage;row++)
            {
                int index=first+row;if(index>=entries.Count)break;
                SceneEditorRuntime.SceneEntry e=entries[index];Rect rr=new Rect(22,204+row*35,232,31);
                bool selected=rt.Selected==e.Transform;Texture2D tex=selected?rowActiveTex:(rr.Contains(Event.current.mousePosition)?rowHoverTex:rowNormalTex);
                if(tex!=null)GUI.DrawTexture(rr,tex,ScaleMode.StretchToFill,true);
                GUI.Label(new Rect(rr.x+9,rr.y+5,rr.width-18,20),e.FriendlyName,smallStyle);
                if(GUI.Button(rr,GUIContent.none,GUIStyle.none)){rt.Select(e.Transform);craftControlPage=0;}
            }
            GUI.Label(new Rect(24,540,90,20),(entries.Count==0?"0":(first+1).ToString())+"-"+Mathf.Min(entries.Count,first+perPage)+" / "+entries.Count,smallStyle);
            if(ImageButton(new Rect(132,536,54,26),buttonNormalTex,buttonActiveTex,false,"<")&&objectPage>0)objectPage--;
            if(ImageButton(new Rect(192,536,54,26),buttonNormalTex,buttonActiveTex,false,">")&&objectPage<pageCount-1)objectPage++;
        }

        private void DrawControlPanel(SceneEditorRuntime rt)
        {
            // Graphical tabs
            Texture2D[] tn={tabEditNormal,tabAddNormal,tabSaveNormal,tabAdvancedNormal};
            Texture2D[] ta={tabEditActive,tabAddActive,tabSaveActive,tabAdvancedActive};
            for(int i=0;i<4;i++)
                if(ImageButton(new Rect(294+i*126,126,120,38),tn[i],ta[i],rightTab==i,string.Empty))rightTab=i;

            if(rightTab==0)DrawEditPage(rt);
            else if(rightTab==1)DrawAddPage(rt);
            else if(rightTab==2)DrawSavePage(rt);
            else DrawAdvancedPage(rt);
        }

        private void DrawEditPage(SceneEditorRuntime rt)
        {
            Transform t=rt.Selected;
            if(t==null)
            {
                GUI.Label(new Rect(308,190,470,26),"AUCUN OBJET SÉLECTIONNÉ",titleStyle);
                GUI.Label(new Rect(308,226,450,50),"Cliquez directement sur un élément de la scène ou utilisez la liste de secours.",smallStyle);
                return;
            }

            GUI.Label(new Rect(308,178,470,24),FriendlySelected(rt),titleStyle);
            GUI.Label(new Rect(308,205,470,20),"PROFONDEUR "+rt.GetSelectedCameraDepth().ToString("0.00")+" m   //   TAILLE "+rt.GetSelectedScaleAverage().ToString("0.000")+" x",statusStyle);

            Texture2D[] n={toolMoveNormal,toolDepthNormal,toolRotateNormal,toolScaleNormal};
            Texture2D[] a={toolMoveActive,toolDepthActive,toolRotateActive,toolScaleActive};
            int[] tools={0,3,1,2};
            for(int i=0;i<4;i++)
                if(ImageButton(new Rect(302+i*128,236,124,60),n[i],a[i],rt.DirectTool==tools[i],string.Empty))rt.SetDirectTool(tools[i]);

            if(ImageButton(new Rect(306,310,104,32),actionFrontNormal,actionFrontActive,false,string.Empty))rt.MoveSelectedLayer(true);
            if(ImageButton(new Rect(416,310,104,32),actionBackNormal,actionBackActive,false,string.Empty))rt.MoveSelectedLayer(false);
            if(ImageButton(new Rect(526,310,104,32),actionDuplicateNormal,actionDuplicateActive,false,string.Empty))rt.DuplicateSelected();
            if(ImageButton(new Rect(636,310,74,32),actionResetNormal,actionResetActive,false,"RESET"))rt.RestoreSelected();
            if(rt.IsSelectedCreated)
            {
                if(ImageButton(new Rect(716,310,72,32),buttonDangerTex,buttonDangerTex,false,"SUPPR."))rt.DeleteSelected();
                GUI.Label(new Rect(636,346,150,16),"AJOUTÉ : "+rt.SelectedCreatedType,smallStyle);
            }

            if(rt.HasSelectedText)
            {
                GUI.Label(new Rect(306,360,460,22),"TEXTE // PERSONNALISATION",sectionStyle);
                string current=rt.GetSelectedText();
                if(textEditLast!=t){textEditLast=t;textEditDraft=current;}
                textEditDraft=GUI.TextArea(new Rect(306,386,482,44),textEditDraft,120);
                if(ImageButton(new Rect(306,436,142,28),buttonNormalTex,buttonActiveTex,false,"APPLIQUER TEXTE"))rt.SetSelectedText(textEditDraft);

                float ts=rt.GetSelectedTextSize();
                GUI.Label(new Rect(458,441,48,18),"TAILLE",smallStyle);
                if(ImageButton(new Rect(506,436,38,28),buttonNormalTex,buttonActiveTex,false,"-"))rt.SetSelectedTextSize(ts-0.01f);
                GUI.Label(new Rect(548,441,50,18),ts.ToString(ts>=8f?"0":"0.00"),statusStyle);
                if(ImageButton(new Rect(600,436,38,28),buttonNormalTex,buttonActiveTex,false,"+"))rt.SetSelectedTextSize(ts+0.01f);
                bool bold=rt.GetSelectedTextBold();
                if(ImageButton(new Rect(648,436,140,28),buttonNormalTex,buttonActiveTex,bold,bold?"GRAS : ON":"GRAS : OFF"))rt.SetSelectedTextBold(!bold);

                GUI.Label(new Rect(306,474,66,18),"ALIGNER",smallStyle);
                int al=rt.GetSelectedTextAlignment();
                if(ImageButton(new Rect(372,470,72,26),buttonNormalTex,buttonActiveTex,al==0,"GAUCHE"))rt.SetSelectedTextAlignment(0);
                if(ImageButton(new Rect(448,470,72,26),buttonNormalTex,buttonActiveTex,al==1,"CENTRE"))rt.SetSelectedTextAlignment(1);
                if(ImageButton(new Rect(524,470,72,26),buttonNormalTex,buttonActiveTex,al==2,"DROITE"))rt.SetSelectedTextAlignment(2);
                float ls=rt.GetSelectedTextLineSpacing();
                GUI.Label(new Rect(606,474,48,18),"LIGNES",smallStyle);
                if(ImageButton(new Rect(654,470,34,26),buttonNormalTex,buttonActiveTex,false,"-"))rt.SetSelectedTextLineSpacing(ls-0.05f);
                GUI.Label(new Rect(692,474,48,18),ls.ToString("0.00"),smallStyle);
                if(ImageButton(new Rect(746,470,34,26),buttonNormalTex,buttonActiveTex,false,"+"))rt.SetSelectedTextLineSpacing(ls+0.05f);

                string[] fonts=rt.ListAvailableFonts();
                string currentFont=rt.GetSelectedTextFontName();
                int currentFontIndex=0;
                for(int fi=0;fi<fonts.Length;fi++)if(string.Equals(fonts[fi],currentFont,StringComparison.OrdinalIgnoreCase)){currentFontIndex=fi;break;}
                GUI.Label(new Rect(306,506,48,18),"POLICE",smallStyle);
                if(ImageButton(new Rect(356,502,32,26),buttonNormalTex,buttonActiveTex,false,"<")&&fonts.Length>0)
                    rt.SetSelectedTextFont(fonts[(currentFontIndex-1+fonts.Length)%fonts.Length]);
                ImageButton(new Rect(392,502,210,26),buttonNormalTex,buttonActiveTex,true,string.IsNullOrEmpty(currentFont)?"POLICE":currentFont);
                if(ImageButton(new Rect(606,502,32,26),buttonNormalTex,buttonActiveTex,false,">")&&fonts.Length>0)
                    rt.SetSelectedTextFont(fonts[(currentFontIndex+1)%fonts.Length]);
                GUI.Label(new Rect(648,506,140,18),fonts.Length+" police(s) chargée(s)",smallStyle);

                if(textColorLast!=t)
                {
                    textColorLast=t;Color cc=rt.GetSelectedTextColor();textR=cc.r;textG=cc.g;textB=cc.b;
                }
                GUI.Label(new Rect(306,538,96,18),"COULEUR RGB",sectionStyle);
                GUI.Label(new Rect(410,536,16,18),"R",smallStyle);
                float nr=GUI.HorizontalSlider(new Rect(426,542,92,16),textR,0f,1f);
                GUI.Label(new Rect(524,536,16,18),"G",smallStyle);
                float ng=GUI.HorizontalSlider(new Rect(540,542,92,16),textG,0f,1f);
                GUI.Label(new Rect(638,536,16,18),"B",smallStyle);
                float nb=GUI.HorizontalSlider(new Rect(654,542,92,16),textB,0f,1f);
                if(Mathf.Abs(nr-textR)>0.001f||Mathf.Abs(ng-textG)>0.001f||Mathf.Abs(nb-textB)>0.001f)
                {
                    textR=nr;textG=ng;textB=nb;rt.SetSelectedTextColor(new Color(textR,textG,textB,1f));
                }

                Color old=GUI.color;GUI.color=new Color(textR,textG,textB,1f);
                GUI.DrawTexture(new Rect(306,568,100,24),Texture2D.whiteTexture);GUI.color=old;
                if(ImageButton(new Rect(416,566,88,28),buttonNormalTex,buttonActiveTex,false,"BLANC")){textR=1;textG=1;textB=1;rt.SetSelectedTextColor(Color.white);}
                if(ImageButton(new Rect(510,566,88,28),buttonNormalTex,buttonActiveTex,false,"VERT")){textR=.72f;textG=1;textB=.80f;rt.SetSelectedTextColor(new Color(textR,textG,textB,1f));}
                if(ImageButton(new Rect(604,566,88,28),buttonNormalTex,buttonActiveTex,false,"AMBRE")){textR=1;textG=.72f;textB=.28f;rt.SetSelectedTextColor(new Color(textR,textG,textB,1f));}
                if(ImageButton(new Rect(698,566,90,28),buttonNormalTex,buttonActiveTex,false,"RESET"))rt.RestoreSelected();
                return;
            }

            if(rt.HasSelectedPlanet)
            {
                GUI.Label(new Rect(306,360,460,22),"PLANÈTE // TEXTURE",sectionStyle);
                GUI.Label(new Rect(306,388,320,22),rt.GetSelectedPlanetTextureLabel(),smallStyle);
                if(ImageButton(new Rect(636,382,68,30),buttonNormalTex,buttonActiveTex,false,"< TEX"))rt.CycleSelectedPlanetTexture(-1);
                if(ImageButton(new Rect(712,382,76,30),buttonNormalTex,buttonActiveTex,false,"TEX >"))rt.CycleSelectedPlanetTexture(1);
                GUI.Label(new Rect(306,424,470,42),"Si l'auto-détection choisit une mauvaise carte, parcourez les textures ScaledSpace candidates.",smallStyle);
                return;
            }

            if(rt.HasSelectedCraft)
            {
                GUI.Label(new Rect(306,366,460,22),"CRAFT // ANIMATIONS",sectionStyle);
                if(ImageButton(new Rect(306,392,150,32),actionCloseAllNormal,actionCloseAllActive,false,string.Empty))rt.SetSelectedCraftAll(false);
                if(ImageButton(new Rect(464,392,150,32),actionOpenAllNormal,actionOpenAllActive,false,string.Empty))rt.SetSelectedCraftAll(true);
                GUI.Label(new Rect(630,398,150,20),rt.SelectedCraftControlCount+" commande(s)",smallStyle);

                int per=5,pageCount=Mathf.Max(1,Mathf.CeilToInt(rt.SelectedCraftControlCount/(float)per));
                craftControlPage=Mathf.Clamp(craftControlPage,0,pageCount-1);
                int first=craftControlPage*per;
                for(int i=0;i<per;i++)
                {
                    int ci=first+i;if(ci>=rt.SelectedCraftControlCount)break;
                    bool open=rt.GetSelectedCraftControlOpen(ci);float y=432+i*29;
                    GUI.Label(new Rect(306,y,286,22),rt.GetSelectedCraftControlLabel(ci),smallStyle);
                    if(ImageButton(new Rect(596,y,58,23),buttonNormalTex,buttonActiveTex,!open,"FERMÉ"))rt.SetSelectedCraftControl(ci,false);
                    if(ImageButton(new Rect(658,y,58,23),buttonNormalTex,buttonActiveTex,open,"OUVERT"))rt.SetSelectedCraftControl(ci,true);
                    if(ImageButton(new Rect(720,y,68,23),buttonNormalTex,buttonActiveTex,false,"INVERSER"))rt.InvertSelectedCraftControl(ci);
                }
                if(pageCount>1)
                {
                    GUI.Label(new Rect(614,578,70,18),(craftControlPage+1)+"/"+pageCount,smallStyle);
                    if(ImageButton(new Rect(686,572,44,24),buttonNormalTex,buttonActiveTex,false,"<")&&craftControlPage>0)craftControlPage--;
                    if(ImageButton(new Rect(736,572,44,24),buttonNormalTex,buttonActiveTex,false,">")&&craftControlPage<pageCount-1)craftControlPage++;
                }
            }
            else
            {
                GUI.Label(new Rect(306,374,450,22),"AJUSTEMENT RAPIDE",sectionStyle);
                if(ImageButton(new Rect(306,404,92,30),buttonNormalTex,buttonActiveTex,false,"GAUCHE"))rt.MoveSelectedScreenRelative(-1,0,0,screenStep);
                if(ImageButton(new Rect(404,404,92,30),buttonNormalTex,buttonActiveTex,false,"DROITE"))rt.MoveSelectedScreenRelative(1,0,0,screenStep);
                if(ImageButton(new Rect(502,404,92,30),buttonNormalTex,buttonActiveTex,false,"HAUT"))rt.MoveSelectedScreenRelative(0,1,0,screenStep);
                if(ImageButton(new Rect(600,404,92,30),buttonNormalTex,buttonActiveTex,false,"BAS"))rt.MoveSelectedScreenRelative(0,-1,0,screenStep);
                GUI.Label(new Rect(306,450,440,50),"La manipulation directe est recommandée. Les coordonnées précises sont disponibles dans Réglages précis.",smallStyle);
            }
        }

        private void DrawAddPage(SceneEditorRuntime rt)
        {
            string[] labels={"ASTRES","TEXTE","CRAFTS","IMAGES","SKYBOX"};
            for(int i=0;i<5;i++)
            {
                if(ImageButton(new Rect(306+i*96,180,90,30),buttonNormalTex,buttonActiveTex,addSubTab==i,labels[i]))
                {
                    addSubTab=i;
                    if(i==3)rt.SelectMainMenuLogo();
                }
            }

            if(addSubTab==0)
            {
                GUI.Label(new Rect(306,226,460,22),"ASTRES",sectionStyle);
                string[] bodies=rt.ListAvailableBodies();const int perBody=16;int bodyPages=Mathf.Max(1,Mathf.CeilToInt(bodies.Length/(float)perBody));
                bodyPage=Mathf.Clamp(bodyPage,0,bodyPages-1);int firstBody=bodyPage*perBody;
                GUI.Label(new Rect(672,228,116,18),"PAGE "+(bodyPage+1)+"/"+bodyPages,smallStyle);
                for(int i=0;i<perBody;i++)
                {
                    int bi=firstBody+i;if(bi>=bodies.Length)break;
                    int col=i%4,row=i/4;float x=306+col*120,y=258+row*42;
                    if(ImageButton(new Rect(x,y,112,34),buttonNormalTex,buttonActiveTex,false,bodies[bi].ToUpperInvariant()))rt.AddPlanetClone(bodies[bi]);
                }
                if(bodyPages>1)
                {
                    if(ImageButton(new Rect(306,438,52,28),buttonNormalTex,buttonActiveTex,false,"<")&&bodyPage>0)bodyPage--;
                    if(ImageButton(new Rect(364,438,52,28),buttonNormalTex,buttonActiveTex,false,">")&&bodyPage<bodyPages-1)bodyPage++;
                }
                GUI.Label(new Rect(306,482,470,48),bodies.Length+" corps détecté(s). Scene Editor tente le ScaledSpace KSP réel puis utilise son fallback texturé.",smallStyle);
                return;
            }

            if(addSubTab==1)
            {
                GUI.Label(new Rect(306,226,460,22),"NOUVEAU TEXTE",sectionStyle);
                GUI.Label(new Rect(306,258,460,20),"Écrivez librement le contenu à afficher dans la scène.",smallStyle);
                newText=GUI.TextArea(new Rect(306,286,482,100),newText,120);
                if(ImageButton(new Rect(306,402,180,34),buttonNormalTex,buttonActiveTex,false,"AJOUTER LE TEXTE"))rt.AddTextLabel(newText);
                GUI.Label(new Rect(306,456,470,72),"Après l'ajout, passez dans ÉDITER : contenu, taille, couleur RGB, alignement, gras et interligne y sont réglables.",smallStyle);
                return;
            }

            if(addSubTab==2)
            {
                GUI.Label(new Rect(306,226,300,22),"CRAFTS",sectionStyle);
                if(ImageButton(new Rect(654,222,134,30),buttonNormalTex,buttonActiveTex,false,"ACTUALISER"))rt.RefreshUserContent();
                GUI.Label(new Rect(306,254,360,20),"GameData/KSPSceneEditor/Crafts",smallStyle);
                string[] crafts=rt.ListCraftFiles();const int per=6;int pages=Mathf.Max(1,Mathf.CeilToInt(crafts.Length/(float)per));
                craftPage=Mathf.Clamp(craftPage,0,pages-1);
                GUI.Label(new Rect(674,258,114,18),"PAGE "+(craftPage+1)+"/"+pages,smallStyle);
                int first=craftPage*per;
                for(int i=0;i<per;i++)
                {
                    int idx=first+i;if(idx>=crafts.Length)break;float y=286+i*38;
                    bool sel=string.Equals(craftFile,crafts[idx],StringComparison.OrdinalIgnoreCase);
                    if(ImageButton(new Rect(306,y,330,32),sel?rowActiveTex:rowNormalTex,rowActiveTex,sel,crafts[idx]))craftFile=crafts[idx];
                    if(ImageButton(new Rect(642,y,72,32),buttonNormalTex,buttonActiveTex,false,"AJOUTER")){craftFile=crafts[idx];rt.SpawnCraft(craftFile,forceStowed);}
                    if(ImageButton(new Rect(720,y,68,32),buttonDangerTex,buttonDangerTex,false,"SUPPR.")){craftFile=crafts[idx];if(rt.DeleteUserCraft(craftFile)){craftFile="";craftPage=0;break;}}
                }
                if(pages>1)
                {
                    if(ImageButton(new Rect(684,532,48,28),buttonNormalTex,buttonActiveTex,false,"<")&&craftPage>0)craftPage--;
                    if(ImageButton(new Rect(740,532,48,28),buttonNormalTex,buttonActiveTex,false,">")&&craftPage<pages-1)craftPage++;
                }
                forceStowed=GUI.Toggle(new Rect(306,538,240,22),forceStowed,"Pièces rangées au spawn");
                return;
            }

            if(addSubTab==3)
            {
                GUI.Label(new Rect(306,226,310,22),"IMAGES",sectionStyle);
                if(ImageButton(new Rect(654,222,134,30),buttonNormalTex,buttonActiveTex,false,"ACTUALISER"))rt.RefreshUserContent();

                GUI.Label(new Rect(306,248,482,40),"Importez n'importe quel PNG/JPG/JPEG. Utilisez-le pour remplacer le logo OU comme image libre dans la composition.",smallStyle);
                imageImportPath=GUI.TextField(new Rect(306,278,350,28),imageImportPath);
                if(ImageButton(new Rect(664,276,124,32),buttonNormalTex,buttonActiveTex,false,"IMPORTER"))
                {
                    if(rt.ImportImageFromPath(imageImportPath))
                    {
                        imageImportPath="";logoPage=0;logoImage=rt.LastImportedImage;
                    }
                }

                string[] imgs=rt.ListLogoImages();const int logoPer=4;int logoPages=Mathf.Max(1,Mathf.CeilToInt(imgs.Length/(float)logoPer));
                logoPage=Mathf.Clamp(logoPage,0,logoPages-1);
                GUI.Label(new Rect(306,316,240,20),"BIBLIOTHÈQUE",sectionStyle);
                GUI.Label(new Rect(684,316,104,18),"PAGE "+(logoPage+1)+"/"+logoPages,smallStyle);
                int lf=logoPage*logoPer;
                for(int i=0;i<logoPer;i++)
                {
                    int idx=lf+i;if(idx>=imgs.Length)break;
                    int col=i%2,row=i/2;float x=306+col*240,y=342+row*94;
                    Rect card=new Rect(x,y,224,82);bool sel=string.Equals(logoImage,imgs[idx],StringComparison.OrdinalIgnoreCase);
                    Texture2D frame=sel?rowActiveTex:rowNormalTex;if(frame!=null)GUI.DrawTexture(card,frame,ScaleMode.StretchToFill,true);
                    Texture2D pv=GetLogoPreview(imgs[idx]);
                    if(pv!=null)GUI.DrawTexture(new Rect(x+8,y+7,74,66),pv,ScaleMode.ScaleToFit,true);
                    string nm=Path.GetFileNameWithoutExtension(imgs[idx]);if(nm.Length>18)nm=nm.Substring(0,18)+"…";
                    GUI.Label(new Rect(x+90,y+22,126,22),nm,smallStyle);
                    if(sel)GUI.Label(new Rect(x+90,y+48,126,18),"SÉLECTIONNÉ",sectionStyle);
                    if(GUI.Button(card,GUIContent.none,GUIStyle.none))logoImage=imgs[idx];
                }
                if(imgs.Length==0)
                {
                    GUI.Label(new Rect(306,354,470,50),"Aucune image disponible. Importez un fichier PNG ou JPG.",smallStyle);
                    return;
                }
                if(string.IsNullOrEmpty(logoImage))logoImage=imgs[0];

                // Compact pagination cluster instead of arrows spread across the panel.
                if(ImageButton(new Rect(306,540,42,30),buttonNormalTex,buttonActiveTex,false,"<")&&logoPage>0)logoPage--;
                GUI.Label(new Rect(354,546,70,20),(logoPage+1)+"/"+logoPages,sectionStyle);
                if(ImageButton(new Rect(426,540,42,30),buttonNormalTex,buttonActiveTex,false,">")&&logoPage<logoPages-1)logoPage++;

                if(ImageButton(new Rect(482,532,146,34),buttonNormalTex,buttonActiveTex,false,"REMPLACER LOGO"))
                {
                    rt.ApplyImageToMainLogo(logoImage);
                }
                if(ImageButton(new Rect(636,532,152,34),buttonDangerTex,buttonDangerTex,false,"RESET LOGO"))
                {
                    rt.ResetMainMenuLogo();
                }
                if(ImageButton(new Rect(482,570,196,34),buttonNormalTex,buttonActiveTex,false,"AJOUTER IMAGE LIBRE"))
                {
                    rt.AddFreeImage(logoImage);
                }
                if(ImageButton(new Rect(686,570,102,34),buttonDangerTex,buttonDangerTex,false,"SUPPRIMER"))
                {
                    if(rt.DeleteUserImage(logoImage)){logoImage="";logoPage=0;}
                }
                return;
            }

            GUI.Label(new Rect(306,226,300,22),"SKYBOX // ENVIRONNEMENT",sectionStyle);
            if(ImageButton(new Rect(654,222,134,30),buttonNormalTex,buttonActiveTex,false,"ACTUALISER"))rt.RefreshUserContent();
            GUI.Label(new Rect(306,256,470,34),"Pack utilisateur de 6 faces. Aucun skybox d’exemple n’est fourni.",smallStyle);
            string[] packs=rt.ListSkyboxPacks();const int perSky=6;int skyPages=Mathf.Max(1,Mathf.CeilToInt(packs.Length/(float)perSky));
            skyboxPage=Mathf.Clamp(skyboxPage,0,skyPages-1);
            GUI.Label(new Rect(674,286,114,18),"PAGE "+(skyboxPage+1)+"/"+skyPages,smallStyle);
            int sf=skyboxPage*perSky;
            for(int i=0;i<perSky;i++)
            {
                int idx=sf+i;if(idx>=packs.Length)break;float y=310+i*38;
                bool sel=string.Equals(skyboxPack,packs[idx],StringComparison.OrdinalIgnoreCase);
                if(ImageButton(new Rect(306,y,336,32),sel?rowActiveTex:rowNormalTex,rowActiveTex,sel,packs[idx]))skyboxPack=packs[idx];
                if(ImageButton(new Rect(650,y,72,32),buttonNormalTex,buttonActiveTex,false,"APPLI.")){skyboxPack=packs[idx];rt.ApplySkyboxPack(skyboxPack);}
                if(ImageButton(new Rect(728,y,60,32),buttonDangerTex,buttonDangerTex,false,"SUPPR.")){skyboxPack=packs[idx];if(rt.DeleteUserSkybox(skyboxPack)){skyboxPack="";skyboxPage=0;break;}}
            }
            if(packs.Length==0)GUI.Label(new Rect(306,318,470,58),"Aucun pack. Créez un dossier dans PluginData/Skyboxes et ajoutez les 6 images GalaxyTex_*.png.",smallStyle);
            if(skyPages>1)
            {
                if(ImageButton(new Rect(306,544,52,28),buttonNormalTex,buttonActiveTex,false,"<")&&skyboxPage>0)skyboxPage--;
                if(ImageButton(new Rect(364,544,52,28),buttonNormalTex,buttonActiveTex,false,">")&&skyboxPage<skyPages-1)skyboxPage++;
            }
            GUI.Label(new Rect(444,550,170,20),"ACTIF : "+(string.IsNullOrEmpty(rt.ActiveSkyboxPack)?"ORIGINAL":rt.ActiveSkyboxPack),smallStyle);
            if(ImageButton(new Rect(620,540,168,34),buttonDangerTex,buttonDangerTex,false,"RESET SKYBOX"))rt.RestoreOriginalSkybox();
        }

        private void DrawSavePage(SceneEditorRuntime rt)
        {
            GUI.Label(new Rect(306,184,460,24),"COMPOSITIONS",titleStyle);
            GUI.Label(new Rect(306,208,476,18),"Vue : "+rt.CurrentContextLabel,smallStyle);
            GUI.Label(new Rect(306,230,476,18),"Composition active : "+rt.CurrentActiveProfile,sectionStyle);

            GUI.Label(new Rect(306,258,160,18),"NOM DE LA COMPOSITION",smallStyle);
            sceneName=GUI.TextField(new Rect(306,280,326,26),sceneName);
            if(ImageButton(new Rect(642,278,146,30),buttonNormalTex,buttonActiveTex,false,"SAUVEGARDER"))rt.SaveScene(sceneName);

            if(ImageButton(new Rect(306,316,194,30),buttonNormalTex,buttonActiveTex,false,"APPLIQUER"))rt.LoadScene(sceneName);
            if(ImageButton(new Rect(510,316,194,30),buttonDangerTex,buttonDangerTex,false,"KSP ORIGINAL"))rt.UseStockForCurrentContext();

            string[] scenes=rt.ListSceneFilesForCurrentContext();
            const int perPage=5;
            int pages=Mathf.Max(1,Mathf.CeilToInt(scenes.Length/(float)perPage));
            compositionPage=Mathf.Clamp(compositionPage,0,pages-1);

            GUI.Label(new Rect(306,356,300,20),"SAUVEGARDES DE CETTE VUE",sectionStyle);
            GUI.Label(new Rect(626,356,64,20),(compositionPage+1)+" / "+pages,smallStyle);
            if(ImageButton(new Rect(696,350,42,26),buttonNormalTex,buttonActiveTex,false,"<")&&compositionPage>0)compositionPage--;
            if(ImageButton(new Rect(744,350,42,26),buttonNormalTex,buttonActiveTex,false,">")&&compositionPage<pages-1)compositionPage++;

            int first=compositionPage*perPage;
            for(int row=0;row<perPage;row++)
            {
                int index=first+row;if(index>=scenes.Length)break;
                bool selected=string.Equals(sceneName,scenes[index],StringComparison.OrdinalIgnoreCase);
                bool active=string.Equals(rt.CurrentActiveProfile,scenes[index],StringComparison.OrdinalIgnoreCase);
                string label=(active?"ACTIF  •  ":"")+scenes[index];
                if(ImageButton(new Rect(306,382+row*30,480,27),active?rowActiveTex:rowNormalTex,rowActiveTex,selected,label))
                {
                    sceneName=scenes[index];
                    renameSceneName=scenes[index];
                }
            }

            if(scenes.Length==0)
                GUI.Label(new Rect(306,386,470,38),"Aucune composition sauvegardée pour cette vue.",smallStyle);

            GUI.Label(new Rect(306,536,92,18),"Renommer",smallStyle);
            renameSceneName=GUI.TextField(new Rect(394,532,214,26),renameSceneName);
            if(ImageButton(new Rect(616,530,86,30),buttonNormalTex,buttonActiveTex,false,"VALIDER"))
            {
                string old=sceneName;
                if(rt.RenameScene(old,renameSceneName))sceneName=renameSceneName;
            }
            if(ImageButton(new Rect(708,530,80,30),buttonDangerTex,buttonDangerTex,false,"SUPPR.")) 
            {
                string deleted=sceneName;
                if(rt.DeleteScene(deleted))
                {
                    sceneName="Ma composition";
                    renameSceneName="";
                    compositionPage=0;
                }
            }
        }

        private void DrawAdvancedPage(SceneEditorRuntime rt)
        {
            GUI.Label(new Rect(306,180,460,22),"CHOIX DE LA SCÈNE",sectionStyle);
            GUI.Label(new Rect(306,202,476,18),"Chaque vue possède ses propres éléments et compositions.",smallStyle);

            GUI.Label(new Rect(306,228,232,20),"SCÈNE PRINCIPALE",sectionStyle);
            GUI.Label(new Rect(550,228,232,20),"SCÈNE MUN",sectionStyle);

            if(ImageButton(new Rect(306,252,176,32),buttonNormalTex,buttonActiveTex,rt.IsNativeOrbit&&rt.NativeStageIndex==0,"ÉTAT 1  MENU PRINCIPAL"))rt.SelectNativeContext(1,0);
            if(ImageButton(new Rect(486,252,52,32),buttonDangerTex,buttonDangerTex,false,"STOCK"))rt.ResetNativeContext(1,0,false);
            if(ImageButton(new Rect(306,290,176,32),buttonNormalTex,buttonActiveTex,rt.IsNativeOrbit&&rt.NativeStageIndex==1,"ÉTAT 2  NOUVELLE PARTIE"))rt.SelectNativeContext(1,1);
            if(ImageButton(new Rect(486,290,52,32),buttonDangerTex,buttonDangerTex,false,"STOCK"))rt.ResetNativeContext(1,1,false);

            if(ImageButton(new Rect(550,252,176,32),buttonNormalTex,buttonActiveTex,rt.IsNativeMun&&rt.NativeStageIndex==0,"ÉTAT 1  MUN"))rt.SelectNativeContext(0,0);
            if(ImageButton(new Rect(730,252,52,32),buttonDangerTex,buttonDangerTex,false,"STOCK"))rt.ResetNativeContext(0,0,false);
            if(ImageButton(new Rect(550,290,176,32),buttonNormalTex,buttonActiveTex,rt.IsNativeMun&&rt.NativeStageIndex==1,"ÉTAT 2  MUN"))rt.SelectNativeContext(0,1);
            if(ImageButton(new Rect(730,290,52,32),buttonDangerTex,buttonDangerTex,false,"STOCK"))rt.ResetNativeContext(0,1,false);

            float y=334f;
            if(rt.IsNativeMun&&rt.HasNativeSandcastleVariant())
            {
                GUI.Label(new Rect(550,y,112,20),"VARIANTE MUN",sectionStyle);y+=22;
                if(ImageButton(new Rect(550,y,108,30),buttonNormalTex,buttonActiveTex,!rt.NativeSandcastleActive,"NORMAL"))rt.SetNativeSandcastleVariant(false);
                if(ImageButton(new Rect(664,y,112,30),buttonNormalTex,buttonActiveTex,rt.NativeSandcastleActive,"CHÂTEAU"))rt.SetNativeSandcastleVariant(true);
                y+=40;
            }

            GUI.Label(new Rect(306,350,230,20),"ÉTAT ACTUEL",sectionStyle);
            GUI.Label(new Rect(306,374,470,22),rt.CurrentContextLabel,smallStyle);
            GUI.Label(new Rect(306,398,470,20),"Réinitialiser agit uniquement sur cette vue.",smallStyle);

            if(ImageButton(new Rect(306,430,232,30),buttonDangerTex,buttonDangerTex,false,"ANNULER MODIFS"))rt.ResetCurrentWorkToActiveComposition();
            if(ImageButton(new Rect(550,430,232,30),buttonNormalTex,buttonActiveTex,false,"KSP ORIGINAL"))rt.UseStockForCurrentContext();

            GUI.Label(new Rect(306,474,460,20),"RÉGLAGES PRÉCIS",sectionStyle);
            if(ImageButton(new Rect(306,498,184,30),buttonNormalTex,buttonActiveTex,expertMode,expertMode?"RÉGLAGES : ON":"RÉGLAGES : OFF"))expertMode=!expertMode;
            if(!expertMode)return;
            Transform t=rt.Selected;float sy=538f;
            if(t==null){GUI.Label(new Rect(306,sy,420,24),"Sélectionnez un objet pour éditer XYZ.",smallStyle);return;}
            SyncFields(t);GUI.Label(new Rect(306,sy,130,18),"Position X / Y / Z",smallStyle);sy+=18;Vec3FieldsAt(ref px,ref py,ref pz,306,sy);
            if(ImageButton(new Rect(682,sy,106,24),buttonNormalTex,buttonActiveTex,false,"APPLIQUER")){Vector3 v;if(Parse3(px,py,pz,out v)&&rt.CanEdit(t)){rt.BeginEdit(t);rt.ApplyWorldPosition(t,v);SyncNow(t);}}
        }

        private void Vec3FieldsAt(ref string a,ref string b,ref string c,float x,float y)
        {
            a=GUI.TextField(new Rect(x,y,112,24),a);b=GUI.TextField(new Rect(x+120,y,112,24),b);c=GUI.TextField(new Rect(x+240,y,112,24),c);
        }

        private string FriendlySelected(SceneEditorRuntime rt)
        {
            if(rt==null||rt.Selected==null)return "AUCUN";
            for(int i=0;i<rt.SceneEntries.Count;i++){SceneEditorRuntime.SceneEntry e=rt.SceneEntries[i];if(e!=null&&e.Transform==rt.Selected)return e.FriendlyName.ToUpperInvariant();}
            return rt.Selected.name.ToUpperInvariant();
        }

        private SceneEditorRuntime.SceneEntry FindEntry(SceneEditorRuntime rt,Transform t){IReadOnlyList<SceneEditorRuntime.SceneEntry> src=rt.SceneEntries;for(int i=0;i<src.Count;i++)if(src[i]!=null&&src[i].Transform==t)return src[i];return null;}
        private void StepField(string label,ref float value,ref int y){GUI.Label(new Rect(0,y,95,22),label,smallStyle);string s=GUI.TextField(new Rect(98,y,75,22),value.ToString(CultureInfo.InvariantCulture));float x;if(float.TryParse(s,NumberStyles.Float,CultureInfo.InvariantCulture,out x)&&x>0)value=x;y+=26;}
        private void NudgeRow(SceneEditorRuntime rt,Transform t,ref int y){string[] labs={"X-","X+","Y-","Y+","Z-","Z+"};Vector3[] dirs={Vector3.left,Vector3.right,Vector3.down,Vector3.up,Vector3.back,Vector3.forward};for(int i=0;i<6;i++)if(GUI.Button(new Rect((i%3)*112,y+(i/3)*25,106,22),labs[i],subtleStyle)&&rt.CanEdit(t)){rt.BeginEdit(t);Vector3 np=t.position+dirs[i]*step;if(snapMove)np=Snap(np,step);rt.ApplyWorldPosition(t,np);SyncNow(t);}y+=55;}
        private void RotationRow(SceneEditorRuntime rt,Transform t,ref int y){string[] labs={"PITCH-","PITCH+","YAW-","YAW+","ROLL-","ROLL+"};Vector3[] dirs={Vector3.left,Vector3.right,Vector3.down,Vector3.up,Vector3.back,Vector3.forward};for(int i=0;i<6;i++)if(GUI.Button(new Rect((i%3)*112,y+(i/3)*25,106,22),labs[i],subtleStyle)&&rt.CanEdit(t)){rt.BeginEdit(t);t.Rotate(dirs[i],rotStep,Space.World);if(snapRot)t.rotation=Quaternion.Euler(Snap(t.rotation.eulerAngles,rotStep));SyncNow(t);}y+=55;}
        private Vector3 Snap(Vector3 v,float inc){if(inc<=0)return v;return new Vector3(Mathf.Round(v.x/inc)*inc,Mathf.Round(v.y/inc)*inc,Mathf.Round(v.z/inc)*inc);}
        private void Vec3Fields(ref string a,ref string b,ref string c,int y){a=GUI.TextField(new Rect(0,y,132,22),a);b=GUI.TextField(new Rect(138,y,132,22),b);c=GUI.TextField(new Rect(276,y,132,22),c);}
        private bool Parse3(string a,string b,string c,out Vector3 v){v=Vector3.zero;float x,y,z;if(!float.TryParse(a,NumberStyles.Float,CultureInfo.InvariantCulture,out x)||!float.TryParse(b,NumberStyles.Float,CultureInfo.InvariantCulture,out y)||!float.TryParse(c,NumberStyles.Float,CultureInfo.InvariantCulture,out z))return false;v=new Vector3(x,y,z);return true;}
        private void SyncFields(Transform t){if(t==last)return;last=t;SyncNow(t);}private void SyncNow(Transform t){if(t==null)return;Vector3 p=t.position;px=F(p.x);py=F(p.y);pz=F(p.z);}private string F(float f){return f.ToString("0.######",CultureInfo.InvariantCulture);}
    }
}
