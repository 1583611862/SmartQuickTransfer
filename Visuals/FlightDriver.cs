using System;
using UnityEngine;

namespace SmartQuickTransfer.Visuals
{
    public sealed class FlightDriver : MonoBehaviour
    {
        private const float HighlightSeconds = 0.15f;

        private FlightCapture capture;
        private GameItem item;
        private SlotMarker slot;
        private RectTransform destination;
        private Camera destinationCamera;
        private RectTransform parent;
        private Camera parentCamera;
        private GameObject ghost;
        private GameObject trail;
        private GameObject hidden;
        private float duration = 0.24f;

        private bool started;
        private bool flying = true;
        private bool finished;
        private bool highlighted;
        private float elapsed;
        private float highlightElapsed;
        private Vector2 startLocal;

        internal void Setup(FlightCapture flightCapture, GameItem sourceItem, SlotMarker targetSlot,
            RectTransform destinationRect, Camera destinationCameraValue, RectTransform parentRect,
            Camera parentCameraValue, GameObject ghostObject, GameObject trailObject)
        {
            capture = flightCapture;
            item = sourceItem;
            slot = targetSlot;
            destination = destinationRect;
            destinationCamera = destinationCameraValue;
            parent = parentRect;
            parentCamera = parentCameraValue;
            ghost = ghostObject;
            trail = trailObject;
            duration = ModEntry.Settings == null ? 0.24f : ModEntry.Settings.AnimationDuration;
        }

        internal void SetHidden(GameObject hiddenObject)
        {
            hidden = hiddenObject;
        }

        private void Update()
        {
            if (finished)
            {
                return;
            }

            try
            {
                if (destination == null || parent == null || ghost == null)
                {
                    Finish();
                    return;
                }

                if (!started)
                {
                    started = true;
                    startLocal = ToLocal(parent, parentCamera, capture.StartWorldPosition, capture.SourceCamera);
                }

                RectTransform ghostRect = ghost.transform as RectTransform;
                RectTransform trailRect = trail == null ? null : trail.transform as RectTransform;
                Vector2 end = ToLocal(parent, parentCamera, destination.position, destinationCamera);

                if (flying)
                {
                    elapsed += Time.unscaledDeltaTime;
                    float t = Mathf.Clamp01(elapsed / duration);
                    float eased = Mathf.SmoothStep(0f, 1f, t);
                    Vector2 control = ControlPoint(startLocal, end);

                    Vector2 position = Bezier(startLocal, control, end, eased);
                    position.y += 9f * (1f - Mathf.Clamp01(t * 3f));

                    if (ghostRect != null)
                    {
                        ghostRect.anchoredPosition = position;
                        ghostRect.localScale = Vector3.one * ScaleAt(eased);
                    }

                    if (trailRect != null)
                    {
                        float trailEase = Mathf.Clamp01(eased - 0.07f);
                        trailRect.anchoredPosition = Bezier(startLocal, control, end, trailEase);
                        trailRect.localScale = Vector3.one * (ScaleAt(trailEase) * 0.94f);
                    }

                    if (t >= 1f)
                    {
                        OnArrived();
                    }
                }
                else
                {
                    highlightElapsed += Time.unscaledDeltaTime;
                    if (highlightElapsed >= HighlightSeconds)
                    {
                        ClearHighlight();
                        Finish();
                    }
                }
            }
            catch (Exception e)
            {
                ModEntry.LogError("flight animation aborted: " + e);
                Finish();
            }
        }

        private void OnArrived()
        {
            flying = false;
            highlightElapsed = 0f;

            if (ghost != null)
            {
                UnityEngine.Object.Destroy(ghost);
                ghost = null;
            }
            if (trail != null)
            {
                UnityEngine.Object.Destroy(trail);
                trail = null;
            }
            RestoreSource();
            PlayHighlight();
        }

        private void PlayHighlight()
        {
            if (slot == null || slot.inventory == null)
            {
                return;
            }

            try
            {
                slot.inventory.HighlightSlot(slot, RenderHandler.BackgroundColor.Green, true);
                highlighted = true;
            }
            catch (Exception e)
            {
                ModEntry.LogError("could not highlight the destination slot: " + e.Message);
            }
        }

        private void ClearHighlight()
        {
            if (!highlighted || slot == null || slot.inventory == null)
            {
                return;
            }

            try
            {
                slot.inventory.ClearHighlight();
            }
            catch (Exception)
            {
                // The window may have been closed while the flight was playing.
            }
            highlighted = false;
        }

        internal void RestoreSource()
        {
            if (hidden == null)
            {
                return;
            }

            try
            {
                hidden.SetActive(true);
            }
            catch (Exception e)
            {
                ModEntry.LogError("could not restore the original item visual: " + e.Message);
            }
            hidden = null;
        }

        internal void Finish()
        {
            if (finished)
            {
                return;
            }
            finished = true;

            if (ghost != null)
            {
                UnityEngine.Object.Destroy(ghost);
                ghost = null;
            }
            if (trail != null)
            {
                UnityEngine.Object.Destroy(trail);
                trail = null;
            }
            RestoreSource();
            ClearHighlight();

            UnityEngine.Object.Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (ghost != null)
            {
                UnityEngine.Object.Destroy(ghost);
            }
            if (trail != null)
            {
                UnityEngine.Object.Destroy(trail);
            }
            RestoreSource();
            ClearHighlight();
        }

        internal static Vector2 ToLocal(RectTransform targetParent, Camera targetParentCamera, Vector3 worldPosition, Camera worldCamera)
        {
            if (targetParent == null)
            {
                return Vector2.zero;
            }

            Vector2 screen = RectTransformUtility.WorldToScreenPoint(worldCamera, worldPosition);
            Vector2 local;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(targetParent, screen, targetParentCamera, out local))
            {
                return local;
            }
            return Vector2.zero;
        }

        internal static Vector2 ControlPoint(Vector2 start, Vector2 end)
        {
            Vector2 control = (start + end) * 0.5f;
            float distance = Vector2.Distance(start, end);
            control.y += Mathf.Clamp(distance * 0.16f, 16f, 80f);
            return control;
        }

        internal static Vector2 Bezier(Vector2 start, Vector2 control, Vector2 end, float t)
        {
            float inverse = 1f - t;
            return inverse * inverse * start + 2f * inverse * t * control + t * t * end;
        }

        internal static float ScaleAt(float eased)
        {
            if (eased < 0.3f)
            {
                return Mathf.Lerp(1f, 1.1f, eased / 0.3f);
            }
            if (eased < 0.85f)
            {
                return Mathf.Lerp(1.1f, 0.9f, (eased - 0.3f) / 0.55f);
            }
            return Mathf.Lerp(0.9f, 1f, (eased - 0.85f) / 0.15f);
        }
    }
}
