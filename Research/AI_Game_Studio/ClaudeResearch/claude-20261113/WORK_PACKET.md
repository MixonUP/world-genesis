# WORK_PACKET: claude-20261113 — исследовательская архитектура, реестр знаний и проверка связей

## Цель

Дать студии воспроизводимый ответ на вопросы: какое решение, на чём основано, откуда это известно, что проверено нами, какое требование затронуто, чем проверить и что устаревает после изменения. Результат состоит из подробной спецификации R00–R11 и работающего офлайн-валидатора реестра.

Границы: интеграция с runner, Unity, адаптеры jobs и коммерческие действия в этот пакет не входят.

## Изоляция и база

| Параметр | Значение |
|---|---|
| Репозиторий | https://github.com/MixonUP/world-genesis |
| Базовая ветка | `codex/ai-game-studio-collaboration-20261112` |
| Проверенный commit | `3fdbd6f8fa60b56b4bfa26dbcce0daed66c4262c`. Remote HEAD ветки на 13.11.2026 совпал, продвижения нет |
| Рабочая ветка | `codex/claude-research-architecture-20261113`. До создания на remote не существовала (`git ls-remote --heads`: только `codex/ai-game-studio-collaboration-20261112` и `codex/world-genesis`) |
| Checkout | отдельный blobless-клон с sparse checkout (`Docs/`, `Research/`, `Portfolio/`, `Evidence/`, файлы корня) в папке сессии Claude. Рабочая папка Codex не затрагивалась |
| Авторизация | клонирование и `ls-remote` анонимно, с `GIT_TERMINAL_PROMPT=0`, `GCM_INTERACTIVE=never`; локально `credential.helper=""` — интерактивные окна исключены |
| ОС / инструменты | Windows 11 (10.0.26200), Git 2.41, Python 3.12.7. `gh` отсутствует |

## Ownership

Перед началом проверено:
- `Research/AI_Game_Studio/ClaudeResearch/` на базе не существует;
- CODEOWNERS нет;
- родитель `Research/AI_Game_Studio/` содержит только `Orchestrator/` (Codex).

**Выбранная область (единственная изменяемая):** `Research/AI_Game_Studio/ClaudeResearch/claude-20261113/**`.

Только чтение: `Research/AI_Game_Studio/Orchestrator/**` (runner, каталог), `Portfolio/**`, `Docs/**`, файлы корня, приватная локальная библиотека.

## Исходный Git status (до изменений, на ветке базы)

```
 M Research/AI_Game_Studio/Orchestrator/COMPONENT_REQUIREMENTS.json
```

Это **не правка Claude**, а артефакт checkout. Blob в индексе хранит CRLF (905 строк), а `.gitattributes` требует `*.json eol=lf`. Git сразу после checkout показывает файл изменённым (содержимое при нормализации LF идентично; sha256_lf `bfb25e1c…0397` у blob и рабочей копии совпадает). Файл не трогается и не коммитится. Предложение по исправлению — в HANDOFF_TO_CODEX.md.

## Входы

См. [INPUT_INVENTORY.md](INPUT_INVENTORY.md).

## Выходы

README, WORK_PACKET, INPUT_INVENTORY, STATE, ARCHITECTURE, CONTRACTS, schemas/, examples/, COMPONENT_TRACEABILITY.{json,md}, SOURCE_REGISTER.json, CAPABILITY_MATRIX, RESEARCH_GAPS, PORTFOLIO_AUDIT, validator.py, tests/, Evidence/, INTEGRATION_PROPOSAL, HANDOFF_TO_CODEX, PR_DESCRIPTION, OBSIDIAN_PENDING.

## Приёмка

1. Все требования выбранной ревизии отражены в матрице; `validator.py trace` = CONSISTENT.
2. Валидатор исполняет минимальные проверки 1–8 из задания; негативные случаи покрыты тестами.
3. Существующие тесты runner запущены без изменений, фактический результат записан.
4. Вне owned-области изменений нет (`git status` проверяется перед commit).

## Stop conditions

- Базовый commit исчез из истории → остановить Git-интеграцию, не делать reset.
- Ветка или область заняты другим исполнителем → выбрать свободный суффикс.
- Нужен GUI, Unity, оплата или контакт → отметить not-run.

## Evidence plan

Отчёты в `Evidence/`:
- журнал запуска тестов;
- отчёт валидатора по реальной цепочке;
- отчёт трассировки;
- проверка неизменности входов.

Сырые машинные логи не публикуются.

## Handoff

[HANDOFF_TO_CODEX.md](HANDOFF_TO_CODEX.md).
