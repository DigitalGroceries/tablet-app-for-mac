using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using OpenTabletArtist.Domain;
using OpenTabletArtist.ViewModels;
using OpenTabletArtist.Views;

namespace OpenTabletArtist.UiTests;

/// <summary>Build I: the user could type "5" but not "50" into the ring step box. Type into the real control.</summary>
public class RingStepEditorTests
{
    private static (Window Window, TextBox Box, RingModeRow Row, List<int> Changes) Open()
    {
        var changes = new List<int>();
        var row = new RingModeRow(1,
            new RingMode("Rotate", new AuxCombo(false, false, true, "F13"), new AuxCombo(false, false, true, "F14"), StepDegrees: 10),
            canEdit: true, editBinding: null, changed: () => { changes.Add(1); return Task.CompletedTask; });
        var editor = new RingStepEditor { DataContext = row };
        var other = new TextBox();
        var window = new Window { Content = new StackPanel { Children = { editor, other } }, Width = 600, Height = 200 };
        window.Show();
        var box = editor.GetVisualDescendants().OfType<TextBox>().First(t => t.Name == "SpeedBox");
        return (window, box, row, changes);
    }

    [AvaloniaFact]
    public void Typing50_ThenEnter_Gives50_WithOneSave()
    {
        var (window, box, row, changes) = Open();
        box.Focus();
        box.SelectAll();
        window.KeyTextInput("5");
        Assert.Empty(changes);                // nothing saved (and no row rebuilt) after the first digit
        window.KeyTextInput("0");
        Assert.Equal("50", box.Text);
        Assert.Empty(changes);
        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        Assert.Equal(50, row.Speed);          // build K: the box is the per-mode Speed
        Assert.Equal(RingMode.DegreesOfSpeed(50), row.StepDegrees);
        Assert.Single(changes);
    }

    [AvaloniaFact]
    public void TypingWithACommaAndDegreeSign_CommitsWhenFocusLeaves()
    {
        var (window, box, row, changes) = Open();
        box.Focus();
        box.SelectAll();
        window.KeyTextInput("72,4");
        Assert.Empty(changes);
        var other = window.GetVisualDescendants().OfType<TextBox>().Last();
        other.Focus();
        Assert.Equal(72, row.Speed);
        Assert.Single(changes);
    }
}
