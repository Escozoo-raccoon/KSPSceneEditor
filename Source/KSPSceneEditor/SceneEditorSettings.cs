using System;
using System.IO;
using UnityEngine;

namespace KSPSceneEditor
{
    internal sealed class SceneEditorSettings
    {
        internal KeyCode ToggleKey = KeyCode.F10;
        internal bool RequireCtrl = true;
        internal bool RequireAlt = true;
        internal bool RequireShift = false;

        internal string ShortcutLabel
        {
            get
            {
                string s = string.Empty;
                if (RequireCtrl) s += "Ctrl+";
                if (RequireAlt) s += "Alt+";
                if (RequireShift) s += "Shift+";
                return s + ToggleKey;
            }
        }

        internal static SceneEditorSettings Load()
        {
            SceneEditorSettings settings = new SceneEditorSettings();
            try
            {
                string path = Path.Combine(KSPUtil.ApplicationRootPath, "GameData/KSPSceneEditor/PluginData/settings.cfg");
                if (!File.Exists(path))
                {
                    SceneEditorLog.Info("Settings file not found; using default shortcut " + settings.ShortcutLabel);
                    return settings;
                }

                string[] lines = File.ReadAllLines(path);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = (lines[i] ?? string.Empty).Trim();
                    if (line.Length == 0 || line.StartsWith("//") || line.StartsWith("#")) continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq).Trim();
                    string value = line.Substring(eq + 1).Trim();

                    if (key.Equals("toggleKey", StringComparison.OrdinalIgnoreCase))
                    {
                        KeyCode parsed;
                        if (Enum.TryParse<KeyCode>(value, true, out parsed)) settings.ToggleKey = parsed;
                    }
                    else if (key.Equals("requireCtrl", StringComparison.OrdinalIgnoreCase)) settings.RequireCtrl = ParseBool(value, settings.RequireCtrl);
                    else if (key.Equals("requireAlt", StringComparison.OrdinalIgnoreCase)) settings.RequireAlt = ParseBool(value, settings.RequireAlt);
                    else if (key.Equals("requireShift", StringComparison.OrdinalIgnoreCase)) settings.RequireShift = ParseBool(value, settings.RequireShift);
                }
                SceneEditorLog.Info("Shortcut loaded: " + settings.ShortcutLabel);
            }
            catch (Exception ex)
            {
                SceneEditorLog.Warn("Settings load failed; defaults used: " + ex.Message);
            }
            return settings;
        }

        private static bool ParseBool(string value, bool fallback)
        {
            bool parsed;
            return bool.TryParse(value, out parsed) ? parsed : fallback;
        }

        internal bool TogglePressed()
        {
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (RequireCtrl && !ctrl) return false;
            if (RequireAlt && !alt) return false;
            if (RequireShift && !shift) return false;
            return Input.GetKeyDown(ToggleKey);
        }
    }
}
