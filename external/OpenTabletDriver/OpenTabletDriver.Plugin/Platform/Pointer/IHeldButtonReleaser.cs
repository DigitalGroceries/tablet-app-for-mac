namespace OpenTabletDriver.Plugin.Platform.Pointer
{
    /// <summary>
    /// Tablet App for Mac (#73): a pointer whose buttons stay down in the operating system until someone posts
    /// the matching "up". When the pointer is dropped (tablet unplugged, settings change, driver stopping) any
    /// button it still holds must be let go first, or the system keeps a button pressed that nobody will ever
    /// release.
    /// </summary>
    public interface IHeldButtonReleaser
    {
        /// <summary>True when the pointer has posted a button down that it hasn't let go yet.</summary>
        bool HasHeldButtons { get; }

        /// <summary>Posts an up for every button this pointer holds.</summary>
        void ReleaseHeldButtons();
    }
}
