# Команды проекта

Актуальная версия: **0.5.1**, `Builds/Windows-0.5.1/WorldGenesis.exe`. Финальный snapshot — `subsistence-release-v051-r3`; результаты — [отчёт снабжения](../Docs/Development/SUBSISTENCE_REPORT.md). Исторические команды ниже сохранены; передавайте путь текущего Player явно: capture/package-скрипты всё ещё имеют старые значения по умолчанию 0.2.

Проверка снабжения: `dotnet run --project Tools/SubsistenceProbe/SubsistenceProbe.csproj -- Evidence/Simulation/subsistence-probe-final.json`. Режим `--sustain` вместо пути результата проверяет пять дополнительных хозяйственных суток в подготовленном мире; методика — [SubsistenceProbe](SubsistenceProbe/README.md).

В PowerShell из корня репозитория:

```powershell
.\Tools\Project.ps1 -Action Doctor
.\Tools\Project.ps1 -Action Validate
.\Tools\Project.ps1 -Action Import -DryRun
```

Doctor читает версии/пути. Validate проверяет структуру, Markdown-ссылки, JSON, assembly references и фиксированную версию; пишет `Evidence/project-setup-report.json`. Это проверка организации, не компиляция и не тест игры.

Импорт, подготовка сцены, тесты и сборка:

```powershell
.\Tools\Project.ps1 -Action Import
.\Tools\Project.ps1 -Action Prepare
.\Tools\Project.ps1 -Action Open
.\Tools\Project.ps1 -Action EditMode
.\Tools\Project.ps1 -Action PlayMode
.\Tools\Project.ps1 -Action Build
```

Import запускает выбранный Unity в batchmode, сохраняет журнал и проверяет exit code/ошибки. Prepare вызывает Editor.ProjectSetup.Prepare; Open намеренно открывает видимый редактор. Не запускать batch-процесс для проекта, открытого в другом Unity. После аварийного выхода старый lock удаляется только при отсутствии процессов Unity и успешном эксклюзивном открытии файла. Tests сохраняют NUnit XML; отсутствие тестов/результата или отказ считается ошибкой. `-DryRun` показывает команду без запуска.

Логи команд — `Evidence/<время>-<действие>/`. Нельзя судить о корректности игры по exit 0 при импорте. Версии пакетов закреплены, Unity создал packages-lock.json. Сырые логи остаются локально. Автоматического обновления редактора, покупок и удаления Library скрипт не выполняет.

Build вызывает Editor.ProjectSetup.BuildWindows, использует Main.unity и StandaloneWindows64, пишет Evidence/build-report.json. Результат текущей версии — Builds/Windows-0.5.1/WorldGenesis.exe вместе с соседними файлами; запуск проверяется отдельно.

`dotnet run --project Tools/SimulationProbe/SimulationProbe.csproj -c Release -- Evidence/Simulation/simulation-probe.json` проверяет настоящий Simulation под .NET 8. Это дополнительная независимая проверка, не замена Unity. NuGet.Config не содержит внешних источников; сторонних .NET-пакетов нет.

`Tools/Run-Capture.ps1 -PlayerRelativePath Builds/Windows-0.4.0/WorldGenesis.exe -Name overview -Seconds 8` открывает настоящий Player для native capture и записывает хеши исходников. Скрытый Windows Player может дать чёрный кадр, поэтому проверка намеренно видимая. `-FocusSettlement -Zoom 18` меняет только камеру; `-Power Meteor` применяет настоящую игровую команду с отдельной квитанцией автоматического сценария. Пример профиля: `-Name load-1000 -Seconds 40 -ProfileSeconds 30 -StressPopulation 1000`. Измеряется реальный Player, но не все конфигурации A11.

`Sync-ProjectKnowledge.ps1` — исполненная одноразовая идемпотентная синхронизация записи подготовки от 12.10.2026. Дополняет только карточку World_Genesis, дневник этой даты и индекс установленного vault. Это не фоновая синхронизация и не команда записи всех будущих этапов. Квитанция readback — `Evidence/obsidian-sync.json`; повтор не дублирует запись.

## Исследование процедурной генерации

`python Tools/Research/procgen_probe.py` — небольшие численные проверки из корня workspace, Python standard library, без установки зависимостей. Проверены Python 3.12.7, 38 проверок, 0 ошибок. [Код](Research/procgen_probe.py), [отчёт](../Evidence/Research/2026-10-12/procgen-probe.json), [границы применимости](../Docs/Research/PROCEDURAL_FORMULAS.md). Программа не запускает Unity, не тестирует FastNoise Lite и не является игровым benchmark.

`Sync-ResearchKnowledge.ps1` — отдельный датированный пакет исследования: новая source-card и дополнения к существующим Obsidian project/journal/index с readback; требует разрешения записи в установленный vault. Не заменяет историю, не включает фоновую синхронизацию. Локальные подготовленные записи — Docs/ObsidianPending/2026-10-12-procgen-*.md.

Параметр `-Bundle AgentQuality` синхронизирует отдельное уточнение об инструкциях качества Anthropic/OpenAI; по умолчанию `-Bundle Procgen`. Каждый пакет имеет собственную квитанцию и проверку от дублирования.

## Первая Windows-альфа

`powershell -File Tools/Run-Capture.ps1 -Name overview` запускает видимый Player, ждёт настоящий PNG и сохраняет SHA256 исходников. `-FocusSettlement -Power Meteor` явно включает автоматическую камеру и production-команду, что отмечается в квитанциях. `-Width 1280 -Height 720` меняет разрешение. Нагрузочный пример: `-Name load-1000 -Seconds 40 -ProfileSeconds 30 -StressPopulation 1000`; три секунды прогрева исключены из кадровой выборки.

После сборки и проверки: `powershell -File Tools/Package-Alpha.ps1`. Полный ZIP получает руководство и отчёт; каждое ZIP-содержимое читается обратно и сравнивается по SHA256. Скрипт не перезаписывает уже существующий архив той же версии. Архивы лежат в ignored `Builds`, исходники и компактные evidence отправляются обычным Git. Сводка — Docs/Development/ALPHA_REPORT.md.

## Итерация 0.2.0

Сборка по умолчанию теперь находится в `Builds/Windows-0.2.0/`; предыдущая папка сохраняется. `Package-Alpha.ps1` по умолчанию создаёт `WorldGenesis-v0.2.0-alpha-win64.zip`, добавляет актуальное руководство, CITIZENS_REPORT и происхождение стороннего арта. Для старой версии параметры задаются явно: `-Version 0.1.0-alpha -BuildFolder Windows`.

Когда исходный проект открыт в Unity, `New-VerificationSnapshot.ps1 -Name имя` создаёт копию Assets/Packages/ProjectSettings в Builds/Verification и квитанцию SHA256. Передайте абсолютный путь этой копии в `Project.ps1 -Action EditMode/PlayMode/Build -VerificationProject ...`. Не закрывайте пользовательский редактор ради batch-проверки. Не повторяйте имя snapshot: история проверок сохраняется.

`Run-Capture.ps1 -Name citizens -FocusSettlement -Zoom 12 -InspectCitizen` снимает настоящий Player, записывая SHA256 DLL и исходников на момент запуска. `Run-MotionCapture.ps1 -Name citizens -Seconds 6` сохраняет короткий native MP4: кадры сняты с настоящим временем, частота выборки 10 Гц, монтаж использует timestamps. Это видеодоказательство движения, не профиль FPS; захват сам влияет на скорость. Для профиля используйте отдельный Run-Capture с ProfileSeconds.

Проектные данные: `python Tools/Validate-EconomyDesign.py --report Evidence/Design/economy-design-validation.json` и `python Tools/Validate-TechnologyDesign.py`. Они проверяют ссылки, цепочки и математику, а не выполняют будущую экономику в Unity. Результаты — Evidence/Design; формулы и ограничения — Docs/Design.
## Ресурсная итерация 0.3.0

`Tools/ResourceAudit/ResourceAudit.csproj` воспроизводит команды обычного стартового мира128×128/seed1618033 без подстановки ресурсов, затем4500тиков чистого ядра с45проверками состояния. Пример: `dotnet run --project Tools/ResourceAudit/ResourceAudit.csproj -- Evidence/Simulation/resource-natural-opening.json`. Это offline-расчёт, не время Player; соседство источника в квадрате18клеток не доказывает доступный путь к нему. Для повторных экспериментальных вариантов используйте новое имя отчёта и сохраняйте историю.

Нативные захваты новой сборки требуют `-PlayerRelativePath Builds/Windows-0.3.0/WorldGenesis.exe`. `Run-Capture.ps1 -InspectResourceWorker` при наступлении времени кадра выбирает существующего носильщика брёвен/рабочего, не меняя мир. `-Resource IronOre` кадрирует реальный узел указанного вида. `Run-MotionCapture.ps1 -InspectResourceWorker -Delay 20 -Seconds 6` начинает наблюдение после20с обычной симуляции и сохраняет ID/позицию/задачу/груз по кадрам. Выбор/камера автоматические; деньги, материал и приказы не добавляются.

Выпуск: `Tools/Package-Alpha.ps1 -Version 0.3.0-alpha -BuildFolder Windows-0.3.0` включает текущий RESOURCES_REPORT, руководство и provenance CC0. Само описание команды не подтверждает, что выпуск прошёл приёмку: статус в Docs/STATE.md.

### Наблюдение демографии (0.3)

`Tools/Run-Capture.ps1 -PlayerRelativePath Builds/Windows-0.3.0/WorldGenesis.exe -Name woman -Seconds 6 -Demographic Woman` выбирает живую взрослую женщину в обычном мире. `-Demographic Child -Speed 10 -Seconds 35` ищет ребёнка после реальных рождений. Man/Woman/Child — фильтры наблюдателя; он не создаёт жителя и завершается ошибкой, если подходящего нет. Скорость, возраст и пол фиксируются в PNG JSON. При ускорении не называть кадр обычной1×симуляцией.

ResourceAudit теперь также проверяет общий лимит ноши каждую итерацию, считает настоящие Contribution/Gift/Barter, пол/возраст и натуральный налог в4500-тиковом обычном старте. Это offline-проверка ядра, не замер FPS Unity.

## Социальная и аграрная итерация 0.4.0

Финальные отчёты прочитаны: [EditMode119/119](../Evidence/20261012-150819-401-editmode/tests.xml), [PlayMode10/10](../Evidence/20261012-151113-824-playmode/tests.xml), [Windows Build](../Evidence/build-report.json) — Succeeded,0errors/0warnings. EditMode включает16кара­ванных и14аграрных сценариев. Более ранний .NET-отчёт `agriculture-v040-release.json` содержит13/13;14-й `FedWorkerCanMakeFirstBasketDespiteTownFoodShortage` подтверждён финальным Unity XML. Эти числа не означают полный баланс всех миров или художественное принятие.

### Дополнительные проверки ядра

Все перечисленные проекты используют .NET8, настоящий Simulation и не требуют внешних NuGet-пакетов. Social/Wildlife/Agriculture/Caravan импортируют общие сценарии из EditMode; автономный runner использует System.Text.Json, Unity — JsonUtility.

| Проект в Tools | Область |
|---|---|
| SocialProbe | Потребности, религии, голосование, кризис, обучение, миграция/сохранение |
| WildlifeProbe | Животные, группы, охота, добыча и воспроизводимость |
| AgricultureProbe | Поля, животноводство, физические поставки, корзины/телеги/упряжь, пивоварение |
| CaravanProbe | Материальный обмен, налог, физический найм, отмена/война/смерть, сохранение |

Пример первого запуска; имя проекта и отчёта заменяются для нужной группы:

```powershell
dotnet restore Tools/CaravanProbe/CaravanProbe.csproj --configfile Tools/CaravanProbe/NuGet.Config
dotnet run --project Tools/CaravanProbe/CaravanProbe.csproj --no-restore -- Evidence/Simulation/caravan-new-run.json
dotnet restore Tools/AgricultureProbe/AgricultureProbe.csproj --configfile Tools/CaravanProbe/NuGet.Config
dotnet run --project Tools/AgricultureProbe/AgricultureProbe.csproj --no-restore -- Evidence/Simulation/agriculture-new-run.json
```

Один явный NuGet.Config с пустыми sources подходит всем этим проектам. Сохраняйте прошлые отчёты, выбирая новые имена. Подготовленные сценарии не доказывают естественный рост поселений и не измеряют FPS.

### Подготовленный мир для визуальной проверки

`RuralShowcase` создаёт **assisted fixture**, а не обычный старт: авторские участки карты/здания, выданные исследования и материалы, стартовые животные. Телега проходит настоящую производственную операцию с явно приложенными8секундами работы; караван загружается и движется настоящими тиками. Исходное состояние — `Builds/rural-assisted-showcase.json`; происхождение — `Evidence/Simulation/rural-assisted-showcase.json`. Программа использует фиксированные имена и при повторе заменяет эти два файла: нужный предыдущий вариант сохраните под отдельным именем.

`Wrap-CaptureWorld.py` добавляет только существующий save-конверт Schema1/FormatWorldGenesis к сырому состоянию, не меняя содержимое World. Принимает два относительных аргумента: источник внутри проекта, результат внутри Builds. Пишет SHA256-квитанцию в Evidence/Development. Это не обход проверки: настоящий Player всё ещё обязан загрузить мир через SaveStore и Validate.

```powershell
New-Item -ItemType Directory -Force Builds, Evidence/Simulation, Evidence/Development | Out-Null
dotnet restore Tools/RuralShowcase/RuralShowcase.csproj --configfile Tools/CaravanProbe/NuGet.Config
dotnet run --project Tools/RuralShowcase/RuralShowcase.csproj --no-restore
python Tools/Wrap-CaptureWorld.py Builds/rural-assisted-showcase.json Builds/rural-assisted-showcase.world.json
.\Tools\Run-Capture.ps1 -PlayerRelativePath Builds/Windows-0.4.0/WorldGenesis.exe -Name rural-assisted -WorldFile Builds/rural-assisted-showcase.world.json -InspectCaravan -FocusSettlement -Zoom 12 -Seconds 6
.\Tools\Run-MotionCapture.ps1 -PlayerRelativePath Builds/Windows-0.4.0/WorldGenesis.exe -Name caravan-assisted -WorldFile Builds/rural-assisted-showcase.world.json -InspectCaravan -Delay 2 -Seconds 6
```

`-WorldFile` загружает существующий save внутри проекта; `-InspectCaravan` выбирает существующего участника для наблюдения и не выдаёт ему товар/оборудование. Источник fixture, автоматический выбор, камера и скорость должны быть обозначены рядом с кадром/клипом. Для естественного сохранения используйте другой WorldFile с его собственной квитанцией происхождения. Сам по себе assisted кадр не подтверждает самостоятельное развитие от костра.

Текущая упаковка вызывается явно: `Tools/Package-Alpha.ps1 -Version 0.4.0-alpha -BuildFolder Windows-0.4.0`. Она включает руководство, SOCIETY_REPORT и происхождение арта; существующий архив той же версии сохраняется и повторно не перезаписывается. Запуск упаковки и Git-публикация — отдельные действия интегратора, не следствие этих инструкций.


### Транспорт и хранение 0.5

Чистые общие сценарии: `Tools/HydrologyProbe`, `SaltProbe`, `TransportProbe`, `TransportBoundaryProbe`, `RulerPlanningProbe`, `RulerBoundaryProbe`, `WarehouseProbe`. Запуск: `dotnet run --project Tools/<имя> -- Evidence/Simulation/<новое-имя>.json`. NUnit использует те же сценарии и Unity JsonUtility; оба результата фиксируются отдельно.

`Tools/TransportShowcase` и `Tools/StorageShowcase` создают явно подготовленные миры для визуального обзора. Рецепты/стройка исполняются, входные запасы и технологии выдаются и записываются в квитанции. Это не обычный старт.

`Run-Capture.ps1` и `Run-MotionCapture.ps1` принимают `-InspectX 65 -InspectY 64 -Zoom 10`: выбор существующей клетки и камера без изменения мира. `Run-Capture` также принимает `-Resource Salt`. Применяйте `-PlayerRelativePath Builds/Windows-0.5.0/WorldGenesis.exe` и `-WorldFile` с существующим envelope.

Упаковка 0.5: `Tools/Package-Alpha.ps1 -Version 0.5.0-alpha -BuildFolder Windows-0.5.0`; включает TRANSPORT_REPORT.md, руководство и происхождение арта.

Климат: `Tools/BiomeProbe` и `Tools/WeatherProbe` исполняют общие с Unity сценарии. `Tools/SocialProbe --natural <report> 18000 <seed>` сохраняет переходы автоматической погоды, пиковое затопление, биомы, население и проверки ноши. Это core-прогон, не измерение кадров Player.

`dotnet run --project Tools/WeatherShowcase -- Builds/storage-assisted-v050.save.json Builds/weather-assisted-v050-r2 Evidence/Simulation/weather-assisted-v050-r2.json` создаёт шесть погодных сцен через SetWeather и обычные тики. Каждый файл проверяется и одинаково продолжается100тиков после roundtrip. Источник наследует помощь транспортного/складского showcase, автоматическая смена отключена; снежный вариант дополнительно получает явно записанное охлаждение участка. Природная частота событий этим инструментом не доказывается.

`Run-Capture.ps1`/`Run-MotionCapture.ps1 -WeatherTab` открывает погодную вкладку для нативного обзора, не меняя погоду. Погодный фронт берётся из `-WorldFile`. Исправление старого TransportShowcase: native envelope должен иметь поля Schema/Format/World; анонимный объект с IgnoreReadOnlyProperties давал `{}`. `Wrap-CaptureWorld.py` оборачивает только настоящий raw WorldState и записывает SHA256; загрузчик Player продолжает валидировать мир.

### Собственные жители 0.6

Галерея: [Run-CitizenGallery](CitizenGallery-README.md), режимы families, wardrobe и actions. Она использует production-renderer и подготовленные состояния, а не естественный игровой процесс. Для обычной игры используйте Run-Capture.ps1 и Run-MotionCapture.ps1 с PlayerRelativePath Builds/Windows-0.6.0/WorldGenesis.exe. Упаковка: Tools/Package-Alpha.ps1 -Version 0.6.0-alpha -BuildFolder Windows-0.6.0. Наличие локального ZIP не означает публикацию бинарника в GitHub Releases.

### Рыбалка, реки и берег 0.6.1

`dotnet run --project Tools/FishingProbe -- Evidence/Simulation/fishing-new-run.json` исполняет14 целевых сценариев на production Simulation. `dotnet run --project Tools/FishingNaturalProbe -- Evidence/Simulation/fishing-natural-new-run.json` проверяет два обычных мира без последующей выдачи технологий/снастей; ограничения и параметры — в README инструмента. Для первого запуска используйте `dotnet restore <проект> --configfile Tools/FishingProbe/NuGet.Config`.

[Подготовленный мир для сравнения снастей и брода](FishingShowcase/README.md) явно выдаёт снасти1–5 и меняет локальный берег, продлевая первый заброс до20с. Его кадры не доказывают естественное исследование этих грейдов. Используйте `-PlayerRelativePath Builds/Windows-0.6.1/WorldGenesis.exe` в существующих Run-Capture/Run-MotionCapture.

Упаковка: `Tools/Package-Alpha.ps1 -Version 0.6.1-alpha -BuildFolder Windows-0.6.1`. Архив включает отчёт, руководство и происхождение арта; каждый файл ZIP перечитывается и сверяется поSHA256. Это локальная упаковка, не создание GitHub Release. Project Validate теперь также проверяет длину32 и уникальность GUID всех Asset.meta; результат организации не заменяет Unity-тесты.
