namespace FlowDesk.Core.Calm;

/// <summary>
/// The Calm Windows rows FlowDesk reads and writes. Every registry path is relative to HKCU and no
/// row writes machine-wide state or a policy key. Values follow the research table in
/// <c>research/CALM-WINDOWS-TOGGLES-2026-11-17.md</c> (rows 1, 2, 4-11, 13, 14, 16, 20, 21, 24, 25).
/// A <see langword="null"/> default means the Windows default is unknown or disputed, so a missing
/// value is never treated as a finding.
/// </summary>
public static class CalmCatalog
{
    public static IReadOnlyList<CalmSetting> All { get; } =
    [
        new CalmSetting(
            Id: "start-recommendations",
            Group: "Start",
            Title: "Start recommendations",
            Description: "Tips, shortcuts and new app suggestions shown in the Start menu.",
            Values:
            [
                new CalmValue(
                    Address: new RegistryAddress(
                        Path: @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                        Name: "Start_IrisRecommendations")),
            ],
            Effect: "Next Start menu open"),

        new CalmSetting(
            Id: "start-account-notifications",
            Group: "Start",
            Title: "Account notifications in Start",
            Description: "Account-related notifications shown in the Start menu.",
            Values:
            [
                new CalmValue(
                    Address: new RegistryAddress(
                        Path: @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                        Name: "Start_AccountNotifications")),
            ],
            Effect: "Next Start menu open"),

        new CalmSetting(
            Id: "start-suggestions",
            Group: "Start",
            Title: "Start suggestions",
            Description: "Occasional suggestions from Windows and from the Start menu.",
            Values:
            [
                new CalmValue(
                    Address: new RegistryAddress(
                        Path: @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager",
                        Name: "SubscribedContent-338388Enabled")),
                new CalmValue(
                    Address: new RegistryAddress(
                        Path: @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager",
                        Name: "SystemPaneSuggestionsEnabled")),
            ],
            Effect: "Next suggestion"),

        new CalmSetting(
            Id: "windows-tips",
            Group: "Start",
            Title: "Windows tips",
            Description: "Tips, tricks and suggestions shown while you use Windows.",
            Values:
            [
                new CalmValue(
                    Address: new RegistryAddress(
                        Path: @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager",
                        Name: "SubscribedContent-338389Enabled")),
                new CalmValue(
                    Address: new RegistryAddress(
                        Path: @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager",
                        Name: "SoftLandingEnabled")),
            ],
            Effect: "Next tip; some after sign-in"),

        new CalmSetting(
            Id: "welcome-experience",
            Group: "Start",
            Title: "Welcome after updates",
            Description: "The welcome experience shown after an update the next time you sign in.",
            Values:
            [
                new CalmValue(
                    Address: new RegistryAddress(
                        Path: @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager",
                        Name: "SubscribedContent-310093Enabled")),
            ],
            Effect: "Next sign-in"),

        new CalmSetting(
            Id: "finish-setup-nudge",
            Group: "Start",
            Title: "Finish-setup suggestions",
            Description: "Suggestions to finish setting up the device after a feature update.",
            Values:
            [
                new CalmValue(
                    Address: new RegistryAddress(
                        Path: @"Software\Microsoft\Windows\CurrentVersion\UserProfileEngagement",
                        Name: "ScoobeSystemSettingEnabled")),
            ],
            Effect: "Next sign-in"),

        new CalmSetting(
            Id: "settings-suggested-content",
            Group: "Start",
            Title: "Suggested content in Settings",
            Description: "Suggested content and app promotions shown in the Settings app.",
            Values:
            [
                new CalmValue(
                    Address: new RegistryAddress(
                        Path: @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager",
                        Name: "SubscribedContent-338393Enabled")),
                new CalmValue(
                    Address: new RegistryAddress(
                        Path: @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager",
                        Name: "SubscribedContent-353694Enabled")),
                new CalmValue(
                    Address: new RegistryAddress(
                        Path: @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager",
                        Name: "SubscribedContent-353696Enabled")),
                new CalmValue(
                    Address: new RegistryAddress(
                        Path: @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager",
                        Name: "SubscribedContent-353698Enabled")),
            ],
            Effect: "Next Settings open"),

        new CalmSetting(
            Id: "settings-app-notifications",
            Group: "Start",
            Title: "Settings app notifications",
            Description: "Notifications and tips shown by the Settings app.",
            Values:
            [
                new CalmValue(
                    Address: new RegistryAddress(
                        Path: @"Software\Microsoft\Windows\CurrentVersion\SystemSettings\AccountNotifications",
                        Name: "EnableAccountNotifications")),
            ],
            Effect: "Now"),

        new CalmSetting(
            Id: "taskbar-widgets",
            Group: "Taskbar",
            Title: "Widgets button",
            Description: "The Widgets button on the taskbar.",
            Values:
            [
                new CalmValue(
                    Address: new RegistryAddress(
                        Path: @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                        Name: "TaskbarDa")),
            ],
            Effect: "Now; Explorer restart if it does not change",
            Broadcast: "TraySettings",
            ExplorerRestart: true),

        new CalmSetting(
            Id: "taskbar-task-view",
            Group: "Taskbar",
            Title: "Task view button",
            Description: "The Task view button on the taskbar.",
            Values:
            [
                new CalmValue(
                    Address: new RegistryAddress(
                        Path: @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                        Name: "ShowTaskViewButton")),
            ],
            Effect: "Now; Explorer restart if it does not change",
            Broadcast: "TraySettings",
            ExplorerRestart: true),

        new CalmSetting(
            Id: "taskbar-search",
            Group: "Taskbar",
            Title: "Search on the taskbar",
            Description: "Search box, search icon or no search on the taskbar.",
            Values:
            [
                new CalmValue(
                    Address: new RegistryAddress(
                        Path: @"Software\Microsoft\Windows\CurrentVersion\Search",
                        Name: "SearchboxTaskbarMode"),
                    Calm: 0,
                    On: 2,
                    Default: null),
            ],
            Effect: "After File Explorer restart",
            Broadcast: "TraySettings",
            ExplorerRestart: true,
            SearchChoice: true),

        new CalmSetting(
            Id: "search-highlights",
            Group: "Taskbar",
            Title: "Search highlights",
            Description: "News and daily moments shown inside the search box.",
            Values:
            [
                new CalmValue(
                    Address: new RegistryAddress(
                        Path: @"Software\Microsoft\Windows\CurrentVersion\SearchSettings",
                        Name: "IsDynamicSearchBoxEnabled")),
            ],
            Effect: "After Explorer restart; sign-in is safest",
            Broadcast: "TraySettings",
            ExplorerRestart: true),

        new CalmSetting(
            Id: "lock-screen-fun-facts",
            Group: "Lock",
            Title: "Lock screen fun facts",
            Description: "Fun facts, tips and Spotlight images shown on the lock screen.",
            Values:
            [
                new CalmValue(
                    Address: new RegistryAddress(
                        Path: @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager",
                        Name: "RotatingLockScreenOverlayEnabled")),
                new CalmValue(
                    Address: new RegistryAddress(
                        Path: @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager",
                        Name: "SubscribedContent-338387Enabled")),
            ],
            Effect: "Next lock or unlock"),

        new CalmSetting(
            Id: "suggested-toasts",
            Group: "Notifications",
            Title: "Suggested notifications",
            Description: "Suggested notifications from Microsoft, shown as toasts.",
            Values:
            [
                new CalmValue(
                    Address: new RegistryAddress(
                        Path: @"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings\Windows.SystemToast.Suggested",
                        Name: "Enabled")),
            ],
            Effect: "Now"),

        new CalmSetting(
            Id: "backup-reminder-toasts",
            Group: "Notifications",
            Title: "Backup reminders",
            Description: "Reminders to set up Windows Backup, shown as toasts.",
            Values:
            [
                new CalmValue(
                    Address: new RegistryAddress(
                        Path: @"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings\Windows.SystemToast.BackupReminder",
                        Name: "Enabled")),
            ],
            Effect: "Now"),

        new CalmSetting(
            Id: "sync-provider-notifications",
            Group: "Explorer",
            Title: "Sync provider notifications",
            Description: "OneDrive and other sync provider ads shown in File Explorer.",
            Values:
            [
                new CalmValue(
                    Address: new RegistryAddress(
                        Path: @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                        Name: "ShowSyncProviderNotifications"),
                    Default: null),
            ],
            Effect: "New File Explorer window"),

        new CalmSetting(
            Id: "explorer-home-recommended",
            Group: "Explorer",
            Title: "Recommended section in Home",
            Description: "The Recommended section on the File Explorer Home page.",
            Values:
            [
                new CalmValue(
                    Address: new RegistryAddress(
                        Path: @"Software\Microsoft\Windows\CurrentVersion\Explorer",
                        Name: "ShowRecommendations")),
            ],
            Effect: "Refresh or reopen File Explorer",
            BuildGated: true),
    ];
}
