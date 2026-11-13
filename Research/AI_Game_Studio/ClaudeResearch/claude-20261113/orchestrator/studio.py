"""Studio orchestrator: validate studio_state.json and render Obsidian notes.

Usage (from the module directory):
    python orchestrator/studio.py check  --data <DATA>
    python orchestrator/studio.py render --data <DATA>     # writes <DATA>/obsidian_pack/
    python orchestrator/obsidian_sync.py <DATA>/obsidian_pack/manifest.json --vault <vault>          # dry run
    python orchestrator/obsidian_sync.py <DATA>/obsidian_pack/manifest.json --vault <vault> --apply  # write

<DATA> holds studio_state.json, notes/*.md, appends.json and appends/. Real studio
data is PRIVATE and lives outside the public repository (in the vault under
01_Project/AI_Game_Studio/.studio_data/, hidden from Obsidian). The repository
ships only orchestrator/example_data (synthetic) for tests.

Standard library only; no network, subprocess or GUI. Rendering is deterministic
(no wall-clock timestamps), so repeated runs give identical notes.
"""
from __future__ import annotations

import argparse
import json
from pathlib import Path
import re
import sys

HERE = Path(__file__).resolve().parent
STUDIO = "01_Project/AI_Game_Studio"

V_STATUS = {"GO", "CONDITIONAL", "BLOCKED", "NO-GO", "UNKNOWN"}
T_STATUS = {"todo", "doing", "blocked", "done", "dropped"}
OWNERS = {"user", "claude", "codex", "grok"}
D_STATUS = {"proposed", "accepted", "superseded", "rejected"}
LEVELS = {"high", "medium", "low"}
AREAS = {"architecture": "Архитектура производства", "research": "Исследование: что строить",
         "orchestrator": "Оркестратор", "resources": "Ресурсы"}
STATUS_RU = {"todo": "сделать", "doing": "в работе", "blocked": "заблокировано", "done": "готово",
             "dropped": "отменено"}
OWNER_RU = {"user": "Михаил", "claude": "Claude", "codex": "Codex", "grok": "Grok"}


class StateError(ValueError):
    pass


def slug(text):
    s = re.sub(r"[^0-9A-Za-zА-Яа-яЁё]+", "_", text).strip("_")
    return s[:48]


def load_state(path):
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        raise StateError(f"Cannot read state: {exc}") from exc
    if data.get("schema") != "ags.studio-state/1":
        raise StateError("Unsupported studio state schema")
    ids = set()

    def uid(i, where):
        if not isinstance(i, str) or not i or i in ids:
            raise StateError(f"{where}: missing or duplicate id {i!r}")
        ids.add(i)

    for v in data["verdicts"]:
        uid(v["id"], "verdict")
        if v["status"] not in V_STATUS or v["area"] not in AREAS:
            raise StateError(f"{v['id']}: bad status/area")
        if v["confidence"] not in LEVELS or v["evidence_strength"] not in LEVELS:
            raise StateError(f"{v['id']}: confidence/evidence_strength must be high/medium/low")
        for k in ("basis", "resources", "needs", "better"):
            if not isinstance(v.get(k), list) or not v[k]:
                raise StateError(f"{v['id']}.{k}: nonempty list required")
        for r in v.get("reviews", []):
            if not {"by", "verdict", "points"} <= set(r):
                raise StateError(f"{v['id']}: review needs by/verdict/points")
    vids = {v["id"] for v in data["verdicts"]}
    for t in data["tasks"]:
        uid(t["id"], "task")
        if t["status"] not in T_STATUS or t["owner"] not in OWNERS:
            raise StateError(f"{t['id']}: bad status/owner")
        if t["verdict"] not in vids:
            raise StateError(f"{t['id']}: unknown verdict {t['verdict']}")
    tids = {t["id"] for t in data["tasks"]}
    for t in data["tasks"]:
        for d in t["depends_on"]:
            if d not in tids:
                raise StateError(f"{t['id']}: unknown dependency {d}")
    # cycle check on task dependencies
    seen, stack = {}, []

    def visit(n):
        seen[n] = 1
        for m in next(t for t in data["tasks"] if t["id"] == n)["depends_on"]:
            if seen.get(m) == 1:
                raise StateError(f"Task dependency cycle through {m}")
            if m not in seen:
                visit(m)
        seen[n] = 2

    for t in data["tasks"]:
        if t["id"] not in seen:
            visit(t["id"])
    for d in data["decisions"]:
        uid(d["id"], "decision")
        if d["status"] not in D_STATUS:
            raise StateError(f"{d['id']}: bad status")
    for g in data["games"] + data["services"]:
        uid(g["id"], "game/service")
    return data


# ------------------------------------------------------------------ rendering
def fm(title, typ, tags, extra=None):
    lines = ["---", f"title: {title}", f"type: {typ}", "tags:"] + [f"- {t}" for t in tags]
    for k, v in (extra or {}).items():
        lines.append(f"{k}: {v}")
    return "\n".join(lines + ["---", ""])


def vnote(v):
    return f"{STUDIO}/Verdicts/{v['id']}_{slug(v['title'])}"


def task_ready(t, tasks):
    done = {x["id"] for x in tasks if x["status"] == "done"}
    return t["status"] == "todo" and all(d in done for d in t["depends_on"])


def render_verdict(v, data):
    tasks = [t for t in data["tasks"] if t["verdict"] == v["id"]]
    out = [fm(f"{v['id']} — {v['title']}", "verdict", ["ai-game-studio", "verdict", v["area"]],
              {"verdict_status": v["status"], "confidence": v["confidence"], "as_of": data["as_of"]})]
    out.append(f"# {v['id']} — {v['title']}\n")
    out.append(f"Раздел: [[{STUDIO}/00_HUB|Студия]] → {AREAS[v['area']]} · Все вердикты: [[{STUDIO}/Verdicts/00_Verdicts|список]]\n")
    out.append(f"**Вердикт: {v['status']}** — {data['verdict_statuses'][v['status']]}. "
               f"Уверенность: {v['confidence']}; сила доказательств: {v['evidence_strength']}.\n")
    out.append(v["verdict"] + "\n")
    for head, key in (("Основания", "basis"), ("Ресурсы", "resources"), ("Что для этого нужно", "needs"),
                      ("Что можно сделать лучше", "better")):
        out.append(f"## {head}\n")
        out += [f"- {x}" for x in v[key]]
        out.append("")
    out.append("## Независимая проверка\n")
    if v.get("reviews"):
        for r in v["reviews"]:
            out.append(f"- **{r['by']}**: {r['verdict']}")
            out += [f"  - {p}" for p in r["points"]]
            if r.get("response"):
                out.append(f"  - Ответ: {r['response']}")
    else:
        out.append("- Не проводилась.")
    out.append("")
    out.append("## Задачи\n")
    if tasks:
        for t in tasks:
            out.append(f"- {t['id']} [{STATUS_RU[t['status']]}] {t['title']} — {OWNER_RU[t['owner']]}")
    else:
        out.append("- Нет.")
    return "\n".join(out) + "\n"


def table(rows, head):
    lines = ["| " + " | ".join(head) + " |", "|" + "---|" * len(head)]
    lines += ["| " + " | ".join(str(c).replace("|", "\\|") for c in r) + " |" for r in rows]
    return "\n".join(lines)


def render_dashboard(data):
    tasks = data["tasks"]
    out = [fm("AI Game Studio — дашборд", "dashboard", ["ai-game-studio", "dashboard"], {"as_of": data["as_of"]})]
    out.append("# Дашборд студии\n")
    out.append(f"Состояние на {data['as_of']}. Генерируется из `studio_state.json`; руками не править — "
               f"писать в [[{STUDIO}/Inbox/00_Inbox|почту студии]].\n")
    m = data.get("external_metrics", {})
    out.append("## Внешний результат (главное)\n")
    out.append(table([[m.get("applications_sent", 0), m.get("replies", 0), m.get("paid_orders", 0),
                       f"${m.get('usd_received', 0)}", m.get("games_with_store_page", 0)]],
                     ["Откликов отправлено", "Ответов", "Оплаченных заказов", "Получено", "Игр со страницей"]))
    out.append("\nОркестратор заморожен до первого оплаченного заказа или продажи (D07).\n")
    out.append("## Следующие шаги (можно начинать)\n")
    ready = [t for t in tasks if task_ready(t, tasks)]
    out.append(table([[t["id"], t["title"], OWNER_RU[t["owner"]], t["cost"],
                       f"[[{vnote(next(v for v in data['verdicts'] if v['id'] == t['verdict']))}|{t['verdict']}]]"]
                      for t in ready], ["ID", "Задача", "Кто", "Цена", "Вердикт"]))
    out.append("\n## Ждут Михаила\n")
    mine = [t for t in tasks if t["owner"] == "user" and t["status"] in {"todo", "blocked"}]
    out.append(table([[t["id"], t["title"], STATUS_RU[t["status"]], t["cost"]] for t in mine],
                     ["ID", "Задача", "Статус", "Цена"]))
    out.append("\n## В работе и заблокировано\n")
    other = [t for t in tasks if t["status"] in {"doing", "blocked"} or (t["status"] == "todo" and not task_ready(t, tasks))]
    out.append(table([[t["id"], t["title"], OWNER_RU[t["owner"]], STATUS_RU[t["status"]],
                       ", ".join(t["depends_on"]) or "—"] for t in other],
                     ["ID", "Задача", "Кто", "Статус", "Зависит от"]))
    out.append("\n## Вердикты\n")
    out.append(table([[f"[[{vnote(v)}|{v['id']}]]", v["title"], v["status"], v["confidence"]]
                      for v in data["verdicts"]], ["ID", "Компонент/этап", "Вердикт", "Уверенность"]))
    out.append("\n## Решения\n")
    out.append(table([[d["id"], d["title"], d["status"], d["revisit"]] for d in data["decisions"]],
                     ["ID", "Решение", "Статус", "Пересмотр"]))
    out.append("\n## Готово\n")
    done = [t for t in tasks if t["status"] == "done"]
    out.append(table([[t["id"], t["title"], OWNER_RU[t["owner"]]] for t in done], ["ID", "Задача", "Кто"])
               if done else "- Пока нет.")
    return "\n".join(out) + "\n"


def render_verdict_index(data):
    out = [fm("AI Game Studio — вердикты", "index", ["ai-game-studio", "verdict"])]
    out.append("# Вердикты по этапам и компонентам\n")
    out.append("Статусы: " + "; ".join(f"**{k}** — {v}" for k, v in data["verdict_statuses"].items()) + ".\n")
    for area, name in AREAS.items():
        vs = [v for v in data["verdicts"] if v["area"] == area]
        if not vs:
            continue
        out.append(f"## {name}\n")
        out += [f"- [[{vnote(v)}|{v['id']} {v['title']}]] — **{v['status']}**" for v in vs]
        out.append("")
    return "\n".join(out)


def render_hub(data):
    counts = {}
    for v in data["verdicts"]:
        counts[v["status"]] = counts.get(v["status"], 0) + 1
    out = [fm("AI Game Studio — главная", "hub", ["ai-game-studio", "hub"], {"as_of": data["as_of"]})]
    out.append("# AI Game Studio — главная\n")
    out.append(f"**Цель.** {data['goal']}\n")
    out.append("## Куда смотреть\n")
    out += [
        f"- [[{STUDIO}/Dashboard|Дашборд]] — что делаем сейчас, что ждёт Михаила, статусы.",
        f"- [[{STUDIO}/Verdicts/00_Verdicts|Вердикты]] — по каждому этапу и компоненту: делаем/условие/блок.",
        f"- [[{STUDIO}/Architecture|Архитектура производства игр]]",
        f"- [[{STUDIO}/Research_Funnel|Исследование: как выбрать игру или сервис]]",
        f"- [[{STUDIO}/Agents_Protocol|Оркестратор и протокол агентов (Claude, Codex, Grok)]]",
        f"- [[{STUDIO}/Resources_and_Accounts|Ресурсы, бюджет и аккаунты — инструкции]]",
        *[f"- [[{STUDIO}/{h['note']}|{h['title']}]]" for h in data.get("hub_links", [])],
        f"- [[{STUDIO}/Sources|Источники]]",
        f"- [[{STUDIO}/Games/00_Games|Игры]] · [[{STUDIO}/Services/00_Services|Услуги]]",
        f"- [[{STUDIO}/Inbox/00_Inbox|Почта студии]]",
        f"- История и библиотека: [[01_Project/AI_Game_Studio_Research|AI Game Studio — журнал Codex]]",
    ]
    out.append("\n## Сводка вердиктов\n")
    out.append(", ".join(f"{k}: {counts[k]}" for k in sorted(counts)) + ".\n")
    rs = data.get("review_summary")
    if rs:
        out.append(f"**Независимая проверка — {rs['by']}:** {rs['overall']} Проверка фактов: {rs['fact_checks']}.\n")
    ready = [t for t in data["tasks"] if task_ready(t, data["tasks"])][:5]
    out.append("## Ближайшие шаги\n")
    out += [f"- {t['id']} — {t['title']} ({OWNER_RU[t['owner']]})" for t in ready]
    out.append("\n## Как обновлять\n")
    out.append("Скажи агенту: **«синхронизируй студию»**. Агент меняет `studio_state.json` в репозитории, "
               "запускает `studio.py render`, затем `obsidian_sync.py` (сначала пробный прогон). "
               "Сгенерированные заметки руками не правим — правки и вопросы пишем в почту студии.")
    return "\n".join(out) + "\n"


def render_collection(kind, items, data):
    name = "Игры" if kind == "Games" else "Услуги"
    out = [fm(f"AI Game Studio — {name.lower()}", "index", ["ai-game-studio", kind.lower()])]
    out.append(f"# {name}\n")
    out.append(f"Каждая {'игра' if kind == 'Games' else 'услуга'} — своя папка `{STUDIO}/{kind}/<имя>/` "
               "с карточкой `00_<имя>.md`. Новые кандидаты появляются после этапов V09–V10.\n")
    for it in items:
        out.append(f"- [[{STUDIO}/{kind}/{slug(it['title'])}/00_{slug(it['title'])}|{it['title']}]] — "
                   f"{it['status']}: {it['summary']}")
    return "\n".join(out) + "\n"


def render_item(kind, it, data):
    out = [fm(it["title"], "game" if kind == "Games" else "service",
              ["ai-game-studio", kind.lower()], {"item_status": it["status"]})]
    out.append(f"# {it['title']}\n")
    out.append(f"[[{STUDIO}/{kind}/00_{kind}|← список]]\n")
    out.append(f"**Статус:** {it['status']}\n\n{it['summary']}\n")
    if it.get("note"):
        out.append(f"Основная заметка проекта: [[{it['note']}]]\n")
    out.append("## Карточка (заполняется по мере исследования)\n")
    out += ["- Аудитория / покупатель:", "- Сопоставимые (включая провалы):", "- Оценка спроса и её погрешность:",
            "- Стоимость производства (часы, $):", "- Решение и вердикт:", "- Следующий эксперимент:"]
    return "\n".join(out) + "\n"


def render(data, data_dir):
    files = {}
    files[f"{STUDIO}/00_HUB.md"] = render_hub(data)
    files[f"{STUDIO}/Dashboard.md"] = render_dashboard(data)
    files[f"{STUDIO}/Verdicts/00_Verdicts.md"] = render_verdict_index(data)
    for v in data["verdicts"]:
        files[vnote(v) + ".md"] = render_verdict(v, data)
    for kind, items in (("Games", data["games"]), ("Services", data["services"])):
        files[f"{STUDIO}/{kind}/00_{kind}.md"] = render_collection(kind, items, data)
        for it in items:
            files[f"{STUDIO}/{kind}/{slug(it['title'])}/00_{slug(it['title'])}.md"] = render_item(kind, it, data)
    files[f"{STUDIO}/Inbox/00_Inbox.md"] = "\n".join([
        fm("AI Game Studio — почта студии", "index", ["ai-game-studio", "agents", "inbox"]),
        "# Почта студии\n",
        f"Правила — [[{STUDIO}/Agents_Protocol|протокол агентов]]. Новые сообщения — сверху, формат "
        "`## дата время MSK — от кого → кому: суть`.\n",
        f"- [[{STUDIO}/Inbox/Codex|Codex]] — сообщения для Codex",
        f"- [[{STUDIO}/Inbox/Claude|Claude]] — сообщения для Claude",
        f"- [[{STUDIO}/Inbox/Grok|Grok]] — задачи генерации для Grok",
        f"- [[{STUDIO}/Inbox/Mikhail|Михаил]] — вопросы и решения, которые ждут Михаила",
        "", "Общая почта всех проектов: [[01_Project/Agent_Inbox|Agent_Inbox]].", ""])
    for src in sorted((data_dir / "notes").glob("*.md")):
        files[f"{STUDIO}/{src.name}"] = src.read_text(encoding="utf-8").replace("\r\n", "\n")
    return files


def write_pack(files, appends, data_dir):
    PACK = data_dir / "obsidian_pack"
    if PACK.exists():
        for p in sorted(PACK.rglob("*"), reverse=True):
            if p.is_file() and p.suffix in {".md", ".json"}:
                p.unlink()
    managed = []
    for dst, text in sorted(files.items()):
        rel = "notes/" + dst
        target = PACK / rel
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(text, encoding="utf-8", newline="\n")
        managed.append({"src": rel, "dst": dst})
    app = []
    for a in appends:
        rel = f"appends/{a['marker']}.md"
        (PACK / "appends").mkdir(parents=True, exist_ok=True)
        (PACK / rel).write_text(a["text"], encoding="utf-8", newline="\n")
        item = {"src": rel, "dst": a["dst"], "marker": a["marker"]}
        for k in ("anchor", "create_title"):
            if a.get(k):
                item[k] = a[k]
        app.append(item)
    manifest = {"schema": "ags.obsidian-sync/1", "default_eol": "crlf",
                "description": "Generated by orchestrator/studio.py render; do not edit by hand.",
                "allowed_prefixes": [f"{STUDIO}/", "01_Project/AI_Game_Studio_Research.md",
                                     "01_Project/Agent_Inbox.md", "07_Journal/", "00_HOME.md"],
                "managed": managed, "appends": app}
    (PACK / "manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n",
                                        encoding="utf-8", newline="\n")
    return len(managed), len(app)


def load_appends(data_dir):
    path = data_dir / "appends.json"
    if not path.is_file():
        return []
    items = json.loads(path.read_text(encoding="utf-8"))
    for a in items:
        src = data_dir / a.pop("text_file")
        a["text"] = src.read_text(encoding="utf-8").replace("\r\n", "\n")
    return items


def main(argv=None):
    p = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    p.add_argument("command", choices=["check", "render"])
    p.add_argument("--data", type=Path, required=True, help="studio data directory (private for real data)")
    args = p.parse_args(argv)
    try:
        data_dir = args.data.resolve()
        data = load_state(data_dir / "studio_state.json")
        if args.command == "check":
            print(json.dumps({"status": "VALID", "verdicts": len(data["verdicts"]), "tasks": len(data["tasks"]),
                              "decisions": len(data["decisions"])}, ensure_ascii=False))
            return 0
        n, m = write_pack(render(data, data_dir), load_appends(data_dir), data_dir)
        print(json.dumps({"status": "RENDERED", "managed_notes": n, "appends": m}, ensure_ascii=False))
        return 0
    except (StateError, KeyError, TypeError) as exc:
        print(json.dumps({"status": "INVALID", "error": f"{type(exc).__name__}: {exc}"}, ensure_ascii=False))
        return 2


if __name__ == "__main__":
    sys.exit(main())
