using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using OpenTabletArtist.Domain.Health;
using OpenTabletArtist.Services;
using OpenTabletArtist.ViewModels;

namespace OpenTabletArtist.Views;

public partial class DashboardView : UserControl
{
    public DashboardView()
    {
        InitializeComponent();
        ApplyLanguage();
        Localizer.LanguageChanged += ApplyLanguage;
        DetachedFromLogicalTree += (_, _) => Localizer.LanguageChanged -= ApplyLanguage;
        // Hidden developer affordance: right-clicking a synthetic ("developer-induced") Needs-attention
        // card offers a Dismiss. Real warnings raise no menu, so they can't be dismissed this way.
        AddHandler(ContextRequestedEvent, OnCardContextRequested, RoutingStrategies.Bubble);
    }

    void ApplyLanguage()
    {
        Localizer.EnsureLoaded();
        if (LblYourTablets != null) LblYourTablets.Text = Localizer.T("home.your_tablets");
        // TASKS #76: the no-tablet state and the copy-settings offer.
        if (LblNoTablets != null) LblNoTablets.Text = Localizer.Te("home.no_tablets", "No tablets connected or remembered");
        if (LblNoTabletsHint != null) LblNoTabletsHint.Text = Localizer.Te("home.no_tablets_hint",
            "Connect a tablet and it appears here, or choose your model and set it up now.");
        if (ChooseTabletButton != null) ChooseTabletButton.Content = Localizer.Te("notablet.choose", "Choose your tablet");
        if (OfflineOfferCopyButton != null) OfflineOfferCopyButton.Content = Localizer.Te("offer.copy", "Copy settings");
        if (OfflineOfferNotNowButton != null) OfflineOfferNotNowButton.Content = Localizer.Te("offer.not_now", "Not now");
    }

    private void OnCardContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (e.Source is not Control src || src.DataContext is not HealthIssue { IsDeveloperInduced: true } issue)
            return;
        if (DataContext is not DashboardViewModel vm) return;

        var menu = new ContextMenu
        {
            ItemsSource = new[]
            {
                new MenuItem
                {
                    Header = "Dismiss (developer)",
                    Command = vm.DismissDeveloperIssueCommand,
                    CommandParameter = issue,
                },
            },
        };
        menu.Open(src);
        e.Handled = true;
    }

    /// <summary>Double-clicking a tablet card opens that tablet's settings — the per-card "edit" button
    /// was removed in favour of this (#tablet-card-mapping). A double-click on the trash icon is
    /// handled by the button itself and won't reach here as a card activation.</summary>
    private void OnTabletCardDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: TabletOverviewItemViewModel item } && item.OpenCommand.CanExecute(null))
        {
            item.OpenCommand.Execute(null);
            e.Handled = true;
        }
    }
}
