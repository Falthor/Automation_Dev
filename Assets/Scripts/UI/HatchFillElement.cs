using UnityEngine;
using UnityEngine.UIElements;

namespace Game.UI
{
    /// <summary>
    /// A block filled with 45° diagonal stripes - CSS's
    /// <c>repeating-linear-gradient(135deg, ...)</c>, which USS has no equivalent for.
    ///
    /// Drawn with Painter2D rather than tiling a hatch texture as a repeated background: the project
    /// already draws this way (HistoryGraphElement), so it needs no new art asset, no import
    /// settings and no texture lifetime, and the stripe angle stays exact at any element size
    /// instead of being whatever a bitmap was authored at.
    ///
    /// The point of the hatch is that it distinguishes a state by <b>texture</b> rather than by hue,
    /// which survives both a palette where the accent colour already means something else and a
    /// player who cannot separate the two hues at all.
    /// </summary>
    public sealed class HatchFillElement : VisualElement
    {
        /// <summary>Distance between stripes, measured along the horizontal axis. Tuned by eye against a 7px-tall bar.</summary>
        const float StripeSpacing = 4f;

        const float StripeWidth = 1.5f;

        Color _stripeColor = Color.white;

        public HatchFillElement()
        {
            AddToClassList("hatch-fill");
            generateVisualContent += OnGenerateVisualContent;
        }

        public Color StripeColor
        {
            get => _stripeColor;
            set
            {
                if (_stripeColor == value) return;
                _stripeColor = value;
                MarkDirtyRepaint();
            }
        }

        void OnGenerateVisualContent(MeshGenerationContext mgc)
        {
            float width = contentRect.width;
            float height = contentRect.height;
            if (width <= 0f || height <= 0f) return;

            Painter2D painter = mgc.painter2D;
            painter.strokeColor = _stripeColor;
            painter.lineWidth = StripeWidth;
            painter.BeginPath();

            // Every stripe lies on x + y = k, which runs bottom-left to top-right. Sweeping k from 0
            // to width + height covers the rect exactly once, whatever its aspect.
            //
            // Each segment is clipped to the rect analytically rather than drawn long and left to
            // the clip rectangle: a stripe overhanging its own element would bleed into the
            // neighbouring segment of the same bar, which is precisely the boundary this element
            // exists to make visible.
            for (float k = 0f; k <= width + height; k += StripeSpacing)
            {
                float x0 = Mathf.Max(0f, k - height);
                float x1 = Mathf.Min(width, k);
                if (x1 <= x0) continue;

                painter.MoveTo(new Vector2(x0, k - x0));
                painter.LineTo(new Vector2(x1, k - x1));
            }

            painter.Stroke();
        }
    }
}
