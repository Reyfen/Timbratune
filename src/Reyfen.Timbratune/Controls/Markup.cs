using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace Reyfen.Timbratune.Controls;

/// <summary>
/// Tiny inline markup for TextBlocks so ported copy keeps its emphasis:
/// <c>**bold**</c> and <c>_italic_</c>. Usage: <c>controls:Markup.Text="{Binding Sub}"</c>.
/// </summary>
public static class Markup
{
    public static readonly AttachedProperty<string?> TextProperty =
        AvaloniaProperty.RegisterAttached<TextBlock, string?>("Text", typeof(Markup));

    static Markup()
    {
        TextProperty.Changed.AddClassHandler<TextBlock>((tb, e) => Apply(tb, e.NewValue as string));
    }

    public static string? GetText(TextBlock element) => element.GetValue(TextProperty);
    public static void SetText(TextBlock element, string? value) => element.SetValue(TextProperty, value);

    private static void Apply(TextBlock tb, string? text)
    {
        var inlines = new InlineCollection();
        if (!string.IsNullOrEmpty(text))
        {
            bool bold = false, italic = false;
            var buffer = new System.Text.StringBuilder();
            void Flush()
            {
                if (buffer.Length == 0) return;
                inlines.Add(new Run(buffer.ToString())
                {
                    FontWeight = bold ? FontWeight.Bold : FontWeight.Normal,
                    FontStyle = italic ? FontStyle.Italic : FontStyle.Normal,
                });
                buffer.Clear();
            }
            for (var i = 0; i < text.Length; i++)
            {
                if (text[i] == '*' && i + 1 < text.Length && text[i + 1] == '*')
                {
                    Flush();
                    bold = !bold;
                    i++;
                }
                else if (text[i] == '_' && IsItalicToggle(text, i))
                {
                    Flush();
                    italic = !italic;
                }
                else buffer.Append(text[i]);
            }
            Flush();
        }
        tb.Inlines = inlines;
    }

    // "_" toggles italics only at a word boundary, so snake_case survives.
    private static bool IsItalicToggle(string s, int i)
    {
        var before = i == 0 ? ' ' : s[i - 1];
        var after = i + 1 >= s.Length ? ' ' : s[i + 1];
        return !char.IsLetterOrDigit(before) || !char.IsLetterOrDigit(after);
    }
}
