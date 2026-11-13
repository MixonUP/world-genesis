# Контракты данных `ags.research-registry/1`

Каноническими данными считаются JSON-файлы. Markdown-таблицы и граф генерируются из них. Graph database и vector service не используются.

| Файл | Схема | Чем проверяется |
|---|---|---|
| `examples/*.json`, будущие реестры | [schemas/research-registry.schema.json](schemas/research-registry.schema.json) | `python validator.py check` |
| `COMPONENT_TRACEABILITY.json` | [schemas/component-traceability.schema.json](schemas/component-traceability.schema.json) | `python validator.py trace` |
| `SOURCE_REGISTER.json` | `ags.source-register/1` (Source-сущности той же схемы) | `python validator.py sources` |

Версия схемы указана явно. Неизвестная версия даёт `INVALID` с перечнем поддерживаемых версий (exit 2).

## 1. Сущности

У каждой записи есть `type`, `id`, `title` и необязательные `record_status`, `status_reason`, `notes`, `synthetic`. Неизвестные ключи отвергаются. Обязательный ключ может иметь значение `null`; это значит «неизвестно». Такое значение попадает в отчёт `unknown_values` и никогда не превращается в 0 или false.

| Тип | Префикс ID | Ключевые поля | Важные правила |
|---|---|---|---|
| Source | `SRC-` | locator, source_kind, author, published, accessed, version, access_method, study_depth, relevance, reliability, rights, limitations[≥1] | relevance ∈ {1.2, 1.0, 0.7, null}; oEmbed → только `metadata`; видео: `video_layers`, таймкоды только при реальном просмотре (`observed_by/on`); GitHub: `repository{commit_or_tag, license_spdx…}`; сообщество: `contacted` строго false |
| Claim | `CLM-` | statement, claim_kind, source_refs[{source, locator}]≥1, scope, confidence, confidence_basis, verification, limitations, recheck{reason, due} | confidence ≠ null требует основания; source_refs разрешаются в Source |
| RequirementRef | `Cxx-Rxx` | component, gate, catalog{path, revision, sha256_lf}, checked_property, accepted_evidence_classes, target_revision, requires_independent_review | ID и gate сверяются с каталогом; текст требования **не копируется** (ключ `statement` запрещён) |
| Capability | `CAP-` | name, cap_kind, location, version, owner_agent, inputs, outputs, declared, available, benchmarked, invocation_verified, external_action_authorized, dependencies, limitations | `invocation_verified` требует `available=verified_today` |
| Experiment | `EXP-` | question, hypothesis, baseline, protocol, success_criterion, criterion_fixed_on, sample{n, unit}, resource_bounds{cash, currency, hours}, experiment_status, completed_on, results, limitations | completed требует completed_on и results; критерий после завершения → `EXPERIMENT_CRITERION_LATE` |
| Artifact | `AFT-` | kind, locator, revision, sha256, origin, license{status, terms, scope}, lifecycle | accepted + unknown/restricted rights → `RIGHTS_BLOCK_ACCEPTANCE`; accepted без человека → `ACCEPTANCE_UNSUPPORTED` |
| Evidence | `EVD-` | criterion, artifact_ref, artifact_revision, evidence_class, method, evidence_status, result, date, valid_until, producer, reviewer, observations, publication, limitations | prepared/failed_to_run → result обязан быть UNKNOWN; valid_until ≥ date |
| Decision | `DEC-` | question, options[≥2], assumptions, decision, decision_status, unknowns, owner, revisit_when, resolves[Claim] | accepted требует текста решения; только accepted разрешает противоречие |
| WorkPacket | `WP-` | goal, owner, owned_paths, inputs, outputs, acceptance, dependencies_note, stop_conditions, evidence_plan, handoff, wp_status | owned_paths — безопасные репозиторные пути; пересечения → `OWNERSHIP_OVERLAP` |

## 2. Рёбра

Направление всегда `from → to`.

| Тип | from | to | Семантика | Цикл |
|---|---|---|---|---|
| `supports` | Source, Claim, Evidence | Claim, Decision | from — основание принять to | допустим (граф знаний) |
| `contradicts` | Claim, Evidence | Claim | несовместимы; обе записи сохраняются до accepted Decision.resolves | допустим |
| `derived_from` | Claim, Artifact, Decision, Experiment, WorkPacket, Evidence | Source, Claim, Artifact, Evidence, Decision, Experiment | from получен преобразованием to (PROV `wasDerivedFrom`) | **ошибка** `PROVENANCE_CYCLE` |
| `implements` | WorkPacket, Artifact, Capability, Experiment | Decision, RequirementRef, Experiment, WorkPacket | from реализует намерение to; **не** доказательство | допустим |
| `verified_by` | RequirementRef, Claim, Artifact, Experiment, Decision | Evidence | to — результат проверки from | допустим |
| `depends_on` | WorkPacket, Experiment, Artifact | WorkPacket, Experiment, Artifact, Capability, Decision | from нельзя начать/закончить до to; **исполнимый DAG** | **ошибка** `DEPENDENCY_CYCLE`, порядок не строится |
| `produced_by` | Artifact, Evidence | Capability, WorkPacket, Experiment | from создан активностью/инструментом to (PROV `wasGeneratedBy`) | допустим |
| `supersedes` | любой тип | тот же тип | from заменяет to, история сохраняется (ADR) | **ошибка** `PROVENANCE_CYCLE` |
| `invalidates` | Evidence, Claim, Decision, Artifact, Capability, Source | Claim, Evidence, Decision, Artifact, Capability | to нельзя использовать как основание до перепроверки (PROV `wasInvalidatedBy`) | допустим |

Самопетли и дубли рёбер запрещены. Висячие ссылки — ошибка (exit 2).

## 3. Вердикт требования

Evidence, связанное `RequirementRef --verified_by--> Evidence`, **квалифицируется** только если выполнены все условия:

1. не synthetic (ни запись, ни её Artifact, ни весь реестр);
2. `evidence_status = executed`. Значения `historical_import`, `prepared`, `failed_to_run` не квалифицируются;
3. класс входит в `accepted_evidence_classes` и не равен `metadata_review`;
4. сама запись пригодна: не `draft/stale/superseded/withdrawn`, без входящего `invalidates` и без входящего `supersedes`. Ребро `supersedes` действует даже без смены `record_status`;
5. `date ≤ as_of ≤ valid_until`. Если `valid_until` не указан, запись не квалифицируется;
6. есть `artifact_ref`. Artifact пригоден по тем же правилам, что в п. 4, и его `revision` равен `Evidence.artifact_revision`;
7. `target_revision` известен и равен `artifact_revision`;
8. при `requires_independent_review`: reviewer указан и не совпадает с producer (сравнение без учёта регистра и пробелов).

Те же правила пригодности (пп. 1–6) применяются в трёх местах:
- в запросе `promises`;
- при проверке принятия артефакта. Здесь дополнительно нужны класс `human_*`, `artifact_ref` на этот же артефакт и независимый reviewer;
- при разрешении противоречий: Decision должна быть пригодной.

Итог: **FAIL**, если есть квалифицированный FAIL; иначе **PASS**, если есть квалифицированный PASS; иначе **OPEN** с причинами. Исторический PASS отражается флагом `historical_pass`, но требование не закрывает.

## 4. Исполняется vs документируется

| Правило | validator.py | Только в документации |
|---|---|---|
| точные наборы ключей, enum, даты, типы, ID-префиксы, уникальность | ✔ | |
| разрешение ссылок, допустимые (from,to) рёбер | ✔ | |
| сверка ID/gate/хэша с каталогом | ✔ (с `--catalog`) | |
| циклы `depends_on` / `derived_from+supersedes` | ✔ | |
| квалификация evidence (выше) | ✔ | |
| противоречия, unknown, права, capability, ownership, предрегистрация | ✔ | |
| лексическая безопасность locator | ✔ `check_locator` | |
| sha256 артефакта под явным корнем + resolved-containment | ✔ `--artifact-root` | |
| containment вывода (внутри модуля, не вход, .json/.md, без перезаписи без `--overwrite`) | ✔ `safe_output` | |
| JSON Schema `format`, `$ref`-валидация движком | | ✔ (движок не используется; `format` по стандарту 2020-12 и так аннотация) |
| истинность утверждений, подлинность подписи и личности | | ✔ (не проверяется никогда) |
| адаптеры submit/status/cancel/reconcile | | ✔ (spec R09) |

**Read-only проверка пути и containment вывода — две разные проверки.**
- `check_locator` ничего не открывает. Он отвергает `..`, абсолютные пути, буквы дисков, `\`, `~`, NUL, не-https схемы и учётные данные в URL.
- Чтение артефакта для хэша идёт только под корнем, явно переданным в `--artifact-root`. Разрешённый путь, включая symlink, должен остаться внутри этого корня.
- Запись допустима только внутри каталога модуля (`safe_output`).

### Известные ограничения (после review)

- **`as_of`** по умолчанию берётся из реестра. Реестр с «будущим» `as_of` может сделать будущее evidence текущим. Для решений передавайте `--as-of` явно. В отчёте всегда виден применённый `as_of`.
- **Противоречие Evidence → Claim** считается разрешённым, если accepted Decision разрешает Claim. Сам Evidence в `resolves` не перечисляется.
- **`module_checks` в матрице трассировки** проверяются на существование кода, а не на смысловое соответствие требованию. Соответствие подтверждает reviewer.
- **Ownership** сравнивается лексически после нормализации: `./`, `**`, регистр. Реальные писатели и git не проверяются.
- **Вывод на консоль** всегда в UTF-8, независимо от code page.

## 5. Примеры запросов

Все команды запускаются из корня модуля. Ниже — фактические выводы на реальной цепочке, кратко.

**Почему принято решение?**
```bash
python validator.py query examples/portfolio_chain.real.json why DEC-SEPARATE-RELEVANCE
```
→ DEC ← supports CLM-SCALE-OBSERVED и CLM-SCALE-POLICY. Они противоречат друг другу; ссылки ведут на SRC-LIB-SOURCES-JSON и SRC-LIB-README.

**Что устарело после смены модели или движка?**
```bash
python validator.py query examples/portfolio_chain.real.json impact CAP-UNITY-6000
```
→ EVD-HIST-BUILD, EVD-HIST-EDITMODE-40, EXP-RERUN-EDITMODE, WP-LAB-VISUAL-NOTRUN (расстояние 1) → C07-R03, CLM-SAVE-VALIDATES, CLM-PROMISE-…, WP-LAB-HUMAN-ART (2) → DEC-PORTFOLIO-SCOPE (3) → WP-LAB-CONTRAST (4).

**Какие требования не имеют evidence нужной ревизии?**
```bash
python validator.py query examples/portfolio_chain.real.json requirement-gaps --catalog ../../Orchestrator/COMPONENT_REQUIREMENTS.json
```
→ C05-R03 OPEN (evidence нет); C07-R03 OPEN (`historical_import_not_rerun`, `no_validity_limit_recorded`); C08-R02 OPEN (`class_integrity_check_not_accepted`).

**Какой навык лишь заявлен?**
```bash
python validator.py query examples/portfolio_chain.real.json declared-only
```
→ CAP-SKILL-ART-ACCEPT, CAP-SKILL-GSO (`not_checked`), CAP-UNITY-6000 (`historical`).

**Какие обещания портфолио подтверждены?**
```bash
python validator.py query examples/portfolio_chain.real.json promises
```
→ CLM-PROMISE-BOUNDED-UI-SAVE: `historical_support_only`. Текущих executed-проверок поведения нет.

## 6. Exit codes

- `0` — отчёт построен. Открытые вопросы при этом возможны.
- `1` — семантические ошибки (цикл, неизвестное требование, synthetic accepted, несогласованная capability, несовпадение хэша) или несогласованная матрица.
- `2` — нечитаемый, небезопасный или структурно некорректный вход.
