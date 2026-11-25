using UnityEngine;

namespace SmartQuickTransfer.EquipmentWheel
{
    /// <summary>
    /// The single source of truth for the wheel's annular geometry. Sector rendering, icon
    /// placement and mouse hit-testing all read these numbers, so the highlighted sector is
    /// exactly the region that selects the item.
    ///
    /// Angle convention: 0 radians is 12 o'clock, increasing clockwise. Local UI coordinates
    /// are derived as x = sin(a) * r, y = cos(a) * r (matching UnityEngine.UI's y-up rect space).
    /// </summary>
    internal static class WheelGeometry
    {
        internal const float OuterRadius = 220f;
        internal const float InnerRadius = 86f;

        // Content sits inside the ring, never on the circle buttons that the old UI used.
        internal const float IconRadius = 144f;
        internal const float LabelRadius = 188f;

        // A thin gap between sectors leaves a clean radial divider without overlapping geometry.
        internal const float GapDegrees = 1.6f;

        internal static float SectorSpan(int count)
        {
            return count > 0 ? 2f * Mathf.PI / count : 0f;
        }

        /// <summary>Centre angle of a sector (0 = up, clockwise).</summary>
        internal static float CentreAngle(int index, int count)
        {
            return SectorSpan(count) * index;
        }

        /// <summary>Start angle of the drawn sector, leaving half the divider gap.</summary>
        internal static float StartAngle(int index, int count)
        {
            float span = SectorSpan(count);
            return CentreAngle(index, count) - span * 0.5f + GapRadians() * 0.5f;
        }

        /// <summary>End angle of the drawn sector, leaving half the divider gap.</summary>
        internal static float EndAngle(int index, int count)
        {
            float span = SectorSpan(count);
            return CentreAngle(index, count) + span * 0.5f - GapRadians() * 0.5f;
        }

        internal static float GapRadians()
        {
            return GapDegrees * Mathf.Deg2Rad;
        }

        /// <summary>Convert an angle (0 = up, clockwise) into a point at the given radius.</summary>
        internal static Vector2 PointAt(float radius, float angle)
        {
            return new Vector2(Mathf.Sin(angle) * radius, Mathf.Cos(angle) * radius);
        }

        /// <summary>Angle of a local vector, 0 = up, clockwise, in [0, 2*pi).</summary>
        internal static float AngleOf(Vector2 local)
        {
            float angle = Mathf.Atan2(local.x, local.y);
            if (angle < 0f)
            {
                angle += 2f * Mathf.PI;
            }
            return angle;
        }

        /// <summary>True only inside the selectable annulus; excludes the hollow centre and outside.</summary>
        internal static bool IsInRing(Vector2 local)
        {
            float radiusSq = local.x * local.x + local.y * local.y;
            float inner = InnerRadius * InnerRadius;
            float outer = OuterRadius * OuterRadius;
            return radiusSq >= inner && radiusSq <= outer;
        }

        /// <summary>Sector that contains an angle. Uses the exact same boundaries as drawing.</summary>
        internal static int IndexAt(float angle, int count)
        {
            if (count <= 0)
            {
                return -1;
            }
            float span = SectorSpan(count);
            int index = Mathf.FloorToInt((angle + span * 0.5f) / span);
            index %= count;
            if (index < 0)
            {
                index += count;
            }
            return index;
        }

        /// <summary>Icon size shrinks as sectors get narrower so content never leaves its sector.</summary>
        internal static float IconSize(int count)
        {
            float arc = SectorSpan(count) * IconRadius;
            float size = arc * 0.58f;
            if (size < 36f)
            {
                size = 36f;
            }
            if (size > 72f)
            {
                size = 72f;
            }
            return size;
        }
    }
}
