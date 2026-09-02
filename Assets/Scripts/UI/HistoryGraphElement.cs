using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.UI
{
    /// <summary>
    /// Reusable two-series line graph (e.g. Power's supply/demand or Compute's
    /// available/requested) - a pure view, samples pushed in from outside on a timer (see
    /// PowerPanelController/ComputePanelController). Not persisted: history lives only in memory
    /// for as long as this element exists, cleared on scene reload - no save system involved.
    /// Direct translation of the source project's history_graph.gd, using UI Toolkit's
    /// Painter2D (generateVisualContent) instead of Godot's Control._draw().
    /// </summary>
    public sealed class HistoryGraphElement : VisualElement
    {
        public const int MaxSamples = 60; // 60 samples * 5s sample interval = 5 minutes.

        readonly Color _colorA;
        readonly Color _colorB;
        readonly List<float> _seriesA = new List<float>();
        readonly List<float> _seriesB = new List<float>();

        public HistoryGraphElement(Color colorA, Color colorB)
        {
            _colorA = colorA;
            _colorB = colorB;
            AddToClassList("history-graph");
            generateVisualContent += OnGenerateVisualContent;
        }

        public void AddSample(float a, float b)
        {
            _seriesA.Add(a);
            _seriesB.Add(b);
            if (_seriesA.Count > MaxSamples)
            {
                _seriesA.RemoveAt(0);
                _seriesB.RemoveAt(0);
            }
            MarkDirtyRepaint();
        }

        void OnGenerateVisualContent(MeshGenerationContext mgc)
        {
            if (_seriesA.Count < 2) return;

            float width = contentRect.width;
            float height = contentRect.height;
            if (width <= 0f || height <= 0f) return;

            float maxVal = 1f;
            foreach (float v in _seriesA) maxVal = Mathf.Max(maxVal, v);
            foreach (float v in _seriesB) maxVal = Mathf.Max(maxVal, v);

            DrawSeries(mgc, _seriesA, maxVal, width, height, _colorA);
            DrawSeries(mgc, _seriesB, maxVal, width, height, _colorB);
        }

        static void DrawSeries(MeshGenerationContext mgc, List<float> series, float maxVal, float width, float height, Color color)
        {
            float step = width / (MaxSamples - 1);
            int offset = MaxSamples - series.Count;

            Painter2D painter = mgc.painter2D;
            painter.strokeColor = color;
            painter.lineWidth = 2f;
            painter.BeginPath();

            for (int i = 0; i < series.Count; i++)
            {
                float x = (offset + i) * step;
                float y = height - (series[i] / maxVal) * height;
                if (i == 0) painter.MoveTo(new Vector2(x, y));
                else painter.LineTo(new Vector2(x, y));
            }

            painter.Stroke();
        }
    }
}
