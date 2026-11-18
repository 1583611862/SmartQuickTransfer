using UnityEngine;

namespace SmartQuickTransfer.Visuals
{
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
    }
}
