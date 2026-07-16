using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using OptiscalerClient.Models;
using OptiscalerClient.Services;

namespace OptiscalerClient.Views
{
    public partial class AutoConfigWindow : Window
    {
        private readonly Game _game;
        private readonly IGpuDetectionService? _gpuService;
        private readonly AutoConfigService _autoConfigService;
        private GpuInfo? _detectedGpu;
        private AutoConfigResult? _lastResult;

        /// <summary>
        /// The generated profile, available after the user clicks Apply.
        /// </summary>
        public OptiScalerProfile? GeneratedProfile { get; private set; }

        /// <summary>
        /// Component recommendations from the auto-config result.
        /// </summary>
        public ComponentRecommendation? ComponentRecommendations { get; private set; }

        public AutoConfigWindow(Game game)
        {
            InitializeComponent();
            _game = game;
            _gpuService = PlatformServiceFactory.CreateGpuDetectionService();
            _autoConfigService = new AutoConfigService();

            var titleBar = this.FindControl<Border>("TitleBar");
            if (titleBar != null)
                titleBar.PointerPressed += (s, e) => this.BeginMoveDrag(e);

            DetectGpu();
            RegenerateProfile();
        }

        private void DetectGpu()
        {
            var txtGpuIcon = this.FindControl<TextBlock>("TxtGpuIcon");
            var txtGpuName = this.FindControl<TextBlock>("TxtGpuName");
            var txtGpuTier = this.FindControl<TextBlock>("TxtGpuTier");

            if (_gpuService == null)
            {
                if (txtGpuName != null) txtGpuName.Text = "GPU detection unavailable";
                return;
            }

            _detectedGpu = _gpuService.GetDiscreteGPU() ?? _gpuService.GetPrimaryGPU();
            if (_detectedGpu == null)
            {
                if (txtGpuName != null) txtGpuName.Text = "No GPU detected";
                return;
            }

            var tier = AutoConfigService.ClassifyGpu(_detectedGpu);

            if (txtGpuIcon != null)
            {
                txtGpuIcon.Text = _detectedGpu.Vendor switch
                {
                    GpuVendor.AMD => "🔴",
                    GpuVendor.NVIDIA => "🟢",
                    GpuVendor.Intel => "🔵",
                    _ => "⚪"
                };
            }

            if (txtGpuName != null)
                txtGpuName.Text = $"{_detectedGpu.Name} ({_detectedGpu.VideoMemoryGB})";

            if (txtGpuTier != null)
                txtGpuTier.Text = $"Architecture: {FormatTier(tier)}";
        }

        private SimRacingPreferences BuildPreferences()
        {
            var prefs = new SimRacingPreferences();

            // Resolution
            var cmbResolution = this.FindControl<ComboBox>("CmbResolution");
            if (cmbResolution?.SelectedItem is ComboBoxItem resItem)
            {
                var tag = resItem.Tag?.ToString() ?? "QHD1440";
                if (Enum.TryParse<ResolutionTier>(tag, out var resTier))
                    prefs.Resolution = resTier;
            }

            // Refresh Rate
            var cmbRefreshRate = this.FindControl<ComboBox>("CmbRefreshRate");
            if (cmbRefreshRate?.SelectedItem is ComboBoxItem hzItem)
            {
                if (int.TryParse(hzItem.Tag?.ToString(), out var hz))
                    prefs.TargetRefreshRate = hz;
            }

            // Priority
            var cmbPriority = this.FindControl<ComboBox>("CmbPriority");
            if (cmbPriority?.SelectedItem is ComboBoxItem prioItem)
                prefs.CompetitiveMode = prioItem.Tag?.ToString() == "Competitive";

            // Frame Gen
            var cmbFrameGen = this.FindControl<ComboBox>("CmbFrameGen");
            if (cmbFrameGen?.SelectedItem is ComboBoxItem fgItem)
                prefs.EnableFrameGen = fgItem.Tag?.ToString() == "On";

            // VRR
            var chkVrr = this.FindControl<CheckBox>("ChkVrr");
            prefs.HasVrr = chkVrr?.IsChecked ?? true;

            return prefs;
        }

        private void RegenerateProfile()
        {
            if (_detectedGpu == null) return;

            var prefs = BuildPreferences();
            _lastResult = _autoConfigService.GenerateProfile(_detectedGpu, _game, prefs);

            UpdateGameMatch();
            UpdateProfilePreview();
        }

        private void UpdateGameMatch()
        {
            var txtGameMatch = this.FindControl<TextBlock>("TxtGameMatch");
            var txtGameNotes = this.FindControl<TextBlock>("TxtGameNotes");

            if (_lastResult?.DetectedGame != null)
            {
                var game = _lastResult.DetectedGame;
                if (txtGameMatch != null)
                    txtGameMatch.Text = $"✓ Matched: {game.Name} ({game.Engine} / {game.Api})";
                if (txtGameNotes != null)
                    txtGameNotes.Text = game.Notes ?? "";
            }
            else
            {
                if (txtGameMatch != null)
                    txtGameMatch.Text = $"Generic sim racing config for: {_game.Name}";
                if (txtGameNotes != null)
                    txtGameNotes.Text = "No specific game profile found. Using GPU-optimized defaults.";
            }
        }

        private void UpdateProfilePreview()
        {
            if (_lastResult == null) return;

            var txtProfileName = this.FindControl<TextBlock>("TxtProfileName");
            var txtProfileSummary = this.FindControl<TextBlock>("TxtProfileSummary");
            var txtComponents = this.FindControl<TextBlock>("TxtComponents");

            if (txtProfileName != null)
                txtProfileName.Text = _lastResult.Profile.Name;

            if (txtProfileSummary != null)
            {
                var lines = new List<string>();
                var settings = _lastResult.Profile.IniSettings;

                if (settings.ContainsKey("Upscalers"))
                {
                    var ups = settings["Upscalers"];
                    if (ups.TryGetValue("Dx12Upscaler", out var dx12))
                        lines.Add($"DX12: {FormatUpscaler(dx12)}");
                    if (ups.TryGetValue("Dx11Upscaler", out var dx11))
                        lines.Add($"DX11: {FormatUpscaler(dx11)}");
                }

                if (settings.ContainsKey("Sharpness") && settings["Sharpness"].TryGetValue("Sharpness", out var sharp))
                    lines.Add($"Sharpness: {sharp}");

                if (settings.ContainsKey("Framerate") && settings["Framerate"].TryGetValue("FramerateLimit", out var fps))
                    lines.Add(fps == "0.0" ? "FPS Limit: Unlimited" : $"FPS Limit: {fps}");

                if (settings.ContainsKey("FrameGen") && settings["FrameGen"].TryGetValue("Enabled", out var fg))
                    lines.Add($"Frame Gen: {(fg == "true" ? "ON" : "OFF")}");

                if (settings.ContainsKey("FSR") && settings["FSR"].TryGetValue("Fsr4Model", out var model))
                    lines.Add($"Quality Mode: {FormatQualityMode(model)}");

                txtProfileSummary.Text = string.Join("\n", lines);
            }

            if (txtComponents != null)
            {
                var comp = _lastResult.RecommendedComponents;
                var parts = new List<string>();
                if (comp.InstallFakenvapi) parts.Add("✓ Fakenvapi (Reflex on AMD)");
                if (comp.InstallNukemFG) parts.Add("✓ Nukem DLSSG-to-FSR3");
                if (comp.InstallExtras) parts.Add("✓ FSR4 INT8 Extras");
                if (comp.InstallOptiPatcher) parts.Add("✓ OptiPatcher");
                if (parts.Count == 0) parts.Add("No additional components needed");
                txtComponents.Text = string.Join("\n", parts);
            }
        }

        #region Event Handlers

        private void CmbResolution_Changed(object? sender, SelectionChangedEventArgs e) => RegenerateProfile();
        private void CmbRefreshRate_Changed(object? sender, SelectionChangedEventArgs e) => RegenerateProfile();
        private void CmbPriority_Changed(object? sender, SelectionChangedEventArgs e) => RegenerateProfile();
        private void CmbFrameGen_Changed(object? sender, SelectionChangedEventArgs e) => RegenerateProfile();
        private void ChkVrr_Changed(object? sender, RoutedEventArgs e) => RegenerateProfile();

        private void BtnApply_Click(object? sender, RoutedEventArgs e)
        {
            if (_lastResult != null)
            {
                GeneratedProfile = _lastResult.Profile;
                ComponentRecommendations = _lastResult.RecommendedComponents;
            }
            Close(true);
        }

        private void BtnClose_Click(object? sender, RoutedEventArgs e)
        {
            Close(false);
        }

        #endregion

        #region Formatting Helpers

        private static string FormatTier(GpuTier tier) => tier switch
        {
            GpuTier.AmdRdna4 => "AMD RDNA 4 (RX 9000)",
            GpuTier.AmdRdna3 => "AMD RDNA 3 (RX 7000)",
            GpuTier.AmdRdna2 => "AMD RDNA 2 (RX 6000)",
            GpuTier.AmdRdna1 => "AMD RDNA 1 (RX 5000)",
            GpuTier.NvidiaBlackwell => "NVIDIA Blackwell (RTX 50xx)",
            GpuTier.NvidiaAda => "NVIDIA Ada Lovelace (RTX 40xx)",
            GpuTier.NvidiaAmpere => "NVIDIA Ampere (RTX 30xx)",
            GpuTier.NvidiaTuring => "NVIDIA Turing (RTX 20xx)",
            GpuTier.IntelArc => "Intel Arc (Alchemist)",
            GpuTier.IntelBattlemage => "Intel Arc (Battlemage)",
            _ => "Unknown"
        };

        private static string FormatUpscaler(string value) => value switch
        {
            "fsr31" => "FSR 3.1/4 (Native DX12)",
            "fsr31_12" => "FSR 3.1/4 (DX11-on-12 Bridge)",
            "fsr22" => "FSR 2.2.1 (Native)",
            "xess" => "XeSS (Intel)",
            "dlss" => "DLSS (NVIDIA)",
            _ => value
        };

        private static string FormatQualityMode(string model) => model switch
        {
            "0" => "Native AA (no upscaling)",
            "1" => "Quality / Ultra Quality",
            "2" => "Balanced",
            "3" => "Performance",
            "4" => "DRS (Dynamic)",
            "5" => "Ultra Performance",
            _ => $"Mode {model}"
        };

        #endregion
    }
}
