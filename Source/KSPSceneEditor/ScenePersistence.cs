using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace KSPSceneEditor
{
    internal static class ScenePersistence
    {
        internal static string SceneFilePath(string name)
        {
            string safe=Sanitize(string.IsNullOrEmpty(name)?"Default":name);
            return Path.Combine(KSPUtil.ApplicationRootPath,"GameData","KSPSceneEditor","Scenes",safe+".cfg");
        }

        internal static void Save(string name, IEnumerable<Transform> edited, IEnumerable<SceneEditorRuntime.SpawnedCraft> crafts, IEnumerable<SceneEditorRuntime.SpawnedLight> lights, IEnumerable<SceneEditorRuntime.SpawnedPlanet> planets, IEnumerable<SceneEditorRuntime.SpawnedText> texts, IEnumerable<SceneEditorRuntime.SpawnedImage> images, Color ambient, int area, int stage, bool rare, string contextKey, SceneEditorRuntime rt)
        {
            SaveToPath(SceneFilePath(name),name,edited,crafts,lights,planets,texts,images,ambient,area,stage,rare,contextKey,rt);
        }

        internal static void SaveToPath(string pathOut,string name, IEnumerable<Transform> edited, IEnumerable<SceneEditorRuntime.SpawnedCraft> crafts, IEnumerable<SceneEditorRuntime.SpawnedLight> lights, IEnumerable<SceneEditorRuntime.SpawnedPlanet> planets, IEnumerable<SceneEditorRuntime.SpawnedText> texts, IEnumerable<SceneEditorRuntime.SpawnedImage> images, Color ambient, int area, int stage, bool rare, string contextKey, SceneEditorRuntime rt)
        {

            ConfigNode file=new ConfigNode(); ConfigNode root=file.AddNode("KSP_SCENE_EDITOR_SCENE"); root.AddValue("version","1.0"); root.AddValue("name",name); root.AddValue("ambient",C(ambient)); root.AddValue("area",area); root.AddValue("stage",stage); root.AddValue("rare",rare); root.AddValue("context",contextKey??string.Empty);root.AddValue("skybox",rt!=null?rt.ActiveSkyboxPack:string.Empty);
            HashSet<string> seen=new HashSet<string>();
            foreach(Transform t in edited)
            {
                if(t==null||!ScenePath.InLoadedScene(t)||(rt!=null&&rt.IsKnownKerbal(t)))continue; string path=ScenePath.Get(t); if(string.IsNullOrEmpty(path)||seen.Contains(path))continue; seen.Add(path);
                ConfigNode n=root.AddNode("OBJECT"); n.AddValue("path",path);n.AddValue("hint",rt!=null?rt.GetPersistenceHint(t):string.Empty); n.AddValue("active",t.gameObject.activeSelf); n.AddValue("localPosition",V(t.localPosition)); n.AddValue("localRotation",Q(t.localRotation)); n.AddValue("localScale",V(t.localScale));
                Camera c=t.GetComponent<Camera>(); if(c!=null){n.AddValue("cameraFov",F(c.fieldOfView));n.AddValue("cameraNear",F(c.nearClipPlane));n.AddValue("cameraFar",F(c.farClipPlane));}
                Light l=t.GetComponent<Light>(); if(l!=null){n.AddValue("lightEnabled",l.enabled);n.AddValue("lightIntensity",F(l.intensity));n.AddValue("lightRange",F(l.range));n.AddValue("lightColor",C(l.color));}
                if(rt!=null)rt.WriteTextProperties(n,t);
            }
            foreach(SceneEditorRuntime.SpawnedCraft c in crafts)
            {
                if(c==null||c.Root==null)continue; ConfigNode n=root.AddNode("CRAFT"); n.AddValue("file",c.FileName);n.AddValue("forceStowed",c.ForceStowed);n.AddValue("position",V(c.Root.transform.position));n.AddValue("rotation",Q(c.Root.transform.rotation));n.AddValue("scale",V(c.Root.transform.localScale));if(rt!=null)rt.WriteCraftControlState(n,c);
            }
            foreach(SceneEditorRuntime.SpawnedLight sl in lights)
            {
                if(sl==null||sl.Root==null)continue; Light l=sl.Root.GetComponent<Light>(); if(l==null)continue; ConfigNode n=root.AddNode("LIGHT"); n.AddValue("position",V(sl.Root.transform.position));n.AddValue("rotation",Q(sl.Root.transform.rotation));n.AddValue("intensity",F(l.intensity));n.AddValue("range",F(l.range));n.AddValue("color",C(l.color));n.AddValue("enabled",l.enabled);
            }
            foreach(SceneEditorRuntime.SpawnedPlanet sp in planets)
            {
                if(sp==null||sp.Root==null)continue;ConfigNode n=root.AddNode("PLANET");n.AddValue("body",sp.BodyName);n.AddValue("position",V(sp.Root.transform.position));n.AddValue("rotation",Q(sp.Root.transform.rotation));n.AddValue("scale",V(sp.Root.transform.localScale));n.AddValue("textureIndex",sp.TextureIndex);
            }
            foreach(SceneEditorRuntime.SpawnedText st in texts)
            {
                if(st==null||st.Root==null)continue;ConfigNode n=root.AddNode("TEXT");n.AddValue("text",st.Text);n.AddValue("position",V(st.Root.transform.position));n.AddValue("rotation",Q(st.Root.transform.rotation));n.AddValue("scale",V(st.Root.transform.localScale));if(rt!=null)rt.WriteTextProperties(n,st.Root.transform);
            }
            foreach(SceneEditorRuntime.SpawnedImage si in images)
            {
                if(si==null||si.Root==null)continue;ConfigNode n=root.AddNode("IMAGE");n.AddValue("file",si.FileName);n.AddValue("position",V(si.Root.transform.position));n.AddValue("rotation",Q(si.Root.transform.rotation));n.AddValue("scale",V(si.Root.transform.localScale));
            }
            if(rt!=null){rt.WriteVisualOverrides(root,contextKey);rt.WriteKerbalOffsets(root);}
            Directory.CreateDirectory(Path.GetDirectoryName(pathOut)); file.Save(pathOut);
                }

        internal static bool TryGetContext(string name,out int area,out int stage,out bool rare,out string contextKey)
        {
            area=-1;stage=-1;rare=false;contextKey=string.Empty;try{string path=SceneFilePath(name);if(!File.Exists(path))return false;ConfigNode file=ConfigNode.Load(path);if(file==null)return false;ConfigNode root=file.GetNode("KSP_SCENE_EDITOR_SCENE")??file;int.TryParse(root.GetValue("area"),out area);int.TryParse(root.GetValue("stage"),out stage);bool.TryParse(root.GetValue("rare"),out rare);contextKey=root.GetValue("context")??string.Empty;return area>=0&&stage>=0;}catch{return false;}
        }

        internal static void Load(string name, SceneEditorRuntime rt)
        {
            LoadFromPath(SceneFilePath(name),rt);
        }

        internal static void LoadFromPath(string path, SceneEditorRuntime rt)
        {

            if(!File.Exists(path))throw new FileNotFoundException("Scene file not found",path);
            ConfigNode file=ConfigNode.Load(path); if(file==null)throw new InvalidDataException("Scene cfg unreadable."); ConfigNode root=file.GetNode("KSP_SCENE_EDITOR_SCENE")??file;
            string savedContext=root.GetValue("context")??string.Empty;
            Color ambient; if(TryColor(root.GetValue("ambient"),out ambient))RenderSettings.ambientLight=ambient;
            string skybox=root.GetValue("skybox");
            if(!string.IsNullOrEmpty(skybox))rt.ApplySkyboxPack(skybox);
            else rt.RestoreOriginalSkybox();
            ConfigNode[] objs=root.GetNodes("OBJECT");
            for(int i=0;i<objs.Length;i++)
            {
                Transform t=rt.ResolvePersistedTransform(objs[i].GetValue("path"),objs[i].GetValue("hint")); if(t==null)continue; rt.MarkEditedForContext(t,savedContext);
                bool b;if(bool.TryParse(objs[i].GetValue("active"),out b))t.gameObject.SetActive(b);
                Vector3 v;Quaternion q;if(TryV(objs[i].GetValue("localPosition"),out v))t.localPosition=v;if(TryQ(objs[i].GetValue("localRotation"),out q))t.localRotation=q;if(TryV(objs[i].GetValue("localScale"),out v))t.localScale=v;
                Camera c=t.GetComponent<Camera>(); float f;if(c!=null){if(TryF(objs[i].GetValue("cameraFov"),out f))c.fieldOfView=f;if(TryF(objs[i].GetValue("cameraNear"),out f))c.nearClipPlane=f;if(TryF(objs[i].GetValue("cameraFar"),out f))c.farClipPlane=f;}
                Light l=t.GetComponent<Light>(); if(l!=null){if(bool.TryParse(objs[i].GetValue("lightEnabled"),out b))l.enabled=b;if(TryF(objs[i].GetValue("lightIntensity"),out f))l.intensity=f;if(TryF(objs[i].GetValue("lightRange"),out f))l.range=f;Color col;if(TryColor(objs[i].GetValue("lightColor"),out col))l.color=col;}
                rt.ReadTextProperties(objs[i],t,savedContext);
            }
            rt.ReadVisualOverrides(root,savedContext);
            rt.ReadKerbalOffsets(root);
            ConfigNode[] crafts=root.GetNodes("CRAFT");
            for(int i=0;i<crafts.Length;i++)
            {
                bool st=true;bool.TryParse(crafts[i].GetValue("forceStowed"),out st);
                GameObject go=rt.SpawnCraft(crafts[i].GetValue("file"),st);if(go==null)continue;
                rt.MarkCreatedForContext(go,savedContext);
                Vector3 v;Quaternion q;
                if(TryV(crafts[i].GetValue("position"),out v))go.transform.position=v;
                if(TryQ(crafts[i].GetValue("rotation"),out q))go.transform.rotation=q;
                if(TryV(crafts[i].GetValue("scale"),out v))go.transform.localScale=v;
                rt.ReadCraftControlState(crafts[i],go);
            }

            ConfigNode[] lights=root.GetNodes("LIGHT");
            for(int i=0;i<lights.Length;i++)
            {
                GameObject go=rt.AddLight();if(go==null)continue;rt.MarkCreatedForContext(go,savedContext);
                Vector3 v;Quaternion q;float f;bool b;Color col;
                if(TryV(lights[i].GetValue("position"),out v))go.transform.position=v;
                if(TryQ(lights[i].GetValue("rotation"),out q))go.transform.rotation=q;
                Light l=go.GetComponent<Light>();
                if(l!=null)
                {
                    if(TryF(lights[i].GetValue("intensity"),out f))l.intensity=f;
                    if(TryF(lights[i].GetValue("range"),out f))l.range=f;
                    if(TryColor(lights[i].GetValue("color"),out col))l.color=col;
                    if(bool.TryParse(lights[i].GetValue("enabled"),out b))l.enabled=b;
                }
            }

            ConfigNode[] planets=root.GetNodes("PLANET");
            for(int i=0;i<planets.Length;i++)
            {
                GameObject go=rt.AddPlanetClone(planets[i].GetValue("body"));if(go==null)continue;rt.MarkCreatedForContext(go,savedContext);
                Vector3 v;Quaternion q;int textureIndex=0;
                if(TryV(planets[i].GetValue("position"),out v))go.transform.position=v;
                if(TryQ(planets[i].GetValue("rotation"),out q))go.transform.rotation=q;
                if(TryV(planets[i].GetValue("scale"),out v))go.transform.localScale=v;
                if(int.TryParse(planets[i].GetValue("textureIndex"),out textureIndex))rt.RestorePlanetTextureIndex(go,textureIndex);
            }

            ConfigNode[] texts=root.GetNodes("TEXT");
            for(int i=0;i<texts.Length;i++)
            {
                GameObject go=rt.AddTextLabel(texts[i].GetValue("text"));if(go==null)continue;rt.MarkCreatedForContext(go,savedContext);
                Vector3 v;Quaternion q;
                if(TryV(texts[i].GetValue("position"),out v))go.transform.position=v;
                if(TryQ(texts[i].GetValue("rotation"),out q))go.transform.rotation=q;
                if(TryV(texts[i].GetValue("scale"),out v))go.transform.localScale=v;
                rt.ReadTextProperties(texts[i],go.transform,savedContext);
            }

            ConfigNode[] images=root.GetNodes("IMAGE");
            for(int i=0;i<images.Length;i++)
            {
                GameObject go=rt.AddFreeImage(images[i].GetValue("file"));if(go==null)continue;rt.MarkCreatedForContext(go,savedContext);
                Vector3 v;Quaternion q;
                if(TryV(images[i].GetValue("position"),out v))go.transform.position=v;
                if(TryQ(images[i].GetValue("rotation"),out q))go.transform.rotation=q;
                if(TryV(images[i].GetValue("scale"),out v))go.transform.localScale=v;
            }
                }

        private static string Sanitize(string s){foreach(char c in Path.GetInvalidFileNameChars())s=s.Replace(c,'_');return s;}
        private static string F(float v){return v.ToString("R",CultureInfo.InvariantCulture);} private static string V(Vector3 v){return F(v.x)+","+F(v.y)+","+F(v.z);} private static string Q(Quaternion q){return F(q.x)+","+F(q.y)+","+F(q.z)+","+F(q.w);} private static string C(Color c){return F(c.r)+","+F(c.g)+","+F(c.b)+","+F(c.a);}
        internal static bool TryF(string s,out float f){return float.TryParse(s,NumberStyles.Float,CultureInfo.InvariantCulture,out f);} internal static bool TryV(string s,out Vector3 v){v=Vector3.zero;if(string.IsNullOrEmpty(s))return false;string[]p=s.Split(',');float a,b,c;if(p.Length<3||!TryF(p[0],out a)||!TryF(p[1],out b)||!TryF(p[2],out c))return false;v=new Vector3(a,b,c);return true;} internal static bool TryQ(string s,out Quaternion q){q=Quaternion.identity;if(string.IsNullOrEmpty(s))return false;string[]p=s.Split(',');float a,b,c,d;if(p.Length<4||!TryF(p[0],out a)||!TryF(p[1],out b)||!TryF(p[2],out c)||!TryF(p[3],out d))return false;q=new Quaternion(a,b,c,d);return true;} internal static bool TryColor(string s,out Color c){c=Color.white;if(string.IsNullOrEmpty(s))return false;string[]p=s.Split(',');float a,b,d,e;if(p.Length<3||!TryF(p[0],out a)||!TryF(p[1],out b)||!TryF(p[2],out d))return false;e=1;if(p.Length>3)TryF(p[3],out e);c=new Color(a,b,d,e);return true;}
    }
}
