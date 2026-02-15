using Avalonia.Controls;
using WiiGSC.UI.ViewModels;

namespace WiiGSC.UI.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        
        // Pass window reference to ViewModel for file picker access
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.SetWindow(this);
        }
        
        // Also handle when DataContext changes
        DataContextChanged += (s, e) =>
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.SetWindow(this);
            }
        };
    }
}