# PORTFOLIO_AUDIT — Inventory & Save Lab (R10)

**Объект:** `Portfolio/UnityDeliveryLab` на базе `3fdbd6f`, ревизия образца `polished-20-f1`.
**Метод:** чтение исходников и записей, сверка хэшей, статический подсчёт тестов. **Unity и Player не запускались**, поэтому все Unity-результаты ниже исторические.
**Машинное представление:** [examples/portfolio_chain.real.json](examples/portfolio_chain.real.json); отчёт — [Evidence/portfolio_chain.report.json](Evidence/portfolio_chain.report.json).

## Что проверено Claude 13–14.11.2026

| Проверка | Результат | Что это доказывает | Чего не доказывает |
|---|---|---|---|
| sha256 шести `Screenshots/*.png` против VERIFICATION.json | 6/6 совпали | файлы не изменились после записи | качество UI, связь с отправляемой сборкой |
| sha256 `InventoryTests.cs` против blob `5b41fad` | совпал | исходник теста — ровно тот, что в базе | что тесты проходят |
| Статический подсчёт тестов | 20 `[Test]` + 20 `[TestCase]` = 40 | объявленный набор согласован с историческим «40» | 40/40 PASS сегодня |
| Границы asmdef | Core `noEngineReferences: true`; Editor только для Editor; Tests → Core+Runtime | модель не зависит от UnityEngine | поведение UI |

## Обещания и их опора

| Клиентское обещание (ограниченное) | Опора: source / test / evidence | Статус | Границы переноса | Непроверенные условия |
|---|---|---|---|---|
| Ограниченный Windows Unity uGUI-модуль с явными критериями приёмки | PORTFOLIO_CASE «Scope and limitations»; VERIFICATION build 0/0 (historical) | **historical_support_only** | Unity 6000.0.45f1, Built-in/uGUI, Windows x64, маленький модуль | чужой проект, другая версия, срок |
| Сохранение валидирует данные до замены состояния, при сбое сессия не теряется | `CorruptFileCannotDestroyRunningState`, `UnknownSchemaCannotReplaceRunningState`, `FailedReplacePreservesCommittedFile` + 40/40 historical | исходник прочитан; PASS historical | фиксированный ASCII v1, один писатель на каталог | power-loss, долгий прогон |
| Резервная копия последнего валидного коммита | `SecondCommitPreservesFirstAsBackup`, `SavingAfterCorruptionKeepsLastValidBackup` | исходник прочитан; PASS historical | тот же формат | восстановление на другой машине |
| Контролируемое изменение конфигурации (stack 10 → 20) | `BaselineSaveLoadsAfterStackCapIncrease`, `ConfigurableStackCapChangesDistribution` | исходник прочитан; PASS historical | одна настройка | серия изменений |
| Две темы на одной иерархии | скриншоты 01/02 (agent-operated) | хэши совпали; визуально не принято | 1280×720 | light-тема 1024×768 **not-run** |
| Перезапуск восстанавливает сохранение | VERIFICATION `ordinary_separate_restart`, кадр 06 | historical, agent-operated | 1024×768 | позднее упорядоченное завершение процесса по неизвестной причине |

## Чего образец не доказывает (не приписывать)

Полная игра, «игра за несколько часов», мобильные платформы, сеть, Figma, AI-персонажи, клиентская приёмка, выручка, срок поставки на незнакомом проекте, человеческое художественное принятие, долговременная стабильность, доступность (accessibility certification).

## Пробелы демонстрации

1. Full-inventory и light-тема 1024×768 **not-run**.
2. Shift+click и полная клавиатурная навигация не проверены.
3. Метка HEALTH FULL слабо контрастна (minor, по handoff).
4. Нет человеческого художественного принятия (C05-R03 OPEN).
5. Скриншоты не несут `build_sha256` (C08-R02 OPEN: хэш кадра ≠ привязка к сборке).
6. В репозитории нет LICENSE (права третьих лиц на исходники и кадры не определены).
7. Новые проверки на текущем blob не выполнялись (C07-R03 OPEN, `historical_import_not_rerun`).

## Задания интегратору (Claude Unity-код не меняет)

| WP | Владелец | Пути | Приёмка | Зависит от |
|---|---|---|---|---|
| `WP-LAB-CONTRAST` | Codex (Unity) | `Project/Assets/Lab/Runtime/LabApp.cs` | контраст метки выше, disabled визуально отличим, 40 EditMode повторно на новой ревизии | — |
| `WP-LAB-VISUAL-NOTRUN` | Codex (Unity) | `Screenshots/`, `VERIFICATION.json` | full-inventory и light-1024 с `build_sha256` и меткой способа ввода | WP-LAB-CONTRAST, разрешение GUI |
| `WP-LAB-HUMAN-ART` | пользователь | новый `ART_ACCEPTANCE_DECISION.md` | явное решение accept/revise с датой | WP-LAB-VISUAL-NOTRUN |
| `EXP-RERUN-EDITMODE` | Codex (Unity) | Evidence образца | 40/40 с хэшем result-файла | разрешение Unity |

Предложение к чужому файлу: путь `Portfolio/UnityDeliveryLab/VERIFICATION.json`.
- **Причина:** кадры не связаны с отправляемой сборкой.
- **Изменение:** добавить в каждый элемент `screenshots[]` поле `build_sha256` (или `build_manifest_sha256`).
- **Проверка:** валидатор с `accepted_evidence_classes=["native_ui_check"]` и `target_revision` = id сборки даст для C08-R02 вердикт PASS только при совпадении.
