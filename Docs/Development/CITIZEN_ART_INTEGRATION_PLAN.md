# Интеграция нового атласа жителей: аудит и минимальный план

2026-10-12 20:25 +03:00. Статус: **подготовлено по коду; реализация и Unity-проверки не запускались**. В этом задании изменён только этот документ. Root создаёт собственный оригинальный арт для Human/Elf/Dwarf/Orc, четырёх демографических групп и четырёх ракурсов; направление — компактные приглушённые фигуры. Snoblin служит визуальным ориентиром, его пиксели здесь не импортируются. Утверждение художественного результата отдельно от технической интеграции.

Прочитаны AGENTS, STATE, индекс, архитектура, ART_AND_AUDIO, навык game-animation-integration; фактические CitizenSprites, CitizenVariants, WorldView, CrowdPresentation, QuadBatch, HUD, импортёр, PlayMode и native capture. Принцип навыка применён к спрайтам: игровое перемещение, поворот изображения и фаза клипа разделены; последствия удара остаются у Simulation.

## Что действительно работает сейчас

| Область | Текущая реализация и связь |
|---|---|
| Жители | `Scripts/Runtime/Rendering/CitizenSprites.cs` загружает восемь readable PNG TinySwordsCC0 из Resources, вырезает общий alpha-union для Pawn/Warrior, упаковывает runtime atlas и три производных варианта женщины/детей. Константы192px,6кадров, фиксированные строки и pivot принадлежат именно старому арту. |
| Расовая развилка | `WorldView.Update` вызывает CitizenSprites только для Human. Elf/Dwarf/Orc сейчас получают статические ResourceArt-спрайты и bob по Tick. Sheep/Wolf идут по той же старой ветке, виды6+ — WildlifeArt. Нельзя заменить условие на «все остальные»: это захватит животных. |
| Время | `ResolvePose` читает `World.Tick`, `TaskStartedTick`, путь, ресурс, WorkTimer, AttackTimer; генератор RNG мира не вызывается. Пауза замораживает UV и vertices. Move/Work выбираются по фактическому состоянию, а не одному названию профессии. |
| Ракурс | CitizenSprites хранит словарь направления по ID: цель Fight/Hunt, иначе ближайшая точка пути, иначе последнее направление. Сейчас L/R — flip, отдельные вертикальные клипы есть лишь у Warrior-удара. Ракурс не сохраняется в WorldState. |
| Позиция | `CrowdPresentation` даёт смещение не более.28клетки внутри исходной клетки, обновляясь по Tick/Revision. `GetPresentationPosition` используется выбором мышью в GameBootstrap. Посадка на реальное животное определяется `IsRiding`, координаты Simulation не меняются. |
| Груз | `WorldView.DrawCargo` рисует реальные Cargo/CarriedFood/Wood/Water/Harvest отдельно. Бревно и верёвка направлены назад. Есть **две Human-only поправки высоты** для carry-строк4/5; они непригодны для нового atlas. |
| Слои | `QuadBatch` находится в `Rendering/PixelArt.cs`: общие списки quad/UV/color, один mesh/material, `UInt32` индексы; GameObject на жителя отсутствует. Нынешние orders: buildings30, wildlife40, equipmentBack/wagons45, citizen50, front-cargo/markers60. Внутри списка нет сортировки поY. |
| Портрет | `WorldView.CreateHumanIcon()` → `CitizenSprites.CreatePortrait()`. GameHud кэширует Sprite по iconID6 и освобождает вместе с HUD. `CreateAppearanceSprite(appearance,row,frame)` — публичный просмотр того же atlas; возвращаемый Sprite принадлежит вызывающему. |

## Минимальный формат нового контента

Сначала зафиксировать контракт вместе с генератором, затем подключать PNG. Предлагаемый компактный manifest — JSON/TextAsset рядом с новым PNG в `WorldGenesis/Assets/_Project/Art/Units/Resources/WorldGenesisCitizens/`, исходник и рецепт в `SourceAssets/`. Имена файлов окончательно выбирает root. Не переписывать GUID старого импорта; старые PNG/provenance остаются историческими исходниками.

Нужные поля определения:

- `schemaVersion`, `atlasResourcePath`, фактические width/height, координатная система прямоугольников и explicit directionOrder. UV Unity идут снизу вверх; генераторы изображений часто считают строки сверху вниз — преобразовать один раз при загрузке.
- Ключ: `species × demographic × action × direction`; demographics AdultMale, AdultFemale, Boy, Girl. Расы — явные enum Human/Elf/Dwarf/Orc. Warrior — экипировка/роль, **не пятая демографическая группа**.
- `frames[]` с pixelRect, `ticksPerFrame` либо durationsTicks, loop/non-loop, neutralFrame; целочисленная длительность кратна0.1s. Не предполагать6кадров и номера строк для всех состояний.
- Pivot у стоп и единый world-pixel scale на семейство. Per-frame anchors: headTop, handFront, handBack, cargo, seat; нужны хотя бы проверенные значения на action/direction, если руки не двигаются между кадрами. Для каждого ракурса явный порядок верхней одежды/рук/инструмента.
- Missing/fallback задаётся явно. Если пока есть только walk4dir, idle берёт его нейтральный кадр; work/carry нельзя объявлять полноценной авторской анимацией. Допустим временный общий жест без лишнего оружия, с записанным ограничением. Промежуточный пакет не смешивать со старой большой головой Tiny Swords для отсутствующих действий.

Loader валидирует положительные размеры/длительности, границы всех rect, наличие16базовых тел ×4направления, совместимые anchors/pivot. Проверять фактическую непрозрачную область и пустые клипы при подготовке, не сканировать atlas каждый кадр. Point, clamp, без mipmaps/compression; textureShape2D, NPOT без рескейла. Prepacked atlas может быть non-readable после загрузки; старое требование readable нужно только старому runtime repack. MaxTextureSize устанавливается по реальному размеру и проверяется против устройства, не наследуется вслепую2048.

Масштаб выбирать по **видимой фигуре**, не размеру прозрачной клетки. Ориентир первого сравнения: взрослая фигура около нынешней высоты1–1.3клетки, ребёнок заметно ниже; гном короче, орк шире без огромного размера. Если новые дети уже нарисованы с правильными пропорциями, дополнительный старый множитель.70 не применять автоматически. Окончательные значения — после native сравнения с домом, деревом и грузом.

## Runtime-проход

1. **CitizenSprites остаётся фасадом.** Загрузить уже упакованный atlas/definition, хранить lookup в массивах, не искать строки и не создавать Sprite на каждого юнита. Сохранить конструктор, Begin/Add/Submit/Reset/Dispose, Facing, AtlasWidth/AtlasHeight, CreatePortrait и существующую перегрузку CreateAppearanceSprite. Новый overload может принимать species/demographic/action/direction; прежний возвращает Human и явно документированный semantic mapping.
2. `Pose` дополнить семантическими `Action`, `Direction`, UV/frame и anchors. Legacy Row не должен становиться источником поведения. Если Row/Warrior остаются ради совместимости, это адаптер; тесты состояния переходят к смысловым полям, а не требуют старые координаты atlas. `AppearanceFor` сохраняет пол/возраст; Guard нельзя превращать в мужскую внешность для женщин. Reserved Warrior compatibility отделить от выбора тела.
3. В `WorldView.Update` все четыре цивилизованные расы отправлять в CitizenSprites; Sheep/Wolf/Wildlife оставить в своих ветках. Для этих четырёх брать Facing из того же фасада. Head marker, здоровье, blessing и груз получают новые anchors. Удаляется bob статических жителей, чтобы не складывать его с новой походкой.
4. Сохранить внешний `WorldView.CreateHumanIcon()` и добавить `CreateCitizenIcon(SpeciesType)`. `GameHud.IconSprite` для IDs6–9 может брать человеческий/эльфийский/гномий/орочий портрет из нового atlas; Sheep/Wolf10–11 и остальные значки не затрагивать. Кэш/Dispose остаются прежними. Портрет следует новым headRect, не старому crop62/63/68/66.
5. `DrawCargo` больше не читает `pose.Row==4/5` и не ограничивается Human. Верёвка бревна крепится к той же руке, которая видна в atlas; вода/пакет не перекрывают лицо; actual pack-animal/wagon position остаётся приоритетным. `IsRiding` не меняется. Старый mounted lowerClip80px заменяется seat/mount clip metadata; при отсутствии сидячего арта фиксируется временный pose, не новая логика посадки.

**Четыре направления.** World +Y = North/up на экране; -Y = South, -X=West, +X=East. Выбирать доминирующую ось из target/path delta; при равенстве сохранять предыдущую сторону для подавления дрожания. Нулевой вектор сохраняет направление, после SetWorld детерминированный defaultSouth. Четыре нарисованных ракурса используют четыре набора UV; FlipX не заменяет West, если West действительно нарисован. Зеркалирование допустимо лишь по явному флагу manifest. Render-direction cache не меняет path, положение, TargetId или RNG; после загрузки стоящий житель может начать с default, поскольку прежний facing не сериализуется. Не обещать pixel-identical pose после save/load без отдельного расширения формата.

**Костюмы.** Минимум — один общий atlas и body+clothing quads, добавляемые рядом для каждого жителя, либо заранее скомпонованные нужные варианты. Не один глобальный слой «вся одежда выше всех тел»: в толпе это кладёт чужой фартук поверх чужой головы. Объединить body/clothing в одной последовательности quad; если нужна сортировка, использовать reused list/index buffer с стабильным ключом presentationY и ID, без LINQ/per-frame allocations. Не менять общий QuadBatch только ради четырёх направлений — прямоугольные UV уже поддерживаются.

| Источник состояния | Разрешённое внешнее различие | Чего не дорисовывать как факт |
|---|---|---|
| Species/Sex/Age | Свои тела, причёски, детские пропорции; обоим полам все реальные профессии | «Женщина = только домашняя работа»; раса как простая смена цвета |
| None/Farmer/Forester/Hunter/Herder/Merchant/Carpenter | Скромные одежда/фартук/пояс профессии; могут быть косметическими и так документируются | Непроизведённые оружие/доспехи/товары; статус одежды как запас экономики |
| BasketLevel>0 | Существующая настоящая корзина и её размер | Бесплатная корзина только за профессию |
| HuntingToolLevel>0 | Существующее охотничье копьё/дротик у взрослого | Копьё всем Hunter, если уровень0 |
| MountId/HandlerId,MountBags,HasWagon | Сохранённые реальные снаряжение/животное/телега | Второй дубликат животного или телега только по профессии Merchant |
| Child<16 | Одежда и движения ребёнка; LightWork отражает только уже назначенное разрешённое действие | Добыча руды, война, тяжёлый инструмент из взрослого workclip |
| Guard | Только зарезервированная тестовая совместимость, пока нет реальной выдачи воинского комплекта | Автоматические доспехи населению или новый найм через renderer |

## Действия, часы и удар

Маппинг должен сохранить приоритет: actual Fight/Hunt impact → mounted presentation где разрешено → movement → стационарная работа/потребность → idle. Для пути читаются Path/PathCursor, для Carry — `SimulationEngine.CarriedLoad`, для Gather — живой ResourceCell/Amount и Fire≤.1, для Process — WorkTimer>0, CraftEquipment — EquipmentProgress>0. Просто Task=Gather по пути к дереву не означает рубку; нулевой груз не оставляет пакет. Farm, Tend/Brew, Drink/Fish, Eat/Rest/Socialize/Play/Learn получают собственные действия только если такие кадры существуют. BuildTransport пока не имеет особого клипа: его подтверждённую работу связывать с фактическим проектом/WorkSeconds, а не безусловной петлёй молотка.

Loop-фаза считается из сохранённых Tick и TaskStartedTick, длительность — из manifest. При смене направления шаг остаётся в совместимой фазе, не начинается заново каждую смену стороны. Полный переходный анимационный state machine/Animator не нужен. Tick10Hz ограничивает доступную точность; Time.time/deltaTime не должны двигать части тела на паузе. Имеющиеся микродвижения по WorkTimer допустимы, поскольку этот таймер также заморожен.

**Удар:** SimulationEngine после реального урона ставит AttackTimer=.8. Текущий renderer показывает impact/recovery только при `.5001 < timer ≤ .8001`. Новый клип отображает это окно по `.8-timer`; не проигрывает новую атаку на всём cooldown и не откладывает игровой урон до нарисованного касания. До изменения боевого core достоверны post-hit impact/recovery и короткий выпад; wind-up до реального попадания нельзя восстановить из этих полей. Гражданское ополчение остаётся без брони, при нулевом направлении сохраняется детерминированный видимый выпад. PlayMode synthetic Guard sheet не доказывает наличие постоянных войск.

Смерть сейчас исключается из живого прохода при Health≤0, затем юнит удаляется core. Отдельный deathclip потребует bounded presentation event-instance с сохранёнными race/sex/appearance и временем; один WorldEvent.Death сейчас не гарантирует все данные удалённого тела. Для минимального прохода не заявлять death animation и не удерживать умершего в Simulation ради картинки.

## Точные файлы и границы владения следующего шага

Все ниже относительно `WorldGenesis/Assets/_Project/`, если не указан Tools/SourceAssets.

| Файл | Нужное изменение при назначении реализации |
|---|---|
| `Scripts/Runtime/Rendering/CitizenSprites.cs` | Новый definition loader/semantic pose/four-way atlas, anchors, портреты; публичный фасад сохраняется |
| Новый `Scripts/Runtime/Rendering/CitizenAtlasDefinition.cs` +meta | Небольшой serializable manifest/lookup/validation; можно объединить с CitizenSprites, если это действительно меньше |
| `Scripts/Runtime/Rendering/WorldView.cs` | Четыре расы, head/cargo/mount anchors, сохранённые actual equipment/позиции |
| `Scripts/Runtime/UI/GameHud.cs` | Только иконки6–9 из нового atlas; public портрет API и игровой ввод без изменения |
| `Editor/CitizenArtImporter.cs` | Root: новый путь/точные import settings, старый путь/GUID сохраняются; runtime не зависит от Editor |
| `Art/Units/Resources/WorldGenesisCitizens/*` и meta | Root: оригинальные atlas/manifest; без импорта Snoblin |
| `Scripts/Runtime/Rendering/CitizenVariants.cs` | Старый derivative больше не вызывается новым загрузчиком; не требуется удалять исторический код в первом проходе |
| `Scripts/Runtime/Rendering/PixelArt.cs` / QuadBatch | Изменений для basic integration не нужно |
| `Scripts/Runtime/Rendering/CrowdPresentation.cs` | Сохранить bounded offset/clock/Reset; новая форма не оправдывает телепортацию |
| `Tests/PlayMode/RuntimeIntegrationTests.cs` | Перенести зависимость от старых row/flip на семантические состояния и проверить actual new atlas |
| `Tests/PlayMode/RuralRenderingIntegrationTests.cs` | Сохранить cargo/water/mount/equipment реальные проверки; добавить один иной civilized species в связку |
| Новый `Tests/PlayMode/CitizenAtlasIntegrationTests.cs` +meta | Coverage64базовыхракурсов, pause, одежды/anchors, public portraits, сброс мира; не набор тестов, копирующих indexing formula |
| `GameBootstrap.cs`, `MotionCapture.cs`, `Tools/Run-Capture.ps1`, `Tools/Run-MotionCapture.ps1` | Root: точный readonly selector по unit/species для доказательства всех рас, дополнение receipt по необходимости |
| `SourceAssets/manifest.csv`, новый source recipe/provenance | Root: оригинальное авторство, источник направления, версия генератора, SHAactualatlas, что просмотрено/принято |

Simulation, сохранения, navigation, атаки, RNG, asmdef и баланс этой интеграцией не меняются. Runtime asmdef уже ссылается на Simulation/uGUI; новые зависимости не нужны.

## Зависимости существующих тестов

`RuntimeIntegrationTests.ImportedCitizensRenderAndTheirMeshFreezesOnPause` ищет mesh по имени **Animated human citizens**, требует texture.width>256, Point, frozen UV/vertices и что `CreateHumanIcon().texture` — тот же atlas. Имя можно временно сохранить; при переименовании обновить тест явно. Проверка ширины — историческая эвристика; заменить доказательством coverage/реального содержимого, если новый layout уже256px, не растягивать текстуру ради теста.

`CitizenFramesDistinguishTravelWorkCargoAndActualHits` сейчас жёстко требует row1walk,row3chop,row5carry,FlipXдляLeft, militiaRow0, WarriorUp row6/frame3. Не сохранять эти числа ценой ложной анимации; смысловые assertions оставить: движение до места, валидная рубка на месте, фактическая ноша, отдельный левый ракурс, настоящая реакция удара без выданной брони, отсутствие повторного удара в recovery.

`DemographyAndRulerPolicyAreVisibleInTheProductionInspector` требует AdultFemale/Girl и настоящие подписи/закон. `ResourceInspectorReflectsCargoAndRegrowthWithoutCreatingStock` — бревно позади, без прибавки stock, scroll/input protection. `RealCargoEquipmentAndMountRemainVisibleWithoutCreditingStock` — bucket UV из PixelArt в mesh Life and citizen markers, корзина, лошадь/телега/пределы. Не переносить эти props в новыйatlas без обоснованного обновления их checks. `AnimalPalettePagesExposeLastSpeciesWithNativeIconAndUiBlocking` зависит от Horse atlas256; эта ветка не должна меняться.

Также прогнать существующие замену мира, HUD/input, weather/transport/subsistence PlayMode: они могут обнаружить сломанное освобождение материалов или startup, хотя новые кадры сами не проверяют. Новая схема не требует изменения EditMode core, но полный финальный Unity-набор остаётся у интегратора.

## Проверка до объявления результата

1. Контент:16тел×4ракурса, nonemptyframes и pivots, корректные несовпадающие стороны; согласованный scale/padding, без усечения ушей/инструмента. Женская и детская фигуры проверяются по видимому силуэту, не только enum.
2. PlayMode: настоящий Scene mesh содержит всех четырёх рас; UV меняется при шаге и ракурсе, не при времени паузы. Нет изменения World/RNG/товаров после render. Реальный hit→impact один раз, cancellation/arrival→idle, load/SetWorld сбрасывает facing/lookup, портрет получен из используемой текстуры.
3. Native: карта при1920×1080 и1280×720, обычный zoom18 и близкий7; трава/песок/снег, четыре расы, мужчина/женщина/ребёнок, толпа у фермы. Отдельно реальное бревно, вода, корзина, груз на животном, верховая посадка и поворотNS/EW. Не только atlas на нейтральном фоне.
4. Motion6s при1×: ходьба во все стороны с остановкой, работа и короткий militia hit. PNG не доказывает движение. Новые фикстуры помечаются assisted, естественный мир — своим источником/seed/тик. Упакованный атлас и работающий renderer подтверждаются разными доказательствами.
5. Отдельный native CPU/GC профиль1000 после warm-up, без параллельного screencapture. Сравнить с предыдущим точным билдом; добавленные overlays/сортировка не оправдывают per-unitGameObjects или allocation каждый кадр.

Существующие capture scripts указывают по умолчанию старый `Builds/Windows-0.2.0/WorldGenesis.exe`: **всегда передавать новый PlayerRelativePath**. `-WorldFile` загружает валидированный envelope, camera/inspection не спавнят население. Сейчас GameBootstrap `--inspect-citizen`, `InspectResourceWorker` и `InspectDemographic` выбирают только Human; эти флаги сами не доказывают другие расы. `Run-MotionCapture` фиксирует1280×720,10Hz выборку и wall timestamps; 30fpsкодирование не создаёт30нативныхкадров/сек. Receipt содержит unit/task/cargo/xy/tick, но не species/facing/animation: root может добавить readonly поля для контролируемого сравнения. Настоящий FPS измерять отдельно, поскольку capture снижает производительность.

Следующий шаг: root выдаёт atlas+manifest и набор готовых action IDs, затем назначает реализацию Runtime и импортёр разным владельцам. До этого никаких новых runtime файлов этим аудитом не создано. Для общей записи Obsidian: подготовлен план, visual/native acceptance нового арта — **not-run**.

## Дополнение: первая реализация передана интегратору

2026-10-12, после отдельного назначения реализации. Исторический аудит выше сохранён. Реализованы `CitizenSprites`, `OriginalCitizenAtlas`, `OriginalCitizenTools`, `OriginalCitizenArtImporter` и точечная связь WorldView. Одежда — отдельный `CitizenWardrobe` r2 от simulation-агента; root обновляет HUD и создаёт native gallery.

Фактические исходники оказались1254×1254, а не равномерной клеточной сеткой. Загрузчик теперь сегментирует восемь полос и восемь фигур в каждой по alpha≥128; независимое чтение через Pillow подтвердило8×8 у Human/Elf/Dwarf/Orc. Изображения не перезаписываются. Для каждого appearance вычисляется общий масштаб по максимальным bounds16кадров, фигуры центрируются и выравниваются по стопам в32×32. Потенциальная дрожь из-за center-of-bounds требует native клипа, математическая проверка не закрывает художественную приёмку.

Startup pack:256bodyframes ×8costume slots плюс64toolframes; итоговый atlas1024×2112, RGBA8.25MiB, Point/Clamp/noMip. СлотыNone/Guard/все дети получают полностью прозрачную одежду r2, сохраняя исходный наряд. Source PNG unload после копирования, atlas освобождается через Dispose. Сортировка жителей по presentationY/ID использует переиспользуемый список; body и одежда скомпонованы в одном frame, инструмент рисуется внутри того же отсортированного субъекта. Все четыре цивилизованные расы проходят CitizenSprites, животные сохранены в прежних путях.

**Фактическое покрытие:** одна исходная четырёхсторонняя походка по4кадра на каждую из16демографических/расовых форм; idle удерживает frame1. Производные рабочие семейства — топор/кирка/мотыга/молот, каждое имеет4направления и4фазы взмаха. Отдельные процедурные позы/жесты еды, отдыха, общения, игры, учёбы, питья, рыбалки и обслуживания не называются полными авторскими клипами. Semantic ActionLabel охватывает работающие задачи, но число enum не является количеством уникальных анимаций и не доказывает соответствие WorldBox. До настоящего урона нет выдуманной фазы атаки; после hit — impact/recovery по AttackTimer. Нового damage/inventory/RNG нет.

Рабочий инструмент отображается только по разрешённому неподвижному action: дерево/минерал с положительным запасом и допустимым Fire, реальный WorkTimer/EquipmentProgress или завершённая доставка материалов и присутствие у TransportProject. На пути, при пустом узле/огне, прекращении работы и у детей инструмента нет. Это примитивные орудия видимого труда, а не новая система собственности или бесплатное боевое снаряжение. Северный инструмент находится за телом, остальные перед телом. Груз/верёвка/ведро привязаны к новым положениям рук для всех четырёх рас; реальные владельцы корзины/животного/телеги по-прежнему определяются сохранённым состоянием.

Публичный UI API сохранён: `CreateHumanIcon`, `CreatePortrait`, `CreateAppearanceSprite`. Добавлены `WorldView.CreateCitizenIcon(species)` и `CitizenSprites.CreateCitizenSprite(species,appearance,direction,frame=1,profession=None)`, возвращающие Sprite из используемой production texture; Sprite освобождает вызывающий. Новые `Pose.Action`, `Direction`, `DerivedGesture`, `ToolFrame`, `SourceTask` служат проверке/галерее. Старый Row — только совместимый смысловой hint; FlipX всегдаfalse, поскольку четыре стороны нарисованы отдельно. Reserved Warrior не создаёт доспехи и не меняет пол взрослого.

Проверено до Unity: все Runtime `.cs` с wardrobe r2 скомпилированы в памяти Add-Type против реальных Unity6000модулей и Simulation DLL0.5.1;0ошибок. Unity/Player при этом **не запускался**. `RuntimeIntegrationTests` обновлён по semantic action/West вместо прежнегоFlipX/Guardrow6; добавлен один PlayMode на256отдельных rect и production batch всех четырёх рас, повторную отрисовку на паузе и неизменность Tick/RNG. Эти новые проверки подготовлены, **ещё not-run**. Нативная посадка одежды, инструменты, масштаб, плотная толпа и профиль ожидают интегратора. Готовность к первой сборке не означает художественного принятия.
