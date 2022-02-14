using System;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;

namespace csGameIorga;

public partial class Board : Window
{
    private readonly Messenger _formHook;
    public Board(Messenger formHook)
    {
        _formHook = formHook;
        InitializeComponent();
    }

    private bool _validate(Vector2 vec)
    {
        return vec.X >= 0 && vec.X <= this.BoardCanvas.Width && vec.Y >= 0 && vec.Y <= BoardCanvas.Height;
    }
    public string Move(Vector2 vec)
    {
        if (!_validate(vec)) return "invalid parameters";
        Canvas.SetTop(Player, vec.Y);
        Canvas.SetLeft(Player, vec.X);
        return "";
    }
    private void Window_OnClosed(object? sender, EventArgs e)
    {
        _formHook.Shutdown();
    }
}