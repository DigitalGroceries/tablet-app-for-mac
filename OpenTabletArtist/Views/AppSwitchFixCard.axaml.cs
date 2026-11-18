using Avalonia.Controls;
using OpenTabletArtist.ViewModels;

namespace OpenTabletArtist.Views;

public partial class AppSwitchFixCard : UserControl
{
    public AppSwitchFixCard()
    {
        InitializeComponent();
        DataContext = new AppSwitchFixViewModel();
    }
}
