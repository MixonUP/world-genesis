# Команды проекта

В PowerShell из корня репозитория:

```powershell
.\Tools\Project.ps1 -Action Doctor
.\Tools\Project.ps1 -Action Validate
.\Tools\Project.ps1 -Action Import -DryRun
```

Doctor читает версии/пути. Validate проверяет структуру, Markdown-ссылки, JSON, assembly references и фиксированную версию; пишет `Evidence/project-setup-report.json`. Это проверка организации, не компиляция и не тест игры.

Следующий этап, после чтения известного сбоя в STATE:

```powershell
.\Tools\Project.ps1 -Action Import
.\Tools\Project.ps1 -Action Open
.\Tools\Project.ps1 -Action EditMode
.\Tools\Project.ps1 -Action PlayMode
```

Import запускает выбранный Unity в batchmode, сохраняет журнал и проверяет exit code/ошибки. Open — намеренно открывает проект в видимом редакторе. Не запускать batch-процесс для проекта, уже открытого в другом Unity. Tests сохраняют NUnit XML; отсутствие тестов/результата или любой отказ считается ошибкой. Сейчас тестов нет, поэтому их успех не ожидается. `-DryRun` для этих действий только показывает команду, без запуска.

Логи команд — `Evidence/<время>-<действие>/`. Нельзя судить о корректности игры по exit 0 при импорте. Пакеты указаны по версиям, обнаруженным в установленном редакторе; разрешение зависимостей Unity ещё не проверено. Автоматического обновления, установки, покупки, изменения execution policy или удаления Library скрипт не выполняет.

Сборка Windows будет добавлена с настоящей Bootstrap-сценой на M1: Editor build method, явный список сцен, `StandaloneWindows64`, `BuildReport`, проверка обычного запуска. Подготовленная сейчас команда build без существующей игры создавала бы ложное ожидание, поэтому её пока нет.

`Sync-ProjectKnowledge.ps1` — исполненная одноразовая идемпотентная синхронизация записи подготовки от 12.10.2026. Дополняет только карточку World_Genesis, дневник этой даты и индекс установленного vault. Это не фоновая синхронизация и не команда записи всех будущих этапов. Квитанция readback — `Evidence/obsidian-sync.json`; повтор не дублирует запись.

## Исследование процедурной генерации

`python Tools/Research/procgen_probe.py` — небольшие численные проверки из корня workspace, Python standard library, без установки зависимостей. Проверены Python 3.12.7, 38 проверок, 0 ошибок. [Код](Research/procgen_probe.py), [отчёт](../Evidence/Research/2026-10-12/procgen-probe.json), [границы применимости](../Docs/Research/PROCEDURAL_FORMULAS.md). Программа не запускает Unity, не тестирует FastNoise Lite и не является игровым benchmark.

`Sync-ResearchKnowledge.ps1` — отдельный датированный пакет исследования: новая source-card и дополнения к существующим Obsidian project/journal/index с readback; требует разрешения записи в установленный vault. Не заменяет историю, не включает фоновую синхронизацию. Локальные подготовленные записи — Docs/ObsidianPending/2026-10-12-procgen-*.md.

Параметр `-Bundle AgentQuality` синхронизирует отдельное уточнение об инструкциях качества Anthropic/OpenAI; по умолчанию `-Bundle Procgen`. Каждый пакет имеет собственную квитанцию и проверку от дублирования.
