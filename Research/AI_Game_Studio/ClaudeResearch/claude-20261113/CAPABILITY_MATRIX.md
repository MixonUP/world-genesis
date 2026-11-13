# CAPABILITY_MATRIX (R00) — 13.11.2026

Колонки соответствуют пяти разным фактам: заявлено, доступно, вызвано сегодня, benchmark, право на внешнее действие. «Доступно» означает, что инструмент проверен в этой сессии, а не просто упомянут где-то.

| Capability | Владелец | Заявлено | Доступно | Вызвано сегодня | Benchmark | Внешнее действие | Ограничения |
|---|---|---|---|---|---|---|---|
| Python 3.12.7 (стандартная библиотека) | Claude | да | verified_today | да | — | нет | — |
| Git 2.41 CLI (анонимный read) | Claude | да | verified_today | да (clone, ls-remote) | — | push — см. ниже | интерактивная авторизация отключена |
| `gh` CLI | — | нет | unavailable | нет | — | — | не установлен; устанавливать запрещено |
| runner.py / b2b_quote.py | Codex | да | verified_today | да (33 теста) | none | нет | trusted-input checker, без резерва бюджета |
| validator.py (этот модуль) | Claude | да | verified_today | да | none | нет | только согласованность записей |
| Unity 6000.0.45f1 | Codex-интегратор | да | historical | **нет** (запрет пользователя, даже batch) | historical (40/40, build) | — | not-run |
| Windows Player InventoryLab | Codex-интегратор | да | historical | нет | — | — | GUI запрещён |
| game-studio-orchestrator skill | Codex | да | not_checked для Claude | нет | none | — | прочитан SKILL.md (из библиотеки) |
| game-long-run-production, production-brief-architect | Codex | да | not_checked | нет | none | — | только имена каталогов |
| game-art-acceptance / -playtest-design / -production-benchmark / -opportunity-validation | не назначен | да (prepared) | not_checked | нет | none | — | по библиотеке: не установлены глобально |
| ComfyUI / Krea / Blender / Runware | пользователь / Codex | да (библиотека) | not_checked | нет | historical по библиотеке | платные действия запрещены | генерация не выполнялась |
| WebFetch (текстовое чтение веба) | Claude | да | verified_today | да (9 страниц) | — | нет | без входа в аккаунты |
| Страницы ChatGPT Space | — | да (ссылки) | unavailable | нет | — | — | нужен авторизованный доступ |
| Obsidian vault (запись) | Codex-интегратор | да | not_checked | нет | — | — | запись делегирована (OBSIDIAN_PENDING) |
| Push в `MixonUP/world-genesis` | пользователь/Codex | да (по AGENTS) | см. STATE.md | см. STATE.md | — | **внешнее действие** | без интерактивных окон |

## Пробелы доступности

1. Unity и Player — нет новых проверок. Все Unity PASS остаются **historical**.
2. Навыки Codex для Claude не проверены. Перенос «как есть» не предполагается.
3. Space-страницы недоступны. Их содержание не выдаётся за прочитанное.
4. `gh` отсутствует. PR через API не открыть; подготовлены `PR_DESCRIPTION.md` и patch.
