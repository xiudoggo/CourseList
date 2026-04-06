using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using Windows.Foundation;

namespace CourseList.Helpers.Ui;

/// <summary>
/// Left-to-right wrap for <see cref="ItemsRepeater"/> with measured child widths (not uniform columns).
/// </summary>
public sealed class FlowWrapLayout : NonVirtualizingLayout
{
    public static readonly DependencyProperty HorizontalSpacingProperty =
        DependencyProperty.Register(nameof(HorizontalSpacing), typeof(double), typeof(FlowWrapLayout),
            new PropertyMetadata(6d));

    public static readonly DependencyProperty VerticalSpacingProperty =
        DependencyProperty.Register(nameof(VerticalSpacing), typeof(double), typeof(FlowWrapLayout),
            new PropertyMetadata(4d));

    public double HorizontalSpacing
    {
        get => (double)GetValue(HorizontalSpacingProperty);
        set => SetValue(HorizontalSpacingProperty, value);
    }

    public double VerticalSpacing
    {
        get => (double)GetValue(VerticalSpacingProperty);
        set => SetValue(VerticalSpacingProperty, value);
    }

    protected override Size MeasureOverride(NonVirtualizingLayoutContext context, Size availableSize)
    {
        var children = context.Children;
        if (children == null || children.Count == 0)
            return new Size(0, 0);

        double x = 0;
        double y = 0;
        double lineHeight = 0;
        double maxUsedWidth = 0;
        var childConstraint = new Size(double.PositiveInfinity, double.PositiveInfinity);
        double maxWidth = double.IsInfinity(availableSize.Width) ? double.PositiveInfinity : availableSize.Width;

        for (int i = 0; i < children.Count; i++)
        {
            var element = children[i];
            element.Measure(childConstraint);
            Size d = element.DesiredSize;

            if (x > 0 && x + HorizontalSpacing + d.Width > maxWidth)
            {
                x = 0;
                y += lineHeight + VerticalSpacing;
                lineHeight = 0;
            }

            if (x > 0)
                x += HorizontalSpacing;

            x += d.Width;
            lineHeight = Math.Max(lineHeight, d.Height);
            maxUsedWidth = Math.Max(maxUsedWidth, x);
        }

        y += lineHeight;
        double width = double.IsInfinity(availableSize.Width) ? maxUsedWidth : Math.Min(maxUsedWidth, availableSize.Width);
        return new Size(width, y);
    }

    protected override Size ArrangeOverride(NonVirtualizingLayoutContext context, Size finalSize)
    {
        var children = context.Children;
        if (children == null || children.Count == 0)
            return finalSize;

        double x = 0;
        double y = 0;
        double lineHeight = 0;
        double maxWidth = finalSize.Width;

        for (int i = 0; i < children.Count; i++)
        {
            var element = children[i];
            Size d = element.DesiredSize;

            if (x > 0 && x + HorizontalSpacing + d.Width > maxWidth)
            {
                y += lineHeight + VerticalSpacing;
                x = 0;
                lineHeight = 0;
            }

            if (x > 0)
                x += HorizontalSpacing;

            element.Arrange(new Rect(x, y, d.Width, d.Height));
            x += d.Width;
            lineHeight = Math.Max(lineHeight, d.Height);
        }

        return finalSize;
    }
}
