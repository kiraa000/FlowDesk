using System;

namespace FlowDesk.App.Autosave;

/// <summary>Compact row wording shared by active and inactive workspaces.</summary>
public static class AutoSaveText
{
    public static string For(bool enabled, AutoSaveStatus? status, DateTimeOffset? layoutSavedAt, DateTimeOffset now)
    {
        DateTimeOffset? savedAt = status?.SavedAt;
        if (layoutSavedAt is { } layout && (savedAt is null || layout > savedAt)) savedAt = layout;

        // The current toggle wins over a queued/stale Off status after off -> on.
        if (!enabled)
            return savedAt is { } offSaved ? $"Autosave off · saved {Age(offSaved, now)}" : "Autosave off";
        if (status?.State == AutoSaveState.Failed) return "Autosave on · not saved";
        if (status?.State == AutoSaveState.Pending) return "Autosave on · changes pending";
        return savedAt is { } saved
            ? $"Autosave on · saved {Age(saved, now)}"
            : "Autosave on · not saved yet";
    }

    private static string Age(DateTimeOffset saved, DateTimeOffset now)
    {
        var age = now - saved;
        if (age < TimeSpan.FromSeconds(45)) return "just now";
        if (age < TimeSpan.FromSeconds(90)) return "1 min ago";
        if (age < TimeSpan.FromHours(1)) return $"{(int)age.TotalMinutes} min ago";
        if (age < TimeSpan.FromHours(24))
        {
            int hours = (int)age.TotalHours;
            return hours == 1 ? "1 hour ago" : $"{hours} hours ago";
        }
        if (age < TimeSpan.FromHours(48)) return "yesterday";
        int days = (int)age.TotalDays;
        return days == 1 ? "1 day ago" : $"{days} days ago";
    }
}
