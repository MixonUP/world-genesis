# AgingProbe

Чистые общие сценарии AgingTests; Unity использует те же проверки с JsonUtility. Из корня:

```powershell
$env:APPDATA = Join-Path (Get-Location) 'Builds/MarketProbeHome'
dotnet build Tools/AgingProbe -c Release --configfile Tools/AgingProbe/NuGet.Config
dotnet Tools/AgingProbe/bin/Release/net8.0/AgingProbe.dll Evidence/Simulation/aging-probe-final.json
```

8 сценариев: будущая скорость возраста вдвое меньше, старые возраста без пересчёта, Aging law, четыре расы/животные/морские экипажи, save continuation. Это не Unity и не диагностика выживания.
