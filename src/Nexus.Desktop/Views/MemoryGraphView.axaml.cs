using Avalonia.Controls;
using Nexus.Desktop.ViewModels;

namespace Nexus.Desktop.Views;

public partial class MemoryGraphView : UserControl
{
    private MemoryGraphViewModel? _subscribedVm;

    public MemoryGraphView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_subscribedVm is not null)
        {
            _subscribedVm.LayoutUpdated -= OnLayoutUpdated;
            _subscribedVm = null;
        }

        if (DataContext is MemoryGraphViewModel vm)
        {
            _subscribedVm = vm;
            vm.LayoutUpdated += OnLayoutUpdated;
            _ = vm.LoadGraphAsync();
        }
    }

    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        GraphCanvasControl?.InvalidateVisual();
    }
}
