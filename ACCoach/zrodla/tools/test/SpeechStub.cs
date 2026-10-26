// Reference-only System.Speech for building on Linux/Mono (Mono has none). Never shipped: Windows has the real
// assembly, and this stub carries the same name, version and Microsoft public key, so ACCoach.exe binds to it.
// build.sh delay-signs it with the public key taken from Mono's System.Web.Extensions.dll (same Microsoft key).
using System;
using System.Globalization;
using System.Reflection;
[assembly: AssemblyVersion("4.0.0.0")]
namespace System.Speech.Synthesis
{
    public enum SynthesizerState { Ready, Speaking, Paused }
    public class VoiceInfo { public string Name { get { return null; } } public CultureInfo Culture { get { return null; } } }
    public class InstalledVoice { public bool Enabled { get { return false; } set { } } public VoiceInfo VoiceInfo { get { return null; } } }
    public class Prompt { }
    public sealed class SpeechSynthesizer : IDisposable
    {
        public void SetOutputToDefaultAudioDevice() { }
        public System.Collections.ObjectModel.ReadOnlyCollection<InstalledVoice> GetInstalledVoices() { return new System.Collections.ObjectModel.ReadOnlyCollection<InstalledVoice>(new InstalledVoice[0]); }
        public void SelectVoice(string name) { }
        public int Rate { get { return 0; } set { } }
        public int Volume { get { return 0; } set { } }
        public VoiceInfo Voice { get { return new VoiceInfo(); } }
        // "speaks" for as long as a real voice would (about 14 characters a second at rate 0), so tests see the
        // queue wait for the voice like on Windows
        DateTime until;
        public SynthesizerState State { get { return DateTime.Now < until ? SynthesizerState.Speaking : SynthesizerState.Ready; } }
        public Prompt SpeakAsync(string t) { until = DateTime.Now.AddSeconds(t.Length / 14.0); return null; }
        public void Speak(string t) { }
        public void SpeakAsyncCancelAll() { until = DateTime.MinValue; }
        public void Dispose() { }
    }
}
