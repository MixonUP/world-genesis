# FaunaBalanceProbe

Чистая .NET8 проверка тех же Simulation исходников и shared NUnit-сценариев. Не запускает Unity и не измеряет native FPS.

```powershell
dotnet restore Tools/FaunaBalanceProbe/FaunaBalanceProbe.csproj --configfile Tools/FaunaBalanceProbe/NuGet.Config
dotnet run --project Tools/FaunaBalanceProbe --no-restore -- --tests Evidence/Simulation/fauna-placement-tests.json
dotnet run --project Tools/FaunaBalanceProbe --no-restore -- Evidence/Simulation/fauna-after-0_9.json 1200
```

В данной рабочей среде использован существующий локальный `Tools/ProductionProbe/obj/project.assets.json` через `dotnet build --no-restore -p:ProjectAssetsFile=<absolute path>`; дополнительных библиотек нет.

Исходную Simulation0.8 перед любыми правками скопировали в `Builds/FaunaBaselineSources/Simulation`. Для воспроизведения baseline:

```powershell
dotnet build Tools/FaunaBalanceProbe/FaunaBalanceProbe.csproj --no-restore -p:EnableFaunaTests=false -p:SimulationSourceRoot=../../Builds/FaunaBaselineSources/Simulation
dotnet Tools/FaunaBalanceProbe/bin/Debug/net8.0/FaunaBalanceProbe.dll Evidence/Simulation/fauna-baseline-repeat.json 1200 Builds/FaunaBaselineSources/Simulation
```

Это отдельная копия исходников, не альтернативная реализация механик. `Builds` исключён из Git; baseline можно восстановить из опубликованного0.8. Сравнение выполняет одни команды на3seed Island128×128: нет отладочной выдачи животных/ресурсов/технологий/профессий и нет жителей; только естественная экология и обычные погода/законы. Каждые60с записывается численность, рождения, взрослое пополнение и смерти. Начальные IDs животных отсутствуют; новые IDs классифицируются по фактическому возрасту<0.1. Ограничение120сек реального времени наseed отмечает неполный опыт как failure. Отчёт сохраняет исходные SHA256 и проверяет их неизменность во время выполнения.

Финальный0.9: `fauna-placement-final.json` —38/38 PASS (16 новых+22 прежних); `fauna-after-0_9-final.json` —3×1200с, все инварианты и неизменность исходников PASS. В shared входят WildlifeSimulationTests/AgricultureTests; их возраст/интервалы подготовлены по явному каталогу, утверждения реальных пар/корма/груза сохранены.
