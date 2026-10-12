# CaravanProbe

Общие сценарии: `WorldGenesis/Assets/_Project/Tests/EditMode/CaravanTests.cs`. В Unity они используют NUnit + JsonUtility; этот автономный .NET 8 runner — System.Text.Json. Подготовленные миры проверяют транспорт/учёт/прерывания/сохранения, а не естественный баланс.

```powershell
dotnet restore Tools/CaravanProbe/CaravanProbe.csproj --configfile Tools/CaravanProbe/NuGet.Config
dotnet run --project Tools/CaravanProbe/CaravanProbe.csproj --no-restore -- Evidence/Simulation/caravan-probe.json
```

Сторонних NuGet-пакетов нет. Если изолированная среда запрещает читать пользовательский NuGet.Config, для subprocess допустим временный APPDATA в рабочей папке; это не установка и не изменение глобального профиля. После подготовленного restore повторные проверки выполняются с `--no-restore`.
