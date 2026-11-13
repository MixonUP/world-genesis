# UNITY_BUILD_20261114 — headless-сборка Windows-плеера портфолио

Выполнил Claude 14.11.2026 около 01:10 MSK, по разрешению пользователя на batch-режим. Окна не открывались, Player не запускался.

| Параметр | Значение |
|---|---|
| Команда | `Unity.exe -batchmode -nographics -quit -projectPath <короткая копия> -executeMethod DeliveryLab.EditorTools.BuildEntry.Build -labBuildPath <копия>/Builds/InventoryLab/InventoryLab.exe` |
| Исходник | `Portfolio/UnityDeliveryLab/Project` с базы `3fdbd6f`, копия в коротком временном пути |
| build-result.json | `result=Succeeded`, `errors=0`, `warnings=0`, `bytes=86050141`, `unity=6000.0.45f1`, config `{"stackLimit":20,"revision":"polished-20-f1"}` |
| sha256 InventoryLab.exe | `e6fe27700b3c3c2c4b2d8021b384e698a22925ddfcc62a814274b5f49932704e` |

## Что доказано и что нет

**Доказано:** опубликованный исходник собирается в Windows x64 Player без ошибок и предупреждений. Конфиг в сборке несёт метку `polished-20-f1`. Это метка в файле, а не доказательство побайтового равенства исторической ревизии.

**Не доказано:**
- что Player запускается и работает: запуск открыл бы окно;
- визуальные состояния;
- PlayMode;
- человеческая приёмка.

Сама сборка в репозиторий не добавлялась.
