# Подготовленная излучина для native-проверки

`dotnet restore Tools/FishingShowcase/FishingShowcase.csproj --configfile Tools/FishingShowcase/NuGet.Config`

`dotnet run --project Tools/FishingShowcase/FishingShowcase.csproj --no-restore -- Builds/fishing-river-showcase.save.json Evidence/Simulation/fishing-river-showcase.json`

Собирает исходники Simulation напрямую. Создаёт проверенное сохранение и проверяет ещё 100 обычных тиков после roundtrip. Явно подготавливает рельеф, запасы рыбы, мост, снасти 1–5, начальные задания ловли, внешность и маршруты трёх жителей. Дальше работает настоящая симуляция. Это инструмент художественного/интеграционного сравнения, а не естественное развитие города.

Финальные native-камеры: `(63,66)`, zoom9 — пять рыбаков и берег; `(72,66)`, zoom6 — вода/мост; `(82,66)`, zoom9 — расширение в озеро. Использовать `Tools/Run-Capture.ps1` с `-WorldFile Builds/fishing-river-showcase.save.json` и Player 0.6.1. Первый заброс явно продлён до20с, чтобы снасти не исчезали до кадра после загрузки; обычный таймер3–5с не меняется. Клип показывает ловлю и движение через брод. В обычной игре грейды создаёт крафт, выданных улучшенных снастей нет.
