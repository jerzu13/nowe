using System;
using System.Speech.Synthesis;

namespace ACCoach
{
    // Text-to-speech through the Windows voices; prefers a Polish voice (e.g. Microsoft Paulina).
    public sealed class Voice : IDisposable
    {
        SpeechSynthesizer synth;
        public string VoiceName = L.T("(wyciszony)", "(muted)");

        public Voice(bool mute, int rate, string voiceName = null, int volume = 100)
        {
            if (mute) return;
            try
            {
                synth = new SpeechSynthesizer();
                synth.SetOutputToDefaultAudioDevice();
                if (!Select(voiceName))
                {
                    Console.WriteLine(L.T("Brak polskiego głosu w Windows – trener mówi domyślnym głosem.", "No English voice in Windows – the trainer speaks with the default voice."));
                    Console.WriteLine(L.T("Dodaj głos: Ustawienia > Czas i język > Mowa > Dodaj głosy > Polski, potem wybierz go w zakładce Jazda.",
                        "Add one: Settings > Time & language > Speech > Add voices > English, then pick it on the Drive tab."));
                }
                synth.Rate = Math.Max(-10, Math.Min(10, rate));
                synth.Volume = Math.Max(0, Math.Min(100, volume));
                VoiceName = synth.Voice.Name;
            }
            catch (Exception e)
            {
                Console.WriteLine(L.T("Nie udało się uruchomić głosu: ", "Could not start the voice: ") + e.Message);
                synth = null;
            }
        }

        // Picks the named voice, else the first one in the app's language; false when neither exists.
        // A named voice of another language is skipped: after switching the language the trainer would
        // otherwise read English with a Polish voice (or the other way round).
        bool Select(string voiceName)
        {
            foreach (InstalledVoice v in synth.GetInstalledVoices())
                if (!string.IsNullOrEmpty(voiceName) && v.Enabled && v.VoiceInfo.Name == voiceName && Lang(v) == L.Code)
                {
                    synth.SelectVoice(v.VoiceInfo.Name);
                    return true;
                }
            foreach (InstalledVoice v in synth.GetInstalledVoices())
                if (v.Enabled && Lang(v) == L.Code)
                {
                    synth.SelectVoice(v.VoiceInfo.Name);
                    return true;
                }
            return false;
        }

        static string Lang(InstalledVoice v) { return v.VoiceInfo.Culture != null ? v.VoiceInfo.Culture.TwoLetterISOLanguageName : ""; }

        // Live changes from the panel window. Muted keeps the console log but says nothing.
        public bool Muted;

        public void SetVoice(string voiceName)
        {
            if (synth == null) return;
            try { Select(voiceName); VoiceName = synth.Voice.Name; } catch (Exception) { }
        }

        public void SetRate(int rate)
        {
            if (synth != null) synth.Rate = Math.Max(-10, Math.Min(10, rate));
        }

        public void SetVolume(int volume)
        {
            if (synth != null) synth.Volume = Math.Max(0, Math.Min(100, volume));
        }

        public bool Busy
        {
            get { return synth != null && synth.State == SynthesizerState.Speaking; }
        }

        public void Say(string text)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine(L.T("  [GŁOS] ", "  [VOICE] ") + text);
            Console.ResetColor();
            if (synth != null && !Muted) synth.SpeakAsync(text);
        }

        public void SayAndWait(string text)
        {
            Console.WriteLine(L.T("  [GŁOS] ", "  [VOICE] ") + text);
            if (synth != null && !Muted) synth.Speak(text);
        }

        // Enabled Windows voices as "name|culture", for the settings window.
        public static System.Collections.Generic.List<string[]> Installed()
        {
            var list = new System.Collections.Generic.List<string[]>();
            try
            {
                using (SpeechSynthesizer s = new SpeechSynthesizer())
                    foreach (InstalledVoice v in s.GetInstalledVoices())
                        if (v.Enabled) list.Add(new[] { v.VoiceInfo.Name, v.VoiceInfo.Culture != null ? v.VoiceInfo.Culture.Name : "" });
            }
            catch (Exception) { }
            return list;
        }

        public void Dispose()
        {
            if (synth != null) synth.Dispose();
        }
    }
}
