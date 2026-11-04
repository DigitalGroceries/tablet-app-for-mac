using System.Collections.Generic;
using System.Threading.Tasks;
using OpenTabletArtist.Services;
using OpenTabletArtist.ViewModels;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>Item 15 (#26): unsaved edits are resolved before an import is written, never after.</summary>
public class ImportUnsavedEditsTests
{
    static SettingsBundle.Bundle Bundle() => new();

    [Fact]
    public async Task UnsavedEdits_AreResolvedBeforeTheImportIsWritten()
    {
        var order = new List<string>();
        var vm = new BackupViewModel(null,
            reloadDriver: () => { order.Add("restart"); return Task.CompletedTask; },
            resolveUnsaved: () => { order.Add("resolve"); return Task.FromResult(true); })
        {
            ApplyOverride = _ => { order.Add("apply"); return "/tmp/safety.zip"; },
            Pending = Bundle(),
        };
        await vm.ApplyImportCommand.ExecuteAsync(null);
        Assert.Equal(new[] { "resolve", "apply", "restart" }, order);
    }

    [Fact]
    public async Task Cancel_ImportsNothing()
    {
        bool applied = false, restarted = false;
        var vm = new BackupViewModel(null,
            reloadDriver: () => { restarted = true; return Task.CompletedTask; },
            resolveUnsaved: () => Task.FromResult(false))
        {
            ApplyOverride = _ => { applied = true; return "x"; },
            Pending = Bundle(),
        };
        await vm.ApplyImportCommand.ExecuteAsync(null);
        Assert.False(applied);
        Assert.False(restarted);
        Assert.NotNull(vm.Pending);   // still there to try again
    }
}
