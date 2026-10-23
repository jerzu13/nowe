// Test only: runs a fake game (AcSim / AcReplay, arguments before "--") and ACCoach (arguments after "--") in ONE
// Mono process. On Linux, named shared memory ("Local\acpmf_test_*") is not shared between processes.
// Usage: mono Harness.exe AcSim.exe -- ACCoach.exe --test-shm --once --train --data <dir>
using System;
using System.Linq;
using System.Reflection;
using System.Threading;
static class Harness
{
    static void Main(string[] a)
    {
        int sep = Array.IndexOf(a, "--");
        Assembly sim = Assembly.LoadFrom(a[0]), app = Assembly.LoadFrom(a[sep + 1]);
        string[] simArgs = a.Skip(1).Take(sep - 1).ToArray(), appArgs = a.Skip(sep + 2).ToArray();
        Thread t = new Thread(() => sim.EntryPoint.Invoke(null, new object[] { simArgs })) { IsBackground = true };
        t.Start();
        Thread.Sleep(1500);
        app.EntryPoint.Invoke(null, new object[] { appArgs });
    }
}
