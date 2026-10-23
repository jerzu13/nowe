# Budowanie bez Windows (opcjonalne)

Na Windows wystarczy `build.bat`. Na Linuksie z Mono (`mono-devel`) potrzebny jest
tylko plik referencyjny `System.Speech.dll` (Mono go nie ma) – np. skopiowany z
`C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF\System.Speech.dll`:

    mcs -codepage:65001 -optimize -target:winexe -sdk:4.8 -out:../ACCoach.exe -win32icon:ACCoach.ico \
      -r:System.Core.dll -r:System.Speech.dll -r:System.Web.Extensions.dll \
      -r:System.Windows.Forms.dll -r:System.Drawing.dll \
      -resource:src/map_template.html,ACCoach.map_template.html src/*.cs

Instalator (NSIS, `apt install nsis` albo NSIS dla Windows):

    makensis ../instalator/ACCoach.nsi      # -> instalator/AC_Coach_Setup.exe
