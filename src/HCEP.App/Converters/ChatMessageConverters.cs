// ──────────────────────────────────────────────────────────────
// HCEP — Human Communication Eye Protocol
// Copyright © 2026 Kirk LaSalle. All rights reserved.
// ──────────────────────────────────────────────────────────────
using System;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace HCEP.App.Converters;

/// <summary>
/// Parses a raw chat-log entry (string) into a structured role + display text.
/// The chat log continues to be an <c>ObservableCollection&lt;string&gt;</c> for
/// backward compatibility with existing pipeline callbacks; converters below
/// re-project those strings into modern chat-bubble visuals.
///
/// Recognized prefixes (case-sensitive, all produced by MainViewModel):
///   "You: …"                → User (typed)
///   "User: …"               → User (voice/pipeline)
///   "HCEP: …"               → Assistant (unspecified route)
///   "HCEP (local): …"       → Assistant, local route
///   "HCEP (cloud): …"       → Assistant, cloud route
///   "[Error: …]"            → Error
/// </summary>
internal static class ChatMessageParser
{
    public enum ChatRole { User, Assistant, Error, System }

    public readonly record struct ParsedMessage(ChatRole Role, string Badge, string Text, string? RouteTag);

    private static readonly Regex AssistantWithRoute =
        new(@"^HCEP\s*\((?<route>[^)]+)\):\s*(?<body>.*)$", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex ErrorPattern =
        new(@"^\[Error:\s*(?<body>.*?)\]\s*$", RegexOptions.Singleline | RegexOptions.Compiled);

    public static ParsedMessage Parse(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
            return new ParsedMessage(ChatRole.System, "", "", null);

        var m = AssistantWithRoute.Match(raw);
        if (m.Success)
        {
            var route = m.Groups["route"].Value.Trim();
            return new ParsedMessage(ChatRole.Assistant, $"HCEP · {route}", m.Groups["body"].Value, route);
        }

        m = ErrorPattern.Match(raw);
        if (m.Success)
            return new ParsedMessage(ChatRole.Error, "Error", m.Groups["body"].Value, null);

        if (raw.StartsWith("HCEP:", StringComparison.Ordinal))
            return new ParsedMessage(ChatRole.Assistant, "HCEP", raw.Substring(5).TrimStart(), null);

        if (raw.StartsWith("You:", StringComparison.Ordinal))
            return new ParsedMessage(ChatRole.User, "You", raw.Substring(4).TrimStart(), null);

        if (raw.StartsWith("User:", StringComparison.Ordinal))
            return new ParsedMessage(ChatRole.User, "You · voice", raw.Substring(5).TrimStart(), "voice");

        // Fallback — unrecognized entry, render as a neutral system note.
        return new ParsedMessage(ChatRole.System, "System", raw, null);
    }
}

/// <summary>Returns the cleaned message body without the role prefix.</summary>
public sealed class ChatTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => ChatMessageParser.Parse(value as string).Text;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>Returns the display badge (e.g. "You", "HCEP · local", "Error").</summary>
public sealed class ChatBadgeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => ChatMessageParser.Parse(value as string).Badge;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>Returns HorizontalAlignment based on role (User = Right, everything else = Left).</summary>
public sealed class ChatAlignmentConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var role = ChatMessageParser.Parse(value as string).Role;
        return role == ChatMessageParser.ChatRole.User
            ? HorizontalAlignment.Right
            : HorizontalAlignment.Left;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>Returns a bubble background brush appropriate to the message role.</summary>
public sealed class ChatBubbleBrushConverter : IValueConverter
{
    // Tuned against HCEP dark palette (App.xaml: Primary=#7C3AED, Accent=#06B6D4, Surface=#282840).
    private static readonly Brush UserBrush =
        new SolidColorBrush(Color.FromArgb(0x66, 0x7C, 0x3A, 0xED)); // translucent primary
    private static readonly Brush AssistantBrush =
        new SolidColorBrush(Color.FromArgb(0xB0, 0x35, 0x35, 0x5A)); // elevated surface
    private static readonly Brush ErrorBrush =
        new SolidColorBrush(Color.FromArgb(0x55, 0xEF, 0x44, 0x44)); // translucent red
    private static readonly Brush SystemBrush =
        new SolidColorBrush(Color.FromArgb(0x60, 0x94, 0xA3, 0xB8)); // muted grey

    static ChatBubbleBrushConverter()
    {
        UserBrush.Freeze();
        AssistantBrush.Freeze();
        ErrorBrush.Freeze();
        SystemBrush.Freeze();
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var role = ChatMessageParser.Parse(value as string).Role;
        return role switch
        {
            ChatMessageParser.ChatRole.User => UserBrush,
            ChatMessageParser.ChatRole.Assistant => AssistantBrush,
            ChatMessageParser.ChatRole.Error => ErrorBrush,
            _ => SystemBrush,
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>Returns a border brush that subtly accents the bubble.</summary>
public sealed class ChatBorderBrushConverter : IValueConverter
{
    private static readonly Brush UserBrush =
        new SolidColorBrush(Color.FromArgb(0xB0, 0x7C, 0x3A, 0xED));
    private static readonly Brush AssistantBrush =
        new SolidColorBrush(Color.FromArgb(0x55, 0x06, 0xB6, 0xD4));
    private static readonly Brush ErrorBrush =
        new SolidColorBrush(Color.FromArgb(0xB0, 0xEF, 0x44, 0x44));
    private static readonly Brush SystemBrush =
        new SolidColorBrush(Color.FromArgb(0x55, 0x94, 0xA3, 0xB8));

    static ChatBorderBrushConverter()
    {
        UserBrush.Freeze();
        AssistantBrush.Freeze();
        ErrorBrush.Freeze();
        SystemBrush.Freeze();
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var role = ChatMessageParser.Parse(value as string).Role;
        return role switch
        {
            ChatMessageParser.ChatRole.User => UserBrush,
            ChatMessageParser.ChatRole.Assistant => AssistantBrush,
            ChatMessageParser.ChatRole.Error => ErrorBrush,
            _ => SystemBrush,
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>Multiplies a source Width by a numeric parameter (0–1) — used to cap bubble width.</summary>
public sealed class WidthFractionConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not double w || double.IsNaN(w) || w <= 0)
            return double.PositiveInfinity;

        double fraction = 0.85;
        if (parameter is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            fraction = parsed;

        // Reserve a little for scrollbar / padding.
        return Math.Max(80, (w - 24) * fraction);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>Returns Visibility.Visible when integer count is zero (used for empty-state overlay).</summary>
public sealed class ZeroCountToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        int count = value switch
        {
            int i => i,
            long l => (int)l,
            _ => 1
        };
        return count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
