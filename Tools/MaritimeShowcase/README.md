# Подготовленная морская сцена

`dotnet restore Tools/MaritimeShowcase/MaritimeShowcase.csproj --configfile Tools/MaritimeShowcase/NuGet.Config`

`dotnet run --project Tools/MaritimeShowcase --no-restore`

Создаёт пять настоящих сохранений в `Builds/MaritimeShowcase/`: fleet, harbor, voyage, visitor, engagement. После каждого сохранения отдельно проверяет загрузку и 120 тиков продолжения.

Помощь задана явно: берег выровнен, исследования, строительный бюджет, исходные партии, корпуса, экипажи и исходные позиции выданы сценарием. Портовые здания и улучшения оплачиваются обычными командами. Сеть из складов 1/2/3 уровня даёт 35 000 мест. Команды плавания, погрузки, провизии, зарубежного рейса, прибытия странствующего судна и нападения исполняет обычная симуляция. Физическое строительство корпуса и подход моряков к борту проверяются отдельно в MaritimeTests; сцена не доказывает естественное развитие города.

Камера обзора: `(48,65)`, zoom 15. Собственные суда #1–6: лодка, катер, траулер, транспорт, торговый корабль, боевой корабль. Странствующее судно #7 имеет трюм 20 000. Кадры: `Run-Capture.ps1 -PlayerRelativePath Builds/Windows-0.8.0/WorldGenesis.exe -WorldFile Builds/MaritimeShowcase/harbor.save.json -InspectShip 5 -Zoom 10`. Для плавания используйте `Run-MotionCapture.ps1` и voyage/fleet. Все квитанции подготовки — `Evidence/Simulation/maritime-showcase-*.json`.

Это средство проверки, не отдельная реализация игры. Скриншоты и клипы считаются полученными только после запуска Unity Player.
