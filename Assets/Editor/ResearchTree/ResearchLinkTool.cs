using Game.Data;
using Game.Tools;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;

namespace Game.EditorTools
{
    /// <summary>
    /// Drawing prerequisites by clicking: a first node, then a second - the first becomes a
    /// prerequisite of the second. Clicking a pair already linked removes the link; clicking empty
    /// ground drops the first node. A Unity editor tool, offered in the Tools overlay whenever a
    /// research node is selected; every change goes through Undo.
    ///
    /// Clicks, not a list of thirty researches to pick from: it is quicker, and a click cannot name a
    /// research that no longer exists.
    /// </summary>
    [EditorTool("Link Prerequisites", typeof(ResearchNodeHandle))]
    public sealed class ResearchLinkTool : EditorTool
    {
        const float PickRadiusPixels = 18f;

        ResearchNodeHandle _from;
        GUIContent _icon;

        public override GUIContent toolbarIcon => _icon ??= new GUIContent("Link",
            "Click a node, then another: the first becomes a prerequisite of the second. Clicking a linked pair again removes the link.");

        public override void OnWillBeDeactivated() => _from = null;

        public override void OnToolGUI(EditorWindow window)
        {
            if (!ResearchTreeScene.IsOpen) return;

            Event current = Event.current;
            int control = GUIUtility.GetControlID(FocusType.Passive);
            if (current.type == EventType.Layout) HandleUtility.AddDefaultControl(control);

            if (current.type == EventType.MouseDown && current.button == 0)
            {
                ResearchNodeHandle hit = Pick(current.mousePosition);
                if (hit == null) _from = null;
                else if (_from == null) _from = hit;
                else if (hit != _from)
                {
                    Link(_from.Research, hit.Research);
                    _from = null;
                }
                current.Use();
            }

            if (_from == null) return;

            Handles.color = Color.yellow;
            Handles.DrawDottedLine(_from.transform.position, OnPlane(current.mousePosition), 4f);
            if (current.type == EventType.MouseMove) window.Repaint();
        }

        static void Link(ResearchDefinition prerequisite, ResearchDefinition research)
        {
            bool linked = ResearchTreeScene.ToggleLink(prerequisite, research);
            if (!linked) return;

            ResearchTreeDiagnosis.Refresh();
            if (ResearchTreeDiagnosis.InCycle.Contains(research))
            {
                Debug.LogWarning($"Research tree: linking {prerequisite.name} to {research.name} closes a prerequisite cycle - undo it, or break the cycle elsewhere.", research);
            }
        }

        static ResearchNodeHandle Pick(Vector2 mousePosition)
        {
            ResearchNodeHandle nearest = null;
            float best = PickRadiusPixels;
            foreach (ResearchNodeHandle handle in ResearchTreeScene.Handles)
            {
                if (handle == null || handle.Research == null) continue;

                float distance = Vector2.Distance(HandleUtility.WorldToGUIPoint(handle.transform.position), mousePosition);
                if (distance > best) continue;
                best = distance;
                nearest = handle;
            }
            return nearest;
        }

        static Vector3 OnPlane(Vector2 mousePosition)
        {
            Ray ray = HandleUtility.GUIPointToWorldRay(mousePosition);
            return new Plane(Vector3.forward, Vector3.zero).Raycast(ray, out float distance) ? ray.GetPoint(distance) : Vector3.zero;
        }
    }
}
