using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using NetBlitz.Core;

namespace NetBlitz.Desktop;

internal static class Dialogs
{
    private static (Window Window, StackPanel Content) Create(string title, double width = 450)
    {
        var content = new StackPanel { Margin = new(24), Spacing = 14 };
        var window = new Window { Title = title, Width = width, SizeToContent = SizeToContent.Height,
            CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new ScrollViewer { Content = content, MaxHeight = 760 } };
        content.Children.Add(new TextBlock { Text = title, FontSize = 23, FontWeight = Avalonia.Media.FontWeight.SemiBold });
        return (window, content);
    }
    private static void Label(StackPanel panel, string label, Control field)
    {
        panel.Children.Add(new TextBlock { Text = label, FontSize = 12 }); panel.Children.Add(field);
    }
    public static async Task<bool> Confirm(Window owner, string title, string message)
    {
        var (window, content) = Create(title);
        content.Children.Add(new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        var no = new Button { Content = "Cancel" }; no.Click += (_, _) => window.Close(false);
        var yes = new Button { Content = "Continue" }; yes.Classes.Add("primary"); yes.Click += (_, _) => window.Close(true);
        buttons.Children.Add(no); buttons.Children.Add(yes); content.Children.Add(buttons);
        return await window.ShowDialog<bool>(owner);
    }
    public static async Task<(string Name, string Host, int Port)?> Connection(Window owner, Preferences preferences, bool hosting)
    {
        var (window, content) = Create(hosting ? "Host a room" : "Join a room");
        var name = new TextBox { Text = preferences.Nickname, MaxLength = 24 };
        var host = new TextBox { Text = preferences.Host };
        var port = new NumericUpDown { Value = preferences.Port, Minimum = 1, Maximum = 65535, FormatString = "0" };
        Label(content, "Your nickname", name);
        if (!hosting) Label(content, "Host address or hostname", host);
        Label(content, "TCP port", port);
        content.Children.Add(new TextBlock { Text = hosting
            ? "Share your LAN address and port with the other players. Allow Net Blitz through your firewall on trusted networks."
            : "Use the same game mode as the host. Both players need the Avalonia edition.",
            FontSize = 12, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        var error = new TextBlock { Foreground = NetBlitzTheme.Error, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        content.Children.Add(error);
        var button = new Button { Content = hosting ? "Create room" : "Connect", HorizontalAlignment = HorizontalAlignment.Stretch };
        button.Classes.Add("primary");
        button.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(name.Text) || name.Text.Any(char.IsControl) ||
                (!hosting && string.IsNullOrWhiteSpace(host.Text))) { error.Text = "Enter a nickname and a valid address."; return; }
            window.Close((name.Text.Trim(), host.Text?.Trim() ?? "127.0.0.1", (int)(port.Value ?? 5010)));
        };
        content.Children.Add(button);
        return await window.ShowDialog<(string, string, int)?>(owner);
    }

    public static async Task<Preferences?> Settings(Window owner, Preferences preferences, GameSettings settings, bool readOnly)
    {
        var (window, content) = Create("Game settings", 490);
        var seconds = new NumericUpDown { Value = settings.InitialSeconds, Minimum = 1, Maximum = 359999, FormatString = "0", IsEnabled = !readOnly };
        var increment = new NumericUpDown { Value = settings.IncrementSeconds, Minimum = 0, Maximum = 59, FormatString = "0", IsEnabled = !readOnly };
        Label(content, "Starting time in seconds (300 = 5 minutes)", seconds);
        Label(content, "Increment per move in seconds", increment);
        var unlimited = new CheckBox { Content = "Unlimited time", IsChecked = settings.UnlimitedTime, IsEnabled = !readOnly };
        var auto = new CheckBox { Content = "Automatic win when time runs out", IsChecked = settings.AutomaticFlag, IsEnabled = !readOnly };
        var king = new CheckBox { Content = "Allow king capture and moves into check", IsChecked = settings.CaptureKing, IsEnabled = !readOnly };
        var wait = new CheckBox { Content = "Bughouse: allow waiting for a partner’s piece", IsChecked = settings.WaitForPartnerPiece, IsEnabled = !readOnly };
        var last = new CheckBox { Content = "Highlight the last move", IsChecked = preferences.HighlightLast };
        var check = new CheckBox { Content = "Highlight check", IsChecked = preferences.ShowCheck };
        foreach (var control in new[] { unlimited, auto, king, wait, last, check }) content.Children.Add(control);
        if (readOnly) content.Children.Add(new TextBlock { Text = "Rules can only be changed by the host between games. Display options always apply locally.", TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontSize = 12 });
        var apply = new Button { Content = "Save settings", HorizontalAlignment = HorizontalAlignment.Stretch }; apply.Classes.Add("primary");
        apply.Click += (_, _) => window.Close(preferences with
        {
            HighlightLast = last.IsChecked == true, ShowCheck = check.IsChecked == true,
            Settings = settings with { InitialSeconds = (int)(seconds.Value ?? 300), IncrementSeconds = (int)(increment.Value ?? 0),
                UnlimitedTime = unlimited.IsChecked == true, AutomaticFlag = auto.IsChecked == true,
                CaptureKing = king.IsChecked == true, WaitForPartnerPiece = wait.IsChecked == true }
        });
        content.Children.Add(apply); return await window.ShowDialog<Preferences?>(owner);
    }

    public static async Task<PieceType?> Promotion(Window owner, Side side)
    {
        var (window, content) = Create("Promote your pawn", 420);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var type in new[] { PieceType.Queen, PieceType.Rook, PieceType.Bishop, PieceType.Knight })
        {
            var stack = new StackPanel();
            stack.Children.Add(new Controls.PieceIcon { Piece = new(type, side), Width = 48, Height = 48 });
            stack.Children.Add(new TextBlock { Text = type.ToString(), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center });
            var button = new Button { Content = stack, Padding = new(9) };
            button.Click += (_, _) => window.Close(type); buttons.Children.Add(button);
        }
        content.Children.Add(buttons); return await window.ShowDialog<PieceType?>(owner);
    }

    public static async Task About(Window owner)
    {
        var (window, content) = Create("Net Blitz · Avalonia edition", 570);
        content.Children.Add(new TextBlock { Text = "Original game by Yannick Létourneau and Sébastien Lachance (1999).\nOriginal Net Blitz blue palette. Cburnett vector pieces by Colin M. L. Burnett, GNU GPL version 2 or later.\nhttps://github.com/lichess-org/lila/tree/master/public/piece/cburnett\nArtwork source, license, and attribution are included under Assets/Pieces/Cburnett.", TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        content.Children.Add(new TextBlock { Text = "Bughouse rules", FontSize = 18 });
        content.Children.Add(new TextBlock { Text = "Two boards, two teams. White A partners with Black B; Black A partners with White B. Captures go to the partner’s pool. A promoted piece becomes a pawn when captured. Dropping a piece costs one turn; pawns cannot be dropped on ranks 1 or 8.\n\nA mate, resignation, captured king (if enabled), or time loss on either board ends the match. By default, players must call an opponent’s expired clock. With the original waiting rule, a position is not mate if a future dropped piece could block the check.\n\nWith two or three network players, a player without a partner controls both team seats. Choose seats before starting. Local bughouse lets you operate all four seats.\n\nDraw offers are sent with the offering player’s next move. The other team can accept; moving declines the offer.\n\nRooms use an unencrypted LAN protocol. Play with trusted people, or use a trusted VPN for remote games. This edition cannot connect to the original MFC executable.", TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontSize = 13 });
        var close = new Button { Content = "Close", HorizontalAlignment = HorizontalAlignment.Right }; close.Click += (_, _) => window.Close();
        content.Children.Add(close); await window.ShowDialog(owner);
    }
}
