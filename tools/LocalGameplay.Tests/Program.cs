var checks = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception(name);
    checks++;
    Console.WriteLine($"PASS {name}");
}

var trigger = new LocalShotTrigger();
Check(trigger.SetPressed(true, 0, 0.5), "first press fires immediately");
Check(!trigger.SetPressed(true, 1, 0.5), "holding does not fire again after cooldown");
trigger.SetPressed(false, 1, 0.5);
Check(trigger.SetPressed(true, 1, 0.5), "release allows a new shot");
trigger.SetPressed(false, 1.1, 0.5);
Check(!trigger.SetPressed(true, 1.2, 0.5), "rapid clicks respect cooldown");
Check(!trigger.SetPressed(true, 2, 0.5), "a rejected press does not fire later while held");
trigger.SetPressed(false, 2, 0.5);
Check(trigger.SetPressed(true, 2, 0.5), "next press after cooldown fires");
trigger.Release();
Check(!trigger.SetPressed(true, 2.1, 0.5), "disabling cannot bypass cooldown");
trigger.Release();
Check(trigger.SetPressed(true, 2.5, 0.5), "exact cooldown boundary permits fire");
var otherPlayer = new LocalShotTrigger();
Check(otherPlayer.SetPressed(true, 2.5, 0.5), "second player has independent trigger and cooldown");
Check(!trigger.SetPressed(true, 3, 0.5), "second player does not release first player's trigger");
Console.WriteLine($"{checks} shot-trigger checks passed.");
