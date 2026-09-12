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
        static readonly Color RingColor = new Color(1f, 1f, 1f, 0.12f);
        static readonly Color SectorColor = new Color(0.33f, 0.87f, 0.96f, 0.35f);
        static readonly Color LinkColor = new Color(0.85f, 0.85f, 0.85f, 0.8f);
        static readonly Color CoreColor = new Color(0.33f, 0.87f, 0.96f);
        static readonly Color ResearchColor = new Color(0.8f, 0.8f, 0.8f);
        static readonly Color CycleColor = new Color(1f, 0.25f, 0.25f);
        static readonly Color UnreachableColor = new Color(1f, 0.6f, 0.15f);
        static readonly Color OutOfSectorColor = new Color(0.95f, 0.35f, 0.95f);

        static GUIStyle _labelStyle;

        /// <summary>
        /// Where the mouse last was, so the ball under it can name itself. A mutable static, which
        /// DEVELOPMENT_RULES §5 asks to be deliberate about: this is editor-only chrome, and the worst
        /// a value surviving a reload can do is name a ball one frame before the next mouse move.
        /// </summary>
        static Vector2 _mousePosition;

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
            float radius = ResearchTreeDisplay.RadiusOf(isCore);
            Vector3 position = handle.transform.position;

            Gizmos.color = ColourOf(research, isCore);
            Gizmos.DrawSphere(position, radius);
            if ((gizmoType & GizmoType.Selected) != 0)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawWireSphere(position, radius * 1.5f);
            }

            // No name here any more. A tree of thirty balls each carrying a permanent label is mostly
            // text, and the text is the part you do not need while placing: only the ball under the
            // cursor names itself now - see DrawIconsAndHoveredName.
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
            if (!ResearchTreeScene.IsOpen) return;

            // The scene only repaints when something asks it to, so a mouse crossing a ball would
            // otherwise change nothing until the next click or pan - the hover would look broken
            // rather than absent. Same request the Link tool makes while dragging a link.
            if (Event.current.type == EventType.MouseMove)
            {
                _mousePosition = Event.current.mousePosition;
                view.Repaint();
                return;
            }

            if (Event.current.type != EventType.Repaint) return;

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

            DrawIconsAndHoveredName();
            DrawSummary();
        }

        /// <summary>
        /// Each ball's icon, and the name of the one under the cursor.
        ///
        /// The icon is the research's own <see cref="ResearchDefinition.Icon"/> - the sprite chosen in
        /// its inspector, the same one the game's research menu has always had to show. Nothing new is
        /// stored: a research without one simply keeps a plain ball.
        ///
        /// Drawn here rather than in the gizmo pass because a sprite is a texture and gizmos draw
        /// geometry: this runs after them, so an icon sits on top of its ball instead of inside it.
        /// </summary>
        static void DrawIconsAndHoveredName()
        {
            ResearchNodeHandle hovered = null;
            float hoveredRadius = 0f;

            Handles.BeginGUI();
            foreach (ResearchNodeHandle handle in ResearchTreeScene.Handles)
            {
                if (handle == null || handle.Research == null) continue;

                Vector3 position = handle.transform.position;
                float radius = ResearchTreeDisplay.RadiusOf(ResearchTreeDiagnosis.Cores.Contains(handle.Research));

                Vector2 centre = HandleUtility.WorldToGUIPoint(position);
                float pixelsPerWorldUnit = Vector2.Distance(centre, HandleUtility.WorldToGUIPoint(position + Vector3.right));
                float pixelRadius = radius * pixelsPerWorldUnit;

                // The ball itself is the target: hovering means the cursor is on it, whatever size it
                // has been set to. A floor keeps a ball zoomed down to a few pixels still reachable.
                if (Vector2.Distance(centre, _mousePosition) <= Mathf.Max(pixelRadius, 6f))
                {
                    hovered = handle;
                    hoveredRadius = radius;
                }

                DrawIcon(handle.Research.Icon, centre, ResearchTreeDisplay.IconSideFor(radius) * pixelsPerWorldUnit);
            }
            Handles.EndGUI();

            if (hovered == null) return;

            _labelStyle ??= new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.UpperCenter, normal = { textColor = Color.white } };
            Handles.Label(hovered.transform.position + Vector3.down * (hoveredRadius + 0.15f),
                ResearchTreeScene.Label(hovered.Research), _labelStyle);
        }

        /// <summary>
        /// One sprite centred on a ball. Drawn through its own texture rectangle rather than as a
        /// whole texture: an icon packed in a sheet would otherwise show the whole sheet.
        /// </summary>
        static void DrawIcon(Sprite icon, Vector2 centre, float sidePixels)
        {
            if (icon == null || icon.texture == null || sidePixels < 1f) return;

            Rect textureRect = icon.textureRect;
            var uv = new Rect(
                textureRect.x / icon.texture.width,
                textureRect.y / icon.texture.height,
                textureRect.width / icon.texture.width,
                textureRect.height / icon.texture.height);

            GUI.DrawTextureWithTexCoords(
                new Rect(centre.x - sidePixels * 0.5f, centre.y - sidePixels * 0.5f, sidePixels, sidePixels),
                icon.texture, uv, alphaBlend: true);
        }

        /// <summary>
        /// The prerequisite's link, bent exactly as the game's research menu bends its synapse
        /// (ResearchPanelController.SynapseControl), with a head two thirds of the way along pointing at
        /// the research.
        /// </summary>
        static void Arrow(Vector3 from, Vector3 to, float thickness)
        {
            Vector3 control = SynapseControl(from, to);
            Handles.DrawBezier(from, to, from + (control - from) * (2f / 3f), to + (control - to) * (2f / 3f), Handles.color, null, thickness);

            const float t = 0.66f;
            Vector3 head = (1f - t) * (1f - t) * from + 2f * (1f - t) * t * control + t * t * to;
            Vector3 direction = (2f * (1f - t) * (control - from) + 2f * t * (to - control)).normalized;
            const float size = 0.25f;
            Handles.DrawLine(head, head - Quaternion.Euler(0f, 0f, 25f) * direction * size, thickness);
            Handles.DrawLine(head, head - Quaternion.Euler(0f, 0f, -25f) * direction * size, thickness);
        }

        /// <summary>The game's bend, computed in the panel's y-down space and brought back to the scene's y-up plane - so it bows to the same side here as in the game.</summary>
        static Vector3 SynapseControl(Vector3 from, Vector3 to)
        {
            Vector2 control = Game.UI.ResearchPanelController.SynapseControl(new Vector2(from.x, -from.y), new Vector2(to.x, -to.y));
            return new Vector3(control.x, -control.y, 0f);
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
            foreach (ResearchDefinition research in researches) names.Add(ResearchTreeScene.Label(research));
            text.Append(title).Append(": ").Append(string.Join(", ", names)).Append('\n');
        }
    }
}
