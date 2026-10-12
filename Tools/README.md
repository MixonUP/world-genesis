# Команды проекта

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

Build вызывает Editor.ProjectSetup.BuildWindows, использует Main.unity и StandaloneWindows64, пишет Evidence/build-report.json. Результат — Builds/Windows/WorldGenesis.exe вместе с соседними файлами; запуск проверяется отдельно.

`dotnet run --project Tools/SimulationProbe/SimulationProbe.csproj -c Release -- Evidence/Simulation/simulation-probe.json` проверяет настоящий Simulation под .NET 8. Это дополнительная независимая проверка, не замена Unity. NuGet.Config не содержит внешних источников; сторонних .NET-пакетов нет.

`Tools/Run-Capture.ps1 -Name overview -Seconds 8` открывает настоящий Player для native capture и записывает хеши исходников. Скрытый Windows Player может дать чёрный кадр, поэтому проверка намеренно видимая. `-FocusSettlement -Zoom 18` меняет только камеру; `-Power Meteor` применяет настоящую игровую команду с отдельной квитанцией автоматического сценария. Пример профиля: `-Name load-1000 -Seconds 40 -ProfileSeconds 30 -StressPopulation 1000`. Измеряется реальный Player, но не все конфигурации A11.

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
