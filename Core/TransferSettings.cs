using System;
using UnityEngine;

namespace SmartQuickTransfer.Core
{
    /// <summary>
    /// The storage areas of the shop, named exactly like the panels the player sees:
    /// 仓库 = the bottom 24x10 grid (EmporiumEntry.invElement), 桌面 = the middle 18x4 counter
    /// (backInvinvElement), 展示柜 = showcaseElement, 前台柜台 = frontInvinvElement,
    /// 夜间地面 = afterhourInventory.
    /// </summary>
    internal sealed class TransferSettings
    {
        internal const string TargetAuto = "自动";
        internal const string TargetStorage = "仓库";
        internal const string TargetDesk = "桌面";
        internal const string TargetShowcase = "展示柜";
        internal const string TargetFrontCounter = "前台柜台";
        internal const string TargetNightGround = "夜间地面";

        private ConfigEntry<bool> enabled;
        private ConfigEntry<bool> preserveContainerOpen;
        private ConfigEntry<bool> preserveMachineOpen;
        private ConfigEntry<bool> includeStorePanels;
        private ConfigEntry<bool> customerDemandDeskPriority;
        private ConfigEntry<string> fallbackArea;
        private ConfigEntry<string> machineOutputArea;
        private ConfigEntry<bool> animate;
        private ConfigEntry<float> animationDuration;

        // Equipment wheel settings live here but are isolated from the double-click business
        // logic: no wheel value is read by ItemTransferManager, PendingPrediction or the learning
        // service, and no transfer value is read by the wheel.
        private ConfigEntry<bool> wheelEnabled;
        private ConfigEntry<string> wheelKey;
        private ConfigEntry<bool> wheelIncludeNested;
        private ConfigEntry<int> wheelPageSize;
        private ConfigEntry<bool> wheelShowName;
        private ConfigEntry<bool> wheelAnimation;

        internal bool Enabled
        {
            get { return enabled == null || enabled.Value; }
        }

        /// <summary>Non-machine containers (backpack, box) keep the vanilla double-click to open.</summary>
        internal bool PreserveContainerOpen
        {
            get { return preserveContainerOpen == null || preserveContainerOpen.Value; }
        }

        /// <summary>Machines and tools keep the vanilla double-click to open their window.</summary>
        internal bool PreserveMachineOpen
        {
            get { return preserveMachineOpen == null || preserveMachineOpen.Value; }
        }

        internal bool IncludeStorePanels
        {
            get { return includeStorePanels != null && includeStorePanels.Value; }
        }

        internal bool CustomerDemandDeskPriority
        {
            get { return customerDemandDeskPriority == null || customerDemandDeskPriority.Value; }
        }

        /// <summary>Where an item goes when no movable window can take it.</summary>
        internal string FallbackArea
        {
            get { return Normalize(fallbackArea == null ? TargetAuto : fallbackArea.Value, TargetAuto); }
        }

        /// <summary>Where an item goes when it is taken out of a machine or tool.</summary>
        internal string MachineOutputArea
        {
            get { return Normalize(machineOutputArea == null ? TargetStorage : machineOutputArea.Value, TargetStorage); }
        }

        internal bool Animate
        {
            get { return animate == null || animate.Value; }
        }

        internal float AnimationDuration
        {
            get
            {
                float value = animationDuration == null ? 0.24f : animationDuration.Value;
                if (value < 0.1f)
                {
                    value = 0.1f;
                }
                if (value > 0.6f)
                {
                    value = 0.6f;
                }
                return value;
            }
        }

        internal bool WheelEnabled
        {
            get { return wheelEnabled == null || wheelEnabled.Value; }
        }

        internal KeyCode WheelKey
        {
            get
            {
                string value = wheelKey == null ? "Mouse2" : wheelKey.Value;
                KeyCode parsed;
                if (!string.IsNullOrEmpty(value) && Enum.TryParse(value, true, out parsed))
                {
                    return parsed;
                }
                return KeyCode.Mouse2;
            }
        }

        internal bool WheelIncludeNested
        {
            get { return wheelIncludeNested == null || wheelIncludeNested.Value; }
        }

        internal int WheelPageSize
        {
            get
            {
                int value = wheelPageSize == null ? 8 : wheelPageSize.Value;
                return value < 1 ? 1 : (value > 8 ? 8 : value);
            }
        }

        internal bool WheelShowName
        {
            get { return wheelShowName == null || wheelShowName.Value; }
        }

        internal bool WheelAnimation
        {
            get { return wheelAnimation == null || wheelAnimation.Value; }
        }

        internal static TransferSettings CreateDefaults()
        {
            return new TransferSettings();
        }

        internal static TransferSettings Create(string modId)
        {
            TransferSettings settings = new TransferSettings();
            try
            {
                ModConfig config = ModHelper.GetConfig(modId);
                if (config == null)
                {
                    ModEntry.LogError("mod config is unavailable; built-in defaults are used.");
                    return settings;
                }

                settings.enabled = config.Define("enabled", true, "常规",
                    () => "双击快速转移",
                    () => "开启后，双击窗口内的物品即可自动转移。关闭后本模组完全不介入任何双击操作。");

                settings.preserveMachineOpen = config.Define("keep_machine_double_click", true, "常规",
                    () => "保留机器双击开窗",
                    () => "开启时，双击机器或工具本体仍然按原版行为打开它的窗口，不会被转移。建议保持开启。");

                settings.preserveContainerOpen = config.Define("keep_container_double_click", true, "常规",
                    () => "保留容器双击开箱",
                    () => "开启时，双击背包、箱子等自带窗口的物品仍然打开窗口。关闭后这些物品也会被快速转移（此时只认“保留机器双击开窗”这一条保护）。");

                settings.animate = config.Define("play_flight_animation", true, "动画反馈",
                    () => "播放转移飞行动画",
                    () => "转移成功时，用一个临时图标沿弧线飞向实际落点，并在落点短暂高亮。数据在动画播放前就已提交，关闭动画不影响转移结果。");

                settings.animationDuration = config.Define("animation_duration", 0.24f, "动画反馈",
                    () => "飞行动画时长（秒）",
                    0.1f, 0.6f,
                    () => "飞行动画的总时长，0.20 到 0.28 之间最接近预期手感。");

                settings.includeStorePanels = config.Define("include_store_panels", false, "目标选择",
                    () => "常驻存放面板也可作为目标",
                    () => "开启后，仓库、桌面、展示柜、前台柜台、夜间地面这些常驻面板本身也能成为转移目标；关闭时它们只作为兜底存放区。");

                settings.customerDemandDeskPriority = config.Define("customer_demand_desk_priority", true, "目标选择",
                    () => "顾客需要的物品优先放桌面",
                    () => "开启时，若当前顾客想购买被双击的物品，会优先把它投放到桌面柜台，方便直接成交；桌面放不下时仍按原有规则处理。");

                settings.fallbackArea = config.Define("fallback_area", TargetAuto, "目标选择",
                    () => "没有目标窗口时的去向",
                    new[] { TargetAuto, TargetStorage, TargetDesk, TargetShowcase, TargetFrontCounter, TargetNightGround },
                    () => "没有打开任何机器、工具或容器窗口时，物品存放的位置。选“自动”时：店内 仓库→桌面→展示柜→前台柜台；店外 夜间地面优先。");

                settings.machineOutputArea = config.Define("machine_output_area", TargetStorage, "目标选择",
                    () => "从机器取出物品的去向",
                    new[] { TargetAuto, TargetStorage, TargetDesk, TargetShowcase, TargetFrontCounter },
                    () => "双击机器或工具窗口里的物品时，物品存放的位置。默认直接进仓库，满了自动改投桌面。");

                settings.wheelEnabled = config.Define("enable_equipment_wheel", true, "装备轮盘",
                    () => "启用动态装备轮盘",
                    () => "开启后，未拖拽物品时按鼠标中键呼出装备轮盘；关闭后中键等原版操作完全恢复。关闭轮盘不影响双击快速转移。");

                settings.wheelKey = config.Define("equipment_wheel_key", "Mouse2", "装备轮盘",
                    () => "呼出轮盘的按键",
                    new[] { "Mouse2", "Mouse1", "F", "G", "Q", "E" },
                    () => "默认鼠标中键。正在拖拽物品时该键始终优先执行原版翻转，不会呼出轮盘。");

                settings.wheelIncludeNested = config.Define("equipment_wheel_include_nested", true, "装备轮盘",
                    () => "扫描背包与箱子",
                    () => "开启时递归扫描玩家仓库里背包、箱子等容器中的装备（带防循环与深度保护）；关闭时只扫描顶层格子。");

                settings.wheelPageSize = config.Define("equipment_wheel_page_size", 8, "装备轮盘",
                    () => "每页装备数量",
                    1, 8,
                    () => "每页最多显示几件装备，超过后用鼠标滚轮翻页。范围 1～8。");

                settings.wheelShowName = config.Define("equipment_wheel_show_item_name", true, "装备轮盘",
                    () => "显示装备名称",
                    () => "在每个装备图标下方显示名称；关闭时只显示图标与中央信息。");

                settings.wheelAnimation = config.Define("equipment_wheel_animation", true, "装备轮盘",
                    () => "轮盘动画",
                    () => "开启时轮盘有展开/收起动画；关闭后立即显示或关闭。动画不影响输入响应。");
            }
            catch (Exception e)
            {
                ModEntry.LogError("failed to read mod config, built-in defaults are used: " + e.Message);
            }
            return settings;
        }

        private static string Normalize(string value, string fallback)
        {
            if (string.IsNullOrEmpty(value))
            {
                return fallback;
            }

            // The first builds mapped these labels to the wrong panels and used English values
            // before that. Translate every legacy spelling to the panel it really points at.
            switch (value)
            {
                case "auto":
                    return TargetAuto;
                case "player_inventory":
                    return TargetStorage;
                case "back_inventory":
                    return TargetDesk;
                case "front_counter":
                    return TargetFrontCounter;
                case "showcase":
                    return TargetShowcase;
                case "随身背包":
                    return TargetStorage;
                case "后场仓库":
                    return TargetDesk;
                case "前台":
                    return TargetFrontCounter;
                case "地面":
                    return TargetNightGround;
                default:
                    return value;
            }
        }
    }
}
