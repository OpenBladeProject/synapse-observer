using System.ComponentModel;
using SynapseObserver.Core;

namespace SynapseObserver.App;

// A checklist of evidence to ask for, not a declaration of hardware support.
internal sealed class CaptureActivity(string id, string title, string guidance, bool readOnly, params SettingChoice[] choices) : INotifyPropertyChanged
{
    public string Id { get; } = id;
    public string Title { get; } = title;
    public string Guidance { get; } = guidance;
    public bool IsReadOnly { get; } = readOnly;
    public IReadOnlyList<SettingChoice> Choices { get; } = [.. choices,
        new("Other state…", "State or value shown (required)", "Use a short description without personal information.", true)];
    public CoverageState State { get; private set; } = CoverageState.Pending;
    public string DisplayLabel => $"{Title} · {StateLabel}";
    private string StateLabel => State switch
    {
        CoverageState.Pending => "Not recorded",
        CoverageState.Completed => IsReadOnly ? "Reported" : "Recorded",
        CoverageState.CompletedWithGaps => "Recorded with gaps",
        CoverageState.Skipped => "Skipped",
        CoverageState.Unavailable => "Unavailable",
        _ => State.ToString(),
    };
    public event PropertyChangedEventHandler? PropertyChanged;
    public void SetState(CoverageState state)
    {
        State = state;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayLabel)));
    }
}

internal static class CaptureActivityCatalog
{
    private static SettingChoice Value(string label, string hint) => new(label, "Value or details (required)", hint);
    private static SettingChoice[] Toggle => [new("Off"), new("On")];
    private static CaptureActivity Setting(string id, string title, string guidance, params SettingChoice[] choices) => new(id, title, guidance, false, choices);
    private static CaptureActivity Report(string id, string title, string guidance, params SettingChoice[] choices) => new(id, title, guidance, true, choices);

    public static CaptureActivity[] Create() =>
    [
        Setting("performance", "Performance mode", "Use the performance modes available for this laptop in Synapse.",
            new("Balanced"), new("Battery Saver"), new("Silent"), new("Performance"), new("Gaming"), new("Creator"), new("HyperBoost"), Value("Custom", "For example: CPU Boost, GPU High")),
        Setting("fans", "Fan setting", "Save all fan values needed to restore the starting setting.",
            new("Automatic"), Value("Fixed", "For example: CPU 4200 RPM, GPU 4000 RPM"), Value("Manual curve", "Record the temperatures and speeds needed to restore your curve.")),
        Setting("charge-limit", "Battery charge limit", "Use the charge-limit control if Synapse offers it.",
            new("Off"), new("50%"), new("60%"), new("70%"), new("80%"), new("90%"), new("100%")),
        Setting("lighting", "Keyboard lighting", "Record one effect and its brightness, colors, speed and direction.",
            new("Off"), Value("Static", "Include brightness and color."), Value("Breathing", "Include brightness, colors and speed."), Value("Spectrum cycling", "Include brightness and speed."), Value("Wave", "Include brightness, speed and direction."), Value("Reactive", "Include brightness, color and duration."), Value("Starlight", "Include brightness, colors and duration.")),
        Setting("custom-cpu-level", "Custom CPU level", "Record the exact CPU level offered by Synapse.", new("Low"), new("Medium"), new("High"), new("Boost")),
        Setting("custom-gpu-level", "Custom GPU level", "Record the exact GPU level offered by Synapse.", new("Low"), new("Medium"), new("High")),
        Setting("hyperboost", "HyperBoost", "Use only an available HyperBoost control. Keep the cooling pad connected if Synapse requires it.", Toggle),
        Report("cooling-pad", "Cooling pad detection", "Report the pad state currently shown. This step does not ask you to connect or disconnect it.", new("Not connected"), new("Connected"), new("Detected by Synapse")),
        Setting("battery-protection", "Battery protection", "Record the battery-protection setting separately from the charge limit.", Toggle),
        Setting("full-charge", "Full charge override", "Record a temporary full-charge override if it is available.", Toggle),
        Setting("keyboard-brightness", "Keyboard brightness", "Change brightness once, then restore the exact starting value.", new("0%"), new("25%"), new("50%"), new("75%"), new("100%")),
        Setting("lighting-frames", "Chroma frame streaming", "Use a short animated effect in Synapse or Chroma. Record its name and restore the previous effect; the observer records any feature calls it sees.", new("Off"), Value("Animated effect", "Record effect name, brightness, colors and speed.")),
        Setting("lighting-key-map", "Physical key and lighting position", "Choose one physical key in Synapse and change only its color. Report its label, physical row/position and RGB color before and after. Maintainers can correlate these markers with observed reports; a whole frame alone does not prove a key map.", Value("Single key color", "For example: Q, row 3 column 2, RGB 255 0 0. Save its original color and effect for restoration.")),
        Report("keyboard-layout", "Keyboard layout and zones", "Select the visible physical layout. Use Other for another locale or to describe extra keys and lighting zones. Do not type text for the collector to record.", new("ANSI US"), new("ANSI international"), new("ISO UK"), new("ISO German"), new("ISO French"), new("ISO Nordic"), new("ISO Spanish"), new("JIS Japanese")),
        Setting("logo-lighting", "Logo lighting", "Record the logo setting independently of keyboard lighting.", new("Off"), new("On"), Value("Effect", "Record the logo effect and its settings.")),
        Report("mute-indicator", "Mute indicator", "Report the current mute indicator and Windows mute state without changing them.", new("Muted, indicator on"), new("Unmuted, indicator off"), Value("Different behavior", "Describe the mute state and indicator.")),
        Setting("function-row", "Function row behavior", "Use the function-row setting shown in Synapse.", new("Function keys"), new("Multimedia keys")),
        Setting("gaming-mode", "Gaming mode", "Record Gaming mode and any disabled-key options needed for restoration.", new("Off"), Value("On", "List disabled-key options, for example Windows key.")),
        Report("startup-lighting", "Startup lighting behavior", "Describe behavior you have already seen at startup. No restart, shutdown or sleep test is required.", new("Off"), new("Previous effect"), new("Default effect")),
        Report("special-key-m1", "M1 key", "Report the current M1 assignment. Use the separate focused key check for input evidence.", new("Unassigned"), Value("Assigned", "Name the action without recording macro text.")),
        Report("special-key-m2", "M2 key", "Report the current M2 assignment. Use the separate focused key check for input evidence.", new("Unassigned"), Value("Assigned", "Name the action without recording macro text.")),
        Report("special-key-m3", "M3 key", "Report the current M3 assignment. Use the separate focused key check for input evidence.", new("Unassigned"), Value("Assigned", "Name the action without recording macro text.")),
        Report("special-key-m4", "M4 key", "Report the current M4 assignment. Use the separate focused key check for input evidence.", new("Unassigned"), Value("Assigned", "Name the action without recording macro text.")),
        Report("special-key-m5", "M5 key", "Report the current M5 assignment. Use the separate focused key check for input evidence.", new("Unassigned"), Value("Assigned", "Name the action without recording macro text.")),
        Report("special-key-fn", "Fn key", "Report the Fn behavior you know. The focused key check may receive no Windows event for a firmware-handled key.", new("Function layer"), new("No visible action")),
        Report("special-key-fn-p", "Fn + P", "Report previously observed behavior; this declaration does not ask you to switch performance mode.", new("Changes performance mode"), new("No visible action")),
        Report("power-source", "Power source", "Report the current power connection; do not unplug it for this step.", new("Battery"), new("Barrel adapter"), new("USB-C")),
        Report("adapter-rating", "Adapter power rating", "Report wattage explicitly shown by software or printed on the adapter. This is not a measured power reading.", Value("Software rating", "For example: 330 W shown in Synapse."), Value("Adapter label", "For example: 100 W printed on adapter.")),
        Setting("gpu-core-offset", "GPU core offset", "Only use values offered by Synapse and restore the exact original offset.", new("0 MHz"), Value("Offset", "Record the signed MHz value.")),
        Setting("gpu-memory-offset", "GPU memory offset", "Only use values offered by Synapse and restore the exact original offset.", new("0 MHz"), Value("Offset", "Record the signed MHz value.")),
        Setting("cpu-power-limits", "CPU power limits", "Record each displayed power limit so the original values can be restored.", new("Default"), Value("Custom limits", "Record sustained and short-term watts and any time window.")),
        Setting("cpu-optimizer", "CPU optimizer", "Record the optimizer setting and any level shown in Synapse.", new("Off"), new("On"), Value("Custom", "Record the displayed optimizer level or offset.")),
        Setting("display-refresh", "Display refresh rate", "Use Windows or Synapse display settings. The collector may see no HID traffic for a Windows display setting.", new("60 Hz"), new("120 Hz"), new("144 Hz"), new("165 Hz"), new("240 Hz")),
        Setting("display-hdr", "Display HDR", "Use the HDR control shown in Windows or Synapse.", Toggle),
        Setting("display-vrr", "Display variable refresh", "Record the VRR or adaptive-sync setting and where it is shown.", Toggle),
        Setting("display-drr", "Display dynamic refresh", "Record Windows dynamic refresh separately from VRR.", new("Off"), Value("Dynamic", "Record the displayed refresh-rate range.")),
        Setting("display-color", "Display color mode", "Record the color mode and any calibration choice needed to restore it.", new("Native"), new("sRGB"), new("DCI-P3"), Value("Custom", "Describe the displayed color mode without a local file path.")),
        Setting("display-brightness", "Display brightness", "Record the panel brightness shown by Windows or Synapse.", new("0%"), new("25%"), new("50%"), new("75%"), new("100%")),
        Report("sensor-telemetry", "Sensor telemetry", "Report sensor names and values already displayed. Missing feature calls do not establish a missing sensor.", Value("Displayed sensors", "For example: CPU temperature 55 C; fan speed 2400 RPM.")),
        Report("ec-mcu", "EC or MCU information", "Record versions explicitly displayed by software. EC or MCU traffic outside selected HID feature calls remains a collection gap.", Value("Displayed versions", "Record only the displayed EC or MCU version; no serial numbers.")),
        Report("other-feature", "Other laptop feature", "Describe an additional visible feature and its current state. Maintainers can request a separate reversible test if needed.", Value("Feature", "Name the feature and its current state, without personal information.")),
    ];
}
