using UnityEngine;
using UnityEngine.UIElements;

namespace Game.UI
{
    /// <summary>
    /// A small clock face - the "waiting its turn" mark beside a construction site's service line.
    ///
    /// Drawn with Painter2D rather than imported, for the same reasons as HatchFillElement: the
    /// project has no clock in Assets/Art/Icons, and a 12-pixel glyph is cheaper to draw than to
    /// author, import and keep at the right size. A font glyph was the other option and was rejected
    /// - the project's font has no guarantee of carrying one, and a missing glyph renders as tofu
    /// rather than as nothing.
    /// </summary>
    public sealed class ClockGlyphElement : VisualElement
    {
        Color _color = Color.white;

        public ClockGlyphElement()
        {
            AddToClassList("clock-glyph");
            generateVisualContent += OnGenerateVisualContent;
        }

        public Color GlyphColor
        {
            get => _color;
            set
            {
                if (_color == value) return;
                _color = value;
                MarkDirtyRepaint();
            }
        }

        void OnGenerateVisualContent(MeshGenerationContext mgc)
        {
            float width = contentRect.width;
            float height = contentRect.height;
            if (width <= 0f || height <= 0f) return;

            var centre = new Vector2(width * 0.5f, height * 0.5f);

            // Inset by the stroke's own half-width so the face is not clipped by the element's edge.
            float radius = Mathf.Min(width, height) * 0.5f - 1f;
            if (radius <= 0f) return;

            Painter2D painter = mgc.painter2D;
            painter.strokeColor = _color;
            painter.lineWidth = 1f;

            painter.BeginPath();
            painter.Arc(centre, radius, new Angle(0f, AngleUnit.Degree), new Angle(360f, AngleUnit.Degree));
            painter.Stroke();

            // Hands at 12 and 4, which reads as a clock at this size where anything finer turns to
            // mush - the shape has to be recognisable at 12 pixels, not accurate.
            painter.BeginPath();
            painter.MoveTo(centre);
            painter.LineTo(new Vector2(centre.x, centre.y - radius * 0.6f));
            painter.MoveTo(centre);
            painter.LineTo(new Vector2(centre.x + radius * 0.45f, centre.y + radius * 0.3f));
            painter.Stroke();
        }
    }
}
