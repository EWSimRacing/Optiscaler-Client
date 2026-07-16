using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OptiscalerClient.Models;

namespace OptiscalerClient.Services
{
    /// <summary>
    /// Generates optimized OptiScaler profiles based on detected GPU hardware,
    /// game identification, user resolution, and sim racing preferences.
    /// </summary>
    public class AutoConfigService
    {
        private readonly AutoConfigPresets? _presets;

        public AutoConfigService()
        {
            _presets = LoadPresets();
        }

        /// <summary>
        /// Generates an OptiScalerProfile optimized for the given GPU, game, and preferences.
        /// </summary>
        public AutoConfigResult GenerateProfile(GpuInfo gpu, Game? game, SimRacingPreferences preferences)
        {
            var tier = ClassifyGpu(gpu);
            var result = new AutoConfigResult
            {
                GpuTier = tier,
                DetectedGame = null,
                Profile = new OptiScalerProfile(),
                RecommendedComponents = new ComponentRecommendation()
            };

            // 1. Start with GPU-tier base settings
            var baseSettings = GetGpuTierSettings(tier);
            result.Profile.IniSettings = DeepCopySettings(baseSettings);

            // 2. Apply resolution-based quality mode
            ApplyResolutionSettings(result.Profile, tier, preferences);

            // 3. Apply frame rate limiter based on preferences
            ApplyFramerateLimiter(result.Profile, preferences);

            // 4. Apply Frame Generation config
            ApplyFrameGenSettings(result.Profile, preferences, tier);

            // 5. Match game and apply per-game overrides
            if (game != null)
            {
                var matchedGame = MatchGame(game);
                if (matchedGame != null)
                {
                    result.DetectedGame = matchedGame;
                    ApplyGameOverrides(result.Profile, matchedGame);
                }
            }

            // 6. Set profile metadata
            result.Profile.Name = BuildProfileName(tier, result.DetectedGame, preferences);
            result.Profile.Description = BuildProfileDescription(tier, gpu, result.DetectedGame, preferences);
            result.Profile.IsBuiltIn = false;
            result.Profile.CreatedBy = "AutoConfig";
            result.Profile.CreatedDate = DateTime.Now;

            // 7. Determine component recommendations
            result.RecommendedComponents = GetComponentRecommendations(tier, preferences);

            return result;
        }

        /// <summary>
        /// Classifies a GPU into an architecture tier based on name and vendor.
        /// </summary>
        public static GpuTier ClassifyGpu(GpuInfo gpu)
        {
            if (gpu == null) return GpuTier.Unknown;

            var name = gpu.Name.ToUpperInvariant();

            switch (gpu.Vendor)
            {
                case GpuVendor.AMD:
                    // RDNA 4: RX 9000 series
                    if (name.Contains("RX 9") || name.Contains("RADEON 9"))
                        return GpuTier.AmdRdna4;
                    // RDNA 3: RX 7000 series
                    if (name.Contains("RX 7") || name.Contains("RADEON 7"))
                        return GpuTier.AmdRdna3;
                    // RDNA 2: RX 6000 series
                    if (name.Contains("RX 6") || name.Contains("RADEON 6"))
                        return GpuTier.AmdRdna2;
                    // RDNA 1: RX 5000 series
                    if (name.Contains("RX 5") && !name.Contains("RX 50"))
                        return GpuTier.AmdRdna1;
                    return GpuTier.AmdRdna2; // Default AMD to RDNA 2

                case GpuVendor.NVIDIA:
                    // Blackwell: RTX 50xx
                    if (name.Contains("RTX 50") || name.Contains("5090") || name.Contains("5080") || name.Contains("5070") || name.Contains("5060"))
                        return GpuTier.NvidiaBlackwell;
                    // Ada: RTX 40xx
                    if (name.Contains("RTX 40") || name.Contains("4090") || name.Contains("4080") || name.Contains("4070") || name.Contains("4060"))
                        return GpuTier.NvidiaAda;
                    // Ampere: RTX 30xx
                    if (name.Contains("RTX 30") || name.Contains("3090") || name.Contains("3080") || name.Contains("3070") || name.Contains("3060"))
                        return GpuTier.NvidiaAmpere;
                    // Turing: RTX 20xx
                    if (name.Contains("RTX 20") || name.Contains("2080") || name.Contains("2070") || name.Contains("2060") || name.Contains("GTX 16"))
                        return GpuTier.NvidiaTuring;
                    return GpuTier.NvidiaAmpere; // Default NVIDIA to Ampere

                case GpuVendor.Intel:
                    if (name.Contains("B7") || name.Contains("B5") || name.Contains("B3") || name.Contains("BATTLEMAGE"))
                        return GpuTier.IntelBattlemage;
                    if (name.Contains("ARC") || name.Contains("A7") || name.Contains("A5") || name.Contains("A3"))
                        return GpuTier.IntelArc;
                    return GpuTier.IntelArc;

                default:
                    return GpuTier.Unknown;
            }
        }

        /// <summary>
        /// Attempts to match a Game object to a known sim racing title.
        /// </summary>
        public SimRacingGameProfile? MatchGame(Game game)
        {
            if (_presets?.SimRacingGames == null || game == null)
                return null;

            var gameName = game.Name?.ToLowerInvariant() ?? "";
            var exeName = Path.GetFileName(game.ExecutablePath)?.ToLowerInvariant() ?? "";
            int.TryParse(game.AppId, out var steamAppId);

            foreach (var profile in _presets.SimRacingGames)
            {
                // Match by executable name
                if (profile.ExecutableNames.Any(e => e.Equals(exeName, StringComparison.OrdinalIgnoreCase)))
                    return profile;

                // Match by Steam App ID
                if (steamAppId > 0 && profile.SteamAppIds.Contains(steamAppId))
                    return profile;

                // Fuzzy match by game name
                var profileNameLower = profile.Name.ToLowerInvariant();
                if (!string.IsNullOrEmpty(gameName) && 
                    (gameName.Contains(profileNameLower) || profileNameLower.Contains(gameName)))
                    return profile;
            }

            return null;
        }

        /// <summary>
        /// Returns all known sim racing game profiles for UI display.
        /// </summary>
        public List<SimRacingGameProfile> GetAllGameProfiles()
        {
            return _presets?.SimRacingGames ?? new List<SimRacingGameProfile>();
        }

        #region Private Methods

        private Dictionary<string, Dictionary<string, string>> GetGpuTierSettings(GpuTier tier)
        {
            var tierKey = tier.ToString();
            if (_presets?.GpuTierDefaults == null || !_presets.GpuTierDefaults.ContainsKey(tierKey))
            {
                // Fallback: generic AMD RDNA 3 settings
                tierKey = "AmdRdna3";
            }

            if (_presets?.GpuTierDefaults == null || !_presets.GpuTierDefaults.ContainsKey(tierKey))
                return new Dictionary<string, Dictionary<string, string>>();

            var tierConfig = _presets.GpuTierDefaults[tierKey];
            var settings = new Dictionary<string, Dictionary<string, string>>();

            // Map the preset sections to INI sections
            if (tierConfig.Upscalers != null)
                settings["Upscalers"] = new Dictionary<string, string>(tierConfig.Upscalers);
            if (tierConfig.Sharpness != null)
                settings["Sharpness"] = new Dictionary<string, string>(tierConfig.Sharpness);
            if (tierConfig.Spoofing != null)
                settings["Spoofing"] = new Dictionary<string, string>(tierConfig.Spoofing);
            if (tierConfig.FSR != null && tierConfig.FSR.Count > 0)
                settings["FSR"] = new Dictionary<string, string>(tierConfig.FSR);
            if (tierConfig.Framerate != null)
                settings["Framerate"] = new Dictionary<string, string>(tierConfig.Framerate);

            return settings;
        }

        private void ApplyResolutionSettings(OptiScalerProfile profile, GpuTier tier, SimRacingPreferences prefs)
        {
            if (_presets?.ResolutionQualityMap == null) return;

            var resKey = prefs.Resolution.ToString();
            if (!_presets.ResolutionQualityMap.ContainsKey(resKey)) return;

            var qualityMap = _presets.ResolutionQualityMap[resKey];
            var modeKey = prefs.CompetitiveMode ? "Competitive" : "Default";
            if (!qualityMap.ContainsKey(modeKey)) return;

            var qualityMode = qualityMap[modeKey];

            // Only apply FSR4 model selection for AMD GPUs using FSR
            if (tier == GpuTier.AmdRdna3 || tier == GpuTier.AmdRdna4 ||
                tier == GpuTier.AmdRdna2 || tier == GpuTier.AmdRdna1)
            {
                if (!profile.IniSettings.ContainsKey("FSR"))
                    profile.IniSettings["FSR"] = new Dictionary<string, string>();

                profile.IniSettings["FSR"]["Fsr4Model"] = qualityMode;
            }
        }

        private void ApplyFramerateLimiter(OptiScalerProfile profile, SimRacingPreferences prefs)
        {
            if (!profile.IniSettings.ContainsKey("Framerate"))
                profile.IniSettings["Framerate"] = new Dictionary<string, string>();

            if (prefs.TargetRefreshRate > 0 && prefs.HasVrr)
            {
                // With VRR: limit slightly below refresh to keep in VRR range
                var limit = prefs.TargetRefreshRate - 3;
                profile.IniSettings["Framerate"]["FramerateLimit"] = $"{limit}.0";
            }
            else if (prefs.TargetRefreshRate > 0)
            {
                // Without VRR: lock to refresh rate
                profile.IniSettings["Framerate"]["FramerateLimit"] = $"{prefs.TargetRefreshRate}.0";
            }
        }

        private void ApplyFrameGenSettings(OptiScalerProfile profile, SimRacingPreferences prefs, GpuTier tier)
        {
            if (!profile.IniSettings.ContainsKey("FrameGen"))
                profile.IniSettings["FrameGen"] = new Dictionary<string, string>();

            if (prefs.EnableFrameGen && _presets?.FrameGenConfig != null)
            {
                if (_presets.FrameGenConfig.ContainsKey("SimRacing"))
                {
                    var fgConfig = _presets.FrameGenConfig["SimRacing"];
                    foreach (var kv in fgConfig)
                    {
                        profile.IniSettings["FrameGen"][kv.Key] = kv.Value;
                    }
                }

                // FSRFG tuning for sim racing
                if (!profile.IniSettings.ContainsKey("FSRFG"))
                    profile.IniSettings["FSRFG"] = new Dictionary<string, string>();

                profile.IniSettings["FSRFG"]["FPTSafetyMarginInMs"] = "0.01";
                profile.IniSettings["FSRFG"]["FPTVarianceFactor"] = "0.3";
                profile.IniSettings["FSRFG"]["FPTHybridSpin"] = "false";
            }
            else
            {
                profile.IniSettings["FrameGen"]["Enabled"] = "false";
            }
        }

        private void ApplyGameOverrides(OptiScalerProfile profile, SimRacingGameProfile gameProfile)
        {
            if (gameProfile.IniOverrides == null) return;

            foreach (var section in gameProfile.IniOverrides)
            {
                if (!profile.IniSettings.ContainsKey(section.Key))
                    profile.IniSettings[section.Key] = new Dictionary<string, string>();

                foreach (var kv in section.Value)
                {
                    profile.IniSettings[section.Key][kv.Key] = kv.Value;
                }
            }
        }

        private ComponentRecommendation GetComponentRecommendations(GpuTier tier, SimRacingPreferences prefs)
        {
            var rec = new ComponentRecommendation();

            var tierKey = tier.ToString();
            if (_presets?.GpuTierDefaults != null && _presets.GpuTierDefaults.ContainsKey(tierKey))
            {
                var components = _presets.GpuTierDefaults[tierKey].Components;
                if (components != null)
                {
                    rec.InstallFakenvapi = components.InstallFakenvapi;
                    rec.InstallNukemFG = components.InstallNukemFG;
                    rec.InstallOptiPatcher = components.InstallOptiPatcher;
                    rec.InstallExtras = components.InstallExtras;
                }
            }

            // Override: if user wants Frame Gen, recommend NukemFG for games that need it
            if (prefs.EnableFrameGen)
            {
                rec.InstallNukemFG = true;
            }

            return rec;
        }

        private string BuildProfileName(GpuTier tier, SimRacingGameProfile? game, SimRacingPreferences prefs)
        {
            var gpuShort = tier switch
            {
                GpuTier.AmdRdna4 => "RDNA4",
                GpuTier.AmdRdna3 => "RDNA3",
                GpuTier.AmdRdna2 => "RDNA2",
                GpuTier.AmdRdna1 => "RDNA1",
                GpuTier.NvidiaBlackwell => "RTX50",
                GpuTier.NvidiaAda => "RTX40",
                GpuTier.NvidiaAmpere => "RTX30",
                GpuTier.NvidiaTuring => "RTX20",
                GpuTier.IntelArc => "Arc",
                GpuTier.IntelBattlemage => "ArcB",
                _ => "Auto"
            };

            var gameShort = game?.Name ?? "SimRacing";
            var mode = prefs.CompetitiveMode ? "Competitive" : "Quality";

            return $"[{gpuShort}] {gameShort} - {mode}";
        }

        private string BuildProfileDescription(GpuTier tier, GpuInfo gpu, SimRacingGameProfile? game, SimRacingPreferences prefs)
        {
            var parts = new List<string>
            {
                $"Auto-generated for {gpu.Name}",
                $"Resolution: {prefs.Resolution}",
                $"Target: {prefs.TargetRefreshRate}Hz"
            };

            if (prefs.EnableFrameGen)
                parts.Add("Frame Gen: ON");
            if (prefs.CompetitiveMode)
                parts.Add("Mode: Competitive (low latency)");
            if (game != null)
                parts.Add($"Game: {game.Name} ({game.Engine}/{game.Api})");

            return string.Join(" | ", parts);
        }

        private static Dictionary<string, Dictionary<string, string>> DeepCopySettings(
            Dictionary<string, Dictionary<string, string>> source)
        {
            var copy = new Dictionary<string, Dictionary<string, string>>();
            foreach (var section in source)
            {
                copy[section.Key] = new Dictionary<string, string>(section.Value);
            }
            return copy;
        }

        private AutoConfigPresets? LoadPresets()
        {
            try
            {
                var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "configs", "auto_config_presets.json");
                if (!File.Exists(configPath)) return null;

                var json = File.ReadAllText(configPath);
                return JsonSerializer.Deserialize<AutoConfigPresets>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch
            {
                return null;
            }
        }

        #endregion
    }

    #region Supporting Types

    /// <summary>
    /// Result of auto-config generation, including the profile and component recommendations.
    /// </summary>
    public class AutoConfigResult
    {
        public GpuTier GpuTier { get; set; }
        public SimRacingGameProfile? DetectedGame { get; set; }
        public OptiScalerProfile Profile { get; set; } = new();
        public ComponentRecommendation RecommendedComponents { get; set; } = new();
    }

    /// <summary>
    /// Recommended components to install alongside OptiScaler.
    /// </summary>
    public class ComponentRecommendation
    {
        public bool InstallFakenvapi { get; set; }
        public bool InstallNukemFG { get; set; }
        public bool InstallOptiPatcher { get; set; }
        public bool InstallExtras { get; set; }
    }

    /// <summary>
    /// Deserialization model for auto_config_presets.json
    /// </summary>
    public class AutoConfigPresets
    {
        public string Version { get; set; } = "";
        public string Description { get; set; } = "";
        public Dictionary<string, GpuTierConfig> GpuTierDefaults { get; set; } = new();
        public Dictionary<string, Dictionary<string, string>> ResolutionQualityMap { get; set; } = new();
        public Dictionary<string, Dictionary<string, string>> FrameGenConfig { get; set; } = new();
        public List<SimRacingGameProfile> SimRacingGames { get; set; } = new();
    }

    /// <summary>
    /// Per-GPU-tier configuration from the presets file.
    /// </summary>
    public class GpuTierConfig
    {
        public Dictionary<string, string>? Upscalers { get; set; }
        public Dictionary<string, string>? Sharpness { get; set; }
        public Dictionary<string, string>? Spoofing { get; set; }
        public Dictionary<string, string>? FSR { get; set; }
        public Dictionary<string, string>? Framerate { get; set; }
        public ComponentConfig? Components { get; set; }
        public string? Notes { get; set; }
    }

    /// <summary>
    /// Component install flags from presets.
    /// </summary>
    public class ComponentConfig
    {
        public bool InstallFakenvapi { get; set; }
        public bool InstallNukemFG { get; set; }
        public bool InstallOptiPatcher { get; set; }
        public bool InstallExtras { get; set; }
    }

    #endregion
}
