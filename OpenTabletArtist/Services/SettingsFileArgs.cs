using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OpenTabletArtist.Services;

/// <summary>Item 26 (#28): the .tabletsettings files named on the command line, as full paths.</summary>
public static class SettingsFileArgs
{
    public static IReadOnlyList<string> From(IEnumerable<string> args, Func<string, bool>? exists = null)
    {
        exists ??= File.Exists;
        return args.Where(a => a.EndsWith("." + SettingsBundle.Extension, StringComparison.OrdinalIgnoreCase) && exists(a))
                   .Select(Path.GetFullPath)
                   .ToList();
    }
}
