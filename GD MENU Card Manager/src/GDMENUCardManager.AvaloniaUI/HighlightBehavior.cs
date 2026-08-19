using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace GDMENUCardManager
{
    // Highlights every occurrence of Query inside a TextBlock. Set SourceText
    // instead of Text. The text renders as inline runs with the matched
    // segments given a yellow background.
    public class HighlightBehavior : AvaloniaObject
    {
        private static readonly IBrush MatchBrush = new SolidColorBrush(Color.Parse("#FFE066"));

        public static readonly AttachedProperty<string> SourceTextProperty =
            AvaloniaProperty.RegisterAttached<HighlightBehavior, TextBlock, string>("SourceText");

        public static readonly AttachedProperty<string> QueryProperty =
            AvaloniaProperty.RegisterAttached<HighlightBehavior, TextBlock, string>("Query");

        static HighlightBehavior()
        {
            SourceTextProperty.Changed.AddClassHandler<TextBlock>((tb, _) => Rebuild(tb));
            QueryProperty.Changed.AddClassHandler<TextBlock>((tb, _) => Rebuild(tb));
        }

        public static void SetSourceText(AvaloniaObject obj, string value) => obj.SetValue(SourceTextProperty, value);
        public static string GetSourceText(AvaloniaObject obj) => obj.GetValue(SourceTextProperty);
        public static void SetQuery(AvaloniaObject obj, string value) => obj.SetValue(QueryProperty, value);
        public static string GetQuery(AvaloniaObject obj) => obj.GetValue(QueryProperty);

        private static void Rebuild(TextBlock tb)
        {
            var text = GetSourceText(tb) ?? string.Empty;
            var query = GetQuery(tb)?.Trim();

            var inlines = new InlineCollection();
            if (string.IsNullOrEmpty(query))
            {
                inlines.Add(new Run(text));
            }
            else
            {
                int pos = 0;
                while (pos < text.Length)
                {
                    int hit = text.IndexOf(query, pos, StringComparison.InvariantCultureIgnoreCase);
                    if (hit < 0)
                    {
                        inlines.Add(new Run(text.Substring(pos)));
                        break;
                    }
                    if (hit > pos)
                        inlines.Add(new Run(text.Substring(pos, hit - pos)));
                    inlines.Add(new Run(text.Substring(hit, query.Length))
                    {
                        Background = MatchBrush,
                        Foreground = Brushes.Black,
                    });
                    pos = hit + query.Length;
                }
            }
            tb.Inlines = inlines;
        }
    }
}
