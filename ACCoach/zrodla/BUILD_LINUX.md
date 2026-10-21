# Budowanie bez Windows (opcjonalne)

Na Windows wystarczy `build.bat`. Na Linuksie z Mono (`mono-devel`) potrzebny jest
tylko plik referencyjny `System.Speech.dll` (Mono go nie ma) – np. skopiowany z
`C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF\System.Speech.dll`:

    mcs -codepage:65001 -optimize -target:exe -sdk:4.8 -out:../ACCoach.exe \
      -r:System.Core.dll -r:System.Speech.dll -r:System.Web.Extensions.dll \
      -resource:src/map_template.html,ACCoach.map_template.html src/*.cs
