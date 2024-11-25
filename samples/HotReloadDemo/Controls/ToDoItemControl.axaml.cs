using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using HotAvalonia;

namespace HotReloadDemo.Controls;

public partial class ToDoItemControl : UserControl
{
    public ToDoItemControl()
    {
        InitializeComponent();
        Initialize();
    }

    [AvaloniaHotReload]
    private void Initialize()
    {
        // Let's pretend that we did something very important here.
        int hashCode = GetHashCode();
        Debug.WriteLine("Initializing {0}#{1}...", this, hashCode);
    }

    private void CheckBox_Click(object? sender, RoutedEventArgs e)
    {
        int hashCode = GetHashCode();
        Debug.WriteLine("Clicked {0}#{1}", this, hashCode);
    }
}
