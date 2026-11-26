using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>Digital Groceries' rule (team rule 3.11; 26 Nov 2026): this repository and the website made from it are public,
/// and the owner's own name is never written in them: not in a note, a test, a script, a file name or a page. Where a note
/// needs to say who asked for something, it says "Digital Groceries". The name had crept into the notes of about 200
/// files; this test fails if it comes back anywhere. (It is not written here either: it is put together from three
/// letters.) Commit messages are not files, so nothing here can check them: write "Digital Groceries" there too.</summary>
public class OwnerNameTests
{
    static readonly string Name = new(new[] { (char)90, (char)97, (char)107 });

    /// <summary>Folders that are not part of the repository's own files (build output, tools' own folders).</summary>
    static readonly string[] SkippedFolders = { ".git", "bin", "obj", "node_modules", "TestResults", ".vs", ".idea", ".vscode", "dist", "artifacts" };

    static readonly string[] NotText =
    {
        ".png", ".jpg", ".jpeg", ".webp", ".gif", ".ico", ".icns", ".pdf", ".zip", ".dmg", ".dll", ".so", ".dylib", ".ttf", ".otf",
        ".woff", ".woff2", ".psd", ".snk", ".pfx", ".bin", ".exe", ".nupkg", ".gz", ".tar", ".pdb",
    };

    /// <summary>A place that may keep the word, as "path:line text" (for example another person of that name in the
    /// credits of a library we ship). Empty: there is none today.</summary>
    static readonly string[] Allowed = { };

    static string Root()
    {
        var d = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "site", "build_site.py"))) d = d.Parent;
        Assert.NotNull(d);
        return d!.FullName;
    }

    static IEnumerable<string> Files(string dir)
    {
        foreach (var f in Directory.EnumerateFiles(dir))
            if (!NotText.Contains(Path.GetExtension(f).ToLowerInvariant())) yield return f;
        foreach (var sub in Directory.EnumerateDirectories(dir))
            if (!SkippedFolders.Contains(Path.GetFileName(sub)))
                foreach (var f in Files(sub)) yield return f;
    }

    /// <summary>True when the text names the owner: the name as a word or inside a longer name written in code
    /// ("...Tablet", "Is...sLine"), in capitals, or in small letters as a word of its own (a folder name). Not when the three
    /// letters only happen to sit inside a long run of letters and digits, such as a checksum, and not inside another
    /// word in small letters (Swahili "zako" means "your").</summary>
    internal static bool Mentions(string text, string name)
    {
        foreach (Match m in Regex.Matches(text, Regex.Escape(name) + "|" + Regex.Escape(name.ToUpperInvariant())))
        {
            int a = m.Index, b = m.Index + m.Length;
            while (a > 0 && IsRun(text[a - 1])) a--;
            while (b < text.Length && IsRun(text[b])) b++;
            if (b - a < 40) return true;          // a word or a name in code, not a checksum
        }
        return Regex.IsMatch(text, "(?<![A-Za-z0-9])" + Regex.Escape(name.ToLowerInvariant()) + "s?(?![A-Za-z0-9])");
    }
    static string Blank(string place) => Regex.Replace(place, Regex.Escape(Name), "[the name]", RegexOptions.IgnoreCase);
    static bool IsRun(char c) => char.IsAsciiLetterOrDigit(c) || c is '+' or '/' or '=';

    [Fact]
    public void No_file_of_the_repository_names_the_owner()
    {
        var root = Root();
        var found = new List<string>();
        int read = 0;
        foreach (var file in Files(root))
        {
            var rel = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (Mentions(Path.GetFileName(file), Name)) found.Add(rel + " (the file's name)");
            string[] lines;
            try
            {
                if (new FileInfo(file).Length > 20_000_000) continue;
                lines = File.ReadAllLines(file);
            }
            catch (IOException) { continue; }
            catch (System.UnauthorizedAccessException) { continue; }
            read++;
            for (int i = 0; i < lines.Length; i++)
                if (lines[i].IndexOf('\0') < 0 && Mentions(lines[i], Name) && !Allowed.Contains(rel + ":" + lines[i].Trim()))
                    found.Add(rel + ":" + (i + 1));
        }
        Assert.True(read > 500, $"Only {read} files were read under {root}: the search is not looking at the repository.");
        // The places are listed with the name itself blanked out: this message ends up in GitHub's public build log.
        Assert.True(found.Count == 0, "The owner's name is written in " + found.Count + " place(s). Write \"Digital Groceries\" instead (team rule 3.11):\n  "
            + string.Join("\n  ", found.Take(60).Select(Blank)));
    }

    [Fact]
    public void The_search_finds_the_name_where_it_matters_and_only_there()
    {
        var n = Name;
        Assert.True(Mentions($"// ({n}, 26 Oct 2026)", n));
        Assert.True(Mentions($"{n}'s rule", n));
        Assert.True(Mentions($"internal static class {n}Tablet", n));
        Assert.True(Mentions($"public void Wording_Is{n}sLine()", n));
        Assert.True(Mentions($"({n.ToUpperInvariant()}-LISTS L6.2)", n));
        Assert.True(Mentions($"HomeDir = () => \"/Users/{n.ToLowerInvariant()}\";", n));
        Assert.False(Mentions("picha zako na kumbukumbu za programu", n));                       // Swahili: "your pictures"
        Assert.False(Mentions("\"contentHash\": \"q8x" + n + "T9vYb3KpLm2WnR5sD7fG1hJ4kZ6cV8bN0mQwErTyUiOp==\"", n));   // inside a checksum
        Assert.False(Mentions("Digital Groceries, 26 Oct 2026", n));
    }
}
