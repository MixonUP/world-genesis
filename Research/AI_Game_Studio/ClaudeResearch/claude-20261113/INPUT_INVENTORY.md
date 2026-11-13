# INPUT_INVENTORY — что доступно и что прочитано (13.11.2026)

Статусы:
- **read** — прочитано целиком;
- **partial** — прочитаны указанные разделы;
- **listed** — видны только имена и размеры;
- **delegated-read** — прочитано read-only субагентом Claude по моему поручению, с отчётом по разделам. Это не независимый review;
- **unavailable** — доступа нет;
- **not-attempted** — сознательно не открывалось.

## Публичный репозиторий (база `3fdbd6f`)

| Путь | Статус | Примечание |
|---|---|---|
| AGENTS.md, CLAUDE.md, COLLABORATION.md | read | запрет управления окнами и GUI подтверждён |
| Docs/STATE.md | partial | первые 25 и последние 15 строк; про AI Game Studio/портфолио записей нет (grep) |
| Docs/00_INDEX.md | partial | grep по studio/portfolio/orchestr — совпадений нет |
| Docs/Templates/WORK_PACKET.md | read | структура учтена |
| Portfolio/UnityDeliveryLab/{AGENTS,CLAUDE,HANDOFF_CLAUDE,README,PORTFOLIO_CASE}.md | read | |
| Portfolio/UnityDeliveryLab/VERIFICATION.json | read | исторические результаты |
| Portfolio/UnityDeliveryLab/SourceAssets/provenance.csv | read | |
| Portfolio/UnityDeliveryLab/Screenshots/*.png | хэши | 6/6 sha256 совпали с VERIFICATION.json; изображения не открывались в приложениях |
| Portfolio/.../Assets/Lab/{Core,Runtime,Editor,Tests} | partial | asmdef-границы прочитаны: Core `noEngineReferences`, Editor только Editor, Tests → Core+Runtime. Тесты подсчитаны статически (20 `[Test]` + 20 `[TestCase]` = 40) |
| Research/AI_Game_Studio/Orchestrator/COMPONENT_REQUIREMENTS.json | read | 16 компонентов / 49 требований |
| Research/AI_Game_Studio/Orchestrator/runner/{README.md, runner.py, b2b_quote.py, tests/, examples/b2b_quote.synthetic.json} | read | не изменялись |

## Приватная локальная библиотека (только чтение)

Корень: локальная рабочая папка пользователя `…/Research/AI_Game_Studio/` (полный путь указан в поручении и сюда не переносится). **Доступна.** Содержимое в публичную ветку не копируется. В реестр попадают только относительные имена файлов, заголовки разделов и подсчёты.

| Файл | Статус | Примечание |
|---|---|---|
| 00_README.md | read | шкала 1.2/1.0/0.7, главные решения |
| Sources.json (150), Videos.json (29), Orchestrator/SOURCES.json (23) | read (структура + статистика) | записи не перепроверялись по одной |
| 02–11, 13–15 | delegated-read | субагент прочитал все 13 глав (13 частично); 05 и 08 — ещё и read лично по разделам метрик |
| Orchestrator/ORCHESTRATOR.md | read | |
| Orchestrator/skills/game-studio-orchestrator/SKILL.md | read | |
| Orchestrator/OPERATING_RULES.md, COMMERCIAL_GATES.md, COMPONENTS_ART_AND_RUNTIME.md | delegated-read | |
| Orchestrator/COMPONENT_REQUIREMENTS.json | read | ID и содержимое идентичны публичной копии |
| Orchestrator/SOURCES.md (CM-xx) | not-attempted | нужные CM-записи есть в SOURCES.json |
| 01_User_Videos.md, 12_WorldGenesis_Application.md | not-attempted | вне назначенного списка (02–11, 13–15) |
| USER_BUSINESS_PROFILE.json, Client_Launch_*/, RUNNER_FINANCE_REVIEW.md | not-attempted | частные коммерческие данные, не нужны для задачи |

## Страницы ChatGPT Space

| Страница | Статус |
|---|---|
| Библиотека, Оркестратор, Обзор услуги, Портфолио (`chatgpt.com/space/page_…`) | **unavailable** — у аккаунта и инструментов Claude нет авторизованного доступа. Вход не выполнялся, окна не открывались |

## Видео и сообщества пользователя

| Ссылка | Что сделано | Глубина |
|---|---|---|
| youtu.be/nPSwiARbeRQ | oEmbed: заголовок и автор | metadata |
| youtube.com/watch?v=m4py3JSYH1k | oEmbed | metadata |
| youtube.com/watch?v=HaFfexk1h84 | oEmbed | metadata |
| youtube.com/watch?v=xwTJ3E3NU7A (t=185s) | oEmbed. О моменте 185 s ничего не утверждается | metadata |
| t.me/studioigor | публичное превью канала: название и тема | metadata, не контактировал |

Библиотека записывает transcript- и description-изучение этих видео предыдущим агентом. Claude это **не перепроверял**.

## Независимые источники под реальные пробелы

См. [SOURCE_REGISTER.json](SOURCE_REGISTER.json): W3C PROV-O, SLSA Provenance v1, Nygard ADR, JSON Schema 2020-12 Validation, C2PA 2.1. Каждый закрывает конкретный пробел проектирования: семантику рёбер, «хэш ≠ истина», историю решений, долю исполняемой схемы и provenance AI-ассетов.

## Навыки (инвентаризация имён каталогов, без чтения содержимого, кроме указанного)

- `~/.codex/skills`: присутствуют game-studio-orchestrator, game-long-run-production, production-brief-architect, game-ui-workflow, game-vfx-workflow, game-audio-workflow, gameplay-visual-review, blender-game-*, krea-*, character-*, procedural-audio, living-npc-brain и др. Это **навыки Codex**: Claude их не вызывал, а в списке навыков Claude их нет.
- `~/.claude/skills`, `~/.agents/skills`: arkcli-*, civitai-research, runware-seedance. Игровых навыков нет.
- Приватная библиотека, `skills/`: game-art-acceptance, game-opportunity-validation, game-playtest-design, game-production-benchmark. Подготовлены, по тексту библиотеки глобально не установлены.
- Obsidian `E:/Krea_tren_AI/Knowledge` существует. **Запись не выполнялась** (см. OBSIDIAN_PENDING.md).
