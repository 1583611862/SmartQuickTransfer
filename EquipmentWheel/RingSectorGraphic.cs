using UnityEngine;
using UnityEngine.UI;

namespace SmartQuickTransfer.EquipmentWheel
{
    /// <summary>
    /// Draws ONE annular sector as a real mesh: inner arc + outer arc + two radial edges.
    /// Geometry comes from WheelGeometry, the same numbers the mouse selector uses, so the
    /// highlighted area is exactly the selectable area. This replaces the old approach of a
    /// solid circle with circular button sprites scattered around it.
    /// </summary>
    internal sealed class RingSectorGraphic : Graphic
    {
        private float startAngle;
        private float endAngle;
        private float innerRadius = WheelGeometry.InnerRadius;
        private float outerRadius = WheelGeometry.OuterRadius;
        private int arcSteps = 16;
        private Color baseColor = new Color(0.09f, 0.12f, 0.17f, 0.80f);
        private Color highlightColor = new Color(0.20f, 0.56f, 0.85f, 0.95f);
        private bool highlighted;
        private float highlightBlend;

        internal bool Highlighted
        {
            get { return highlighted; }
            set { highlighted = value; }
        }

        /// <summary>Smoothly move the highlight blend toward the target. dt is unscaled.</summary>
        internal void Tick(float dt)
        {
            float target = highlighted ? 1f : 0f;
            if (Mathf.Abs(highlightBlend - target) < 0.001f)
            {
                if (highlightBlend != target)
                {
                    highlightBlend = target;
                    ApplyColor();
                }
                return;
            }
            float speed = 12f;
            highlightBlend = Mathf.MoveTowards(highlightBlend, target, Mathf.Max(0.001f, speed * dt));
            ApplyColor();
        }

        internal void SnapHighlight()
        {
            highlightBlend = highlighted ? 1f : 0f;
            ApplyColor();
        }

        internal void Configure(int index, int count)
        {
            startAngle = WheelGeometry.StartAngle(index, count);
            endAngle = WheelGeometry.EndAngle(index, count);
            float span = Mathf.Max(0.01f, endAngle - startAngle);
            // More steps on wide sectors keeps both arcs visually smooth; bounded for performance.
            arcSteps = Mathf.Clamp(Mathf.CeilToInt(span / 0.09f), 6, 40);
            SetVerticesDirty();
            ApplyColor();
        }

        private void ApplyColor()
        {
            color = Color.Lerp(baseColor, highlightColor, highlightBlend);
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (endAngle <= startAngle || innerRadius <= 0f || outerRadius <= innerRadius)
            {
                return;
            }

            int steps = Mathf.Max(2, arcSteps);
            float span = endAngle - startAngle;
            Color32 vertexColor = color;

            // Two arcs of vertices: inner ring then outer ring. Triangulate between them so
            // every quad is a slice of the annulus, with the hollow centre left untouched.
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                float angle = startAngle + span * t;
                Vector2 inner = WheelGeometry.PointAt(innerRadius, angle);
                Vector2 outer = WheelGeometry.PointAt(outerRadius, angle);

                UIVertex vi = UIVertex.simpleVert;
                vi.color = vertexColor;
                vi.position = new Vector3(inner.x, inner.y, 0f);
                vi.uv0 = new Vector2(t, 0f);
                vh.AddVert(vi);

                UIVertex vo = UIVertex.simpleVert;
                vo.color = vertexColor;
                vo.position = new Vector3(outer.x, outer.y, 0f);
                vo.uv0 = new Vector2(t, 1f);
                vh.AddVert(vo);
            }

            for (int i = 0; i < steps; i++)
            {
                int innerStart = i * 2;
                int outerStart = innerStart + 1;
                int innerNext = innerStart + 2;
                int outerNext = innerStart + 3;
                vh.AddTriangle(innerStart, outerStart, outerNext);
                vh.AddTriangle(innerStart, outerNext, innerNext);
            }
        }
    }
}
