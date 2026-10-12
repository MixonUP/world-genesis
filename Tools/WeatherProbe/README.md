# WeatherProbe

Чистый .NET 8 запуск тех же 13 сценариев, которые подключены к Unity EditMode через `WeatherSimulationTests.cs`. Компилирует production Simulation; не запускает Unity и не проверяет изображение/UI.

Из корня репозитория:

```powershell
dotnet restore Tools/WeatherProbe/WeatherProbe.csproj --configfile Tools/SimulationProbe/NuGet.Config
dotnet run --project Tools/WeatherProbe/WeatherProbe.csproj --no-restore -- Evidence/Simulation/weather-probe-new.json
```

Если окружение блокирует чтение глобальной NuGet-конфигурации, используйте отдельный профиль в workspace перед `restore`:

```powershell
$env:APPDATA = Join-Path (Get-Location).Path 'Builds/WeatherDotnetProfile'
```

NuGet-пакеты не требуются. Код выхода 0 означает отсутствие упавших сценариев, результат и точное количество записываются в указанный JSON. Используйте новый путь evidence для нового запуска, сохраняя предыдущие результаты.

Проверяется миграция без RNG/ID/географических изменений, атомарный отказ неверных команд, локальность дождя, снег и таяние, засуха и огонь, реальное затопление и осушение, преимущество пользовательской покраски, ураганный урон, продолжение сохранения, естественные циклы, предел 192 клеток на каждом тике, молнии при начале между секундными обновлениями и отказ повреждённых сохранений.

Итоговый прогон реализации: `Evidence/Simulation/weather-probe-final.json`, 13/13 PASS. Механика и ограничения: `Docs/Research/WEATHER_SIMULATION.md`.
