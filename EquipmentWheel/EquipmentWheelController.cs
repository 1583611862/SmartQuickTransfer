using System;
using System.Collections.Generic;
using UnityEngine;
using SmartQuickTransfer.Core;

namespace SmartQuickTransfer.EquipmentWheel
{
    /// <summary>
    /// Owns the wheel's single instance and state machine: Closed -&gt; Opening -&gt; Open -&gt;
    /// Executing -&gt; Closing. All input is read here through Unity Input rather than through
    /// InputActionHandler, because the game's handler chain is suppressed while the wheel is up.
    /// </summary>
    public sealed class EquipmentWheelController : MonoBehaviour
    {
        private enum WheelState { Closed, Opening, Open, Executing, Closing }

        internal static EquipmentWheelController Instance { get; private set; }

        private readonly WheelView view = new WheelView();
        private readonly WheelSelector selector = new WheelSelector();
        private readonly List<GameItem> cached = new List<GameItem>();
        private readonly List<GameItem> pageItems = new List<GameItem>();

        private WheelState state = WheelState.Closed;
        private Vector2 centre;
        private int page;
        private int pageSize = 8;
        private int pageCount = 1;
        private float animationElapsed;
        private float animationDuration = 0.14f;
        private bool useAnimation = true;
        private bool includeNested = true;
        private bool showItemName = true;
        private KeyCode openKey = KeyCode.Mouse2;

        internal static void Initialize()
        {
            if (Instance != null)
            {
                return;
            }
            try
            {
                GameObject host = new GameObject("SQT_EquipmentWheel");
                UnityEngine.Object.DontDestroyOnLoad(host);
                Instance = host.AddComponent<EquipmentWheelController>();
                Instance.Configure();
            }
            catch (Exception e)
            {
                ModEntry.LogError("equipment wheel: could not initialise: " + e.Message);
                Instance = null;
                WheelInputState.ForceClear();
            }
        }

        internal static void Shutdown()
        {
            WheelInputState.ForceClear();
            if (Instance == null)
            {
                return;
            }
            try
            {
                Instance.ForceClose();
                UnityEngine.Object.Destroy(Instance.gameObject);
            }
            catch (Exception e)
            {
                ModEntry.LogError("equipment wheel: could not shut down: " + e.Message);
            }
            Instance = null;
        }

        private void Configure()
        {
            TransferSettings settings = ModEntry.Settings;
            if (settings == null)
            {
                return;
            }
            openKey = settings.WheelKey;
            includeNested = settings.WheelIncludeNested;
            showItemName = settings.WheelShowName;
            useAnimation = settings.WheelAnimation;
            pageSize = Mathf.Clamp(settings.WheelPageSize, 1, 8);
        }

        private void Update()
        {
            if (state == WheelState.Closed)
            {
                PollOpen();
                return;
            }

            if (view.Ready == false)
            {
                ForceClose();
                return;
            }

            // centre stays locked to the pointer position captured on open. Selection and the
            // drawn wheel share it; moving the mouse only changes which sector is selected.

            if (Pressed(KeyCode.Escape) || Pressed(KeyCode.Mouse1) || Pressed(openKey))
            {
                Close();
                return;
            }

            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) > 0.01f && state != WheelState.Executing && pageCount > 1)
            {
                ChangePage(scroll > 0f ? -1 : 1);
            }

            if (selector.Update(centre, Input.mousePosition))
            {
                view.UpdateSelection(selector.Selected, pageItems);
            }
            // Advance the annular sector highlight transition every frame (unscaled).
            view.Tick(Time.unscaledDeltaTime);

            if (Pressed(KeyCode.Mouse0) && state != WheelState.Executing)
            {
                Confirm();
            }

            TickAnimation();
        }

        private void PollOpen()
        {
            TransferSettings settings = ModEntry.Settings;
            if (settings == null || !settings.Enabled || !settings.WheelEnabled)
            {
                return;
            }
            if (!Pressed(openKey) || !MayOpen())
            {
                return;
            }
            Open();
        }

        private bool MayOpen()
        {
            // Middle click belongs to the base game while the player is dragging an item
            // (flip_item_alt). Let that interaction win and leave the wheel closed.
            if (ItemMouseDragHandler.current != null && ItemMouseDragHandler.current.IsDraggingItem)
            {
                return false;
            }
            if (ItemMultiSelectHandler.current != null && ItemMultiSelectHandler.current.HasSelection)
            {
                return false;
            }
            if (ItemPinHelper.BlocksClicks)
            {
                return false;
            }
            if (DialogUIManager.Instance != null && DialogUIManager.Instance.IsChoice())
            {
                return false;
            }
            if (StoreUIManager.IsAnyTyping())
            {
                return false;
            }
            try
            {
                EscapeUIManager escape = UnityEngine.Object.FindObjectOfType<EscapeUIManager>();
                if (escape != null && escape.menuPanel != null && escape.menuPanel.activeSelf)
                {
                    return false;
                }
            }
            catch (Exception)
            {
                // An unusual scene layout must not break the hotkey.
            }
            return true;
        }

        private void Open()
        {
            try
            {
                state = WheelState.Opening;
                WheelInputState.SetOpen(true);
                try
                {
                    if (InputActionManager.current != null)
                    {
                        InputActionManager.current.Reset();
                    }
                }
                catch (Exception)
                {
                    // Best-effort: never fail opening because of a handler reset.
                }

                cached.Clear();
                cached.AddRange(WheelScanner.Scan(includeNested));
                page = 0;
                selector.Reset();
                RefreshPage();

                // Lock the wheel centre to the pointer at the moment of opening. It must NOT
                // follow the pointer afterwards, otherwise the mouse stays inside the hollow
                // dead zone and can never select a sector.
                centre = WheelView.ClampCentre(Input.mousePosition);
                view.SetCentre(centre);
                view.Show();
                animationElapsed = 0f;
                if (!useAnimation)
                {
                    view.SetAlpha(1f);
                    view.SetScale(1f);
                    state = WheelState.Open;
                }
                ModEntry.LogInfo("equipment wheel opened with " + cached.Count + " item(s).");
            }
            catch (Exception e)
            {
                ModEntry.LogError("equipment wheel: could not open: " + e.Message);
                ForceClose();
            }
        }

        private void Confirm()
        {
            if (pageItems.Count == 0 || selector.Selected < 0 || selector.Selected >= pageItems.Count)
            {
                return;
            }
            GameItem item = pageItems[selector.Selected];
            state = WheelState.Executing;
            bool equipped = WheelExecutor.TryEquip(item);
            if (equipped)
            {
                ModEntry.LogInfo("equipment wheel: equipped " + item.GetDisplayName(true) + ".");
                Close();
            }
            else
            {
                // Keep the wheel open, the selection intact and the current equipment untouched.
                state = WheelState.Open;
                if (view.Ready)
                {
                    view.SetAlpha(1f);
                    view.SetScale(1f);
                    view.Flash("装备失败：物品已不可用");
                }
                ModEntry.LogInfo("equipment wheel: equip failed, selection kept.");
            }
        }

        private void ChangePage(int delta)
        {
            page += delta;
            while (page < 0)
            {
                page += pageCount;
            }
            while (page >= pageCount)
            {
                page -= pageCount;
            }
            RefreshPage();
        }

        private void RefreshPage()
        {
            pageSize = Mathf.Clamp(pageSize, 1, 8);
            pageCount = cached.Count == 0 ? 1 : (cached.Count + pageSize - 1) / pageSize;
            if (page >= pageCount)
            {
                page = 0;
            }
            int start = page * pageSize;
            int end = Mathf.Min(start + pageSize, cached.Count);
            pageItems.Clear();
            for (int i = start; i < end; i++)
            {
                pageItems.Add(cached[i]);
            }
            selector.SetCount(pageItems.Count);
            view.SetPage(pageItems, page, pageCount, showItemName);
            view.UpdateSelection(selector.Selected, pageItems);
        }

        private void TickAnimation()
        {
            if (!useAnimation || state == WheelState.Executing)
            {
                return;
            }
            animationElapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(animationElapsed / Mathf.Max(0.01f, animationDuration));
            if (state == WheelState.Opening)
            {
                float eased = Mathf.SmoothStep(0f, 1f, t);
                view.SetScale(0.55f + 0.45f * eased);
                view.SetAlpha(eased);
                if (t >= 1f)
                {
                    state = WheelState.Open;
                }
            }
            else if (state == WheelState.Closing)
            {
                float eased = 1f - Mathf.SmoothStep(0f, 1f, t);
                view.SetScale(0.55f + 0.45f * eased);
                view.SetAlpha(eased);
                if (t >= 1f)
                {
                    view.Hide();
                    cached.Clear();
                    pageItems.Clear();
                    state = WheelState.Closed;
                }
            }
        }

        private void Close()
        {
            if (state == WheelState.Closed || state == WheelState.Closing)
            {
                return;
            }
            WheelInputState.SetOpen(false);
            try
            {
                if (InputActionManager.current != null)
                {
                    InputActionManager.current.Reset();
                }
            }
            catch (Exception)
            {
                // Ignore: the suppression timer already guarantees recovery.
            }
            state = WheelState.Closing;
            animationElapsed = 0f;
            if (!useAnimation)
            {
                view.Hide();
                cached.Clear();
                pageItems.Clear();
                state = WheelState.Closed;
            }
        }

        private void ForceClose()
        {
            state = WheelState.Closed;
            WheelInputState.SetOpen(false);
            WheelInputState.ForceClear();
            try
            {
                if (view.Ready)
                {
                    view.Hide();
                }
            }
            catch (Exception)
            {
                // Nothing else to clean up.
            }
            cached.Clear();
            pageItems.Clear();
        }

        private void OnDestroy()
        {
            WheelInputState.ForceClear();
        }

        private static bool Pressed(KeyCode key)
        {
            int mouse = (int)key - (int)KeyCode.Mouse0;
            if (mouse >= 0 && mouse <= 6)
            {
                return Input.GetMouseButtonDown(mouse);
            }
            return Input.GetKeyDown(key);
        }
    }
}
