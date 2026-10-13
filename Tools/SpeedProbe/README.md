# SpeedProbe — точные тики и пути

14.10.2026. Независимый от Unity .NET 8 harness для исполнения **настоящей** Simulation. Это не Player/FPS и не тест естественного роста населения.

```powershell
dotnet build Tools/SpeedProbe -c Release --configfile Tools/SpeedProbe/NuGet.Config
dotnet Tools/SpeedProbe/bin/Release/net8.0/SpeedProbe.dll --tests Evidence/Simulation/speed-correctness.json
$env:DOTNET_TieredCompilation = '0'
dotnet Tools/SpeedProbe/bin/Release/net8.0/SpeedProbe.dll Evidence/Simulation/speed-current.json WorldGenesis/Assets/_Project/Scripts/Simulation 600
```

Порядок аргументов нагрузки: receipt, каталог исходников **собранной** Simulation, число фиксированных тиков, необязательное число жителей (100 или1000; фильтр оставляет Island). Каждый из шести сценариев:128², seed1618033, Island/Archipelago/Continent, запрос100/1000 граждан четырьмя обычными командами Spawn. Реальный результат появления и выживания записывается; исходники/сборка/начальный и конечный мир получают SHA256. Нельзя указывать исходники, не соответствующие исполняемому бинарнику.

Измеряются только Step(.1) через Stopwatch, отдельно —200000 запросов GetUnit/GetBuilding и200 маршрутов. Validate находится вне таймера тиков, хотя строка allocatedBytes включает периодические валидации. Квантили тика — индексы floor(p×(N−1)); не смешивать их с native frame percentiles. `simulationSecondsPerWallSecond` — фактическое TickDelta×.1/сумма времени Step, без рендера, ввода, UI, проверок и сериализации. Настоящая скорость Player будет ниже.

Для изолированной пары старая Simulation должна находиться в отдельном неизменяемом каталоге:

```powershell
dotnet build Tools/SpeedProbe -c Release -p:IncludeCorrectness=false -p:SimulationSourceRoot=../../Builds/FaunaBaselineSources/Simulation -o Builds/SpeedBaselineBin --configfile Tools/SpeedProbe/NuGet.Config
```

`IncludeCorrectness=false` нужен старому0.8: новые проверки используют появившийся в0.9 PlacementRules. Нельзя сравнивать изменённый gameplay0.9 с0.8 и приписывать всю разницу оптимизации. В выполненном A/B на старый снимок перенесены только GridNavigation и BeginUnitBatch/EndUnitBatch в Engine; остальные исходники не меняются — точное множество различий проверяется receipt.

`Instrument.py` создаёт отдельную диагностическую копию `Builds/SpeedProfileSources` из `Builds/FaunaBaselineSources`; она оборачивает выбранные методы счётчиками inclusive wall time. Вложенные времена **не складываются**; профилирование само сильно замедляет код. Используется для поиска дорогих операций, не как результат ускорения. Не запускайте этот генератор поверх папки финального сравнения.

`Compare.py` читает три пары `speed-baseline-repeat-N.json` / `speed-optimized-repeat-N.json`, проверяет SHA итоговых миров/маршрутов, выводит медианы. Исторические receipts не перезаписывать новым поведением. После уточнения округления Mono итоговые пары имеют отдельное имя `speed-mono-optimized-repeat-N.json`; используйте параметр префикса скрипта (см. его код).

Shared проверки расположены в `SimulationPerformanceCorrectnessTests.cs`: одинаковый reference A* и540 запросов, бюджет, пересчёт при мосте/воротах/сносе/прямой правке, пожар, выход с непроходимой стартовой клетки, отсутствие кэшей в сохранении и продолжение после загрузки. Unity выполняет те же сценарии с JsonUtility. .NET PASS не заменяет Unity: именно Unity обнаружила различие промежуточного float-округления в первой реализации.

## Сохранения для native stress

```powershell
dotnet Tools/SpeedProbe/bin/Release/net8.0/SpeedProbe.dll --export-stress Evidence/Simulation/native-speed-worlds.json
```

Экспортирует `Builds/NativeSpeedWorld/island-100.save.json`, `island-1000.save.json`, `archipelago-1000.save.json` (128²/seed1618033, четыре расы поровну, Tick0). Envelope соответствует SaveStore Schema1/WorldGenesis; настоящую загрузку Unity подтверждает интегратор. До64MiB; receipt хранит хэши, команды Spawn и результаты600тиков на отдельной копии.

Это явно **controlledstress**: отключены реальные Aging/Births/Warfare/FireSpread, Weather.Automatic и NaturalWildlife. Закона отключения голода/жажды в игре нет. Поэтому с разрешения интегратора исходным жителям дан конечный Health=MaxHealth100000. Нужды, задания, DecisionTimer, пути, профессии и экономика не заморожены. Нет выдачи ресурсов, пищи, технологий или снаряжения. Настоящий native-профиль обязан записать initial/finalpopulation, даже если они изменятся.
