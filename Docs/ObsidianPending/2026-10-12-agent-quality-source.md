---
title: World Genesis — инструкции качества агента и сравнение с Claude 5.5
type: source
tags:
- world-genesis
- agents
- unity
- quality
---

# World Genesis — качество агентской разработки

## 2026-10-12 02:20 +03:00 · Исследованы инструкции Anthropic и OpenAI

Пользователь дополнительно поручил найти инструкции, чтобы делать игру не хуже Claude 5.5, а лучше. Задача относится к отдельному Unity-проекту World Genesis; это не смена модели, не запуск Claude и не поручение платного benchmark.

Прочитаны первоисточники: [Claude Code best practices](https://code.claude.com/docs/en/best-practices), [Effective harnesses](https://www.anthropic.com/engineering/effective-harnesses-for-long-running-agents), [Harness design for long-running apps](https://www.anthropic.com/engineering/harness-design-long-running-apps), [Codex long horizon](https://developers.openai.com/blog/run-long-horizon-tasks-with-codex), [ExecPlans](https://developers.openai.com/cookbook/articles/codex_exec_plans). Последний рецепт архивный: используются идеи плана, не рекомендации старых моделей/API. Название модели сверено с официальной страницей [Claude Opus 5.5](https://www.anthropic.com/claude-opus-5-5).

Создан [стандарт качества](<C:/Users/ohrem_6c67u02/OneDrive/Документы/ChatGPT/Игра WorldBox/Docs/Research/AGENT_QUALITY_STANDARD.md>): 14 правил от полного объёма и сквозного поведения до проверки в Player, отдельной критики, реальных кадров, профиля и доказательств. Назначен через AGENTS/WORKFLOW. Добавлен шаблон WORK_PACKET для сложных механик, подготовлены восемь сценариев сравнения B01–B08 со статусами not-run.

Вывод: полезнее исполнимые проверки и исправление дефектов, чем обещание преимущества из длинного промпта. Саморевью не называется независимой экспертизой; автоматически подагенты/hooks не включены. Для вывода «лучше Claude» нужны одинаковые задания, версии, инструменты, бюджет/помощь и несколько реальных результатов. Художественное предпочтение пользователя отдельно от функциональной корректности.

Факт: документы созданы, ссылки включены в проект, структура валидируется Tools/Project.ps1. Сравнительные прогоны не запускались, превосходство не установлено. Ранее выполненные 38 CPU-проверок процедурных формул не являются сравнением агентов или тестами Unity. Статус реализации игры не изменился.

Следующий шаг: восстановить запуск Unity 6000.0.45f1 и выполнить первый сквозной сценарий карты/камеры/кисти с этим стандартом. Связи: [[01_Project/World_Genesis]], [[06_Sources/World_Genesis_Procgen_Research_2026-10-12]].
