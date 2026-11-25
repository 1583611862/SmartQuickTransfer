using UnityEngine;

namespace SmartQuickTransfer.EquipmentWheel
{
    /// <summary>
    /// Pure angular selector: the chosen slot is derived from the mouse direction, never from UI
    /// hierarchy or raycast order. Slot 0 sits at 12 o'clock and slots advance clockwise.
    /// </summary>
    internal sealed class WheelSelector
    {
        internal const float DeadZonePixels = 48f;
        private const float HysteresisRadians = 0.06f; // ~3.4 degrees of border stability

        private int count;
        private int selected = -1;

        internal int Count { get { return count; } }
        internal int Selected { get { return selected; } }

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
                selected = -1;
                return false;
            }
            if (count == 1)
            {
                bool changed = selected != 0;
                selected = 0;
                return changed;
            }

            float dx = mouse.x - center.x;
            float dy = mouse.y - center.y;
            // Inside the dead zone keep the previous choice instead of jumping to a random slot.
            if (dx * dx + dy * dy < DeadZonePixels * DeadZonePixels)
            {
                return false;
            }

            float sector = 2f * Mathf.PI / count;
            float fromTop = Mathf.Atan2(dx, dy); // 0 = up, +PI/2 = right
            if (fromTop < 0f)
            {
                fromTop += 2f * Mathf.PI;
            }
            int target = Mathf.FloorToInt((fromTop + sector * 0.5f) / sector) % count;

            if (target != selected && selected >= 0 && selected < count)
            {
                // Border hysteresis: only switch when the mouse is clearly closer to the
                // target slot centre than to the current one. This stops adjacent sectors
                // from flickering when the pointer sits on their boundary.
                float currentCentre = selected * sector;
                float targetCentre = target * sector;
                float toCurrent = Mathf.Abs(AngularDelta(fromTop, currentCentre));
                float toTarget = Mathf.Abs(AngularDelta(fromTop, targetCentre));
                if (toTarget + HysteresisRadians >= toCurrent)
                {
                    return false;
                }
            }

            int previous = selected;
            selected = target;
            return previous != selected;
        }

        /// <summary>Centre of a slot icon in screen pixels.</summary>
        internal static Vector2 PositionFor(Vector2 center, float radius, int index, int count)
        {
            if (count <= 0)
            {
                return center;
            }
            float angle = index * 2f * Mathf.PI / count;
            return new Vector2(center.x + Mathf.Sin(angle) * radius,
                center.y + Mathf.Cos(angle) * radius);
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
