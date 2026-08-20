using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KSPSceneEditor
{
    internal sealed class SceneSnapshot
    {
        internal sealed class TState
        {
            internal Transform T; internal Transform Parent; internal int Sibling;
            internal Vector3 LocalPosition; internal Quaternion LocalRotation; internal Vector3 LocalScale; internal bool Active;
        }
        internal sealed class CameraState { internal Camera C; internal float Fov, Near, Far; internal bool Enabled; }
        internal sealed class LightState { internal Light L; internal float Intensity, Range, Spot; internal Color Color; internal bool Enabled; internal LightShadows Shadows; }

        private readonly List<TState> transforms = new List<TState>();
        private readonly List<CameraState> cameras = new List<CameraState>();
        private readonly List<LightState> lights = new List<LightState>();
        private Color ambient;
        internal int Count { get { return transforms.Count; } }

        internal void Capture(Transform editorRoot)
        {
            transforms.Clear(); cameras.Clear(); lights.Clear(); ambient = RenderSettings.ambientLight;

            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded) return;

            GameObject[] roots = scene.GetRootGameObjects();
            for (int r = 0; r < roots.Length; r++)
            {
                GameObject root = roots[r];
                if (root == null) continue;
                Transform[] all = root.GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < all.Length; i++)
                {
                    Transform t = all[i];
                    if (t == null) continue;
                    if (editorRoot != null && (t == editorRoot || t.IsChildOf(editorRoot))) continue;
                    TState s = new TState();
                    s.T = t; s.Parent = t.parent; s.Sibling = t.GetSiblingIndex();
                    s.LocalPosition = t.localPosition; s.LocalRotation = t.localRotation; s.LocalScale = t.localScale;
                    s.Active = t.gameObject.activeSelf; transforms.Add(s);
                }

                Camera[] cs = root.GetComponentsInChildren<Camera>(true);
                for (int i = 0; i < cs.Length; i++)
                {
                    Camera c = cs[i]; if (c == null) continue;
                    cameras.Add(new CameraState { C = c, Fov = c.fieldOfView, Near = c.nearClipPlane, Far = c.farClipPlane, Enabled = c.enabled });
                }

                Light[] ls = root.GetComponentsInChildren<Light>(true);
                for (int i = 0; i < ls.Length; i++)
                {
                    Light l = ls[i]; if (l == null) continue;
                    lights.Add(new LightState { L = l, Intensity = l.intensity, Range = l.range, Spot = l.spotAngle, Color = l.color, Enabled = l.enabled, Shadows = l.shadows });
                }
            }
        }

        internal bool RestoreOne(Transform target)
        {
            if (target == null) return false;
            for (int i = 0; i < transforms.Count; i++)
            {
                TState s = transforms[i];
                if (s.T != target) continue;
                try
                {
                    if (s.T.parent != s.Parent && s.Parent != null) s.T.SetParent(s.Parent, false);
                    s.T.localPosition = s.LocalPosition; s.T.localRotation = s.LocalRotation; s.T.localScale = s.LocalScale;
                    if (s.T.parent != null && s.T.parent.childCount > 0) s.T.SetSiblingIndex(Mathf.Clamp(s.Sibling, 0, s.T.parent.childCount - 1));
                    s.T.gameObject.SetActive(s.Active);
                    for (int c = 0; c < cameras.Count; c++) if (cameras[c].C != null && cameras[c].C.transform == target) { CameraState cs=cameras[c]; cs.C.fieldOfView=cs.Fov; cs.C.nearClipPlane=cs.Near; cs.C.farClipPlane=cs.Far; cs.C.enabled=cs.Enabled; }
                    for (int l = 0; l < lights.Count; l++) if (lights[l].L != null && lights[l].L.transform == target) { LightState ls=lights[l]; ls.L.intensity=ls.Intensity; ls.L.range=ls.Range; ls.L.spotAngle=ls.Spot; ls.L.color=ls.Color; ls.L.enabled=ls.Enabled; ls.L.shadows=ls.Shadows; }
                    return true;
                } catch { return false; }
            }
            return false;
        }

        internal int Restore()
        {
            int restored = 0;
            for (int i = 0; i < transforms.Count; i++)
            {
                TState s = transforms[i]; if (s.T == null) continue;
                try
                {
                    if (s.T.parent != s.Parent && s.Parent != null) s.T.SetParent(s.Parent, false);
                    s.T.localPosition = s.LocalPosition; s.T.localRotation = s.LocalRotation; s.T.localScale = s.LocalScale;
                    if (s.T.parent != null && s.T.parent.childCount > 0) s.T.SetSiblingIndex(Mathf.Clamp(s.Sibling, 0, s.T.parent.childCount - 1));
                    s.T.gameObject.SetActive(s.Active); restored++;
                } catch { }
            }
            for (int i = 0; i < cameras.Count; i++) { CameraState s = cameras[i]; if (s.C == null) continue; s.C.fieldOfView=s.Fov; s.C.nearClipPlane=s.Near; s.C.farClipPlane=s.Far; s.C.enabled=s.Enabled; }
            for (int i = 0; i < lights.Count; i++) { LightState s = lights[i]; if (s.L == null) continue; s.L.intensity=s.Intensity; s.L.range=s.Range; s.L.spotAngle=s.Spot; s.L.color=s.Color; s.L.enabled=s.Enabled; s.L.shadows=s.Shadows; }
            RenderSettings.ambientLight = ambient;
            return restored;
        }
    }
}
