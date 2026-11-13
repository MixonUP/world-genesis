# HANDOFF_TO_CODEX

## Результат

- **Ветка:** `codex/claude-research-architecture-20261113` от `3fdbd6f8fa60b56b4bfa26dbcce0daed66c4262c`. Remote HEAD базы на 13.11.2026 совпадал.
- **Область:** только `Research/AI_Game_Studio/ClaudeResearch/claude-20261113/**`.
- Спецификация R00–R11, контракты (9 сущностей, 9 рёбер), JSON Schema (документация), трассировка 49/49 требований.
- Работающий `validator.py`: check, trace, render-trace, query, sources.
- Реальная цепочка портфолио; синтетические walkthrough; аудит портфолио; предложение интеграции.

Фактические команды и результаты — [STATE.md](STATE.md) и [Evidence/TEST_RUN.md](Evidence/TEST_RUN.md).

## Ключевые архитектурные решения

1. **JSON — канон.** Markdown-матрица генерируется. Graph database и vector service не нужны.
2. **Требования — только ссылки** `Cxx-Rxx` с `sha256_lf` каталога. Текст не дублируется. Хэш нормализован по LF, чтобы не зависеть от CRLF-checkout.
3. **Вердикт требования вычисляется**, а не вводится. PASS требует executed, не-synthetic evidence нужного класса, на `target_revision`, в сроке `valid_until`, при необходимости — с независимым reviewer.
4. **Историческое evidence** импортируется как `historical_import`. Оно видно (`historical_pass`), но требование не закрывает.
5. **Граф знаний и исполнимый DAG разделены.** Циклы `depends_on` и `derived_from/supersedes` — ошибки; остальные петли допустимы.
6. **Пять разных фактов о capability:** заявлено, доступно, вызвано, benchmark, право на действие.
7. **Три вида проверки путей:** лексическая проверка locator, resolved-containment при чтении для хэша, containment вывода.

## Что требуется от Codex для интеграции

1. Просмотреть PR или patch и смержить ветку в базовую (Claude не мержит).
2. Решить по [INTEGRATION_PROPOSAL.md](INTEGRATION_PROPOSAL.md): необязательный `--registry-report` в runner, без влияния на gates.
3. Внести [OBSIDIAN_PENDING.md](OBSIDIAN_PENDING.md) в общую память и, при желании, ссылку в `Docs/00_INDEX.md`/STATE (общие файлы — ваши).
4. Рабочие пакеты портфолио (Unity — ваш): `WP-LAB-CONTRAST` → `WP-LAB-VISUAL-NOTRUN` → `WP-LAB-HUMAN-ART`; `EXP-RERUN-EDITMODE`.

## Предложения к чужим файлам (не выполнялись)

| Путь | Причина | Предложение | Проверка |
|---|---|---|---|
| `Research/AI_Game_Studio/Orchestrator/COMPONENT_REQUIREMENTS.json` | blob хранит CRLF, `.gitattributes` требует `*.json eol=lf`; после checkout файл сразу «modified» | отдельным commit: `git add --renormalize` этого файла (содержимое при LF то же, sha256_lf `bfb25e1c…0397`) | `git status` чистый после свежего checkout; `validator.py trace` по-прежнему CONSISTENT |
| `Portfolio/UnityDeliveryLab/VERIFICATION.json` | кадры не связаны с хэшем сборки | поле `build_sha256` у каждого capture | C08-R02 закрывается только при совпадении ревизии |
| корень репозитория | нет LICENSE | владелец выбирает лицензию для портфолио и кода | `RIGHTS_UNKNOWN` исчезает после обновления записей |
| приватная библиотека `Sources.json` | 1.2/1.0/0.7 смешаны с P0/P1 в `priority` (57/150) | отдельное поле `relevance` | `DEC-SEPARATE-RELEVANCE` |
| библиотека, главы 05 и 08 | расходятся знаменатели first-pass и yield | одно определение до benchmark | `CLM-YIELD-05 ↔ CLM-YIELD-08` разрешено accepted Decision |

## Пробелы и not-run

- Unity, Player, GUI — not-run по ограничению пользователя. Новых Unity PASS нет.
- Страницы ChatGPT Space недоступны.
- Видео пользователя изучены Claude только на уровне метаданных.
- Symlink-escape: тест пропускается на этой Windows без прав на symlink. Resolved-containment покрыт отдельным тестом.
- Самопроверка автора не является независимым review. Отдельный read-only review субагентом — см. STATE.md.

## Следующая ограниченная задача (предложение)

Read-only интеграция `--registry-report` в runner по INTEGRATION_PROPOSAL: 4 новых теста, без изменения gates. Владелец — Codex.
