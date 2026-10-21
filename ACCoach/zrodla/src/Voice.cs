using System;
using System.Speech.Synthesis;

namespace ACCoach
{
    // Text-to-speech through the Windows voices; prefers a Polish voice (e.g. Microsoft Paulina).
    public sealed class Voice : IDisposable
    {
        SpeechSynthesizer synth;
        public string VoiceName = "(wyciszony)";

        public Voice(bool mute, int rate)
        {
            if (mute) return;
            try
            {
                synth = new SpeechSynthesizer();
                synth.SetOutputToDefaultAudioDevice();
                bool polish = false;
                foreach (InstalledVoice v in synth.GetInstalledVoices())
                {
                    if (v.Enabled && v.VoiceInfo.Culture != null && v.VoiceInfo.Culture.TwoLetterISOLanguageName == "pl")
                    {
                        synth.SelectVoice(v.VoiceInfo.Name);
                        polish = true;
                        break;
                    }
                }
                if (!polish)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("Brak polskiego głosu w Windows – trener mówi domyślnym głosem.");
                    Console.WriteLine("Dodaj głos: Ustawienia > Czas i język > Mowa > Dodaj głosy > Polski,");
                    Console.WriteLine("potem uruchom ponownie trenera. Szczegóły w pliku JAK UZYWAC.txt.");
                    Console.ResetColor();
                }
                synth.Rate = Math.Max(-10, Math.Min(10, rate));
                VoiceName = synth.Voice.Name;
            }
            catch (Exception e)
            {
                Console.WriteLine("Nie udało się uruchomić głosu: " + e.Message);
                synth = null;
            }
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
            if (synth != null) synth.SpeakAsync(text);
        }

        public void SayAndWait(string text)
        {
            Console.WriteLine("  [GŁOS] " + text);
            if (synth != null) synth.Speak(text);
        }

        public void Dispose()
        {
            if (synth != null) synth.Dispose();
        }
    }
}
