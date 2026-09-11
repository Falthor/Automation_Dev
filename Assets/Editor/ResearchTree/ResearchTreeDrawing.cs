using System.Collections.Generic;
using System.Text;
using Game.Data;
using Game.Tools;
using UnityEditor;
using UnityEngine;

namespace Game.EditorTools
{
    /// <summary>
    /// What the research tree scene shows besides its handles, drawn rather than made of objects: the
    /// rings, each core's sector boundaries, the prerequisite links with their direction, and a
    /// summary of what ResearchTreeDiagnosis finds. A node is a pickable gizmo, so a click selects it.
    ///
    /// Colours: a core cyan, a research grey, red on a cycle, orange when unreachable, magenta
    /// outside its sector.
    /// </summary>
    [InitializeOnLoad]
    public static class ResearchTreeDrawing
    {
        const float CoreRadius = 0.45f;
        const float NodeRadius = 0.3f;

        static readonly Color RingColor = new Color(1f, 1f, 1f, 0.12f);
        static readonly Color SectorColor = new Color(0.33f, 0.87f, 0.96f, 0.35f);
        static readonly Color LinkColor = new Color(0.85f, 0.85f, 0.85f, 0.8f);
        static readonly Color CoreColor = new Color(0.33f, 0.87f, 0.96f);
        static readonly Color ResearchColor = new Color(0.8f, 0.8f, 0.8f);
        static readonly Color CycleColor = new Color(1f, 0.25f, 0.25f);
        static readonly Color UnreachableColor = new Color(1f, 0.6f, 0.15f);
        static readonly Color OutOfSectorColor = new Color(0.95f, 0.35f, 0.95f);

        static GUIStyle _labelStyle;

        static ResearchTreeDrawing()
        {
            SceneView.duringSceneGui += DrawScene;
        }

        [DrawGizmo(GizmoType.NonSelected | GizmoType.Selected | GizmoType.Pickable)]
        static void DrawNode(ResearchNodeHandle handle, GizmoType gizmoType)
        {
            ResearchDefinition research = handle.Research;
            if (research == null || !ResearchTreeScene.IsOpen) return;

            ResearchTreeDiagnosis.Refresh();
            bool isCore = ResearchTreeDiagnosis.Cores.Contains(research);
            float radius = isCore ? CoreRadius : NodeRadius;
            Vector3 position = handle.transform.position;

            Gizmos.color = ColourOf(research, isCore);
            Gizmos.DrawSphere(position, radius);
            if ((gizmoType & GizmoType.Selected) != 0)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawWireSphere(position, radius * 1.5f);
            }

            _labelStyle ??= new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.UpperCenter, normal = { textColor = Color.white } };
            Handles.Label(position + Vector3.down * (radius + 0.15f), research.DisplayName, _labelStyle);
        }

        static Color ColourOf(ResearchDefinition research, bool isCore)
        {
            if (ResearchTreeDiagnosis.InCycle.Contains(research)) return CycleColor;
            if (ResearchTreeDiagnosis.Unreachable.Contains(research)) return UnreachableColor;
            if (ResearchTreeDiagnosis.OutOfSector.Contains(research)) return OutOfSectorColor;
            return isCore ? CoreColor : ResearchColor;
        }

        static void DrawScene(SceneView view)
        {
            if (Event.current.type != EventType.Repaint || !ResearchTreeScene.IsOpen) return;

            ResearchTreeDiagnosis.Refresh();
            float outer = (ResearchTreeDiagnosis.MaxTier + 1) * ResearchTreeScene.RingStep;

            Handles.color = RingColor;
            for (int tier = 1; tier <= ResearchTreeDiagnosis.MaxTier + 1; tier++) Handles.DrawWireDisc(Vector3.zero, Vector3.forward, tier * ResearchTreeScene.RingStep);
            Handles.Label(Vector3.zero, "Datacenter");

            Handles.color = SectorColor;
            foreach (ResearchDefinition core in ResearchTreeDiagnosis.Cores)
            {
                Handles.DrawLine(Vector3.zero, Polar(outer, core.Angle - ResearchTreeDiagnosis.HalfSector));
                Handles.DrawLine(Vector3.zero, Polar(outer, core.Angle + ResearchTreeDiagnosis.HalfSector));
            }

            foreach (ResearchNodeHandle handle in ResearchTreeScene.Handles)
            {
                if (handle == null || handle.Research == null) continue;

                foreach (ResearchDefinition prerequisite in handle.Research.Prerequisites)
                {
                    ResearchNodeHandle from = prerequisite != null ? ResearchTreeScene.HandleOf(prerequisite) : null;
                    if (from == null) continue;

                    bool onCycle = ResearchTreeDiagnosis.InCycle.Contains(prerequisite) && ResearchTreeDiagnosis.InCycle.Contains(handle.Research);
                    Handles.color = onCycle ? CycleColor : LinkColor;
                    Arrow(from.transform.position, handle.transform.position, onCycle ? 3f : 1.5f);
                }
            }

            DrawSummary();
        }

        /// <summary>A line from the prerequisite to the research, with a head two thirds of the way along pointing at the research.</summary>
        static void Arrow(Vector3 from, Vector3 to, float thickness)
        {
            Handles.DrawLine(from, to, thickness);

            Vector3 direction = (to - from).normalized;
            Vector3 head = Vector3.Lerp(from, to, 0.66f);
            const float size = 0.25f;
            Handles.DrawLine(head, head - Quaternion.Euler(0f, 0f, 25f) * direction * size, thickness);
            Handles.DrawLine(head, head - Quaternion.Euler(0f, 0f, -25f) * direction * size, thickness);
        }

        static Vector3 Polar(float radius, float angleDegrees)
        {
            Vector2 offset = ResearchNetworkPlacement.Offset(1, angleDegrees, radius);
            return new Vector3(offset.x, offset.y, 0f);
        }

        static void DrawSummary()
        {
            var text = new StringBuilder();
            AppendLine(text, "Prerequisite cycle", ResearchTreeDiagnosis.InCycle);
            AppendLine(text, "Unreachable", ResearchTreeDiagnosis.Unreachable);
            AppendLine(text, "Outside its sector", ResearchTreeDiagnosis.OutOfSector);
            if (text.Length == 0) text.Append("Tree valid: no cycle, every research reachable, every node in its sector.");

            Handles.BeginGUI();
            GUIContent content = new GUIContent(text.ToString().TrimEnd());
            Vector2 size = EditorStyles.helpBox.CalcSize(content);
            GUI.Label(new Rect(10f, 10f, Mathf.Min(size.x + 8f, 560f), size.y + 6f), content, EditorStyles.helpBox);
            Handles.EndGUI();
        }

        static void AppendLine(StringBuilder text, string title, HashSet<ResearchDefinition> researches)
        {
            if (researches.Count == 0) return;

            var names = new List<string>();
            foreach (ResearchDefinition research in researches) names.Add(research.name);
            text.Append(title).Append(": ").Append(string.Join(", ", names)).Append('\n');
        }
    }
}
