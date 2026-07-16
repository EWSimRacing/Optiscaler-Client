using System.Collections.Generic;

namespace OptiscalerClient.Models
{
    /// <summary>
    /// GPU architecture tiers for determining optimal upscaler settings.
    /// </summary>
    public enum GpuTier
    {
        /// <summary>Fallback when GPU cannot be identified.</summary>
        Unknown,
        /// <summary>NVIDIA RTX 20xx (Turing)</summary>
        NvidiaTuring,
        /// <summary>NVIDIA RTX 30xx (Ampere)</summary>
        NvidiaAmpere,
        /// <summary>NVIDIA RTX 40xx (Ada Lovelace)</summary>
        NvidiaAda,
        /// <summary>NVIDIA RTX 50xx (Blackwell)</summary>
        NvidiaBlackwell,
        /// <summary>AMD RX 5000 (RDNA 1)</summary>
        AmdRdna1,
        /// <summary>AMD RX 6000 (RDNA 2)</summary>
        AmdRdna2,
        /// <summary>AMD RX 7000 (RDNA 3)</summary>
        AmdRdna3,
        /// <summary>AMD RX 9000 (RDNA 4)</summary>
        AmdRdna4,
        /// <summary>Intel Arc A-series (Alchemist)</summary>
        IntelArc,
        /// <summary>Intel Arc B-series (Battlemage)</summary>
        IntelBattlemage
    }

    /// <summary>
    /// Graphics API used by a sim racing title.
    /// </summary>
    public enum GraphicsApi
    {
        DX11,
        DX12,
        Vulkan
    }

    /// <summary>
    /// Per-game metadata for sim racing titles, including engine quirks
    /// and recommended OptiScaler configuration overrides.
    /// </summary>
    public class SimRacingGameProfile
    {
        /// <summary>Display name shown in UI.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Game engine (Madness, UE4, UE5, Custom, ISI).</summary>
        public string Engine { get; set; } = string.Empty;

        /// <summary>Primary graphics API.</summary>
        public GraphicsApi Api { get; set; } = GraphicsApi.DX12;

        /// <summary>
        /// Executable names (lowercase) used to match a detected game to this profile.
        /// Example: ["ams2avx.exe", "ams2.exe"]
        /// </summary>
        public List<string> ExecutableNames { get; set; } = new();

        /// <summary>
        /// Steam App IDs for auto-matching scanned games.
        /// </summary>
        public List<int> SteamAppIds { get; set; } = new();

        /// <summary>Whether the game natively supports FSR (no OptiScaler translation needed).</summary>
        public bool NativeFsr { get; set; }

        /// <summary>Whether the game natively supports DLSS.</summary>
        public bool NativeDlss { get; set; }

        /// <summary>Whether Frame Generation works well with this title.</summary>
        public bool FrameGenCompatible { get; set; }

        /// <summary>
        /// Recommended DLL injection method for this game.
        /// Null means use default (dxgi.dll).
        /// </summary>
        public string? RecommendedInjection { get; set; }

        /// <summary>
        /// Per-game INI overrides that supplement the GPU-tier base config.
        /// Key = INI section, Value = dict of key/value pairs.
        /// </summary>
        public Dictionary<string, Dictionary<string, string>> IniOverrides { get; set; } = new();

        /// <summary>Notes shown to user about this game's quirks.</summary>
        public string? Notes { get; set; }
    }

    /// <summary>
    /// Resolution tier for selecting FSR quality mode.
    /// </summary>
    public enum ResolutionTier
    {
        /// <summary>1920x1080 or lower</summary>
        HD1080,
        /// <summary>2560x1440</summary>
        QHD1440,
        /// <summary>3440x1440 (ultrawide)</summary>
        UltraWide,
        /// <summary>3840x2160</summary>
        UHD4K,
        /// <summary>5120x2880 or higher</summary>
        Above4K
    }

    /// <summary>
    /// User's sim racing preferences that influence profile generation.
    /// </summary>
    public class SimRacingPreferences
    {
        /// <summary>Target monitor refresh rate in Hz (e.g., 60, 144, 240).</summary>
        public int TargetRefreshRate { get; set; } = 144;

        /// <summary>Native render resolution tier.</summary>
        public ResolutionTier Resolution { get; set; } = ResolutionTier.QHD1440;

        /// <summary>Whether user prefers lowest input latency over visual quality.</summary>
        public bool CompetitiveMode { get; set; } = false;

        /// <summary>Whether to enable Frame Generation (adds latency but smoother motion).</summary>
        public bool EnableFrameGen { get; set; } = false;

        /// <summary>Whether to use VRR (FreeSync/G-Sync compatible).</summary>
        public bool HasVrr { get; set; } = true;

        /// <summary>Whether user uses triple monitors or ultrawide.</summary>
        public bool MultiMonitorOrUltrawide { get; set; } = false;
    }
}
