using System.Collections.Generic;
using UnityEngine;

namespace SmartQuickTransfer.Visuals
{
    /// <summary>
    /// A snapshot of one content overlay drawn on top of an item icon. The game renders
    /// "content-visible" items (a bucket holding water, a container showing its payload, ...)
    /// as extra sibling Image layers under the icon's rotator. The base sprite on its own is
    /// just the empty container, so the flight ghost has to replay these overlays too.
    /// </summary>
    internal sealed class FlightOverlayLayer
    {
        internal Sprite Sprite;
        internal Color Color = Color.white;
        internal Vector2 ScreenOffset;   // layer centre relative to the icon centre, in screen pixels
        internal Vector2 ScreenSize;     // unrotated size in screen pixels
        internal float RotationZ;        // world Z rotation in degrees
        internal float ScaleX = 1f;      // horizontal flip sign
        internal float ScaleY = 1f;      // vertical scale sign
    }

    internal sealed class FlightCapture
    {
        internal bool Valid;
        internal Sprite Sprite;
        internal Color Color = Color.white;
        internal RectTransform Source;
        internal Vector3 StartWorldPosition;
        internal Camera SourceCamera;
        internal GameInventory SourceInventory;
        internal Vector2 ScreenSize;

        // Content overlays (liquid, visible payload) captured at the source icon.
        internal readonly List<FlightOverlayLayer> Overlays = new List<FlightOverlayLayer>();
    }
}
