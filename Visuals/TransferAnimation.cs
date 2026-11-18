using System;
using UnityEngine;
using UnityEngine.UI;
using SmartQuickTransfer.Core;

namespace SmartQuickTransfer.Visuals
{
    internal static class TransferAnimation
    {
        private const float SafetyLifetime = 3f;
        private const float MinimumTravelPixels = 6f;

        internal static FlightCapture Capture(GameItem item)
        {
            FlightCapture capture = new FlightCapture();
            if (item == null)
            {
                return capture;
            }

            try
            {
                capture.SourceInventory = item.parentInventory;

                PixelElement element = item as PixelElement;
                if (element == null)
                {
                    return capture;
                }

                // The icon lives on GameItemElement.background; the handler node itself carries no
                // sprite (its image is a disabled raycast target), so reading handler.image would
                // always come back empty.
                RectTransform rect = null;
                Image image = null;
                GameItemElement itemElement = item as GameItemElement;
                if (itemElement != null && itemElement.background != null)
                {
                    image = itemElement.background.image;
                    rect = itemElement.background.rectTransform;
                }
                else if (element.handler != null)
                {
                    image = element.handler.image;
                    rect = element.handler.rectTransform;
                }

                if (rect == null)
                {
                    return capture;
                }

                Sprite sprite = image == null ? null : image.sprite;
                if (sprite == null && itemElement != null && !string.IsNullOrEmpty(item.spritePath))
                {
                    sprite = itemElement.ResolveSpriteByName(item.spritePath);
                }
                if (sprite == null)
                {
                    return capture;
                }

                Vector3[] corners = new Vector3[4];
                rect.GetWorldCorners(corners);
                Camera camera = CameraFor(rect);
                Vector2 min = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
                Vector2 max = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);

                capture.Sprite = sprite;
                capture.Color = image == null ? Color.white : image.color;
                capture.Source = rect;
                capture.StartWorldPosition = rect.position;
                capture.SourceCamera = camera;
                capture.ScreenSize = new Vector2(Mathf.Abs(max.x - min.x), Mathf.Abs(max.y - min.y));
                capture.Valid = capture.ScreenSize.x >= 1f && capture.ScreenSize.y >= 1f;
            }
            catch (Exception e)
            {
                ModEntry.LogError("could not capture the item visual for the flight animation: " + e.Message);
                capture.Valid = false;
            }

            return capture;
        }

        internal static void Play(FlightCapture capture, GameItem item, SlotMarker slot, bool hideSource)
        {
            if (capture == null || !capture.Valid || capture.Source == null || capture.Sprite == null)
            {
                ModEntry.LogInfo("animation skipped: the item icon could not be captured.");
                return;
            }
            if (ModEntry.Settings == null || !ModEntry.Settings.Animate)
            {
                return;
            }

            GameObject trail = null;
            GameObject ghost = null;
            FlightDriver driver = null;
            try
            {
                RenderHandler renderer = RenderHandler.current;
                Canvas canvas = renderer == null ? null : renderer.windowUICanvas;
                if (canvas == null)
                {
                    ModEntry.LogInfo("animation skipped: the window canvas is not available.");
                    return;
                }

                RectTransform parent = canvas.transform as RectTransform;
                if (parent == null)
                {
                    return;
                }

                RectTransform destination = ResolveDestination(capture, item, slot);
                if (destination == null)
                {
                    ModEntry.LogInfo("animation skipped: the destination slot has no visual element.");
                    return;
                }

                // The item icon is the same object before and after a whole-stack move - it is only
                // re-parented - so identity cannot tell us whether anything moved. Measure the
                // on-screen distance instead.
                Camera destinationCamera = CameraFor(destination);
                Camera parentCamera = CameraFor(parent);
                Vector2 startScreen = RectTransformUtility.WorldToScreenPoint(capture.SourceCamera, capture.StartWorldPosition);
                Vector2 endScreen = RectTransformUtility.WorldToScreenPoint(destinationCamera, destination.position);
                if (Vector2.Distance(startScreen, endScreen) < MinimumTravelPixels)
                {
                    return;
                }

                float scaleFactor = Mathf.Max(0.0001f, canvas.scaleFactor);
                Vector2 size = capture.ScreenSize / scaleFactor;
                if (size.x < 1f || size.y < 1f)
                {
                    return;
                }

                trail = CreateGhost(parent, "SQT_FlightTrail", capture, size, 0.18f);
                ghost = CreateGhost(parent, "SQT_FlightGhost", capture, size, 1f);
                if (ghost == null)
                {
                    return;
                }
                ghost.transform.SetAsLastSibling();

                ModEntry.LogInfo("flight animation started (" + Mathf.RoundToInt(Vector2.Distance(startScreen, endScreen))
                    + " px, icon " + Mathf.RoundToInt(size.x) + "x" + Mathf.RoundToInt(size.y) + ").");

                GameObject host = new GameObject("SQT_FlightDriver");
                driver = host.AddComponent<FlightDriver>();
                driver.Setup(capture, item, slot, destination, destinationCamera, parent, parentCamera, ghost, trail);

                if (hideSource)
                {
                    GameObject hidden = VisualObjectOf(item);
                    if (hidden != null)
                    {
                        hidden.SetActive(false);
                        driver.SetHidden(hidden);
                    }
                }
            }
            catch (Exception e)
            {
                if (driver != null)
                {
                    driver.Finish();
                }
                else
                {
                    if (ghost != null)
                    {
                        UnityEngine.Object.Destroy(ghost);
                    }
                    if (trail != null)
                    {
                        UnityEngine.Object.Destroy(trail);
                    }
                }
                ModEntry.LogError("could not start the flight animation: " + e.Message);
            }
        }

        private static GameObject CreateGhost(RectTransform parent, string name, FlightCapture capture, Vector2 size, float alpha)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = Vector2.zero;
            rect.localScale = Vector3.one;

            Image image = go.GetComponent<Image>();
            image.sprite = capture.Sprite;
            image.color = new Color(capture.Color.r, capture.Color.g, capture.Color.b, alpha);
            image.raycastTarget = false;
            image.preserveAspect = false;

            CanvasGroup group = go.GetComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;

            UnityEngine.Object.Destroy(go, SafetyLifetime);
            return go;
        }

        private static RectTransform ResolveDestination(FlightCapture capture, GameItem item, SlotMarker slot)
        {
            try
            {
                if (item != null && item.parentInventory != null
                    && !ReferenceEquals(item.parentInventory, capture.SourceInventory))
                {
                    RectTransform moved = IconRect(item);
                    if (moved != null)
                    {
                        return moved;
                    }
                }

                if (slot != null)
                {
                    if (slot.targetItem != null)
                    {
                        RectTransform stacked = IconRect(slot.targetItem);
                        if (stacked != null)
                        {
                            return stacked;
                        }
                    }

                    PixelElement storage = slot.inventory as PixelElement;
                    if (storage != null && storage.handler != null)
                    {
                        return storage.handler.rectTransform;
                    }
                }
            }
            catch (Exception e)
            {
                ModEntry.LogError("could not resolve the destination of the flight animation: " + e.Message);
            }

            return null;
        }

        private static RectTransform IconRect(GameItem item)
        {
            if (item == null)
            {
                return null;
            }

            GameItemElement element = item as GameItemElement;
            if (element != null && element.background != null)
            {
                return element.background.rectTransform;
            }

            PixelElement pixelElement = item as PixelElement;
            if (pixelElement != null && pixelElement.handler != null)
            {
                return pixelElement.handler.rectTransform;
            }

            return null;
        }

        private static GameObject VisualObjectOf(GameItem item)
        {
            PixelElement element = item as PixelElement;
            if (element == null || element.handler == null)
            {
                return null;
            }
            return element.handler.gameObject;
        }

        private static Camera CameraFor(Component component)
        {
            if (component == null)
            {
                return null;
            }

            Canvas canvas = component.GetComponentInParent<Canvas>(true);
            if (canvas == null)
            {
                return null;
            }

            Canvas root = canvas.rootCanvas;
            if (root == null || root.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                return null;
            }
            return root.worldCamera;
        }
    }
}
