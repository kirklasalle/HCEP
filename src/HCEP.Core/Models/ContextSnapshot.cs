// ──────────────────────────────────────────────────────────────
// HCEP — Human Communication Eye Protocol
// Copyright © 2026 Kirk LaSalle. All rights reserved.
// ──────────────────────────────────────────────────────────────
namespace HCEP.Core.Models;

/// <summary>Time-of-day bands that modulate AI communication register.</summary>
public enum TimeOfDayCategory { Dawn, Morning, Midday, Afternoon, Evening, Night }

/// <summary>Day type affecting formality and urgency.</summary>
public enum DayType { Weekday, Weekend, Holiday }

/// <summary>Season — influences mood baseline and cultural context.</summary>
public enum Season { Spring, Summer, Autumn, Winter }

/// <summary>Physical environment type — determines social norms and register.</summary>
public enum EnvironmentType
{
    Unknown,
    Bedroom,
    LivingRoom,
    Kitchen,
    Office,
    Laboratory,
    Studio,
    Outdoors,
    PublicSpace,
    Vehicle
}

/// <summary>Privacy level of the current interaction context.</summary>
public enum SituationPrivacy { Private, SemiPrivate, Public }

/// <summary>Activity context — what the user is currently doing.</summary>
public enum SituationActivity
{
    Unknown,
    Working,
    Relaxing,
    Socializing,
    Creating,
    Learning,
    Exercising,
    Eating,
    Commuting
}

/// <summary>AI communication register derived from time + space + situation.</summary>
public enum CommunicationRegister { Formal, Professional, Informal, Personal, Intimate }

/// <summary>
/// Captures the full contextual state of a human-AI interaction at a point in time:
/// Person × Time × Space × Situation.
///
/// This quadrant is injected into LLM system prompts and modulates the
/// <see cref="HCEP.Core.Enums.HcepMode"/> classification to produce contextually
/// appropriate AI responses. See HCEP_SCIENCE_FOUNDATION.md §Part XI.
/// </summary>
public sealed record ContextSnapshot
{
    // ── Time Dimension ──────────────────────────────────────────
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public TimeOfDayCategory TimeOfDay { get; init; }
    public DayType DayType { get; init; }
    public Season Season { get; init; }
    /// <summary>IANA timezone ID (e.g. "America/New_York").</summary>
    public string TimezoneId { get; init; } = TimeZoneInfo.Local.Id;

    // ── Space Dimension ─────────────────────────────────────────
    /// <summary>WGS84 latitude — null if location unavailable or not consented.</summary>
    public double? Latitude { get; init; }
    /// <summary>WGS84 longitude — null if location unavailable or not consented.</summary>
    public double? Longitude { get; init; }
    public string? CityName { get; init; }
    /// <summary>ISO 3166-1 alpha-2 country code.</summary>
    public string? CountryCode { get; init; }
    public EnvironmentType Environment { get; init; } = EnvironmentType.Unknown;
    /// <summary>User-entered location label, e.g. "my studio", "dad's kitchen".</summary>
    public string? UserDefinedLocation { get; init; }

    // ── Situation Dimension ──────────────────────────────────────
    public SituationPrivacy Privacy { get; init; } = SituationPrivacy.Private;
    public SituationActivity Activity { get; init; } = SituationActivity.Unknown;
    /// <summary>Free-text activity description, e.g. "writing code", "watching TV".</summary>
    public string? ActivityDescription { get; init; }

    // ── Derived AI Strategy ──────────────────────────────────────
    /// <summary>
    /// When true, the avatar should NOT initiate speech — silence is the appropriate response.
    /// Computed by <see cref="HCEP.Intelligence.SilenceProtocolEvaluator"/>.
    /// </summary>
    public bool SilenceProtocolActive { get; init; }
    public CommunicationRegister Register { get; init; } = CommunicationRegister.Informal;
    /// <summary>0.0 = leisurely · 1.0 = time-pressured.</summary>
    public float TemporalUrgency { get; init; }

    /// <summary>Compact summary string injected into LLM system prompts.</summary>
    public string ToPromptString()
    {
        var env = Environment == EnvironmentType.Unknown ? "unknown location" : Environment.ToString();
        var loc = UserDefinedLocation is not null ? $" ({UserDefinedLocation})" : "";
        var act = Activity != SituationActivity.Unknown ? $" · {Activity}" : "";
        var tz = TimezoneId;
        return
            $"[{TimeOfDay} | {DayType} | {Season} | {env}{loc}{act} | " +
            $"Privacy: {Privacy} | Register: {Register} | " +
            $"SilenceProtocol: {(SilenceProtocolActive ? "ACTIVE" : "inactive")} | TZ: {tz}]";
    }
}
