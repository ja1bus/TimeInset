using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using TimeInset.Core;
using TimeInset.Core.Localization;

namespace TimeInset.Views;
public partial class SettingsWindow : Window
{
    private const string Site = "https://www.ja1bus.com/timeinset/";
    private const string Source = "https://github.com/ja1bus/TimeInset";
    private const int HotkeyCount = Services.HotkeyService.Count;
    private readonly AppHost host;
    private readonly ShortcutBox[] keyBoxes;
    private readonly TextBlock[] keyStatus;
    private readonly string?[] keyRejected = new string?[HotkeyCount];
    // Result of trying a newly entered key once: null = usable, otherwise the reason.
    private readonly string?[] keyProbe = new string?[HotkeyCount];
    // Display items in their current order with their ON/OFF state.
    private readonly List<DisplayItems> order = [];
    private DisplayItems shown;
    private AppSettings baseline;
    private bool baselineSpecified, loading;

    public SettingsWindow(AppHost host)
    {
        InitializeComponent();
        this.host = host;
        baseline = host.Settings; baselineSpecified = host.Model.IsSpecified;
        Title = "TimeInset — " + Strings.Settings;
        FontFamily = new FontFamily("Yu Gothic UI");
        keyBoxes = [ToggleTimerBox];
        keyStatus = [ToggleTimerStatus];

        LayoutCombo.ItemsSource = new[] { Strings.Vertical, Strings.Horizontal };
        string[] foregroundNames = [Strings.ColorWhite, Strings.ColorGray, Strings.ColorBlack, Strings.ColorYellow, Strings.ColorGreen, Strings.ColorBlue];
        ForegroundCombo.ItemsSource = AppSettings.ForegroundChoices.Select((c, i) => ColorItem(c, foregroundNames[i])).ToArray();
        string[] backgroundNames = [Strings.ColorBlack, Strings.ColorWhite, Strings.ColorGray, Strings.ColorGreen, Strings.ColorBlue];
        BackgroundCombo.ItemsSource = AppSettings.BackgroundChoices.Select((c, i) => ColorItem(c, backgroundNames[i])).ToArray();
        HourCombo.ItemsSource = Numbers(24); MinuteCombo.ItemsSource = Numbers(60); SecondCombo.ItemsSource = Numbers(60);
        VersionText.Text = Strings.Version + " " + typeof(AppHost).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0];
        SettingsPathText.Text = host.SettingsPath;

        Load(baseline, baselineSpecified);
        foreach (var c in new[] { OutlineCheck, IconsCheck, TopmostCheck })
        { c.Checked += (_, _) => Changed(); c.Unchecked += (_, _) => Changed(); }
        foreach (var s in new[] { FontSizeSlider, OpacitySlider }) s.ValueChanged += (_, _) => Changed();
        foreach (var c in new[] { LayoutCombo, ForegroundCombo, BackgroundCombo, HourCombo, MinuteCombo, SecondCombo }) c.SelectionChanged += (_, _) => Changed();
        foreach (var r in new[] { CurrentRadio, SpecifiedRadio }) r.Checked += (_, _) => Changed();
        SpecifiedDatePicker.SelectedDateChanged += (_, _) => Changed();
        for (int i = 0; i < HotkeyCount; i++)
        {
            int index = i;
            keyBoxes[i].ShortcutChanged += (_, _) =>
            {
                keyRejected[index] = null;
                var key = keyBoxes[index].Shortcut;
                keyProbe[index] = key != null && key != host.Settings.Keys[index] ? host.Hotkeys.Probe(key) : null;
                Changed();
            };
            keyBoxes[i].Rejected += (_, _) => { keyRejected[index] = Strings.InvalidHotkey; UpdateStatus(); };
            keyBoxes[i].CaptureStarted += (_, _) => { host.Hotkeys.Suspend(); SuspendNotice.Visibility = Visibility.Visible; };
            keyBoxes[i].CaptureEnded += (_, _) => Dispatcher.BeginInvoke(() =>
            {
                if (keyBoxes.Any(b => b.IsKeyboardFocusWithin)) return;
                host.Hotkeys.Resume(host.Settings); SuspendNotice.Visibility = Visibility.Collapsed; UpdateStatus();
            });
        }
        Changed();
        host.Changed += UpdateStatus;
        // AppHost resumes the shortcuts when this window closes.
        Closed += (_, _) => host.Changed -= UpdateStatus;
    }

    private static SolidColorBrush Brush(string color) => new((Color)ColorConverter.ConvertFromString(color));
    private static StackPanel ColorItem(string color, string name)
    {
        var item = new StackPanel { Orientation = Orientation.Horizontal, Tag = color };
        item.Children.Add(new Rectangle { Width = 16, Height = 14, Fill = Brush(color), Stroke = Brushes.DimGray, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
        item.Children.Add(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center });
        return item;
    }
    private static string[] Numbers(int count) => Enumerable.Range(0, count).Select(i => i.ToString("00", CultureInfo.InvariantCulture)).ToArray();
    private static int IndexOf(string[] choices, string value) =>
        Math.Max(0, Array.FindIndex(choices, c => string.Equals(c, value, StringComparison.OrdinalIgnoreCase)));
    private static string ItemName(DisplayItems kind) => kind switch { DisplayItems.Clock => Strings.Clock, DisplayItems.Date => Strings.Date, _ => Strings.Timer };

    // One row per item: check box, then up/down buttons. Rebuilt after each move.
    private void BuildItemRows()
    {
        ItemsPanel.Children.Clear();
        for (int i = 0; i < order.Count; i++)
        {
            var kind = order[i]; int index = i;
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 1) };
            var check = new CheckBox { Content = ItemName(kind), IsChecked = shown.HasFlag(kind), Width = 110 };
            check.Checked += (_, _) => { shown |= kind; Changed(); };
            check.Unchecked += (_, _) => { shown &= ~kind; Changed(); };
            row.Children.Add(check);
            row.Children.Add(MoveButton("▲", Strings.MoveUp, index > 0, () => Move(index, -1)));
            row.Children.Add(MoveButton("▼", Strings.MoveDown, index < order.Count - 1, () => Move(index, 1)));
            ItemsPanel.Children.Add(row);
        }
    }
    private static Button MoveButton(string text, string tip, bool enabled, Action action)
    {
        var b = new Button { Content = text, ToolTip = tip, IsEnabled = enabled, Width = 28, Height = 24, Margin = new Thickness(4, 0, 0, 0), FontSize = 10 };
        System.Windows.Automation.AutomationProperties.SetName(b, tip);
        b.Click += (_, _) => action();
        return b;
    }
    private void Move(int index, int delta)
    {
        (order[index], order[index + delta]) = (order[index + delta], order[index]);
        BuildItemRows();
        Changed();
    }

    private void Load(AppSettings s, bool specified)
    {
        loading = true;
        order.Clear(); order.AddRange(s.OrderedItems); shown = s.Items;
        BuildItemRows();
        LayoutCombo.SelectedIndex = s.Layout == DisplayLayout.Horizontal ? 1 : 0;
        FontSizeSlider.Value = s.FontSize;
        OpacitySlider.Value = s.BackgroundOpacity;
        ForegroundCombo.SelectedIndex = IndexOf(AppSettings.ForegroundChoices, s.Foreground);
        BackgroundCombo.SelectedIndex = IndexOf(AppSettings.BackgroundChoices, s.Background);
        OutlineCheck.IsChecked = s.Outline; IconsCheck.IsChecked = s.Icons;
        TopmostCheck.IsChecked = s.Topmost;
        (specified ? SpecifiedRadio : CurrentRadio).IsChecked = true;
        SpecifiedDatePicker.SelectedDate = s.SpecifiedDate.Date;
        HourCombo.SelectedIndex = s.SpecifiedDate.Hour; MinuteCombo.SelectedIndex = s.SpecifiedDate.Minute; SecondCombo.SelectedIndex = s.SpecifiedDate.Second;
        for (int i = 0; i < HotkeyCount; i++) keyBoxes[i].Shortcut = s.Keys[i];
        loading = false;
    }

    // Returns the edited settings, or throws FormatException with a message for the user.
    private AppSettings Read()
    {
        if (SpecifiedDatePicker.SelectedDate is not { } day || day.Year is < 2000 or > 2099) throw new FormatException(Strings.SpecifiedRange);
        var date = day.Date.Add(new TimeSpan(Math.Max(0, HourCombo.SelectedIndex), Math.Max(0, MinuteCombo.SelectedIndex), Math.Max(0, SecondCombo.SelectedIndex)));
        var keys = keyBoxes.Select(b => b.Shortcut).ToArray();
        if (keys.Where(k => k != null).Distinct().Count() != keys.Count(k => k != null)) throw new FormatException(Strings.InvalidHotkey);
        if (keyProbe.FirstOrDefault(e => e != null) is { } unusable) throw new FormatException(unusable);
        return (baseline with
        {
            Layout = LayoutCombo.SelectedIndex == 1 ? DisplayLayout.Horizontal : DisplayLayout.Vertical,
            Order = AppSettings.FormatOrder(order),
            FontSize = (int)Math.Round(FontSizeSlider.Value),
            BackgroundOpacity = (int)Math.Round(OpacitySlider.Value),
            Foreground = AppSettings.ForegroundChoices[Math.Max(0, ForegroundCombo.SelectedIndex)],
            Background = AppSettings.BackgroundChoices[Math.Max(0, BackgroundCombo.SelectedIndex)],
            Outline = OutlineCheck.IsChecked == true, Icons = IconsCheck.IsChecked == true,
            Topmost = TopmostCheck.IsChecked == true,
            SpecifiedDate = date,
        }).WithKeys(keys).WithItems(shown);
    }

    private void Changed()
    {
        FontSizeValue.Text = ((int)Math.Round(FontSizeSlider.Value)).ToString(CultureInfo.InvariantCulture);
        OpacityValue.Text = $"{(int)Math.Round(OpacitySlider.Value)}%";
        SpecifiedPanel.IsEnabled = SpecifiedRadio.IsChecked == true;
        if (loading) return;
        bool valid; AppSettings? draft = null;
        try { draft = Read(); valid = true; ErrorText.Visibility = Visibility.Collapsed; }
        catch (FormatException ex) { valid = false; ErrorText.Text = ex.Message; ErrorText.Visibility = Visibility.Visible; }
        if (draft != null)
        {
            var edited = draft; var before = baseline;
            host.SetPreview(current => AppSettings.Merge(before, edited, current));
        }
        OkButton.IsEnabled = valid;
        ApplyButton.IsEnabled = valid && (draft != baseline || (SpecifiedRadio.IsChecked == true) != baselineSpecified);
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        NoticeText.Text = host.Notice == null ? "" : Strings.Warning + ": " + host.Notice;
        NoticeText.Visibility = host.Notice == null ? Visibility.Collapsed : Visibility.Visible;
        for (int i = 0; i < HotkeyCount; i++)
        {
            var key = keyBoxes[i].Shortcut;
            bool saved = key == host.Settings.Keys[i];
            string? error = keyRejected[i] ?? (saved ? host.Hotkeys.Errors[i] : keyProbe[i]);
            string text; Brush color = Brushes.DimGray;
            if (error != null) { text = error; color = Brushes.Firebrick; }
            else if (key == null) text = saved ? Strings.Unassigned : Strings.HotkeyWillUnassign;
            else if (saved) { text = Format(Strings.HotkeyRegisteredFormat, key); color = Brushes.ForestGreen; }
            else text = Format(Strings.HotkeyAvailableFormat, key);
            keyStatus[i].Text = text; keyStatus[i].Foreground = color;
            keyStatus[i].FontWeight = error != null ? FontWeights.SemiBold : FontWeights.Normal;
        }
    }

    private static string Format(string format, object value) => string.Format(CultureInfo.InvariantCulture, format, value);
    private bool Apply()
    {
        AppSettings edited;
        try { edited = Read(); }
        catch (FormatException ex) { MessageBox.Show(this, ex.Message, Strings.Error, MessageBoxButton.OK, MessageBoxImage.Warning); return false; }
        bool specified = SpecifiedRadio.IsChecked == true;
        if (!host.ApplyEdited(baseline, edited, specified, specified != baselineSpecified)) return false;
        baseline = edited; baselineSpecified = specified;
        Changed();
        return true;
    }

    private void OnOk(object sender, RoutedEventArgs e) { if (!ApplyButton.IsEnabled || Apply()) Close(); }
    private void OnApply(object sender, RoutedEventArgs e) => Apply();
    private void OnCancel(object sender, RoutedEventArgs e) => Close();
    private void OnClearHotkey(object sender, RoutedEventArgs e) => ToggleTimerBox.Shortcut = null;
    private void OnResetDefaults(object sender, RoutedEventArgs e)
    {
        AppSettings current;
        try { current = Read(); } catch (FormatException) { current = baseline; }
        Load(current.WithDefaultAppearance(), SpecifiedRadio.IsChecked == true);
        Changed();
    }
    private void OnOpenWebsite(object sender, RoutedEventArgs e) => host.OpenUrl(Site, this);
    private void OnOpenManual(object sender, RoutedEventArgs e) => host.OpenUrl(Site + "manual/", this);
    private void OnOpenPrivacy(object sender, RoutedEventArgs e) => host.OpenUrl(Site + "privacy/", this);
    private void OnOpenSource(object sender, RoutedEventArgs e) => host.OpenUrl(Source, this);
    private void OnShowLicense(object sender, RoutedEventArgs e) => host.ShowBundledText("LICENSE", Strings.AboutLicenseLink, this);
    private void OnShowNotices(object sender, RoutedEventArgs e) => host.ShowBundledText("THIRD-PARTY-NOTICES.md", Strings.AboutNoticesLink, this);
}
