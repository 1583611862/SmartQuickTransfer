using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace SmartQuickTransfer.EquipmentWheel
{
    /// <summary>
    /// The wheel's own screen-space overlay. It has no GraphicRaycaster and every graphic is a
    /// non-raycast target, so it never competes with game UI for pointer events; input is read
    /// directly from Unity Input by the controller instead. Icons are pooled and rebuilt only on
    /// open / page change, never per frame.
    /// </summary>
    internal sealed class WheelView
    {
        private const int SortingOrder = 31000;
        private const int PoolSize = 8;
        internal const float IconRadius = 196f;
        private const float IconSize = 76f;

        private static Sprite discSprite;
        private static Font uiFont;

        private Canvas canvas;
        private RectTransform root;
        private CanvasGroup group;
        private Image backdrop;
        private Image dial;
        private Image pointer;
        private Text nameText;
        private Text detailText;
        private Text pageText;
        private Text emptyText;
        private readonly List<Slot> slots = new List<Slot>();

        private sealed class Slot
        {
            internal GameObject Object;
            internal Image Background;
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

            backdrop = Image("SQT_WheelBackdrop", root, new Vector2(680f, 680f),
                new Color(0.03f, 0.04f, 0.06f, 0.58f), null);
            backdrop.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            backdrop.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);

            dial = Image("SQT_WheelDial", root, new Vector2(310f, 310f),
                new Color(0.07f, 0.09f, 0.13f, 0.90f), Disc());
            dial.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            dial.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);

            pointer = Image("SQT_WheelPointer", root, new Vector2(IconRadius - 28f, 4f),
                new Color(0.55f, 0.88f, 1f, 0.95f), null);
            pointer.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            pointer.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            pointer.rectTransform.pivot = new Vector2(0f, 0.5f);
            pointer.rectTransform.anchoredPosition = Vector2.zero;

            nameText = Text("SQT_WheelName", root, new Vector2(360f, 34f),
                new Vector2(0f, -82f), 22, TextAnchor.MiddleCenter);
            detailText = Text("SQT_WheelDetail", root, new Vector2(360f, 26f),
                new Vector2(0f, -110f), 15, TextAnchor.MiddleCenter);
            pageText = Text("SQT_WheelPage", root, new Vector2(240f, 24f),
                new Vector2(0f, -146f), 15, TextAnchor.MiddleCenter);
            emptyText = Text("SQT_WheelEmpty", root, new Vector2(420f, 90f),
                Vector2.zero, 20, TextAnchor.MiddleCenter);
            emptyText.text = "没有可用的 Toolbox 装备\n（右键 / 中键 / Esc 关闭）";
            emptyText.gameObject.SetActive(false);

            for (int i = 0; i < PoolSize; i++)
            {
                slots.Add(CreateSlot(i));
            }

            root.gameObject.SetActive(false);
        }

        private Slot CreateSlot(int index)
        {
            Slot slot = new Slot();
            slot.Object = new GameObject("SQT_WheelSlot" + index, typeof(RectTransform));
            RectTransform rect = slot.Object.GetComponent<RectTransform>();
            rect.SetParent(root, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(IconSize, IconSize);

            slot.Background = Image("Back", rect, new Vector2(IconSize, IconSize),
                new Color(0.10f, 0.13f, 0.18f, 0.92f), Disc());
            slot.Background.rectTransform.anchorMin = Vector2.zero;
            slot.Background.rectTransform.anchorMax = Vector2.one;
            slot.Background.rectTransform.offsetMin = Vector2.zero;
            slot.Background.rectTransform.offsetMax = Vector2.zero;

            slot.Icon = Image("Icon", rect, new Vector2(IconSize - 14f, IconSize - 14f),
                Color.white, null);
            slot.Icon.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            slot.Icon.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            slot.Icon.preserveAspect = true;

            slot.Label = Text("Name", rect, new Vector2(150f, 22f),
                new Vector2(0f, -IconSize * 0.62f), 13, TextAnchor.MiddleCenter);

            slot.Object.SetActive(false);
            return slot;
        }

        /// <summary>Rebuild the visible page. Called on open and on page change only.</summary>
        internal void SetPage(List<GameItem> items, int pageIndex, int pageCount, bool showName)
        {
            Ensure();
            for (int i = 0; i < slots.Count; i++)
            {
                slots[i].Object.SetActive(i < items.Count);
            }
            for (int i = 0; i < items.Count && i < slots.Count; i++)
            {
                Slot slot = slots[i];
                GameItem item = items[i];
                Vector2 position = WheelSelector.PositionFor(Vector2.zero, IconRadius, i, items.Count);
                slot.Object.GetComponent<RectTransform>().anchoredPosition = position;
                slot.Icon.sprite = ResolveIcon(item);
                slot.Icon.enabled = slot.Icon.sprite != null;
                slot.Label.gameObject.SetActive(showName && item != null);
                if (item != null && showName)
                {
                    slot.Label.text = SafeName(item, 12);
                }
            }

            nameText.text = "";
            detailText.text = "";
            pageText.text = pageCount > 1
                ? "滚轮切换  " + (pageIndex + 1) + " / " + pageCount
                : "1 / 1";
            emptyText.gameObject.SetActive(items.Count == 0);
            pointer.enabled = items.Count > 0;
        }

        internal void UpdateSelection(int selected, List<GameItem> items)
        {
            for (int i = 0; i < slots.Count; i++)
            {
                Slot slot = slots[i];
                if (!slot.Object.activeSelf)
                {
                    continue;
                }
                bool active = i == selected;
                slot.Background.color = active
                    ? new Color(0.32f, 0.72f, 0.95f, 0.98f)
                    : new Color(0.10f, 0.13f, 0.18f, 0.92f);
                float scale = active ? 1.16f : 1f;
                slot.Object.GetComponent<RectTransform>().localScale = new Vector3(scale, scale, 1f);
                slot.Icon.color = active ? Color.white : new Color(0.78f, 0.82f, 0.88f, 0.92f);
            }

            GameItem item = selected >= 0 && selected < items.Count ? items[selected] : null;
            if (item == null)
            {
                nameText.text = "";
                detailText.text = "";
                pointer.enabled = false;
                return;
            }

            pointer.enabled = true;
            float angle = selected * 2f * Mathf.PI / Mathf.Max(1, items.Count);
            pointer.rectTransform.localEulerAngles = new Vector3(0f, 0f, 90f - angle * Mathf.Rad2Deg);

            nameText.text = item.GetDisplayName(true);
            detailText.text = Describe(item);
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
                : item.IsGameItemType("MELEE_WEAPON") ? "近战武器" : "Toolbox 装备";
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
                return Disc();
            }
            try
            {
                Sprite sprite = RenderHandler.TryLoadFromAtlas(item.spriteAtlasPath, item.spritePath);
                if (sprite == null)
                {
                    sprite = RenderHandler.LoadFromFile(item.spritePath);
                }
                return sprite == null ? Disc() : sprite;
            }
            catch (System.Exception)
            {
                return Disc();
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
                    uiFont = UnityEngine.Font.CreateDynamicFontFromOSFont("Arial", 18);
                }
                catch (System.Exception)
                {
                    uiFont = null;
                }
            }
            return uiFont;
        }

        private static Sprite Disc()
        {
            if (discSprite != null)
            {
                return discSprite;
            }
            const int size = 96;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.name = "SQT_WheelDisc";
            texture.filterMode = FilterMode.Bilinear;
            Color[] pixels = new Color[size * size];
            float centre = (size - 1) * 0.5f;
            float radius = centre - 1f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - centre;
                    float dy = y - centre;
                    float alpha = Mathf.Clamp01(radius - Mathf.Sqrt(dx * dx + dy * dy));
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }
            texture.SetPixels(pixels);
            texture.Apply();
            discSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size),
                new Vector2(0.5f, 0.5f));
            return discSprite;
        }
    }
}
