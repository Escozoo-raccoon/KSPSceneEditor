using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace KSPSceneEditor
{
    internal static class CraftVisualLoader
    {
        private static readonly Dictionary<int,List<CraftControl>> controlRegistry=new Dictionary<int,List<CraftControl>>();
        private sealed class CraftPart
        {
            internal string InternalName, InstanceName;
            internal Vector3 Position, Mirror;
            internal Quaternion Rotation;
        }

        internal sealed class CraftControl
        {
            internal string Label;
            internal string AnimationName;
            internal GameObject VisualRoot;
            internal bool Open;
            internal bool Inverted;
        }

        internal static List<CraftControl> DiscoverControls(GameObject craftRoot)
        {
            List<CraftControl> result=new List<CraftControl>();
            if(craftRoot==null)return result;
            List<CraftControl> registered;
            if(controlRegistry.TryGetValue(craftRoot.GetInstanceID(),out registered)&&registered!=null)
            {
                result.AddRange(registered);return result;
            }
            Transform[] all=craftRoot.GetComponentsInChildren<Transform>(true);
            for(int i=0;i<all.Length;i++)
            {
                Transform t=all[i];if(t==null||!t.name.StartsWith("KSE_VISUAL_",StringComparison.OrdinalIgnoreCase))continue;
                Animation[] aa=t.GetComponentsInChildren<Animation>(true);
                for(int ai=0;ai<aa.Length;ai++)
                {
                    Animation a=aa[ai];if(a==null)continue;
                    foreach(AnimationState s in a)
                    {
                        if(s==null||string.IsNullOrEmpty(s.name))continue;
                        bool duplicate=false;
                        for(int r=0;r<result.Count;r++)if(result[r].VisualRoot==t.gameObject&&string.Equals(result[r].AnimationName,s.name,StringComparison.OrdinalIgnoreCase)){duplicate=true;break;}
                        if(!duplicate)result.Add(new CraftControl{Label=FriendlyControlName(t.name,s.name),AnimationName=s.name,VisualRoot=t.gameObject,Open=false});
                    }
                }
            }
            return result;
        }

        internal static void SetControl(CraftControl control,bool open)
        {
            if(control==null||control.VisualRoot==null)return;
            float sample=open?1f:0f;if(control.Inverted)sample=1f-sample;
            SampleAnimation(control.VisualRoot,control.AnimationName,sample);control.Open=open;
        }

        internal static void InvertControl(CraftControl control)
        {
            if(control==null)return;control.Inverted=!control.Inverted;SetControl(control,control.Open);
        }

        internal static void SetAllControls(List<CraftControl> controls,bool open)
        {
            if(controls==null)return;for(int i=0;i<controls.Count;i++)SetControl(controls[i],open);
        }

        private static string FriendlyControlName(string visual,string anim)
        {
            string part=(visual??"PIÈCE").Replace("KSE_VISUAL_","");
            string a=anim??"ANIMATION";
            string low=a.ToLowerInvariant();
            string kind=low.Contains("gear")||low.Contains("wheel")?"TRAIN":
                        low.Contains("ladder")?"ÉCHELLE":
                        low.Contains("solar")||low.Contains("panel")?"PANNEAU":
                        low.Contains("anten")||low.Contains("deploy")?"DÉPLOIEMENT":"ANIMATION";
            return kind+" // "+part+" // "+a;
        }

        internal static GameObject Load(string craftFile, Transform parent, bool forceStowed, out int mounted, out int missing)
        {
            mounted = 0; missing = 0;
            string safeName = Path.GetFileName(craftFile ?? string.Empty);
            if (string.IsNullOrEmpty(safeName)) throw new ArgumentException("Craft filename is empty.");
            string path = Path.Combine(KSPUtil.ApplicationRootPath, "GameData", "KSPSceneEditor", "Crafts", safeName);
            if (!File.Exists(path)) throw new FileNotFoundException("Craft not found", path);

            ConfigNode rootNode = ConfigNode.Load(path);
            if (rootNode == null) throw new InvalidDataException("ConfigNode.Load returned null.");
            ConfigNode[] partNodes = rootNode.GetNodes("PART");
            if (partNodes == null || partNodes.Length == 0) throw new InvalidDataException("No PART nodes in craft.");
            List<CraftPart> parts = ParseParts(partNodes);
            Vector3 center = CalculateCenter(parts);

            GameObject root = new GameObject("KSE_CRAFT_" + Path.GetFileNameWithoutExtension(safeName));
            if (parent != null) root.transform.SetParent(parent, false);
            List<CraftControl> controls=new List<CraftControl>();

            for (int i = 0; i < parts.Count; i++)
            {
                CraftPart cp = parts[i];
                AvailablePart ap = FindPart(cp.InternalName);
                if (ap == null) { missing++; continue; }
                GameObject visual = CloneVisual(ap, cp.InstanceName);
                if (visual == null) { missing++; continue; }

                GameObject slot = new GameObject("KSE_PART_" + i);
                slot.transform.SetParent(root.transform, false);
                slot.transform.localPosition = cp.Position - center;
                slot.transform.localRotation = cp.Rotation;
                slot.transform.localScale = cp.Mirror;
                visual.transform.SetParent(slot.transform, false);
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localRotation = Quaternion.identity;
                visual.transform.localScale = Vector3.one;
                visual.SetActive(true);
                ForceRenderers(visual);
                SetLayerRecursive(visual, 0);
                CollectPartControls(ap,visual,controls);
                if (forceStowed) ForceStowed(ap, visual);
                mounted++;
            }

            if (mounted == 0)
            {
                UnityEngine.Object.Destroy(root);
                throw new InvalidOperationException("No craft visuals could be mounted.");
            }
            ForceRenderers(root); SetLayerRecursive(root, 0); root.SetActive(true);
            controlRegistry[root.GetInstanceID()]=controls;
            return root;
        }

        private static List<CraftPart> ParseParts(ConfigNode[] nodes)
        {
            List<CraftPart> r = new List<CraftPart>(nodes.Length);
            for (int i = 0; i < nodes.Length; i++)
            {
                ConfigNode n = nodes[i]; string pv=n.GetValue("part"), pos=n.GetValue("pos"), rot=n.GetValue("rot");
                if (string.IsNullOrEmpty(pv) || string.IsNullOrEmpty(pos) || string.IsNullOrEmpty(rot)) continue;
                r.Add(new CraftPart { InstanceName=pv, InternalName=StripId(pv), Position=ParseVector3(pos), Rotation=ParseQuaternion(rot), Mirror=string.IsNullOrEmpty(n.GetValue("mir"))?Vector3.one:ParseVector3(n.GetValue("mir")) });
            }
            if (r.Count == 0) throw new InvalidDataException("No valid craft parts parsed.");
            return r;
        }

        private static string StripId(string value)
        {
            int p=value.LastIndexOf('_'); if (p<=0) return value;
            ulong x; return ulong.TryParse(value.Substring(p+1), NumberStyles.Integer, CultureInfo.InvariantCulture, out x) ? value.Substring(0,p) : value;
        }
        private static Vector3 CalculateCenter(List<CraftPart> p)
        {
            Vector3 min=p[0].Position,max=p[0].Position; for(int i=1;i<p.Count;i++){min=Vector3.Min(min,p[i].Position);max=Vector3.Max(max,p[i].Position);} return (min+max)*0.5f;
        }
        private static AvailablePart FindPart(string name)
        {
            if (PartLoader.LoadedPartsList == null) return null;
            for(int i=0;i<PartLoader.LoadedPartsList.Count;i++){AvailablePart ap=PartLoader.LoadedPartsList[i]; if(ap!=null&&string.Equals(ap.name,name,StringComparison.OrdinalIgnoreCase)) return ap;} return null;
        }
        private static GameObject CloneVisual(AvailablePart ap,string instance)
        {
            if(ap==null||ap.partPrefab==null)return null; Transform model=ap.partPrefab.transform.Find("model"); if(model==null)return null;
            GameObject clone=(GameObject)UnityEngine.Object.Instantiate(model.gameObject); clone.name="KSE_VISUAL_"+instance; return clone;
        }
        private static void ForceRenderers(GameObject go){Renderer[] rr=go.GetComponentsInChildren<Renderer>(true);for(int i=0;i<rr.Length;i++)if(rr[i]!=null)rr[i].enabled=true;}
        private static void SetLayerRecursive(GameObject go,int layer){go.layer=layer;for(int i=0;i<go.transform.childCount;i++)SetLayerRecursive(go.transform.GetChild(i).gameObject,layer);}
        private static void CollectPartControls(AvailablePart ap,GameObject visual,List<CraftControl> controls)
        {
            if(ap==null||ap.partConfig==null||visual==null||controls==null)return;
            ConfigNode[] mods=ap.partConfig.GetNodes("MODULE");
            for(int i=0;i<mods.Length;i++)
            {
                ConfigNode m=mods[i];string mn=m.GetValue("name")??string.Empty;
                bool relevant=
                    mn.IndexOf("WheelDeployment",StringComparison.OrdinalIgnoreCase)>=0||
                    mn.IndexOf("Ladder",StringComparison.OrdinalIgnoreCase)>=0||
                    mn.IndexOf("DeployableSolarPanel",StringComparison.OrdinalIgnoreCase)>=0||
                    mn.IndexOf("DeployableAntenna",StringComparison.OrdinalIgnoreCase)>=0||
                    mn.IndexOf("AnimateGeneric",StringComparison.OrdinalIgnoreCase)>=0||
                    mn.IndexOf("CargoBay",StringComparison.OrdinalIgnoreCase)>=0;
                if(!relevant)continue;
                string anim=FirstValue(m,new string[]{"animationName","animName","ladderAnimationName","deployAnimationName","animation"});
                if(string.IsNullOrEmpty(anim)||!HasAnimation(visual,anim))continue;
                string kind=ModuleKind(mn,anim);
                bool duplicate=false;
                for(int r=0;r<controls.Count;r++)if(controls[r].VisualRoot==visual&&string.Equals(controls[r].AnimationName,anim,StringComparison.OrdinalIgnoreCase)){duplicate=true;break;}
                if(!duplicate)controls.Add(new CraftControl{Label=kind+" // "+ap.title,AnimationName=anim,VisualRoot=visual,Open=false,Inverted=false});
            }
        }

        private static string FirstValue(ConfigNode n,string[] keys)
        {
            for(int i=0;i<keys.Length;i++){string v=n.GetValue(keys[i]);if(!string.IsNullOrEmpty(v))return v;}return null;
        }

        private static bool HasAnimation(GameObject root,string name)
        {
            Animation[] aa=root.GetComponentsInChildren<Animation>(true);
            for(int i=0;i<aa.Length;i++)if(aa[i]!=null&&aa[i][name]!=null)return true;
            return false;
        }

        private static string ModuleKind(string moduleName,string anim)
        {
            string s=(moduleName+" "+anim).ToLowerInvariant();
            if(s.Contains("wheel")||s.Contains("gear"))return "TRAIN";
            if(s.Contains("ladder"))return "ÉCHELLE";
            if(s.Contains("solar")||s.Contains("panel"))return "PANNEAU";
            if(s.Contains("anten"))return "ANTENNE";
            if(s.Contains("cargo"))return "SOUTE";
            return "ANIMATION";
        }

        private static void ForceStowed(AvailablePart ap, GameObject visual)
        {
            if(ap==null||ap.partConfig==null)return; ConfigNode[] mods=ap.partConfig.GetNodes("MODULE");
            for(int i=0;i<mods.Length;i++)
            {
                string mn=mods[i].GetValue("name");
                bool ok=mn.IndexOf("WheelDeployment",StringComparison.OrdinalIgnoreCase)>=0||mn.IndexOf("Ladder",StringComparison.OrdinalIgnoreCase)>=0||mn.IndexOf("DeployableSolarPanel",StringComparison.OrdinalIgnoreCase)>=0||mn.IndexOf("DeployableAntenna",StringComparison.OrdinalIgnoreCase)>=0||mn.IndexOf("AnimateGeneric",StringComparison.OrdinalIgnoreCase)>=0;
                if(!ok)continue;string anim=FirstValue(mods[i],new string[]{"animationName","animName","ladderAnimationName","deployAnimationName","animation"});if(string.IsNullOrEmpty(anim))continue;SampleAnimation(visual,anim,0f);
            }
        }
        private static void SampleAnimation(GameObject root,string name,float time)
        {
            if(root==null)return;root.SetActive(true);
            Animation[] aa=root.GetComponentsInChildren<Animation>(true);
            for(int i=0;i<aa.Length;i++)
            {
                Animation a=aa[i];if(a==null)continue;a.gameObject.SetActive(true);
                AnimationState s=a[name];if(s==null)continue;
                bool wasEnabled=a.enabled;a.enabled=true;
                s.enabled=true;s.weight=1f;s.speed=0f;s.normalizedTime=Mathf.Clamp01(time);
                a.Sample();s.speed=0f;s.enabled=true;a.enabled=wasEnabled;
            }
        }
        private static Vector3 ParseVector3(string s)
        {
            string[] p=s.Split(','); if(p.Length<3)throw new FormatException("Invalid Vector3: "+s); return new Vector3(PF(p[0]),PF(p[1]),PF(p[2]));
        }
        private static Quaternion ParseQuaternion(string s)
        {
            string[] p=s.Split(','); if(p.Length<4)throw new FormatException("Invalid Quaternion: "+s); return new Quaternion(PF(p[0]),PF(p[1]),PF(p[2]),PF(p[3]));
        }
        private static float PF(string s){return float.Parse(s.Trim(),NumberStyles.Float,CultureInfo.InvariantCulture);}
    }
}
