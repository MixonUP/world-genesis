# TEST_RUN — фактические команды и результаты

Выполнено Claude 14.11.2026 около 00:10 MSK (2026-11-13T21:10Z), после исправлений по review, в изолированном клоне. Ветка `codex/claude-research-architecture-20261113` от `3fdbd6f`. Windows 11, Python 3.12.7. Сырые логи не сохраняются — приведены итоги.

## 1. Существующий runner (не изменялся)

```bash
cd Research/AI_Game_Studio/Orchestrator/runner
python -B -m unittest discover -s tests -v
```

Результат: **Ran 33 tests — OK**, exit 0. Совпадение с историческими 33 — факт этого запуска, а не подгонка.

## 2. Новые тесты валидатора

```bash
cd Research/AI_Game_Studio/ClaudeResearch/claude-20261113
python -B -m unittest discover -s tests -v
```

Результат: **Ran 56 tests — OK (skipped=1)**, exit 0. До review было 42 теста; добавлено 14 регрессионных.

| Группа | Тестов | Что проверяют |
|---|---|---|
| ContractTests | 11 | уникальность ID; типы и концы рёбер; висячие ссылки; ID требования из каталога и запрет выдуманной нумерации; хэш каталога; запрет копирования текста требования; категориальная relevance; запрет таймкодов при metadata; запрет контакта с сообществом |
| GraphTests | 4 | цикл `depends_on` блокирует порядок; допустимая обратная связь в графе знаний; цикл provenance; пересечение ownership |
| EvidenceTests | 6 | нет evidence; другая ревизия / истёкшее / будущее / stale / неизвестная целевая ревизия / нет срока / invalidated → OPEN; synthetic, prepared, historical, metadata, bot-вместо-человека → OPEN; prepared+PASS отвергается; FAIL сильнее PASS; независимый reviewer |
| KnowledgeAndRightsTests | 7 | противоречие сохраняется и разрешается только accepted Decision; null остаётся null; строки и bool не числа; неизвестные права и принятие без человека; synthetic accepted; состояния capability; запросы why/impact |
| SafetyTests | 9 | небезопасные locator; неизвестная схема, дубли ключей, NaN, обрыв JSON, отсутствующий файл; CLI exit 2; containment вывода; хэш под корнем; resolved-escape при обходе лексической проверки; **symlink-escape — SKIPPED** (создание symlink запрещено на этом хосте); AST-запрет сетевых и процессных импортов; CLI с заблокированными socket/subprocess/webbrowser, повтор = тот же вывод, входы не изменились |
| ReviewRegressionTests | 14 | по одному на каждую подтверждённую находку review: supersedes без смены статуса; состояние, ревизия и отсутствие Artifact; неверные JSON-типы → exit 2; глубокая вложенность, огромное целое, цепочка из 1100 зависимостей; UTF-8 при cp1251; promises и Decision учитывают пригодность; принятие требует независимого evidence на этот артефакт; некорректная матрица и каталог; все null в unknown_values; нормализация ownership; ADS и область перезаписи; reviewer без учёта регистра; не-ASCII цифры в ID |
| RealDataTests | 5 | реальный каталог: 16/49; матрица покрывает все; пропуск или лишний ID обнаруживается; реальная цепочка оставляет C07-R03/C05-R03/C08-R02 OPEN, C01-R03 PASS только для валидатора; синтетические fixtures никогда не дают PASS |

## 3. CLI smoke и свойства

| Команда (из каталога модуля) | exit | Итог |
|---|---|---|
| `check examples/portfolio_chain.real.json --catalog … --artifact-root ../../../.. --output Evidence/portfolio_chain.report.json --overwrite` | 0 | PASS 1 (C01-R03, только сам валидатор), OPEN 3; 0 ошибок; 2 совпадения хэшей |
| то же дважды в stdout, `cmp` | — | **воспроизводимо: да** |
| `check examples/synthetic/walkthroughs.synthetic.json --catalog …` | 1 | ожидаемо: `SYNTHETIC_ACCEPTED`; все 4 требования OPEN |
| `check examples/synthetic/negative_cycle.synthetic.json --catalog …` | 1 | ожидаемо: `DEPENDENCY_CYCLE`, `execution_order=null` |
| `trace COMPONENT_TRACEABILITY.json --catalog …` | 0 | CONSISTENT, 49/49, 16 компонентов |
| `render-trace … --output COMPONENT_TRACEABILITY.md --overwrite` | 0 | Markdown сгенерирован |
| `sources SOURCE_REGISTER.json` | 0 | VALID, 14 источников |
| `check <файл со schema "bogus">` | 2 | `Unsupported registry schema 'bogus'` |
| `check … --output ../escape.json` | 2 | запись вне модуля отвергнута; файл **не создан** |
| sha256 входов до и после (каталог, примеры, матрица, SOURCE_REGISTER, runner.py, b2b_quote.py) | — | **не изменились** |

## 4. Не выполнялось

- Unity EditMode/PlayMode, сборка, Player, любые GUI-проверки — запрещены пользователем.
- Новых Unity PASS нет. 40/40 и build 0/0 — **historical**.
- Symlink-escape не исполнен на этом хосте (см. выше).
