using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NetBlitz.Core;
using NetBlitz.Desktop;
using NetBlitz.Desktop.Controls;

[assembly: AvaloniaTestApplication(typeof(NetBlitz.Tests.TestAppBuilder))]

namespace NetBlitz.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().WithInterFont().UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

public sealed class UiTests
{
    private static void Click(MainWindow window, string name) => window.FindControl<Button>(name)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Capture(Window window, string name)
    {
        using var bitmap = window.CaptureRenderedFrame(); Assert.NotNull(bitmap);
        string? path = AppContext.BaseDirectory;
        while (path is not null && !File.Exists(Path.Combine(path, "NetBlitz.sln"))) path = Path.GetDirectoryName(path);
        Assert.NotNull(path);
        var output = Path.Combine(path, "artifacts", "screenshots"); Directory.CreateDirectory(output);
        bitmap.Save(Path.Combine(output, name + ".png"));
    }
    private static void Close(MainWindow window)
    {
        window.Close();
        foreach (var dialog in window.OwnedWindows.ToArray()) dialog.Close(true);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void MainWindowRendersClassicAndBughouseAtDesktopAndMinimumSizes()
    {
        var window = new MainWindow(new()); window.Show();
        Assert.True(window.FindControl<Button>("StartButton")!.IsEnabled);
        Capture(window, "chess-desktop");
        window.FindControl<ComboBox>("ModePicker")!.SelectedIndex = 1;
        Assert.Equal(2, window.FindControl<Grid>("BoardsHost")!.Children.Count);
        Capture(window, "bughouse-desktop");
        window.Width = 1080; window.Height = 720; Dispatcher.UIThread.RunJobs();
        Capture(window, "bughouse-minimum");
        Click(window, "StartButton");
        void MoveOnBoard(int boardIndex, string from, string to)
        {
            var pane = (BoardPane)window.FindControl<Grid>("BoardsHost")!.Children[boardIndex];
            Point Position(string square) => pane.Board.TranslatePoint(pane.Board.SquareCenter(Chess.Square(square)), window)!.Value;
            window.MouseDown(Position(from), MouseButton.Left); window.MouseUp(Position(from), MouseButton.Left);
            window.MouseDown(Position(to), MouseButton.Left); window.MouseUp(Position(to), MouseButton.Left);
        }
        MoveOnBoard(0, "e2", "e4");
        MoveOnBoard(1, "d2", "d4");
        MoveOnBoard(0, "e7", "e5");
        window.FindControl<TabControl>("SidebarTabs")!.SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();
        var moves = window.FindControl<ItemsControl>("MovesList")!;
        Assert.Equal(new MoveHistoryRow(1, "e2–e4", "e7–e5"), Assert.IsType<MoveHistoryRow>(Assert.Single(moves.Items)));
        var boardPicker = window.FindControl<ComboBox>("MoveBoardPicker")!;
        Assert.True(boardPicker.IsVisible);
        boardPicker.SelectedIndex = 1;
        Assert.Equal(new MoveHistoryRow(1, "d2–d4", ""), Assert.IsType<MoveHistoryRow>(Assert.Single(moves.Items)));
        MoveOnBoard(1, "g8", "f6");
        MoveOnBoard(1, "c2", "c4");
        Assert.Equal(new[] { new MoveHistoryRow(1, "d2–d4", "Ng8–f6"), new MoveHistoryRow(2, "c2–c4", "") }, moves.Items.Cast<MoveHistoryRow>().ToArray());
        Capture(window, "bughouse-moves");
        boardPicker.SelectedIndex = 0;
        Assert.Equal(new MoveHistoryRow(1, "e2–e4", "e7–e5"), Assert.IsType<MoveHistoryRow>(Assert.Single(moves.Items)));
        Close(window);
    }

    [AvaloniaFact]
    public void RealPointerClicksAndDraggingMovePiecesAndShowHistory()
    {
        var window = new MainWindow(new()); window.Show(); Click(window, "StartButton");
        var pane = (BoardPane)window.FindControl<Grid>("BoardsHost")!.Children[0];
        Point Position(string square) => pane.Board.TranslatePoint(pane.Board.SquareCenter(Chess.Square(square)), window)!.Value;
        window.MouseDown(Position("e2"), MouseButton.Left); window.MouseUp(Position("e2"), MouseButton.Left);
        window.MouseDown(Position("e4"), MouseButton.Left); window.MouseUp(Position("e4"), MouseButton.Left);
        Assert.Equal(PieceType.Pawn, pane.Board.Board[Chess.Square("e4")]?.Type);
        Assert.Equal(Side.Black, pane.Board.Board.Turn);
        window.MouseDown(Position("e7"), MouseButton.Left); window.MouseMove(Position("e5")); window.MouseUp(Position("e5"), MouseButton.Left);
        Assert.Equal(PieceType.Pawn, pane.Board.Board[Chess.Square("e5")]?.Type);
        var moves = window.FindControl<ItemsControl>("MovesList")!;
        var firstRow = Assert.IsType<MoveHistoryRow>(Assert.Single(moves.Items));
        Assert.Equal(new MoveHistoryRow(1, "e2–e4", "e7–e5"), firstRow);
        Capture(window, "chess-playing");
        window.MouseDown(Position("g1"), MouseButton.Left); window.MouseUp(Position("g1"), MouseButton.Left);
        window.MouseDown(Position("f3"), MouseButton.Left); window.MouseUp(Position("f3"), MouseButton.Left);
        Assert.Equal(new MoveHistoryRow(2, "Ng1–f3", ""), moves.Items.Cast<MoveHistoryRow>().Last());
        window.MouseDown(Position("b8"), MouseButton.Left); window.MouseUp(Position("b8"), MouseButton.Left);
        window.MouseDown(Position("c6"), MouseButton.Left); window.MouseUp(Position("c6"), MouseButton.Left);
        Assert.Equal(new[] { firstRow, new MoveHistoryRow(2, "Ng1–f3", "Nb8–c6") }, moves.Items.Cast<MoveHistoryRow>().ToArray());
        window.FindControl<TabControl>("SidebarTabs")!.SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();
        Assert.False(window.FindControl<ComboBox>("MoveBoardPicker")!.IsVisible);
        Capture(window, "chess-moves"); Close(window);
    }

    [AvaloniaFact]
    public void KeyboardCanSelectAndMoveAndEscapeCancels()
    {
        var board = new BoardControl { CanPlay = true, Width = 480, Height = 480 };
        Move? requested = null; board.MoveRequested += move => requested = move;
        var window = new Window { Content = board }; window.Show(); board.Focus();
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None); window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None); window.KeyReleaseQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Assert.Null(requested);
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None); window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        window.KeyPressQwerty(PhysicalKey.ArrowUp, RawInputModifiers.None); window.KeyReleaseQwerty(PhysicalKey.ArrowUp, RawInputModifiers.None);
        window.KeyPressQwerty(PhysicalKey.ArrowUp, RawInputModifiers.None); window.KeyReleaseQwerty(PhysicalKey.ArrowUp, RawInputModifiers.None);
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None); window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Assert.Equal(Move.Parse("e2", "e4"), requested); window.Close();
    }

    [AvaloniaFact]
    public void PocketButtonCanSelectAPieceAndRequestADrop()
    {
        var match = new GameMatch(GameMode.Bughouse); match.Start();
        match.Boards[0].AddToPocket(Side.White, new(PieceType.Knight, Side.White));
        var pane = new BoardPane(0) { Width = 550, Height = 750 };
        pane.Update(match.Snapshot(), null, -1, new(), false, true);
        var window = new Window { Content = pane, Width = 550, Height = 750 }; window.Show();
        Move? requested = null; pane.Board.MoveRequested += move => requested = move;
        var drop = pane.GetVisualDescendants().OfType<Button>().Single(b => b.IsEnabled);
        drop.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var point = pane.Board.TranslatePoint(pane.Board.SquareCenter(Chess.Square("e4")), window)!.Value;
        window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
        Assert.Equal(Move.Place(PieceType.Knight, "e4"), requested); window.Close();
    }
}


