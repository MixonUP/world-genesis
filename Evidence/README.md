# Проверки и доказательства

Один осмысленный запуск — папка `YYYYMMDD-HHMMSS-<scenario>`, время Europe/Moscow. Хранить лог, настройки, NUnit XML/BuildReport и настоящие capture при необходимости. Не дублировать большие ассеты; сохранять их пути и хеши.

Короткий отчёт содержит:

```json
{
  "scenario": "A02-paint-coast",
  "status": "not-run",
  "timestamp": "ISO-8601 with offset",
  "revision": "commit or source manifest SHA256",
  "unityVersion": "6000.0.45f1",
  "hardware": "record observed CPU/GPU/RAM, not assumed minimum",
  "seed": 0,
  "configuration": {},
  "actions": [],
  "expected": "",
  "actual": "",
  "assistance": "none / automated commands / debug setup",
  "artifacts": [],
  "limitations": [],
  "userVisualAcceptance": "not-reviewed"
}
```

При незакоммиченном коде сохранить список SHA256 фактических исходников. Screenshot подтверждает вид; clip — движение; state/log — поведение; Profiler — стоимость. Не смешивать обычную скорость с ускоренным/offline-рендером. Отдельно обозначать стадию: предложено, подготовлено, исполнено, проверено, принято пользователем.

## Индекс первой альфы

Итоговые доказательства и ограничения: [ALPHA_REPORT](../Docs/Development/ALPHA_REPORT.md). Неудачные ранние захваты сохранены с объяснением; не использовать чёрный кадр скрытого Player или кадр до Meteor как подтверждение визуального результата. `Captures/*-run.json` содержит SHA256 исходников; `.png.json` — реальное состояние камеры и мира. Логи исключены из Git.
