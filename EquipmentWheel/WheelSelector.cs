using UnityEngine;

namespace SmartQuickTransfer.EquipmentWheel
{
    /// <summary>
    /// Picks a slot from the mouse direction, but ONLY when the pointer is inside the real
    /// annulus. The hollow centre and the area outside the outer circle select nothing,
    /// matching the drawn sectors exactly. Uses WheelGeometry, the same geometry as the mesh
    /// renderer, so visuals and selection can never disagree.
    /// </summary>
    internal sealed class WheelSelector
    {
        private const float HysteresisRadians = 0.05f; // ~3 degrees of border stability

        private int count;
        private int selected = -1;

        internal int Count { get { return count; } }
        internal int Selected { get { return selected; } }
        internal static float DeadZoneRadius { get { return WheelGeometry.InnerRadius; } }

        internal void SetCount(int value)
        {
            count = value < 0 ? 0 : value;
            if (count == 0)
            {
                selected = -1;
            }
            else if (selected < 0 || selected >= count)
            {
                selected = 0;
            }
        }

        internal void Reset()
        {
            selected = count > 0 ? 0 : -1;
        }

        /// <summary>Returns true only when the selected index actually changed.</summary>
        internal bool Update(Vector2 center, Vector2 mouse)
        {
            if (count <= 0)
            {
                if (selected != -1)
                {
                    selected = -1;
                    return true;
                }
                return false;
            }

            Vector2 local = new Vector2(mouse.x - center.x, mouse.y - center.y);

            // Hollow centre: select nothing (clear), so the element under the pointer is not
            // treated as an item. Outside the wheel: ignore, keeping the last choice stable.
            if (local.sqrMagnitude < WheelGeometry.InnerRadius * WheelGeometry.InnerRadius)
            {
                if (selected != -1)
                {
                    selected = -1;
                    return true;
                }
                return false;
            }
            if (local.sqrMagnitude > WheelGeometry.OuterRadius * WheelGeometry.OuterRadius)
            {
                return false;
            }

            float angle = WheelGeometry.AngleOf(local);
            int target = WheelGeometry.IndexAt(angle, count);
            if (target < 0)
            {
                return false;
            }

            if (target != selected && selected >= 0 && selected < count)
            {
                // Border hysteresis: only switch when the pointer is clearly closer to the
                // target sector centre, avoiding flicker on radial divider lines.
                float span = WheelGeometry.SectorSpan(count);
                float toCurrent = Mathf.Abs(AngularDelta(angle, WheelGeometry.CentreAngle(selected, count)));
                float toTarget = Mathf.Abs(AngularDelta(angle, WheelGeometry.CentreAngle(target, count)));
                if (toTarget + HysteresisRadians >= toCurrent)
                {
                    return false;
                }
            }

            int previous = selected;
            selected = target;
            return previous != selected;
        }

        /// <summary>Visual centre of an item, taken from the shared annular geometry.</summary>
        internal static Vector2 PositionFor(Vector2 center, int index, int count)
        {
            if (count <= 0)
            {
                return center;
            }
            Vector2 offset = WheelGeometry.PointAt(WheelGeometry.IconRadius, WheelGeometry.CentreAngle(index, count));
            return new Vector2(center.x + offset.x, center.y + offset.y);
        }

        private static float AngularDelta(float a, float b)
        {
            float d = a - b;
            while (d > Mathf.PI)
            {
                d -= 2f * Mathf.PI;
            }
            while (d < -Mathf.PI)
            {
                d += 2f * Mathf.PI;
            }
            return d;
        }
    }
}
