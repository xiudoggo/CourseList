using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Windows.UI;

namespace CourseList.Helpers.Ui;

/// <summary>
/// Deterministic mapping: tag name -> fixed color brush.
/// Supports both preset tags and custom tags (stable hash-based hue).
/// </summary>
public sealed class TagNameToBrushConverter : IValueConverter
{
    private static readonly Dictionary<string, Color> PresetTagColors =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // Common presets (extend if you add more defaults in the form).
            ["study"] = Color.FromArgb(255, 0x2E, 0x86, 0xFF),     // blue
            ["work"] = Color.FromArgb(255, 0xFF, 0x8A, 0x00),      // orange
            ["life"] = Color.FromArgb(255, 0x8E, 0x44, 0xAD),      // purple
            ["exercise"] = Color.FromArgb(255, 0x27, 0xAE, 0x60), // green
            ["urgent"] = Color.FromArgb(255, 0xE0, 0x00, 0x00),    // red
            ["exam"] = Color.FromArgb(255, 0x00, 0x8C, 0xFF),      // sky
        };

    private static readonly ConcurrentDictionary<string, SolidColorBrush> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var raw = value?.ToString();
        if (string.IsNullOrWhiteSpace(raw))
            return new SolidColorBrush(Color.FromArgb(255, 0x88, 0x88, 0x88));

        var tag = raw.Trim();
        if (tag.Length == 0)
            return new SolidColorBrush(Color.FromArgb(255, 0x88, 0x88, 0x88));

        if (Cache.TryGetValue(tag, out var cached))
            return cached;

        var key = tag; // keep original casing as cache key

        if (PresetTagColors.TryGetValue(tag, out var preset))
        {
            var b = new SolidColorBrush(preset);
            Cache[key] = b;
            return b;
        }

        // Stable hue based on FNV-1a hash (so colors don't change between app launches).
        uint h = Fnv1a32(tag.ToLowerInvariant());
        double hue = h % 360; // 0..359

        // Chosen for decent saturation and contrast with white text.
        Color generated = HslToRgb(hue, saturation: 0.68, lightness: 0.42);
        var brush = new SolidColorBrush(generated);
        Cache[key] = brush;
        return brush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();

    private static uint Fnv1a32(string s)
    {
        const uint offset = 2166136261;
        const uint prime = 16777619;

        uint hash = offset;
        for (int i = 0; i < s.Length; i++)
        {
            hash ^= s[i];
            hash *= prime;
        }

        return hash;
    }

    // h in degrees [0..360), s/l in [0..1]
    private static Color HslToRgb(double h, double saturation, double lightness)
    {
        // Normalize.
        h = h % 360.0;
        if (h < 0) h += 360.0;

        double c = (1.0 - Math.Abs(2.0 * lightness - 1.0)) * saturation;
        double x = c * (1.0 - Math.Abs((h / 60.0) % 2.0 - 1.0));
        double m = lightness - c / 2.0;

        double r1, g1, b1;
        if (h >= 0 && h < 60)
        {
            r1 = c; g1 = x; b1 = 0.0;
        }
        else if (h >= 60 && h < 120)
        {
            r1 = x; g1 = c; b1 = 0.0;
        }
        else if (h >= 120 && h < 180)
        {
            r1 = 0.0; g1 = c; b1 = x;
        }
        else if (h >= 180 && h < 240)
        {
            r1 = 0.0; g1 = x; b1 = c;
        }
        else if (h >= 240 && h < 300)
        {
            r1 = x; g1 = 0.0; b1 = c;
        }
        else
        {
            r1 = c; g1 = 0.0; b1 = x;
        }

        byte r = (byte)Math.Round((r1 + m) * 255.0);
        byte g = (byte)Math.Round((g1 + m) * 255.0);
        byte b = (byte)Math.Round((b1 + m) * 255.0);

        return Color.FromArgb(255, r, g, b);
    }
}

