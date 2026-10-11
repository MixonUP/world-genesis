---
title: World Genesis — исследование процедурной генерации и Unity-инструментов
type: source
tags:
- world-genesis
- unity
- procedural-generation
- research
---

# World Genesis — исследование процедурной песочницы

## 2026-10-12 02:15 +03:00 · Исследовано и назначено проекту

Поручение: найти лучшие инструкции, frameworks, уроки, skills, модули и MCP для Unity-игры по WorldBox; особый приоритет процедурной генерации и формулам. Это отдельный проект World Genesis, не работа Krea/AIWAIFU.

Канонический результат: [исследование](<C:/Users/ohrem_6c67u02/OneDrive/Документы/ChatGPT/Игра WorldBox/Docs/Research/README.md>), [12 карточек моделей](<C:/Users/ohrem_6c67u02/OneDrive/Документы/ChatGPT/Игра WorldBox/Docs/Research/PROCEDURAL_FORMULAS.md>), [tools/skills/MCP](<C:/Users/ohrem_6c67u02/OneDrive/Документы/ChatGPT/Игра WorldBox/Docs/Research/TOOLS_AND_AGENTS.md>), [порядок реализации](<C:/Users/ohrem_6c67u02/OneDrive/Документы/ChatGPT/Игра WorldBox/Docs/Research/IMPLEMENTATION_ROUTE.md>).

Первичные источники: [Red Blob](https://www.redblobgames.com/maps/terrain-from-noise/), [Catlike Coding](https://catlikecoding.com/unity/tutorials/pseudorandom-noise/), [FastNoise Lite](https://github.com/Auburn/FastNoiseLite), [Priority-Flood](https://arxiv.org/html/1511.04463v1), [Bridson](https://www.cs.ubc.ca/~rbridson/docs/bridson-siggraph07-poissondisk.pdf), [PCG book](https://www.pcgbook.com/), [Unity Agent Plugin](https://github.com/Unity-Technologies/unity-agent-plugin), [Coplay Unity MCP](https://github.com/CoplayDev/unity-mcp). Полный отбор и альтернативы находятся в проектных документах.

Выбранная рабочая схема: независимые RNG → fBm/domain warp → островная маска → сток/водосбор → вода/климат/биомы → размещение по пригодности и расстоянию → проверка → симуляция. Подготовлены также модели навигации, utility-задач, ресурсного баланса, восстановления растительности и вероятности событий. Все коэффициенты предложены; внутренние формулы WorldBox неизвестны.

Фактически выполнена CPU-проверка Python 3.12.7: **38 проверок, 0 ошибок**. Для 29 маленьких карт Priority-Flood с родительским деревом сопоставлен с независимым BFS по порогам высот; проверены отсутствие циклов и баланс стока. Проверены границы fBm, маска, площадь кольца Poisson-кандидатов, logistic growth и постоянная интенсивность событий. [Отчёт с hash программы](<C:/Users/ohrem_6c67u02/OneDrive/Документы/ChatGPT/Игра WorldBox/Evidence/Research/2026-10-12/procgen-probe.json>).

Ограничения: тест не вызывает Unity и FastNoise Lite, не проверяет готовый генератор/распределение принятых Poisson-точек, внешний вид или скорость игры. Видео найдены, не просмотрены; PCG book изучена на уровне содержания, не полностью. Установок skills/модулей/MCP не было.

Выводы по совместимости: официальный plugin заявляет Unity 6+; конкретные CLI/Pipeline-команды ещё требуют местной проверки. Текущий ECS samples master требует более новой Unity 6.2; изученный сторонний tilemap skill — 6.3. В README IvanMurzak MCP есть запрет пробелов в пути, а наш путь их содержит. Не переносить совет generic skill о сохранении только seed в редактируемый мир.

AGENTS/архитектура/workflow обновлены ссылками на методы, уточнены сохранения фактических клеток и версия генератора, добавлен игровой критерий A13 со статусом not-run. Полнота относительно WorldBox не достигнута и не оценена. Следующий шаг: устранить ранний сбой запуска редактора 0x80000003, затем G1/G2/G7 — карта, отображение, камера и кисть в настоящем Unity/Player.

Связанный проект: [[01_Project/World_Genesis]].
