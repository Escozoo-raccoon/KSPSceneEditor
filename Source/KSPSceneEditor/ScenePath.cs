using System;
using System.Text;
using UnityEngine;
namespace KSPSceneEditor
{
    internal static class ScenePath
    {
        internal static string Get(Transform t)
        {
            if (t == null) return string.Empty;
            StringBuilder sb = new StringBuilder();
            Transform cur = t;
            while (cur != null)
            {
                string seg = Escape(cur.name) + "[" + cur.GetSiblingIndex() + "]";
                if (sb.Length == 0) sb.Insert(0, seg); else sb.Insert(0, seg + "/");
                cur = cur.parent;
            }
            return sb.ToString();
        }

        internal static Transform Find(string path)
        {
            Transform exact=FindInternal(path,true);
            if(exact!=null)return exact;
            return FindInternal(path,false);
        }

        private static Transform FindInternal(string path,bool requireSiblingIndex)
        {
            if(string.IsNullOrEmpty(path))return null;
            string[] parts=path.Split('/');
            Transform current=null;
            Transform[] all=Resources.FindObjectsOfTypeAll<Transform>();
            string rootName;int rootIndex;
            Parse(parts[0],out rootName,out rootIndex);

            for(int i=0;i<all.Length;i++)
            {
                Transform t=all[i];
                if(t==null||t.parent!=null||!InLoadedScene(t))continue;
                if(!string.Equals(t.name,rootName,StringComparison.Ordinal))continue;
                if(requireSiblingIndex&&t.GetSiblingIndex()!=rootIndex)continue;
                current=t;break;
            }

            if(current==null)return null;

            for(int i=1;i<parts.Length;i++)
            {
                string name;int index;Parse(parts[i],out name,out index);
                Transform found=null;

                // Exact sibling index first.
                for(int c=0;c<current.childCount;c++)
                {
                    Transform ch=current.GetChild(c);
                    if(ch==null||!string.Equals(ch.name,name,StringComparison.Ordinal))continue;
                    if(ch.GetSiblingIndex()==index){found=ch;break;}
                }

                // KSP can reorder menu children between native stages.
                // If the indexed child no longer exists, use the unique child with the same name.
                if(found==null&&!requireSiblingIndex)
                {
                    Transform candidate=null;int matches=0;
                    for(int c=0;c<current.childCount;c++)
                    {
                        Transform ch=current.GetChild(c);
                        if(ch!=null&&string.Equals(ch.name,name,StringComparison.Ordinal))
                        {
                            candidate=ch;matches++;
                        }
                    }
                    if(matches==1)found=candidate;
                    else if(matches>1)
                    {
                        // Deterministic fallback: closest sibling index among same-name children.
                        int best=int.MaxValue;
                        for(int c=0;c<current.childCount;c++)
                        {
                            Transform ch=current.GetChild(c);
                            if(ch==null||!string.Equals(ch.name,name,StringComparison.Ordinal))continue;
                            int d=Math.Abs(ch.GetSiblingIndex()-index);
                            if(d<best){best=d;found=ch;}
                        }
                    }
                }

                if(found==null)return null;
                current=found;
            }
            return current;
        }

        internal static bool InLoadedScene(Transform t)
        {
            try { return t != null && t.gameObject.scene.IsValid() && t.gameObject.scene.isLoaded; }
            catch { return false; }
        }

        private static string Escape(string s) { return (s ?? string.Empty).Replace("/", "∕"); }
        private static void Parse(string s, out string name, out int index)
        {
            name = s ?? string.Empty; index = 0;
            int a = name.LastIndexOf('['); int b = name.LastIndexOf(']');
            if (a >= 0 && b > a)
            {
                int parsed; if (int.TryParse(name.Substring(a + 1, b - a - 1), out parsed)) index = parsed;
                name = name.Substring(0, a);
            }
            name = name.Replace("∕", "/");
        }
    }
}
