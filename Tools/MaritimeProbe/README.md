# MaritimeProbe

Чистый C# runner для `MaritimeScenarios` из `Tests/EditMode/MaritimeTests.cs`. Unity запускает тот же набор assertions; этот инструмент не проверяет renderer, ввод или нативный Player.

Из корня проекта:

```powershell
dotnet restore Tools/MaritimeProbe/MaritimeProbe.csproj --configfile Tools/MaritimeProbe/NuGet.Config
dotnet run --project Tools/MaritimeProbe/MaritimeProbe.csproj --no-restore -- Evidence/Simulation/maritime-probe.json
```

В окружении с уже проверенными SDK assets без доступа к глобальному NuGet config:

```powershell
$probeAssets = (Resolve-Path Tools/ProductionProbe/obj/project.assets.json).Path
dotnet build Tools/MaritimeProbe/MaritimeProbe.csproj --no-restore --nologo -v:q "-p:ProjectAssetsFile=$probeAssets"
dotnet Tools/MaritimeProbe/bin/Debug/net8.0/MaritimeProbe.dll Evidence/Simulation/maritime-probe.json
```

Второй аргумент runner — необязательный фильтр имени сценария. Код выхода ненулевой при провале, нуле исполненных сценариев или изменении Simulation во время прогона. JSON содержит фактический список, ошибки, SHA исходников/исполняемого модуля и описание fixture-помощи.

29 сценариев покрывают маршруты/закрытое озеро, реальные экипажи и обычные береговые работы, строительство и материалы, провизию, сеть портовых складов, финансовый обмен, пассажиров/рыбу, зарубежные рейсы, боевые границы и захват, смерть/потопление, сохранение/миграцию/валидацию. Длительные естественные миры и native-проверки выполняются отдельно.

Дополнительные границы финального прохода: роль портовой конторы в автоматических поручениях при сохранении ручных команд, монеты без дробления/объединения старых дробей, отключение сети через брод и восстановление по сухой тропе либо мосту.
