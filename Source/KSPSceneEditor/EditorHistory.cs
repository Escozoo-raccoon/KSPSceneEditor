using System.Collections.Generic;
using UnityEngine;

namespace KSPSceneEditor
{
    internal sealed class EditorHistory
    {
        private sealed class State
        {
            internal Transform T;
            internal Vector3 Position;
            internal Quaternion Rotation;
            internal Vector3 Scale;
            internal bool Active;
            internal bool HasCamera;
            internal float Fov;
            internal bool HasLight;
            internal float Intensity;
            internal float Range;
            internal bool LightEnabled;
        }

        private readonly Stack<State> undo = new Stack<State>();
        private readonly Stack<State> redo = new Stack<State>();
        internal int UndoCount { get { return undo.Count; } }
        internal int RedoCount { get { return redo.Count; } }

        internal void Capture(Transform t)
        {
            if (t == null) return;
            undo.Push(Read(t));
            redo.Clear();
            while (undo.Count > 80) TrimBottom(undo);
        }

        internal bool Undo()
        {
            if (undo.Count == 0) return false;
            State s = undo.Pop();
            if (s.T == null) return false;
            redo.Push(Read(s.T));
            Apply(s);
            return true;
        }

        internal bool Redo()
        {
            if (redo.Count == 0) return false;
            State s = redo.Pop();
            if (s.T == null) return false;
            undo.Push(Read(s.T));
            Apply(s);
            return true;
        }

        internal void Clear() { undo.Clear(); redo.Clear(); }

        private static State Read(Transform t)
        {
            State s = new State();
            s.T=t; s.Position=t.position; s.Rotation=t.rotation; s.Scale=t.localScale; s.Active=t.gameObject.activeSelf;
            Camera c=t.GetComponent<Camera>(); if(c!=null){s.HasCamera=true;s.Fov=c.fieldOfView;}
            Light l=t.GetComponent<Light>(); if(l!=null){s.HasLight=true;s.Intensity=l.intensity;s.Range=l.range;s.LightEnabled=l.enabled;}
            return s;
        }

        private static void Apply(State s)
        {
            s.T.position=s.Position; s.T.rotation=s.Rotation; s.T.localScale=s.Scale; s.T.gameObject.SetActive(s.Active);
            if(s.HasCamera){Camera c=s.T.GetComponent<Camera>();if(c!=null)c.fieldOfView=s.Fov;}
            if(s.HasLight){Light l=s.T.GetComponent<Light>();if(l!=null){l.intensity=s.Intensity;l.range=s.Range;l.enabled=s.LightEnabled;}}
        }

        private static void TrimBottom(Stack<State> stack)
        {
            State[] a=stack.ToArray(); stack.Clear();
            for(int i=a.Length-2;i>=0;i--)stack.Push(a[i]);
        }
    }
}
