using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace TimeInset.Views;
/// <summary>読むだけの長い文章（同梱のライセンス表記）。ClipCompose と同じ形。</summary>
public sealed class TextWindow : Window
{
    public TextWindow(string title, string text)
    {
        Title = title;
        // 読み上げの名前はタイトルにする（指定しないと中の文章全体が名前になる）。
        AutomationProperties.SetName(this, title);
        FontFamily = new FontFamily("Consolas, Yu Gothic UI");
        Width = 640; Height = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = new TextBox
        {
            Text = text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            BorderThickness = new Thickness(0), Padding = new Thickness(14), FontSize = 13,
        };
    }
}
