# Building and testing on Linux

Windows: `build.bat` (C# compiler of .NET Framework 4.8, no Visual Studio).

Linux (Mono + NSIS; cloud sessions install them via `.claude/hooks/session-start.sh`):

    ./build.sh            # ACCoach.exe + test tools (tools/test/bin)
    ./build.sh release    # + installer and portable ZIP in <repo>/dist
    ./test.sh             # simulated session, analysis EN/PL, track map in a browser (~2.5 min)

Mono has no System.Speech: `build.sh` compiles a reference-only stub (`tools/test/SpeechStub.cs`) with the same
name and Microsoft public key, so the exe binds to the real assembly on Windows. Never ship the stub.
