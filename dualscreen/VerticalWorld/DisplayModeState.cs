namespace Rgdsplus.VerticalWorld
{
    internal enum LowerScreenMode { SceneHud, ExpandedHud }

    // Mode changes are edge-triggered by SMAPI, never by per-frame button polling.
    internal sealed class DisplayModeState
    {
        internal LowerScreenMode Mode { get; private set; }
        internal int Generation { get; private set; }
        internal bool TryToggle(bool canSwitch)
        {
            if (!canSwitch) return false;
            Mode = Mode == LowerScreenMode.SceneHud ? LowerScreenMode.ExpandedHud : LowerScreenMode.SceneHud;
            Generation++;
            return true;
        }
        internal void Reset()
        {
            Mode = LowerScreenMode.SceneHud;
            Generation++;
        }
    }
}
