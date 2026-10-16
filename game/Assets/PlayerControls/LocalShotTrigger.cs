// Adapted from the shooter's SingleShotMechanism, without its assembly dependencies.
public sealed class LocalShotTrigger
{
    private bool held;
    private double nextShotTime;

    public bool SetPressed(bool pressed, double time, double interval)
    {
        bool justPressed = pressed && !held;
        held = pressed;
        if (!justPressed || time < nextShotTime)
            return false;

        nextShotTime = time + System.Math.Max(0, interval);
        return true;
    }

    public void Release() => held = false;
}
