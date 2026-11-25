using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace SmartQuickTransfer.Patches
{
    /// <summary>
    /// The game's band-selection handler clears the selection on the first press of a click
    /// (it treats that press as the start of a group drag). As a result the native double-click
    /// handler never sees a second press and a "double-click to move the selection" never fires.
    ///
    /// These patches keep the selection alive across a click that did not turn into a drag, and
    /// route the second click of a genuine double-click into the shared batch-transfer path.
    /// Group dragging and all other selection behaviour are left to the base game.
    /// </summary>
    internal static class ItemMultiSelectState
    {
        private const int Selected = 3;
        private const int PendingGroupDrag = 4;

        internal static readonly FieldInfo StateField = Resolve("state");
        private static readonly FieldInfo SelectedItemsField = Resolve("selectedItems");

        private static FieldInfo Resolve(string name)
        {
            try
            {
                return typeof(ItemMultiSelectHandler).GetField(name,
                    BindingFlags.Instance | BindingFlags.NonPublic);
            }
            catch (Exception e)
            {
                ModEntry.LogError("could not resolve band-selection field '" + name + "': " + e.Message);
                return null;
            }
        }

        internal static int GetState(ItemMultiSelectHandler handler)
        {
            if (handler == null || StateField == null)
            {
                return -1;
            }
            try
            {
                return Convert.ToInt32(StateField.GetValue(handler));
            }
            catch (Exception)
            {
                return -1;
            }
        }

        internal static void SetState(ItemMultiSelectHandler handler, int value)
        {
            if (handler == null || StateField == null)
            {
                return;
            }
            try
            {
                StateField.SetValue(handler, Enum.ToObject(StateField.FieldType, value));
            }
            catch (Exception e)
            {
                ModEntry.LogError("could not restore the band-selection state: " + e.Message);
            }
        }

        internal static bool RestoreSelected(ItemMultiSelectHandler handler)
        {
            // PendingGroupDrag means the button went down but never moved far enough to start a
            // drag. Put the state back to Selected instead of clearing the highlights.
            if (GetState(handler) != PendingGroupDrag)
            {
                return false;
            }
            SetState(handler, Selected);
            return true;
        }

        internal static bool IsSelected(ItemMultiSelectHandler handler, GameItemElement element)
        {
            if (element == null || SelectedItemsField == null)
            {
                return false;
            }
            try
            {
                IEnumerable entries = SelectedItemsField.GetValue(handler) as IEnumerable;
                if (entries == null)
                {
                    return false;
                }
                foreach (object entry in entries)
                {
                    if (ReferenceEquals(entry, element))
                    {
                        return true;
                    }
                }
            }
            catch (Exception e)
            {
                ModEntry.LogError("could not read the band selection: " + e.Message);
            }
            return false;
        }

        internal static bool ModifierHeld()
        {
            return Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)
                || Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)
                || Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
        }

        internal static float DoubleClickThreshold
        {
            get
            {
                ItemMouseDoubleClickHandler native = ItemMouseDoubleClickHandler.current;
                float value = native == null ? 0f : native.doubleclickTimeThresholdSeconds;
                return value > 0f ? value : 0.3f;
            }
        }
    }

    [HarmonyPatch(typeof(ItemMultiSelectHandler), "OnEventPress")]
    internal static class ItemMultiSelectPressPatch
    {
        private static GameItemElement lastElement;
        private static float lastTime = -1000f;

        private static bool Prefix(ISet<KeyCode> keysPressed)
        {
            try
            {
                if (Time.frameCount == ItemDoubleClickPatch.LastBatchFrame)
                {
                    return false;
                }
                ItemMultiSelectHandler handler = ItemMultiSelectHandler.current;
                if (handler == null || ItemMultiSelectState.StateField == null
                    || keysPressed == null || !keysPressed.Contains(handler.key)
                    || ItemMultiSelectState.ModifierHeld()
                    || ItemMultiSelectState.GetState(handler) != 3)
                {
                    return true;
                }

                GameItemElement element = RenderHandler.RaycastElement<GameItemElement>(Input.mousePosition);
                if (!ItemMultiSelectState.IsSelected(handler, element))
                {
                    return true;
                }

                bool secondPress = ReferenceEquals(element, lastElement)
                    && Time.realtimeSinceStartup - lastTime <= ItemMultiSelectState.DoubleClickThreshold;
                lastElement = element;
                lastTime = Time.realtimeSinceStartup;
                if (!secondPress)
                {
                    // Let the game start a potential group drag; the release patch keeps the
                    // selection if no drag actually happened.
                    return true;
                }

                lastElement = null;
                lastTime = -1000f;
                PixelWindow sourceWindow;
                if (ItemDoubleClickPatch.TryBatchTransfer(element, out sourceWindow))
                {
                    if (ItemMouseDoubleClickHandler.current != null)
                    {
                        ItemMouseDoubleClickHandler.current.ConsumePress();
                    }
                    return false;
                }
                // Nothing could be moved as a batch; fall back to the base interaction.
            }
            catch (Exception e)
            {
                ModEntry.LogError("band-selection double-click failed: " + e);
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(ItemMultiSelectHandler), "OnEventRelease")]
    internal static class ItemMultiSelectReleasePatch
    {
        private static bool Prefix(ISet<KeyCode> keysReleased)
        {
            try
            {
                if (Time.frameCount == ItemDoubleClickPatch.LastBatchFrame)
                {
                    return false;
                }
                ItemMultiSelectHandler handler = ItemMultiSelectHandler.current;
                if (handler == null || keysReleased == null || !keysReleased.Contains(handler.key))
                {
                    return true;
                }
                if (ItemMultiSelectState.RestoreSelected(handler))
                {
                    return false;
                }
            }
            catch (Exception e)
            {
                ModEntry.LogError("band-selection release failed: " + e);
            }
            return true;
        }
    }
}
