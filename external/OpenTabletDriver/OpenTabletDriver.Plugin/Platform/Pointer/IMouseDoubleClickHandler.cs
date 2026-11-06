namespace OpenTabletDriver.Plugin.Platform.Pointer
{
    /// <summary>
    /// Posts a left-button double-click (click-count 1 then 2) at the current cursor, and exposes the
    /// screen-pixel move tolerance used when counting tip double-taps.
    /// </summary>
    public interface IMouseDoubleClickHandler
    {
        /// <summary>Maximum screen-pixel distance between two tip downs that still count as one double-click.</summary>
        int DoubleClickMoveTolerance { get; set; }

        /// <summary>Posts left down/up with click count 1, then left down/up with click count 2.</summary>
        void PostDoubleClick();
    }
}
