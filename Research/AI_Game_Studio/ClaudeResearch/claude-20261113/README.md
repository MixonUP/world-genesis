# ClaudeResearch / claude-20261113 — реестр знаний и проверка связей

Вклад второго исполнителя (Claude) в AI Game Studio. Основной интегратор — Codex.

В пакете две части:
- **спецификация** исследовательского контура (блоки R00–R11, четыре слоя, контракты, три сквозные последовательности);
- **работающий офлайн-модуль** `validator.py`, только стандартная библиотека Python 3.10+.

Модуль не изменяет `Orchestrator/runner`, каталог требований, Unity-портфолио, STATE/index и Pages.

## Быстрый старт

Из этого каталога:

```bash
python -B -m unittest discover -s tests -v
```

```bash
python -B validator.py check examples/portfolio_chain.real.json --catalog ../../Orchestrator/COMPONENT_REQUIREMENTS.json
```

```bash
python -B validator.py trace COMPONENT_TRACEABILITY.json --catalog ../../Orchestrator/COMPONENT_REQUIREMENTS.json
```

```bash
python -B validator.py query examples/portfolio_chain.real.json requirement-gaps --catalog ../../Orchestrator/COMPONENT_REQUIREMENTS.json
```

```bash
python -B validator.py sources SOURCE_REGISTER.json
```

Другие запросы: `why <DEC-id>`, `impact <id>`, `declared-only`, `promises`. Таблица трассировки генерируется командой `render-trace … --output COMPONENT_TRACEABILITY.md --overwrite`. Хэши артефактов проверяются только под явно указанным корнем: `check … --artifact-root ../../../..`.

Exit codes:
- `0` — отчёт построен (открытые вопросы возможны);
- `1` — семантическая ошибка в данных;
- `2` — нечитаемый, небезопасный или некорректный вход.

## Что модуль гарантирует и чего нет

Гарантирует согласованность **записей**:
- контракты и ссылки;
- существующие ID требований;
- DAG зависимостей;
- квалификацию evidence: ревизия, срок, класс, synthetic, independence;
- противоречия, неизвестные права, только заявленные инструменты;
- безопасность путей и вывода.

Не гарантирует:
- истинность утверждений, подлинность evidence, личность исполнителя и права;
- авторизацию расходов;
- исполнение задач.

PASS требования означает только одно: связанные записи удовлетворяют правилам для указанной ревизии.

## Карта файлов

| Файл | Назначение |
|---|---|
| [WORK_PACKET.md](WORK_PACKET.md) | база, изоляция, ownership, исходный git status, приёмка |
| [INPUT_INVENTORY.md](INPUT_INVENTORY.md) | что доступно и прочитано, что нет |
| [STATE.md](STATE.md) | текущее состояние и продолжение |
| [ARCHITECTURE.md](ARCHITECTURE.md) | слои, R00–R11, walkthroughs, Mermaid |
| [CONTRACTS.md](CONTRACTS.md), [schemas/](schemas/) | контракты; что исполняется, а что только документируется |
| [examples/portfolio_chain.real.json](examples/portfolio_chain.real.json) | реальная ограниченная цепочка портфолио (historical отмечено) |
| [examples/synthetic/](examples/synthetic/) | синтетические walkthrough и негативный fixture |
| [COMPONENT_TRACEABILITY.json](COMPONENT_TRACEABILITY.json) / [.md](COMPONENT_TRACEABILITY.md) | все 49 требований → R-блоки, покрытие |
| [SOURCE_REGISTER.json](SOURCE_REGISTER.json) | источники, фактически открытые 13.11.2026 |
| [CAPABILITY_MATRIX.md](CAPABILITY_MATRIX.md), [RESEARCH_GAPS.md](RESEARCH_GAPS.md) | доступность и приоритеты пробелов |
| [PORTFOLIO_AUDIT.md](PORTFOLIO_AUDIT.md) | проверяемые обещания образца |
| [validator.py](validator.py), [tests/](tests/), [Evidence/](Evidence/) | модуль, тесты, компактные отчёты |
| [INTEGRATION_PROPOSAL.md](INTEGRATION_PROPOSAL.md) | предлагаемая read-only связь с runner |
| [HANDOFF_TO_CODEX.md](HANDOFF_TO_CODEX.md), [PR_DESCRIPTION.md](PR_DESCRIPTION.md) | передача интегратору |
| [OBSIDIAN_PENDING.md](OBSIDIAN_PENDING.md) | черновик записи для общей памяти от 13.11; сейчас заменён оркестратором (ниже) |
| [orchestrator/](orchestrator/) | `studio.py` (вердикты, задачи, решения → заметки Obsidian) и `obsidian_sync.py` (безопасная раскладка в vault: пробный прогон, конфликты, маркеры, CRLF). `example_data/` — только синтетика |
| [Evidence/UNITY_EDITMODE_20261114.md](Evidence/UNITY_EDITMODE_20261114.md), [Evidence/UNITY_BUILD_20261114.md](Evidence/UNITY_BUILD_20261114.md) | headless Unity: EditMode 40/40, сборка Windows Succeeded 0/0 |

## Оркестратор студии (14.11.2026)

Реальные данные студии **приватные**: бюджет, аккаунты, вердикты с личными ограничениями. Они лежат в Obsidian vault пользователя в скрытой папке `01_Project/AI_Game_Studio/.studio_data/` и в этот репозиторий не попадают. Здесь только код и синтетический пример.

```bash
python -B orchestrator/studio.py render --data orchestrator/example_data
```

```bash
python -B orchestrator/obsidian_sync.py orchestrator/example_data/obsidian_pack/manifest.json --vault <путь к vault>
```

Без `--apply` выполняется только пробный прогон. Синхронизация запускается по команде пользователя.
