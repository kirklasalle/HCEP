// ──────────────────────────────────────────────────────────────
// HCEP — Human Communication Eye Protocol
// Copyright © 2026 Kirk LaSalle. All rights reserved.
// ──────────────────────────────────────────────────────────────
using HCEP.Core.Enums;
using HCEP.Core.Models;

namespace HCEP.Intelligence;

/// <summary>
/// Builds a <see cref="ContextSnapshot"/> from the current system clock and
/// user-configured environment settings.
///
/// Scientific basis: Hall (1959, 1983) Chronemics; Barker (1968) Behavior Settings;
/// Aschoff (1965) Circadian Rhythms. See HCEP_SCIENCE_FOUNDATION.md §Part XI.
/// </summary>
public sealed class TimeContextProvider
{
    // ── User-configurable context settings ────────────────────
    /// <summary>Physical environment type. Set by the user in Settings.</summary>
    public EnvironmentType Environment { get; set; } = EnvironmentType.Unknown;

    /// <summary>User-entered location label (e.g. "my studio", "kitchen table").</summary>
    public string? UserDefinedLocation { get; set; }

    /// <summary>Current activity. Set by the user or inferred from context.</summary>
    public SituationActivity Activity { get; set; } = SituationActivity.Unknown;

    /// <summary>Free-text activity description.</summary>
    public string? ActivityDescription { get; set; }

    /// <summary>Privacy level for this interaction context.</summary>
    public SituationPrivacy Privacy { get; set; } = SituationPrivacy.Private;

    // ── Snapshot factory ──────────────────────────────────────
    /// <summary>
    /// Creates a <see cref="ContextSnapshot"/> from the current wall clock and
    /// user-configured environment/activity settings.
    /// </summary>
    public ContextSnapshot BuildSnapshot(bool silenceProtocolActive = false)
    {
        var now = DateTimeOffset.Now;
        var local = now.LocalDateTime;

        return new ContextSnapshot
        {
            Timestamp         = now,
            TimeOfDay         = ClassifyTimeOfDay(local.Hour),
            DayType           = ClassifyDayType(local.DayOfWeek),
            Season            = ClassifySeason(local.Month),
            TimezoneId        = TimeZoneInfo.Local.Id,
            Environment       = Environment,
            UserDefinedLocation = UserDefinedLocation,
            Activity          = Activity,
            ActivityDescription = ActivityDescription,
            Privacy           = Privacy,
            Register          = DeriveRegister(ClassifyTimeOfDay(local.Hour), Environment),
            TemporalUrgency   = DeriveUrgency(ClassifyTimeOfDay(local.Hour), ClassifyDayType(local.DayOfWeek)),
            SilenceProtocolActive = silenceProtocolActive,
        };
    }

    // ── Classification helpers ─────────────────────────────────

    public static TimeOfDayCategory ClassifyTimeOfDay(int hour) => hour switch
    {
        >= 5  and < 8  => TimeOfDayCategory.Dawn,
        >= 8  and < 12 => TimeOfDayCategory.Morning,
        >= 12 and < 14 => TimeOfDayCategory.Midday,
        >= 14 and < 18 => TimeOfDayCategory.Afternoon,
        >= 18 and < 22 => TimeOfDayCategory.Evening,
        _              => TimeOfDayCategory.Night,
    };

    public static DayType ClassifyDayType(DayOfWeek dow) => dow switch
    {
        DayOfWeek.Saturday or DayOfWeek.Sunday => DayType.Weekend,
        _ => DayType.Weekday,
    };

    public static Season ClassifySeason(int month) => month switch
    {
        3 or 4 or 5   => Season.Spring,
        6 or 7 or 8   => Season.Summer,
        9 or 10 or 11 => Season.Autumn,
        _             => Season.Winter,
    };

    private static CommunicationRegister DeriveRegister(
        TimeOfDayCategory tod, EnvironmentType env)
    {
        // Office/Lab during working hours → Professional
        if (env is EnvironmentType.Office or EnvironmentType.Laboratory
            && tod is TimeOfDayCategory.Morning or TimeOfDayCategory.Midday or TimeOfDayCategory.Afternoon)
            return CommunicationRegister.Professional;

        // Bedroom/night → Intimate
        if (env is EnvironmentType.Bedroom || tod is TimeOfDayCategory.Night)
            return CommunicationRegister.Intimate;

        // Evening at home → Personal
        if (tod is TimeOfDayCategory.Evening
            && env is EnvironmentType.LivingRoom or EnvironmentType.Kitchen or EnvironmentType.Unknown)
            return CommunicationRegister.Personal;

        return CommunicationRegister.Informal;
    }

    private static float DeriveUrgency(TimeOfDayCategory tod, DayType day)
    {
        // Weekday mornings are most time-pressured
        if (day == DayType.Weekday && tod is TimeOfDayCategory.Morning)
            return 0.7f;
        // Weekday working hours: moderate urgency
        if (day == DayType.Weekday && tod is TimeOfDayCategory.Midday or TimeOfDayCategory.Afternoon)
            return 0.4f;
        // Evenings/weekends: relaxed
        return 0.1f;
    }
}
