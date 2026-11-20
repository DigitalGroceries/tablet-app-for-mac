using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using OpenTabletArtist.ViewModels;

namespace OpenTabletArtist.Views;

/// <summary>
/// Modal editor for a single express-key / wheel binding, with a tab per type (Keyboard / Mouse /
/// Scroll). Returns the chosen binding on Save, <see cref="AuxBinding.Unbound"/> on Clear, or null on
/// Cancel. Opened via <see cref="ShowAsync"/>.
/// </summary>
public partial class BindingEditorDialog : Window
{
    private readonly TaskCompletionSource<AuxBinding?> _resultTcs = new();

    public BindingEditorDialog() => InitializeComponent();   // designer

    public BindingEditorDialog(BindingEditorViewModel vm)
    {
        InitializeComponent();
        ShellPenFeedback.DisableOnOpen(this);
        NameMediaKeys();
        DataContext = vm;
        vm.CloseRequested += OnCloseRequested;
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                vm.CancelDialogCommand.Execute(null);
                e.Handled = true;
            }
        };
    }

    /// <summary>V29 #150: the Media keys by the same names, in the app's language, as the Touch Ring and ExpressKey rows show.</summary>
    private void NameMediaKeys()
    {
        foreach (var (radio, key) in new (RadioButton? Radio, string Key)[]
                 {
                     (MediaMute, "Mute"), (MediaVolumeDown, "VolumeDown"), (MediaVolumeUp, "VolumeUp"),
                     (MediaBrightnessDown, "BrightnessDown"), (MediaBrightnessUp, "BrightnessUp"),
                     (MediaPlayPause, "PlayPause"), (MediaPrevious, "PreviousSong"), (MediaNext, "NextSong"), (MediaStop, "StopSong"),
                 })
            if (radio != null && AuxKeyBinding.MediaKeyName(key) is { } name) radio.Content = name;
        var brightnessTip = Localizer.Te("binding.media.brightness_tip", "Like the Mac's brightness key: changes the screen you last clicked in.");
        if (MediaBrightnessDown != null) ToolTip.SetTip(MediaBrightnessDown, brightnessTip);
        if (MediaBrightnessUp != null) ToolTip.SetTip(MediaBrightnessUp, brightnessTip);
    }

    private void OnCloseRequested()
    {
        if (DataContext is BindingEditorViewModel vm)
            _resultTcs.TrySetResult(vm.Result);
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        // Complete the awaiter even when the dialog is closed via the title-bar X / Alt+F4, which
        // bypass the VM's CloseRequested path. Without this, ShowAsync's `await _resultTcs.Task` would
        // never return. TrySetResult is a no-op if the normal Save/Cancel path already set it.
        if (!_resultTcs.Task.IsCompleted && DataContext is BindingEditorViewModel vm)
            AppLog.Info($"Binding editor '{vm.Title}' closed with the window button or Alt+F4: nothing changed.");
        _resultTcs.TrySetResult((DataContext as BindingEditorViewModel)?.Result);
    }

    /// <summary>Open the editor modally over <paramref name="owner"/> and return the result.</summary>
    public static async Task<AuxBinding?> ShowAsync(Window owner, AuxBinding initial, string title)
    {
        var vm = new BindingEditorViewModel(initial, title);
        var dialog = new BindingEditorDialog(vm);
        vm.EditRadialMenu = json => RadialMenuEditorDialog.ShowAsync(owner, json, title);
        await OpenTabletArtist.Helpers.InWindowSheet.ShowDialogAsync(dialog, owner);
        return await dialog._resultTcs.Task;
    }
}
