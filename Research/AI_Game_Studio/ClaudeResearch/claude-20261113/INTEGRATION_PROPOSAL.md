# INTEGRATION_PROPOSAL — read-only связь реестра с runner (не выполнена)

Статус: **proposal**. Ни `runner.py`, ни `COMPONENT_REQUIREMENTS.json` не изменялись. Решение и исполнение — за Codex.

## Проблема

`runner.py` проверяет evidence внутри своего manifest (`evidence[]`, `requirements{demand,…}` с ID evidence). Этот модуль ведёт evidence по `Cxx-Rxx` с ревизиями, классами и provenance. Сейчас два источника правды о «проверено» не связаны.

## Предлагаемый интерфейс (минимальный, read-only)

1. Новый **необязательный** аргумент runner: `assess MANIFEST --registry-report REPORT.json`.
2. `REPORT.json` — вывод `validator.py check … --output` со схемой `ags.research-registry-report/1`.
3. Runner читает только два поля: `requirements[Cxx-Rxx].verdict` и `finding_counts.error`.
4. Runner добавляет в свой вывод отдельный блок `registry_requirements`, но **не** меняет вычисление gates G0–G6. Это информационный слой, а не новое условие перехода.
5. Если `finding_counts.error > 0`, блок помечается `registry_inconsistent`, и runner не опирается на его вердикты.

Опционально, позже и отдельным решением: отображение `runner.REQUIREMENTS` (ключи `demand/feasibility/…`) на `Cxx-Rxx`. Его нужно согласовать с владельцем каталога, потому что одна gate-категория может покрывать несколько требований.

## Совместимость

- Без аргумента runner работает как раньше, вывод байт-в-байт тот же. Это проверяется существующими 33 тестами.
- `schema_version` manifest не меняется. В выводе появляется новое необязательное поле.
- Валидатор и runner не импортируют друг друга. Связь идёт только через JSON-файл, поэтому нет общей зависимости кода.

## Точные будущие изменения (для Codex)

| Файл | Изменение |
|---|---|
| `Orchestrator/runner/runner.py` | argparse: `--registry-report` (Path) для `assess`. Функция `load_registry_report(path)` использует существующий `load_manifest` с проверкой `report_schema == "ags.research-registry-report/1"`. Результат добавляется в итог `assess` как `registry_requirements` |
| `Orchestrator/runner/tests/test_runner.py` | (a) без аргумента вывод идентичен; (b) отчёт с OPEN/PASS переносится без влияния на `allowed_next_states`; (c) неверная схема → INVALID exit 2; (d) `error>0` → `registry_inconsistent` |
| `Orchestrator/runner/README.md` | одна строка о необязательном входе |

## Тесты приёмки интеграции

1. `python -B -m unittest discover -s tests -v` в runner: 33 прежних теста плюс 4 новых проходят.
2. `python validator.py check examples/portfolio_chain.real.json --catalog … --output Evidence/x.json`, затем `runner.py assess <manifest> --registry-report …/Evidence/x.json`: в блоке C07-R03 = OPEN, а gate-статусы те же, что без аргумента.

## Rollback

Удалить аргумент и блок. Формат manifest не менялся, данных для миграции нет. Реестр продолжает работать автономно.

## Чего интеграция не даёт

- Подлинность evidence.
- Атомарный резерв бюджета.
- Исполнение jobs.
- Новые PASS для Unity.
