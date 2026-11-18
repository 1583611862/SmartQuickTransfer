using System;

namespace SmartQuickTransfer.Core
{
    internal sealed class TransferSettings
    {
        internal const string DesktopAuto = "自动";
        internal const string DesktopPlayerInventory = "随身背包";
        internal const string DesktopBackInventory = "后场仓库";
        internal const string DesktopFrontCounter = "前台柜台";
        internal const string DesktopShowcase = "展示柜";

        private ConfigEntry<bool> enabled;
        private ConfigEntry<bool> preserveContainerOpen;
        private ConfigEntry<bool> preserveMachineOpen;
        private ConfigEntry<bool> includeStorePanels;
        private ConfigEntry<string> desktopTarget;
        private ConfigEntry<string> machineReturnTarget;
        private ConfigEntry<bool> animate;
        private ConfigEntry<float> animationDuration;

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

        internal string DesktopTarget
        {
            get { return Normalize(desktopTarget == null ? DesktopAuto : desktopTarget.Value, DesktopAuto); }
        }

        internal string MachineReturnTarget
        {
            get { return Normalize(machineReturnTarget == null ? DesktopBackInventory : machineReturnTarget.Value, DesktopBackInventory); }
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
                    () => "开启后，随身背包、后场仓库、前台柜台、展示柜这些常驻面板本身也能成为转移目标；关闭时它们只作为兜底存放区。");

                settings.desktopTarget = config.Define("desktop_target", DesktopAuto, "目标选择",
                    () => "没有目标窗口时的去向",
                    new[] { DesktopAuto, DesktopPlayerInventory, DesktopBackInventory, DesktopFrontCounter, DesktopShowcase },
                    () => "没有打开任何机器、工具或容器窗口时，物品存放的位置。选“自动”时：店内存入随身背包，店外存入夜间地面。");

                settings.machineReturnTarget = config.Define("machine_return_target", DesktopBackInventory, "目标选择",
                    () => "从机器取出物品的去向",
                    new[] { DesktopBackInventory, DesktopPlayerInventory, DesktopFrontCounter, DesktopShowcase },
                    () => "双击机器或工具窗口里的物品时，物品存放的位置。默认存入后场仓库，满了会自动改投随身背包。");
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

            // Values written by 1.1 and earlier were English; accept and translate them so an
            // existing config file keeps working after the labels became Chinese.
            switch (value)
            {
                case "auto":
                    return DesktopAuto;
                case "player_inventory":
                    return DesktopPlayerInventory;
                case "back_inventory":
                    return DesktopBackInventory;
                case "front_counter":
                    return DesktopFrontCounter;
                case "showcase":
                    return DesktopShowcase;
                default:
                    return value;
            }
        }
    }
}
