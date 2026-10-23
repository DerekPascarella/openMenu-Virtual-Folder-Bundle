using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace GDMENUCardManager
{
    // Keyboard behavior for an editable ComboBox. Typing completes the text
    // from the first item that starts with it, Up and Down step through the
    // items, Alt+Up and Alt+Down open or close the list, and Enter on an open
    // list only closes it.
    public class EditableComboBehavior : AvaloniaObject
    {
        public static readonly AttachedProperty<bool> IsEnabledProperty =
            AvaloniaProperty.RegisterAttached<EditableComboBehavior, ComboBox, bool>("IsEnabled");

        static EditableComboBehavior()
        {
            IsEnabledProperty.Changed.AddClassHandler<ComboBox>((cb, e) =>
            {
                if (!e.GetNewValue<bool>())
                    return;
                cb.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
                cb.AddHandler(InputElement.TextInputEvent, OnTextInput, RoutingStrategies.Bubble, handledEventsToo: true);
                cb.GotFocus += OnGotFocus;
            });
        }

        public static void SetIsEnabled(AvaloniaObject obj, bool value) => obj.SetValue(IsEnabledProperty, value);
        public static bool GetIsEnabled(AvaloniaObject obj) => obj.GetValue(IsEnabledProperty);

        private static void OnGotFocus(object sender, RoutedEventArgs e)
        {
            var cb = (ComboBox)sender;
            if (e.Source != cb)
                return;

            // Typing only reaches the inner text box. Focusing it synchronously
            // gets overridden by whatever just focused the ComboBox.
            Dispatcher.UIThread.Post(() =>
            {
                if (!cb.IsFocused)
                    return;
                var tb = cb.GetVisualDescendants().OfType<TextBox>().FirstOrDefault();
                if (tb != null)
                {
                    tb.Focus();
                    tb.SelectAll();
                }
            }, DispatcherPriority.Input);
        }

        // The inner text box swallows Up and Down, and an Enter left unhandled
        // commits the DataGrid cell hosting the ComboBox.
        private static void OnKeyDown(object sender, KeyEventArgs e)
        {
            var cb = (ComboBox)sender;
            if (e.Source is not TextBox tb)
                return;

            if ((e.Key == Key.Down || e.Key == Key.Up) && e.KeyModifiers == KeyModifiers.Alt)
                cb.SetCurrentValue(ComboBox.IsDropDownOpenProperty, !cb.IsDropDownOpen);
            else if (e.Key == Key.Down && e.KeyModifiers == KeyModifiers.None)
                Step(cb, tb, 1);
            else if (e.Key == Key.Up && e.KeyModifiers == KeyModifiers.None)
                Step(cb, tb, -1);
            else if (e.Key == Key.Enter && cb.IsDropDownOpen)
                cb.SetCurrentValue(ComboBox.IsDropDownOpenProperty, false);
            else
                return;

            e.Handled = true;
        }

        private static void Step(ComboBox cb, TextBox tb, int delta)
        {
            var items = cb.Items.Cast<object>().Select(i => i?.ToString()).ToList();
            int current = items.FindIndex(i => string.Equals(i, cb.Text, StringComparison.OrdinalIgnoreCase));

            // Text that matches no item sends Down to the first item and leaves Up nowhere to go.
            int next = current < 0 ? (delta > 0 ? 0 : -1) : Math.Clamp(current + delta, 0, items.Count - 1);
            if (next < 0 || next >= items.Count)
                return;

            cb.SelectedIndex = next;
            cb.SetCurrentValue(ComboBox.TextProperty, items[next]);
            tb.SelectAll();
        }

        private static void OnTextInput(object sender, TextInputEventArgs e)
        {
            var cb = (ComboBox)sender;
            if (e.Source is not TextBox tb)
                return;

            var typed = tb.Text;
            if (string.IsNullOrEmpty(typed) || tb.CaretIndex != typed.Length)
                return;

            var match = cb.Items.Cast<object>()
                .Select(i => i?.ToString())
                .FirstOrDefault(i => i != null && i.StartsWith(typed, StringComparison.OrdinalIgnoreCase));
            if (match == null)
                return;

            cb.SetCurrentValue(ComboBox.TextProperty, match);
            tb.SelectionStart = typed.Length;
            tb.SelectionEnd = match.Length;
        }
    }
}
