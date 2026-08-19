using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;

namespace GDMENUCardManager
{
    // Highlights every occurrence of Query inside a TextBlock. Set SourceText
    // instead of Text. The text renders as inline runs with the matched
    // segments given a yellow background.
    public static class HighlightBehavior
    {
        private static readonly Brush MatchBrush;

        static HighlightBehavior()
        {
            var brush = new SolidColorBrush(Color.FromRgb(0xFF, 0xE0, 0x66));
            brush.Freeze();
            MatchBrush = brush;
        }

        public static readonly DependencyProperty SourceTextProperty = DependencyProperty.RegisterAttached(
            "SourceText", typeof(string), typeof(HighlightBehavior), new PropertyMetadata(null, OnChanged));

        public static readonly DependencyProperty QueryProperty = DependencyProperty.RegisterAttached(
            "Query", typeof(string), typeof(HighlightBehavior), new PropertyMetadata(null, OnChanged));

        public static void SetSourceText(DependencyObject obj, string value) => obj.SetValue(SourceTextProperty, value);
        public static string GetSourceText(DependencyObject obj) => (string)obj.GetValue(SourceTextProperty);
        public static void SetQuery(DependencyObject obj, string value) => obj.SetValue(QueryProperty, value);
        public static string GetQuery(DependencyObject obj) => (string)obj.GetValue(QueryProperty);

        private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (!(d is TextBlock tb)) return;

            var text = GetSourceText(tb) ?? string.Empty;
            var query = GetQuery(tb)?.Trim();

            tb.Inlines.Clear();
            if (string.IsNullOrEmpty(query))
            {
                tb.Inlines.Add(new Run(text));
                return;
            }

            int pos = 0;
            while (pos < text.Length)
            {
                int hit = text.IndexOf(query, pos, StringComparison.InvariantCultureIgnoreCase);
                if (hit < 0)
                {
                    tb.Inlines.Add(new Run(text.Substring(pos)));
                    break;
                }
                if (hit > pos)
                    tb.Inlines.Add(new Run(text.Substring(pos, hit - pos)));
                tb.Inlines.Add(new Run(text.Substring(hit, query.Length))
                {
                    Background = MatchBrush,
                    Foreground = Brushes.Black,
                });
                pos = hit + query.Length;
            }
        }
    }
}
