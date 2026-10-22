using System;
using System.Speech.Synthesis;

namespace ACCoach
{
    // Text-to-speech through the Windows voices; prefers a Polish voice (e.g. Microsoft Paulina).
    public sealed class Voice : IDisposable
    {
        SpeechSynthesizer synth;
        public string VoiceName = "(wyciszony)";

        public Voice(bool mute, int rate, string voiceName = null, int volume = 100)
        {
            if (mute) return;
            try
            {
                synth = new SpeechSynthesizer();
                synth.SetOutputToDefaultAudioDevice();
                bool polish = Select(voiceName);
                if (!polish)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("Brak polskiego głosu w Windows – trener mówi domyślnym głosem.");
                    Console.WriteLine("Dodaj głos: Ustawienia > Czas i język > Mowa > Dodaj głosy > Polski,");
                    Console.WriteLine("potem uruchom ponownie trenera (albo wybierz głos w oknie Konfiguracja).");
                    Console.ResetColor();
                }
                synth.Rate = Math.Max(-10, Math.Min(10, rate));
                synth.Volume = Math.Max(0, Math.Min(100, volume));
                VoiceName = synth.Voice.Name;
            }
            catch (Exception e)
            {
                Console.WriteLine("Nie udało się uruchomić głosu: " + e.Message);
                synth = null;
            }
        }

        // Picks the named voice, else the first Polish one; false when neither exists.
        bool Select(string voiceName)
        {
            foreach (InstalledVoice v in synth.GetInstalledVoices())
                if (!string.IsNullOrEmpty(voiceName) && v.Enabled && v.VoiceInfo.Name == voiceName)
                {
                    synth.SelectVoice(v.VoiceInfo.Name);
                    return true;
                }
            foreach (InstalledVoice v in synth.GetInstalledVoices())
                if (v.Enabled && v.VoiceInfo.Culture != null && v.VoiceInfo.Culture.TwoLetterISOLanguageName == "pl")
                {
                    synth.SelectVoice(v.VoiceInfo.Name);
                    return true;
                }
            return false;
        }

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
            Console.WriteLine("  [GŁOS] " + text);
            Console.ResetColor();
            if (synth != null && !Muted) synth.SpeakAsync(text);
        }

        public void SayAndWait(string text)
        {
            Console.WriteLine("  [GŁOS] " + text);
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
