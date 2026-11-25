using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace SmartQuickTransfer.EquipmentWheel
{
    /// <summary>
    /// GTA5-style annular wheel. The selectable ring is drawn as real annular-sector meshes
    /// (RingSectorGraphic); tool icons and labels sit inside their own sector, with no circular
    /// button backgrounds. The centre is genuinely hollow - only a light information layer is
    /// drawn there, never a big solid disc. Every graphic is a non-raycast target and the canvas
    /// has no GraphicRaycaster, so game UI input is untouched; the controller reads Input directly.
    /// </summary>
    internal sealed class WheelView
    {
        private const int SortingOrder = 31000;
        private const int PoolSize = 8;

        private static Sprite placeholderSprite;
        private static Font uiFont;

        private Canvas canvas;
        private RectTransform root;
        private CanvasGroup group;
        private Image scrim;
        private readonly List<RingSectorGraphic> sectors = new List<RingSectorGraphic>();
        private readonly List<Slot> slots = new List<Slot>();
        private Image pointer;
        private Text nameText;
        private Text detailText;
        private Text pageText;
        private Text emptyText;

        private sealed class Slot
        {
            internal GameObject Object;
            internal Image Icon;
            internal Text Label;
        }

        internal bool Ready { get { return root != null; } }

        internal void Ensure()
        {
            if (root != null)
            {
                return;
            }

            GameObject canvasObject = new GameObject("SQT_EquipmentWheelCanvas", typeof(RectTransform));
            canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;

            root = canvasObject.GetComponent<RectTransform>();
            root.SetParent(null, false);
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;
            root.pivot = new Vector2(0.5f, 0.5f);
            root.localScale = Vector3.one;

            group = canvasObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;
            group.interactable = false;

            // A faint full-screen scrim (never a disc) just lifts the wheel off the game picture.
            // The centre stays effectively hollow: no solid element covers the gameplay below.
            scrim = FullScreenImage("SQT_WheelScrim", root, new Color(0.02f, 0.03f, 0.05f, 0.30f));

            for (int i = 0; i < PoolSize; i++)
            {
                sectors.Add(CreateSector(i));
            }
            for (int i = 0; i < PoolSize; i++)
            {
                slots.Add(CreateSlot(i));
            }

            pointer = Image("SQT_WheelPointer", root, new Vector2(40f, 3f),
                new Color(0.62f, 0.90f, 1f, 0.92f), null);
            pointer.rectTransform.pivot = new Vector2(0f, 0.5f);

            nameText = Text("SQT_WheelName", root, new Vector2(180f, 30f),
                new Vector2(0f, 34f), 19, TextAnchor.MiddleCenter);
            detailText = Text("SQT_WheelDetail", root, new Vector2(190f, 24f),
                new Vector2(0f, 8f), 13, TextAnchor.MiddleCenter);
            pageText = Text("SQT_WheelPage", root, new Vector2(190f, 22f),
                new Vector2(0f, -20f), 12, TextAnchor.MiddleCenter);

            emptyText = Text("SQT_WheelEmpty", root, new Vector2(300f, 80f),
                Vector2.zero, 18, TextAnchor.MiddleCenter);
            emptyText.text = "没有可用的 Toolbox 工具\n（右键 / 中键 / Esc 关闭）";
            emptyText.gameObject.SetActive(false);

            root.gameObject.SetActive(false);
        }

        private RingSectorGraphic CreateSector(int index)
        {
            GameObject go = new GameObject("SQT_WheelSector" + index,
                typeof(RectTransform), typeof(CanvasRenderer));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(root, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(WheelGeometry.OuterRadius * 2f, WheelGeometry.OuterRadius * 2f);

            RingSectorGraphic graphic = go.AddComponent<RingSectorGraphic>();
            graphic.raycastTarget = false;
            return graphic;
        }

        private Slot CreateSlot(int index)
        {
            Slot slot = new Slot();
            slot.Object = new GameObject("SQT_WheelItem" + index, typeof(RectTransform));
            RectTransform rect = slot.Object.GetComponent<RectTransform>();
            rect.SetParent(root, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);

            slot.Icon = Image("Icon", rect, new Vector2(48f, 48f), Color.white, null);
            slot.Icon.preserveAspect = true;

            slot.Label = Text("Name", root, new Vector2(120f, 20f),
                Vector2.zero, 13, TextAnchor.MiddleCenter);

            slot.Object.SetActive(false);
            return slot;
        }

        /// <summary>Rebuild the visible page. Called on open and on page change only.</summary>
        internal void SetPage(List<GameItem> items, int pageIndex, int pageCount, bool showName)
        {
            Ensure();
            int count = Mathf.Min(items.Count, PoolSize);
            float iconSize = WheelGeometry.IconSize(count);
            float labelOffset = WheelGeometry.LabelRadius - WheelGeometry.IconRadius;

            for (int i = 0; i < PoolSize; i++)
            {
                bool active = i < count;
                sectors[i].gameObject.SetActive(active);
                slots[i].Object.SetActive(active);
                slots[i].Label.gameObject.SetActive(false);
                if (!active)
                {
                    continue;
                }

                sectors[i].Configure(i, count);
                float angle = WheelGeometry.CentreAngle(i, count);
                Vector2 iconPos = WheelGeometry.PointAt(WheelGeometry.IconRadius, angle);

                RectTransform slotRect = slots[i].Object.GetComponent<RectTransform>();
                slotRect.anchoredPosition = iconPos;
                slots[i].Icon.rectTransform.sizeDelta = new Vector2(iconSize, iconSize);
                slots[i].Icon.sprite = ResolveIcon(items[i]);
                slots[i].Icon.enabled = slots[i].Icon.sprite != null;

                if (showName && items[i] != null)
                {
                    Vector2 labelPos = WheelGeometry.PointAt(WheelGeometry.IconRadius + labelOffset, angle);
                    RectTransform labelRect = slots[i].Label.rectTransform;
                    labelRect.anchoredPosition = labelPos;
                    slots[i].Label.text = SafeName(items[i], 12);
                    slots[i].Label.gameObject.SetActive(true);
                }
            }

            nameText.text = "";
            detailText.text = "";
            pageText.text = pageCount > 1
                ? "滚轮切换  " + (pageIndex + 1) + " / " + pageCount
                : "1 / 1";
            emptyText.gameObject.SetActive(items.Count == 0);
            pointer.enabled = false;
        }

        internal void UpdateSelection(int selected, List<GameItem> items)
        {
            for (int i = 0; i < sectors.Count; i++)
            {
                sectors[i].Highlighted = i == selected;
            }

            for (int i = 0; i < slots.Count; i++)
            {
                Slot slot = slots[i];
                if (!slot.Object.activeSelf)
                {
                    continue;
                }
                bool active = i == selected;
                float scale = active ? 1.14f : 1f;
                slot.Icon.rectTransform.localScale = new Vector3(scale, scale, 1f);
                slot.Icon.color = active ? Color.white : new Color(0.80f, 0.84f, 0.90f, 0.95f);
            }

            GameItem item = selected >= 0 && selected < items.Count ? items[selected] : null;
            if (item == null)
            {
                nameText.text = "";
                detailText.text = "";
                pointer.enabled = false;
                return;
            }

            float angle = WheelGeometry.CentreAngle(selected, Mathf.Max(1, items.Count));
            Vector2 origin = WheelGeometry.PointAt(WheelGeometry.InnerRadius + 6f, angle);
            pointer.rectTransform.anchoredPosition = origin;
            pointer.rectTransform.sizeDelta = new Vector2(WheelGeometry.IconRadius - WheelGeometry.InnerRadius - 12f, 3f);
            pointer.rectTransform.localEulerAngles = new Vector3(0f, 0f, 90f - angle * Mathf.Rad2Deg);
            pointer.enabled = true;

            nameText.text = item.GetDisplayName(true);
            detailText.text = Describe(item);
        }

        /// <summary>Advance the smooth sector highlight. Call every frame while visible.</summary>
        internal void Tick(float dt)
        {
            for (int i = 0; i < sectors.Count; i++)
            {
                if (sectors[i].gameObject.activeSelf)
                {
                    sectors[i].Tick(dt);
                }
            }
        }

        internal void Show()
        {
            Ensure();
            root.gameObject.SetActive(true);
        }

        internal void Hide()
        {
            if (root != null)
            {
                root.gameObject.SetActive(false);
            }
        }

        /// <summary>Briefly show a status line (e.g. an equip failure) without changing selection.</summary>
        internal void Flash(string message)
        {
            if (detailText != null)
            {
                detailText.text = message;
            }
        }

        internal void SetAlpha(float value)
        {
            if (group != null)
            {
                group.alpha = value;
            }
        }

        internal void SetScale(float value)
        {
            if (root != null)
            {
                root.localScale = new Vector3(value, value, 1f);
            }
        }

        private static string Describe(GameItem item)
        {
            string type = item.IsGameItemType("TOOL") ? "工具"
                : item.IsGameItemType("MELEE_WEAPON") ? "近战武器" : "Toolbox 工具";
            string state = "";
            ItemSelectHandler selector = ItemSelectHandler.current;
            if (selector != null && ReferenceEquals(selector.GetEquippedItem(), item))
            {
                state = " · 使用中";
            }
            if (!WheelEligibility.CanEquipNow(item))
            {
                state += " · 当前不可装备";
            }
            if (item.IsTag("DURABILITY_TAG"))
            {
                state += " · 耐久 " + DurabilityHelper.GetRealDurability(item);
            }
            if (item.unitCount > 1)
            {
                state += " ×" + item.unitCount;
            }
            return type + state;
        }

        private static Sprite ResolveIcon(GameItem item)
        {
            if (item == null)
            {
                return Placeholder();
            }
            try
            {
                Sprite sprite = RenderHandler.TryLoadFromAtlas(item.spriteAtlasPath, item.spritePath);
                if (sprite == null)
                {
                    sprite = RenderHandler.LoadFromFile(item.spritePath);
                }
                return sprite == null ? Placeholder() : sprite;
            }
            catch (System.Exception)
            {
                return Placeholder();
            }
        }

        private static string SafeName(GameItem item, int max)
        {
            string value = item.GetDisplayName(true);
            if (string.IsNullOrEmpty(value))
            {
                return "?";
            }
            return value.Length > max ? value.Substring(0, max - 1) + "…" : value;
        }

        private static Image FullScreenImage(string name, RectTransform parent, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            Image image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static Image Image(string name, RectTransform parent, Vector2 size, Color color, Sprite sprite)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = Vector2.zero;
            Image image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static Text Text(string name, RectTransform parent, Vector2 size,
            Vector2 position, int fontSize, TextAnchor alignment)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Text));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            Text text = go.GetComponent<Text>();
            text.font = Font();
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = new Color(0.92f, 0.95f, 1f, 1f);
            text.raycastTarget = false;
            text.supportRichText = false;
            return text;
        }

        private static Font Font()
        {
            if (uiFont != null)
            {
                return uiFont;
            }
            try
            {
                uiFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
            }
            catch (System.Exception)
            {
                uiFont = null;
            }
            if (uiFont == null)
            {
                try
                {
                    uiFont = UnityEngine.Font.CreateDynamicFontFromOSFont("Arial", 16);
                }
                catch (System.Exception)
                {
                    uiFont = null;
                }
            }
            return uiFont;
        }

        private static Sprite Placeholder()
        {
            if (placeholderSprite != null)
            {
                return placeholderSprite;
            }
            const int size = 64;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.name = "SQT_WheelPlaceholder";
            texture.filterMode = FilterMode.Bilinear;
            Color[] pixels = new Color[size * size];
            float centre = (size - 1) * 0.5f;
            float radius = centre - 2f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - centre;
                    float dy = y - centre;
                    float alpha = Mathf.Clamp01(radius - Mathf.Sqrt(dx * dx + dy * dy));
                    pixels[y * size + x] = new Color(0.55f, 0.62f, 0.72f, alpha * 0.85f);
                }
            }
            texture.SetPixels(pixels);
            texture.Apply();
            placeholderSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size),
                new Vector2(0.5f, 0.5f));
            return placeholderSprite;
        }
    }
}
