namespace FlowDesk.Core.Palette;

/// <summary>
/// One Windows Settings page the command palette can open. <see cref="Uri"/> is an
/// <c>ms-settings:</c> URI; <see cref="Synonyms"/> is a comma-separated list of plain search
/// terms (lower case) so typing "dnd" or "blue light" still finds the page.
/// </summary>
public sealed record SettingsPageLink(string Id, string Title, string Uri, string Synonyms);

/// <summary>
/// Curated <c>ms-settings:</c> links, ordered by how often people open them. Page names follow
/// Windows 11; a few pages keep their Windows 10 name in the Settings app (for example
/// "Installed apps" is still the Apps &amp; features page).
/// </summary>
public static class SettingsPageCatalog
{
    public static IReadOnlyList<SettingsPageLink> All { get; } =
    [
        new("display", "Display", "ms-settings:display",
            "screen, monitor, resolution, scaling, brightness, hdr"),
        new("sound", "Sound", "ms-settings:sound",
            "audio, speakers, microphone, volume, output, input"),
        new("bluetooth", "Bluetooth & devices", "ms-settings:bluetooth",
            "bluetooth, pair, devices, printer, pen"),
        new("wifi", "Wi-Fi", "ms-settings:network-wifi",
            "wifi, wireless, network, hotspot"),
        new("ethernet", "Ethernet", "ms-settings:network-ethernet",
            "ethernet, wired, network, adapter"),
        new("windows-update", "Windows Update", "ms-settings:windowsupdate",
            "update, upgrade, patch, version"),
        new("startup-apps", "Startup apps", "ms-settings:startupapps",
            "startup, boot, autostart, login apps"),
        new("notifications", "Notifications", "ms-settings:notifications",
            "notifications, alerts, banners, tray"),
        new("default-apps", "Default apps", "ms-settings:defaultapps",
            "default apps, file associations, browser, mail"),
        new("apps", "Installed apps", "ms-settings:appsfeatures",
            "apps, programs, uninstall, features, size"),
        new("power", "Power & battery", "ms-settings:powersleep",
            "power, battery, sleep, hibernate, energy saver"),
        new("storage", "Storage", "ms-settings:storagesense",
            "storage, disk, space, cleanup, drive"),
        new("mouse", "Mouse", "ms-settings:mousetouchpad",
            "mouse, pointer, cursor, touchpad, scroll"),
        new("keyboard", "Keyboard", "ms-settings:easeofaccess-keyboard",
            "keyboard, typing, key repeat, blink"),
        new("taskbar", "Taskbar", "ms-settings:taskbar",
            "taskbar, tray, system icons, corner"),
        new("personalization", "Personalization", "ms-settings:personalization",
            "personalization, wallpaper, background, theme, colors, accent"),
        new("night-light", "Night light", "ms-settings:nightlight",
            "night light, blue light, warmth, schedule"),
        new("focus", "Do not disturb", "ms-settings:quiethours",
            "focus, do not disturb, dnd, quiet hours, notifications"),
        new("focus-sessions", "Focus", "ms-settings:quietmomentshome",
            "focus session, timer, concentration"),
        new("privacy", "Privacy & security", "ms-settings:privacy",
            "privacy, security, permissions, app access"),
        new("microphone-privacy", "Microphone access", "ms-settings:privacy-microphone",
            "microphone, mic, privacy, app access"),
        new("camera-privacy", "Camera access", "ms-settings:privacy-webcam",
            "camera, webcam, privacy, app access"),
        new("location-privacy", "Location", "ms-settings:privacy-location",
            "location, gps, privacy"),
        new("date-time", "Date & time", "ms-settings:dateandtime",
            "date, time, time zone, clock, sync"),
        new("language-region", "Language & region", "ms-settings:regionlanguage",
            "language, region, locale, keyboard layout, speech"),
        new("accessibility", "Accessibility", "ms-settings:easeofaccess",
            "accessibility, ease of access, narrator, magnifier, contrast"),
        new("multitasking", "Multitasking", "ms-settings:multitasking",
            "multitasking, snap, virtual desktops, alt tab"),
        new("sign-in-options", "Sign-in options", "ms-settings:signinoptions",
            "sign in, password, pin, windows hello, face, fingerprint"),
        new("windows-security", "Windows Security", "ms-settings:windowsdefender",
            "security, defender, antivirus, virus, firewall, protection"),
        new("recovery", "Recovery", "ms-settings:recovery",
            "recovery, reset, restore, advanced startup"),
        new("about", "About", "ms-settings:about",
            "about, system info, specs, version, device name"),
    ];
}
