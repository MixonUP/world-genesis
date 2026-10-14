# ConquestProbe

13 общих сценариев ConquestTests; настоящий .NET core, Unity wrappers используют JsonUtility.

```powershell
$env:APPDATA = Join-Path (Get-Location) 'Builds/MarketProbeHome'
dotnet build Tools/ConquestProbe -c Release --configfile Tools/ConquestProbe/NuGet.Config
dotnet Tools/ConquestProbe/bin/Release/net8.0/ConquestProbe.dll Evidence/Simulation/conquest-probe-final.json
dotnet Tools/ConquestProbe/bin/Release/net8.0/ConquestProbe.dll --export
```

Экспорт создаёт четыре SaveStore-envelope в Builds/ConquestShowcase и квитанцию Evidence/Simulation/conquest-native-showcase.json. Подготовлены оружие/технологии/позиции и закон, затем только обычный Step: настоящий подход, вход, получение оружия, марш и оккупация. Это контролируемая демонстрация, не естественная стратегическая подготовка. Runtime/Player не запускается. Сборка .NET не заменяет Unity-проверку.
