"""Offline research-registry validator (ags.research-registry/1).

Python 3.10+ standard library only. Reads only files named on the command line,
performs no network, subprocess, GUI or agent actions, and writes reports only
inside this module's own directory. A PASS here means "the supplied records are
internally consistent and satisfy the stated rules"; it never authenticates
evidence, rights or identities.
"""
from __future__ import annotations

import argparse
from collections import defaultdict, deque
from datetime import date
from decimal import Decimal
import functools
import hashlib
import json
from pathlib import Path, PurePosixPath
import re
import sys
from urllib.parse import urlparse

MODULE_ROOT = Path(__file__).resolve().parent
VALIDATOR_VERSION = "0.1.0"
REGISTRY_SCHEMA = "ags.research-registry/1"
TRACE_SCHEMA = "ags.component-traceability/1"
SOURCE_REGISTER_SCHEMA = "ags.source-register/1"
SUPPORTED_SCHEMAS = (REGISTRY_SCHEMA, TRACE_SCHEMA, SOURCE_REGISTER_SCHEMA)
MAX_INPUT_BYTES = 5 * 1024 * 1024
REQ_ID = re.compile(r"C[0-9]{2}-R[0-9]{2}")
COMPONENT_ID = re.compile(r"C[0-9]{2}")
R_BLOCKS = tuple(f"R{i:02d}" for i in range(12))
GATES = tuple(f"G{i}" for i in range(7))
HEX64 = re.compile(r"[0-9a-f]{64}")
TIMECODE = re.compile(r"\d{2}:\d{2}:\d{2}")


class RegistryError(ValueError):
    """Unreadable, unsafe or structurally invalid input (exit code 2)."""


def _structural(func):
    """Convert unexpected type errors from malformed-but-parseable JSON into RegistryError."""
    @functools.wraps(func)
    def wrapper(*args, **kwargs):
        try:
            return func(*args, **kwargs)
        except RegistryError:
            raise
        except (TypeError, KeyError, AttributeError, ValueError, RecursionError, IndexError) as exc:
            raise RegistryError(f"Malformed input structure in {func.__name__}: "
                                f"{type(exc).__name__}: {exc}") from exc
    return wrapper


# --------------------------------------------------------------------------
# Contract tables. These are the parts of schemas/*.schema.json that this
# validator actually executes. CONTRACTS.md lists what is documentation only.
# --------------------------------------------------------------------------
COMMON_FIELDS = {"type", "id", "title", "record_status", "notes", "synthetic",
                 "status_reason"}
RECORD_STATUS = {"draft", "active", "stale", "superseded", "withdrawn"}
ENUMS = {
    "source_kind": {"official_doc", "specification", "license", "code_repository", "video",
                    "talk", "paper", "article", "community", "repo_file", "local_library",
                    "model_card", "dataset"},
    "access_method": {"web_fetch", "git", "local_read", "oembed_metadata", "library_record",
                      "not_accessed"},
    "study_depth": {"not_accessed", "metadata", "abstract_or_description", "partial_text",
                    "transcript", "full_text", "reproduced"},
    "reliability": {"primary", "secondary", "self_report", "vendor_claim", "unknown"},
    "claim_kind": {"fact", "principle", "heuristic", "coefficient", "client_promise",
                   "assumption", "limitation"},
    "confidence": {"high", "medium", "low"},
    "verification": {"unverified", "source_read", "cross_checked", "reproduced", "refuted"},
    "cap_kind": {"skill", "cli", "mcp", "library", "service", "model", "engine", "script"},
    "available": {"verified_today", "historical", "not_checked", "unavailable"},
    "benchmarked": {"none", "historical", "current"},
    "experiment_status": {"proposed", "approved", "running", "completed", "abandoned"},
    "license_status": {"verified", "declared", "unknown", "restricted", "not_applicable"},
    "lifecycle": {"prepared", "built", "verified", "accepted"},
    "evidence_class": {"automated_test", "native_ui_check", "performance_measurement",
                       "machine_visual_review", "human_art_acceptance", "human_playtest",
                       "commercial_record", "source_inspection", "document_review",
                       "metadata_review", "integrity_check", "legal_opinion",
                       "requirement_check"},
    "evidence_status": {"prepared", "executed", "historical_import", "failed_to_run"},
    "result": {"PASS", "FAIL", "UNKNOWN"},
    "publication": {"public_ok", "internal_only", "redacted"},
    "decision_status": {"proposed", "accepted", "superseded", "rejected"},
    "wp_status": {"proposed", "ready", "in_progress", "done", "blocked"},
}
# Evidence classes that can never close an acceptance criterion by themselves.
NEVER_CLOSES = {"metadata_review"}
HUMAN_ACCEPTANCE = {"human_art_acceptance", "human_playtest"}
PROMISE_SUPPORT_CLASSES = {"automated_test", "native_ui_check", "performance_measurement",
                           "human_art_acceptance", "human_playtest", "commercial_record"}

# type -> (id prefix, required fields, optional fields)
ENTITY_SPECS = {
    "Source": ("SRC-", {"locator", "source_kind", "author", "published", "accessed", "version",
                        "access_method", "study_depth", "relevance", "reliability", "rights",
                        "limitations"},
               {"video_layers", "repository", "community"}),
    "Claim": ("CLM-", {"statement", "claim_kind", "source_refs", "scope", "confidence",
                       "verification", "limitations"},
              {"confidence_basis", "recheck"}),
    "RequirementRef": ("", {"component", "gate", "catalog", "checked_property",
                            "accepted_evidence_classes", "target_revision"},
                       {"requires_independent_review"}),
    "Capability": ("CAP-", {"name", "cap_kind", "location", "version", "owner_agent", "inputs",
                            "outputs", "declared", "available", "benchmarked",
                            "invocation_verified", "dependencies", "limitations"},
                   {"external_action_authorized"}),
    "Experiment": ("EXP-", {"question", "hypothesis", "baseline", "protocol",
                            "success_criterion", "criterion_fixed_on", "sample",
                            "resource_bounds", "experiment_status", "results", "limitations"},
                   {"completed_on"}),
    "Artifact": ("AFT-", {"kind", "locator", "revision", "sha256", "origin", "license",
                          "lifecycle"},
                 {"dependencies_note"}),
    "Evidence": ("EVD-", {"criterion", "artifact_ref", "artifact_revision", "evidence_class",
                          "method", "evidence_status", "result", "date", "valid_until",
                          "producer", "reviewer", "observations", "publication",
                          "limitations"},
                 set()),
    "Decision": ("DEC-", {"question", "options", "assumptions", "decision", "decision_status",
                          "unknowns", "owner", "revisit_when"},
                 {"resolves"}),
    "WorkPacket": ("WP-", {"goal", "owner", "owned_paths", "inputs", "outputs", "acceptance",
                           "dependencies_note", "stop_conditions", "evidence_plan", "handoff",
                           "wp_status"},
                   set()),
}
ENTITY_TYPES = set(ENTITY_SPECS)

# edge type -> (allowed from types, allowed to types, meaning)
EDGE_SPECS = {
    "supports": ({"Source", "Claim", "Evidence"}, {"Claim", "Decision"},
                 "from gives a reason to hold/accept to"),
    "contradicts": ({"Claim", "Evidence"}, {"Claim"},
                    "from is incompatible with to; both are kept until a Decision resolves them"),
    "derived_from": ({"Claim", "Artifact", "Decision", "Experiment", "WorkPacket", "Evidence"},
                     {"Source", "Claim", "Artifact", "Evidence", "Decision", "Experiment"},
                     "from was produced by transforming to (PROV wasDerivedFrom); acyclic"),
    "implements": ({"WorkPacket", "Artifact", "Capability", "Experiment"},
                   {"Decision", "RequirementRef", "Experiment", "WorkPacket"},
                   "from realises the intent of to; not proof that it works"),
    "verified_by": ({"RequirementRef", "Claim", "Artifact", "Experiment", "Decision"},
                    {"Evidence"}, "to is a check result about from"),
    "depends_on": ({"WorkPacket", "Experiment", "Artifact"},
                   {"WorkPacket", "Experiment", "Artifact", "Capability", "Decision"},
                   "from cannot start/complete before to; execution DAG, cycles block"),
    "produced_by": ({"Artifact", "Evidence"}, {"Capability", "WorkPacket", "Experiment"},
                    "from was generated by activity/tool to"),
    "supersedes": (ENTITY_TYPES, ENTITY_TYPES,
                   "from replaces to of the same type; history kept; acyclic"),
    "invalidates": ({"Evidence", "Claim", "Decision", "Artifact", "Capability", "Source"},
                    {"Claim", "Evidence", "Decision", "Artifact", "Capability"},
                    "from makes to unusable as a basis until rechecked"),
}
EXECUTION_EDGES = {"depends_on"}
PROVENANCE_EDGES = {"derived_from", "supersedes"}
# Impact propagation: if X changes, Y is affected when (Y -type-> X) or (X -type-> Y).
IMPACT_INCOMING = {"derived_from", "depends_on", "produced_by", "verified_by", "implements"}
IMPACT_OUTGOING = {"supports", "invalidates"}

CHECK_CODES = {
    "CONTRACT_VALIDATION": "entity/edge contract enforced; violation exits 2",
    "REF_RESOLUTION": "every id reference resolves to an entity of an allowed type",
    "UNSAFE_LOCATOR": "lexical read-only path/URL safety check of recorded locators",
    "OUTPUT_CONTAINMENT": "reports are written only inside the module directory",
    "OWNERSHIP_OVERLAP": "two open work packets with different owners claim overlapping paths",
    "REQ_PASS": "requirement has qualifying current PASS evidence",
    "REQ_FAIL": "requirement has qualifying FAIL evidence",
    "REQ_OPEN": "requirement has no qualifying evidence; reasons listed",
    "REQ_UNKNOWN_IN_CATALOG": "RequirementRef id absent from the supplied catalog",
    "CATALOG_NOT_SUPPLIED": "requirement existence cannot be checked without --catalog",
    "CATALOG_HASH_MISMATCH": "RequirementRef points to another catalog revision",
    "DEPENDENCY_CYCLE": "execution depends_on cycle; work order is blocked",
    "PROVENANCE_CYCLE": "derived_from/supersedes cycle; provenance is impossible",
    "CONTRADICTION_OPEN": "contradicting claims with no accepted resolving Decision",
    "CONTRADICTION_RESOLVED": "contradiction kept and resolved by an accepted Decision",
    "RIGHTS_UNKNOWN": "artifact license status unknown",
    "RIGHTS_BLOCK_ACCEPTANCE": "artifact marked accepted while rights unknown/restricted",
    "ACCEPTANCE_UNSUPPORTED": "artifact marked accepted without executed real human acceptance",
    "SYNTHETIC_ACCEPTED": "synthetic artifact marked accepted",
    "CAPABILITY_DECLARED_ONLY": "capability declared but invocation not verified today",
    "CAPABILITY_UNAVAILABLE": "capability unavailable",
    "CAPABILITY_INCONSISTENT": "invocation_verified without availability verified today",
    "AUTHORIZATION_UNVERIFIABLE": "external action authorization recorded; not verifiable here",
    "SUPERSEDED_NOT_MARKED": "supersedes target still active",
    "EXPERIMENT_CRITERION_LATE": "completed experiment lacks a criterion fixed before completion",
    "UNKNOWN_VALUE": "field intentionally null; reported as unknown, never as zero/false",
    "ARTIFACT_HASH_MATCH": "file bytes equal recorded sha256 (unchanged file, not a true claim)",
    "ARTIFACT_HASH_MISMATCH": "file bytes differ from recorded sha256",
    "ARTIFACT_MISSING": "artifact file not found under the explicit artifact root",
    "ARTIFACT_NOT_CHECKED": "artifact has no local locator/sha256 or no artifact root given",
}


# --------------------------------------------------------------------------
# Input loading and primitive checks
# --------------------------------------------------------------------------
def _unique_pairs(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise RegistryError(f"Duplicate JSON key: {key!r}")
        result[key] = value
    return result


def _bad_constant(value):
    raise RegistryError(f"Non-finite JSON number is not allowed: {value}")


def read_bytes(path: Path) -> bytes:
    path = Path(path)
    try:
        if not path.is_file():
            raise RegistryError(f"Input is not a readable file: {path}")
        size = path.stat().st_size
        if size > MAX_INPUT_BYTES:
            raise RegistryError(f"Input exceeds {MAX_INPUT_BYTES} bytes: {path}")
        return path.read_bytes()
    except OSError as exc:
        raise RegistryError(f"Cannot read {path}: {exc}") from exc


def load_json(path: Path):
    raw = read_bytes(path)
    try:
        text = raw.decode("utf-8-sig")
        return json.loads(text, object_pairs_hook=_unique_pairs, parse_float=Decimal,
                          parse_constant=_bad_constant)
    except UnicodeError as exc:
        raise RegistryError(f"{path}: not UTF-8: {exc}") from exc
    except RegistryError:
        raise
    except json.JSONDecodeError as exc:
        raise RegistryError(f"{path}: malformed JSON: {exc}") from exc
    except (RecursionError, ValueError) as exc:
        # e.g. excessive nesting or integers beyond Python's digit limit
        raise RegistryError(f"{path}: JSON cannot be parsed safely: {type(exc).__name__}") from exc


def sha256_bytes(raw: bytes) -> str:
    return hashlib.sha256(raw).hexdigest()


def sha256_lf(raw: bytes) -> str:
    """Digest independent of CRLF/LF checkout conversion."""
    return hashlib.sha256(raw.replace(b"\r\n", b"\n")).hexdigest()


def nonempty(value) -> bool:
    return isinstance(value, str) and bool(value.strip())


def iso_day(value, where, nullable=True):
    if value is None and nullable:
        return None
    if not isinstance(value, str) or not re.fullmatch(r"\d{4}-\d{2}-\d{2}", value):
        raise RegistryError(f"{where}: expected YYYY-MM-DD{' or null' if nullable else ''}")
    try:
        return date.fromisoformat(value)
    except ValueError as exc:
        raise RegistryError(f"{where}: {exc}") from exc


def str_list(value, where, allow_empty=True):
    if not isinstance(value, list) or any(not nonempty(v) for v in value):
        raise RegistryError(f"{where}: expected array of nonempty strings")
    if not allow_empty and not value:
        raise RegistryError(f"{where}: must not be empty")
    return value


def nullable_str(obj, key, where):
    value = obj.get(key)
    if value is not None and not nonempty(value):
        raise RegistryError(f"{where}.{key}: expected nonempty string or null")


def strict_bool(value, where):
    if not isinstance(value, bool):
        raise RegistryError(f"{where}: expected boolean")
    return value


def enum(value, name, where, nullable=False):
    if value is None and nullable:
        return None
    if not isinstance(value, str) or value not in ENUMS[name]:
        raise RegistryError(f"{where}: {value!r} not in {sorted(ENUMS[name])}")
    return value


def json_number(value, where, nullable=True):
    if value is None and nullable:
        return None
    if isinstance(value, bool) or not isinstance(value, (int, float, Decimal)):
        raise RegistryError(f"{where}: expected JSON number (booleans/strings rejected)")
    result = Decimal(str(value))
    if not result.is_finite():
        raise RegistryError(f"{where}: non-finite number")
    return result


def check_locator(value, where):
    """Lexical read-only path check. It does not open anything.

    Accepts https URLs without credentials, or repository-relative POSIX paths
    without '..', drive letters, backslashes, home expansion or NUL bytes.
    """
    if not nonempty(value):
        raise RegistryError(f"{where}: locator must be a nonempty string")
    if "\x00" in value:
        raise RegistryError(f"{where}: NUL byte in path")
    parsed = urlparse(value)
    if parsed.scheme:
        if re.match(r"^[A-Za-z]:", value):
            raise RegistryError(f"{where}: absolute drive path is not allowed: {value!r}")
        if parsed.scheme != "https" or not parsed.netloc:
            raise RegistryError(f"{where}: only https URLs are allowed, got {parsed.scheme!r}")
        if parsed.username or parsed.password:
            raise RegistryError(f"{where}: credentials inside URL are not allowed")
        return "url"
    if "\\" in value or value.startswith(("/", "~")):
        raise RegistryError(f"{where}: path must be repository-relative POSIX: {value!r}")
    parts = PurePosixPath(value).parts
    if ".." in parts:
        raise RegistryError(f"{where}: '..' path traversal is not allowed: {value!r}")
    return "path"


# --------------------------------------------------------------------------
# Structural validation of a registry
# --------------------------------------------------------------------------
def _check_entity(ent, where):
    if not isinstance(ent, dict):
        raise RegistryError(f"{where}: expected object")
    etype = ent.get("type")
    if not isinstance(etype, str) or etype not in ENTITY_SPECS:
        raise RegistryError(f"{where}: unknown entity type {etype!r}")
    prefix, required, optional = ENTITY_SPECS[etype]
    allowed = COMMON_FIELDS | required | optional
    unknown = set(ent) - allowed
    if unknown:
        raise RegistryError(f"{where} ({etype}): unsupported keys {sorted(unknown)}")
    missing = (required | {"id", "title"}) - set(ent)
    if missing:
        raise RegistryError(f"{where} ({etype}): missing keys {sorted(missing)}")
    eid = ent["id"]
    if not nonempty(eid) or not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9_.:-]{1,79}", eid):
        raise RegistryError(f"{where}: invalid id {eid!r}")
    if etype == "RequirementRef":
        if not REQ_ID.fullmatch(eid):
            raise RegistryError(f"{where}: RequirementRef id must be an existing Cxx-Rxx id")
    elif not eid.startswith(prefix):
        raise RegistryError(f"{where}: {etype} id must start with {prefix!r}")
    where = f"{etype} {eid}"
    if not nonempty(ent["title"]):
        raise RegistryError(f"{where}: title required")
    if "record_status" in ent and (not isinstance(ent["record_status"], str)
                                   or ent["record_status"] not in RECORD_STATUS):
        raise RegistryError(f"{where}.record_status: {ent['record_status']!r} not allowed")
    if "synthetic" in ent:
        strict_bool(ent["synthetic"], f"{where}.synthetic")
    for key in ("notes", "status_reason"):
        nullable_str(ent, key, where)
    globals()[f"_check_{etype}"](ent, where)
    return etype, eid


def _check_Source(e, w):
    kind = enum(e["source_kind"], "source_kind", f"{w}.source_kind")
    loc_kind = check_locator(e["locator"], f"{w}.locator")
    for k in ("author", "version", "rights"):
        nullable_str(e, k, w)
    published = iso_day(e["published"], f"{w}.published")
    accessed = iso_day(e["accessed"], f"{w}.accessed")
    if published and accessed and published > accessed:
        raise RegistryError(f"{w}: published after accessed")
    method = enum(e["access_method"], "access_method", f"{w}.access_method")
    depth = enum(e["study_depth"], "study_depth", f"{w}.study_depth")
    if method == "not_accessed" and depth != "not_accessed":
        raise RegistryError(f"{w}: study_depth must be not_accessed when access_method is not_accessed")
    if method == "oembed_metadata" and depth != "metadata":
        raise RegistryError(f"{w}: oEmbed access can only support study_depth=metadata")
    if depth != "not_accessed" and accessed is None:
        raise RegistryError(f"{w}: accessed date required once anything was accessed")
    rel = e["relevance"]
    if rel is not None and (isinstance(rel, bool) or not isinstance(rel, (int, float, Decimal))
                            or Decimal(str(rel)) not in {Decimal("1.2"), Decimal("1.0"), Decimal("0.7")}):
        raise RegistryError(f"{w}.relevance: categorical 1.2/1.0/0.7 or null (not a probability)")
    enum(e["reliability"], "reliability", f"{w}.reliability")
    str_list(e["limitations"], f"{w}.limitations", allow_empty=False)
    if kind == "video":
        layers = e.get("video_layers")
        if not isinstance(layers, dict) or set(layers) != {"metadata", "transcript",
                                                           "viewed_fragments", "reproduced"}:
            raise RegistryError(f"{w}: video needs video_layers{{metadata,transcript,viewed_fragments,reproduced}}")
        for k in ("metadata", "transcript", "reproduced"):
            strict_bool(layers[k], f"{w}.video_layers.{k}")
        frags = layers["viewed_fragments"]
        if not isinstance(frags, list):
            raise RegistryError(f"{w}.video_layers.viewed_fragments: expected array")
        for i, f in enumerate(frags):
            if not isinstance(f, dict) or set(f) != {"start", "end", "observed_by", "observed_on"}:
                raise RegistryError(f"{w}.viewed_fragments[{i}]: needs start,end,observed_by,observed_on")
            if not (TIMECODE.fullmatch(str(f["start"])) and TIMECODE.fullmatch(str(f["end"]))):
                raise RegistryError(f"{w}.viewed_fragments[{i}]: timecodes must be HH:MM:SS")
            iso_day(f["observed_on"], f"{w}.viewed_fragments[{i}].observed_on", nullable=False)
            if not nonempty(f["observed_by"]):
                raise RegistryError(f"{w}.viewed_fragments[{i}].observed_by required")
        if layers["reproduced"] != (depth == "reproduced"):
            raise RegistryError(f"{w}: reproduced flag must match study_depth=reproduced")
        if depth == "transcript" and not layers["transcript"]:
            raise RegistryError(f"{w}: study_depth transcript requires video_layers.transcript")
        if depth in {"metadata", "not_accessed"} and (layers["transcript"] or frags):
            raise RegistryError(f"{w}: metadata-only study cannot claim transcript or viewed fragments")
    if kind == "code_repository":
        repo = e.get("repository")
        if not isinstance(repo, dict) or set(repo) != {"commit_or_tag", "license_spdx",
                                                       "dependencies_reviewed", "maintenance_note",
                                                       "reproducible_example_run"}:
            raise RegistryError(f"{w}: code_repository needs repository{{commit_or_tag,license_spdx,"
                                "dependencies_reviewed,maintenance_note,reproducible_example_run}}")
        for k in ("commit_or_tag", "license_spdx", "maintenance_note"):
            nullable_str(repo, k, f"{w}.repository")
        for k in ("dependencies_reviewed", "reproducible_example_run"):
            strict_bool(repo[k], f"{w}.repository.{k}")
    elif "repository" in e:
        raise RegistryError(f"{w}: repository block only for code_repository")
    if kind == "community":
        com = e.get("community")
        if not isinstance(com, dict) or set(com) != {"specialization", "practice_access",
                                                     "signal_vs_ads", "usage_rules", "contacted"}:
            raise RegistryError(f"{w}: community needs community{{specialization,practice_access,"
                                "signal_vs_ads,usage_rules,contacted}}")
        if com["contacted"] is not False:
            raise RegistryError(f"{w}: research registry records no outreach; contacted must be false")
    if loc_kind == "path" and kind not in {"repo_file", "local_library"}:
        raise RegistryError(f"{w}: relative locator only for repo_file/local_library sources")


def _check_Claim(e, w):
    if not nonempty(e["statement"]) or not nonempty(e["scope"]):
        raise RegistryError(f"{w}: statement and scope required")
    enum(e["claim_kind"], "claim_kind", f"{w}.claim_kind")
    refs = e["source_refs"]
    if not isinstance(refs, list) or not refs:
        raise RegistryError(f"{w}.source_refs: at least one {{source, locator}} required")
    for i, r in enumerate(refs):
        if not isinstance(r, dict) or set(r) != {"source", "locator"} or not nonempty(r["source"]) \
                or not nonempty(r["locator"]):
            raise RegistryError(f"{w}.source_refs[{i}]: needs nonempty source and locator")
    conf = enum(e["confidence"], "confidence", f"{w}.confidence", nullable=True)
    if conf is not None and not nonempty(e.get("confidence_basis")):
        raise RegistryError(f"{w}: confidence requires confidence_basis")
    enum(e["verification"], "verification", f"{w}.verification")
    str_list(e["limitations"], f"{w}.limitations")
    if "recheck" in e:
        rc = e["recheck"]
        if not isinstance(rc, dict) or set(rc) != {"reason", "due"} or not nonempty(rc["reason"]):
            raise RegistryError(f"{w}.recheck: needs reason and due (date or null)")
        iso_day(rc["due"], f"{w}.recheck.due")


def _check_RequirementRef(e, w):
    if not COMPONENT_ID.fullmatch(str(e["component"])) or not e["id"].startswith(e["component"] + "-"):
        raise RegistryError(f"{w}: component must match the id prefix")
    if e["gate"] not in GATES:
        raise RegistryError(f"{w}.gate: expected one of {GATES}")
    cat = e["catalog"]
    if not isinstance(cat, dict) or set(cat) != {"path", "revision", "sha256_lf"}:
        raise RegistryError(f"{w}.catalog: needs path, revision, sha256_lf")
    check_locator(cat["path"], f"{w}.catalog.path")
    if not nonempty(cat["revision"]) or not HEX64.fullmatch(str(cat["sha256_lf"])):
        raise RegistryError(f"{w}.catalog: revision and 64-hex lowercase sha256_lf required")
    if not nonempty(e["checked_property"]):
        raise RegistryError(f"{w}.checked_property required")
    classes = str_list(e["accepted_evidence_classes"], f"{w}.accepted_evidence_classes", False)
    for c in classes:
        enum(c, "evidence_class", f"{w}.accepted_evidence_classes")
    nullable_str(e, "target_revision", w)
    if "requires_independent_review" in e:
        strict_bool(e["requires_independent_review"], f"{w}.requires_independent_review")


def _check_Capability(e, w):
    enum(e["cap_kind"], "cap_kind", f"{w}.cap_kind")
    for k in ("name", "owner_agent"):
        if not nonempty(e[k]):
            raise RegistryError(f"{w}.{k} required")
    for k in ("location", "version"):
        nullable_str(e, k, w)
    for k in ("inputs", "outputs", "dependencies", "limitations"):
        str_list(e[k], f"{w}.{k}")
    for k in ("declared", "invocation_verified"):
        strict_bool(e[k], f"{w}.{k}")
    enum(e["available"], "available", f"{w}.available")
    enum(e["benchmarked"], "benchmarked", f"{w}.benchmarked")
    if "external_action_authorized" in e:
        strict_bool(e["external_action_authorized"], f"{w}.external_action_authorized")


def _check_Experiment(e, w):
    for k in ("question", "hypothesis", "baseline", "protocol"):
        if not nonempty(e[k]):
            raise RegistryError(f"{w}.{k} required")
    nullable_str(e, "success_criterion", w)
    nullable_str(e, "results", w)
    fixed = iso_day(e["criterion_fixed_on"], f"{w}.criterion_fixed_on")
    if fixed and not nonempty(e["success_criterion"]):
        raise RegistryError(f"{w}: criterion_fixed_on without success_criterion")
    s = e["sample"]
    if not isinstance(s, dict) or set(s) != {"n", "unit"} or not nonempty(s["unit"]):
        raise RegistryError(f"{w}.sample: needs n (integer or null) and unit")
    n = json_number(s["n"], f"{w}.sample.n")
    if n is not None and (n < 0 or n != n.to_integral_value()):
        raise RegistryError(f"{w}.sample.n: nonnegative integer or null")
    rb = e["resource_bounds"]
    if not isinstance(rb, dict) or set(rb) != {"cash", "currency", "hours"}:
        raise RegistryError(f"{w}.resource_bounds: needs cash, currency, hours (null = unknown)")
    for k in ("cash", "hours"):
        v = json_number(rb[k], f"{w}.resource_bounds.{k}")
        if v is not None and v < 0:
            raise RegistryError(f"{w}.resource_bounds.{k}: negative")
    nullable_str(rb, "currency", f"{w}.resource_bounds")
    status = enum(e["experiment_status"], "experiment_status", f"{w}.experiment_status")
    done = iso_day(e.get("completed_on"), f"{w}.completed_on")
    if status == "completed" and (done is None or not nonempty(e["results"])):
        raise RegistryError(f"{w}: completed experiment needs completed_on and results")
    str_list(e["limitations"], f"{w}.limitations")


def _check_Artifact(e, w):
    if not nonempty(e["kind"]) or not nonempty(e["origin"]) or not nonempty(e["revision"]):
        raise RegistryError(f"{w}: kind, origin and revision required")
    check_locator(e["locator"], f"{w}.locator")
    if e["sha256"] is not None and not HEX64.fullmatch(str(e["sha256"])):
        raise RegistryError(f"{w}.sha256: 64 lowercase hex or null")
    lic = e["license"]
    if not isinstance(lic, dict) or set(lic) != {"status", "terms", "scope"}:
        raise RegistryError(f"{w}.license: needs status, terms, scope")
    enum(lic["status"], "license_status", f"{w}.license.status")
    nullable_str(lic, "terms", f"{w}.license")
    nullable_str(lic, "scope", f"{w}.license")
    enum(e["lifecycle"], "lifecycle", f"{w}.lifecycle")


def _check_Evidence(e, w):
    if not nonempty(e["criterion"]) or not nonempty(e["method"]) or not nonempty(e["producer"]):
        raise RegistryError(f"{w}: criterion, method and producer required")
    nullable_str(e, "artifact_ref", w)
    nullable_str(e, "artifact_revision", w)
    nullable_str(e, "reviewer", w)
    enum(e["evidence_class"], "evidence_class", f"{w}.evidence_class")
    status = enum(e["evidence_status"], "evidence_status", f"{w}.evidence_status")
    result = enum(e["result"], "result", f"{w}.result")
    if status in {"prepared", "failed_to_run"} and result != "UNKNOWN":
        raise RegistryError(f"{w}: {status} evidence must have result UNKNOWN")
    d = iso_day(e["date"], f"{w}.date", nullable=False)
    until = iso_day(e["valid_until"], f"{w}.valid_until")
    if until and until < d:
        raise RegistryError(f"{w}: valid_until precedes date")
    if not isinstance(e["observations"], str):
        raise RegistryError(f"{w}.observations: string required (may be empty)")
    enum(e["publication"], "publication", f"{w}.publication")
    str_list(e["limitations"], f"{w}.limitations")


def _check_Decision(e, w):
    if not nonempty(e["question"]) or not nonempty(e["owner"]):
        raise RegistryError(f"{w}: question and owner required")
    str_list(e["options"], f"{w}.options")
    if len(e["options"]) < 2:
        raise RegistryError(f"{w}.options: at least two considered options")
    for k in ("assumptions", "unknowns", "revisit_when"):
        str_list(e[k], f"{w}.{k}")
    nullable_str(e, "decision", w)
    status = enum(e["decision_status"], "decision_status", f"{w}.decision_status")
    if status == "accepted" and not nonempty(e["decision"]):
        raise RegistryError(f"{w}: accepted decision needs decision text")
    if "resolves" in e:
        str_list(e["resolves"], f"{w}.resolves")


def _check_WorkPacket(e, w):
    for k in ("goal", "owner", "handoff", "dependencies_note"):
        if not nonempty(e[k]):
            raise RegistryError(f"{w}.{k} required")
    for k in ("inputs", "outputs", "acceptance", "stop_conditions", "evidence_plan"):
        str_list(e[k], f"{w}.{k}", allow_empty=False)
    for i, p in enumerate(str_list(e["owned_paths"], f"{w}.owned_paths", False)):
        if check_locator(p.rstrip("*").rstrip("/") or p, f"{w}.owned_paths[{i}]") != "path":
            raise RegistryError(f"{w}.owned_paths[{i}]: must be a repository path")
    enum(e["wp_status"], "wp_status", f"{w}.wp_status")


@_structural
def validate_registry(data):
    """Structural/contract validation. Raises RegistryError; returns (nodes, edges)."""
    if not isinstance(data, dict):
        raise RegistryError("registry: expected JSON object")
    schema = data.get("schema")
    if schema != REGISTRY_SCHEMA:
        raise RegistryError(f"Unsupported registry schema {schema!r}; supported: {REGISTRY_SCHEMA}")
    allowed = {"schema", "registry_id", "synthetic", "as_of", "description", "entities", "edges"}
    unknown = set(data) - allowed
    if unknown:
        raise RegistryError(f"registry: unsupported keys {sorted(unknown)}")
    for k in ("registry_id", "as_of", "entities", "edges", "synthetic"):
        if k not in data:
            raise RegistryError(f"registry: missing {k}")
    strict_bool(data["synthetic"], "registry.synthetic")
    iso_day(data["as_of"], "registry.as_of", nullable=False)
    if not isinstance(data["entities"], list) or not isinstance(data["edges"], list):
        raise RegistryError("registry: entities and edges must be arrays")
    nodes = {}
    for i, ent in enumerate(data["entities"]):
        etype, eid = _check_entity(ent, f"entities[{i}]")
        if eid in nodes:
            raise RegistryError(f"Duplicate entity id: {eid}")
        nodes[eid] = ent
    edges = []
    seen = set()
    for i, edge in enumerate(data["edges"]):
        w = f"edges[{i}]"
        if not isinstance(edge, dict) or not {"type", "from", "to"} <= set(edge) \
                or set(edge) - {"type", "from", "to", "note"}:
            raise RegistryError(f"{w}: needs type, from, to (+ optional note)")
        etype = edge["type"]
        if not isinstance(etype, str) or etype not in EDGE_SPECS:
            raise RegistryError(f"{w}: unknown edge type {etype!r}; allowed {sorted(EDGE_SPECS)}")
        for end in ("from", "to"):
            if not isinstance(edge[end], str) or edge[end] not in nodes:
                raise RegistryError(f"{w}: dangling {end} reference {edge[end]!r}")
        if edge["from"] == edge["to"]:
            raise RegistryError(f"{w}: self-loop is not allowed")
        ft, tt = nodes[edge["from"]]["type"], nodes[edge["to"]]["type"]
        from_ok, to_ok, _ = EDGE_SPECS[etype]
        if ft not in from_ok or tt not in to_ok:
            raise RegistryError(f"{w}: {etype} not allowed from {ft} to {tt}")
        if etype == "supersedes" and ft != tt:
            raise RegistryError(f"{w}: supersedes requires the same entity type")
        key = (etype, edge["from"], edge["to"])
        if key in seen:
            raise RegistryError(f"{w}: duplicate edge {key}")
        seen.add(key)
        edges.append(edge)
    # Reference fields that are not edges.
    for eid, ent in nodes.items():
        if ent["type"] == "Claim":
            for r in ent["source_refs"]:
                if r["source"] not in nodes or nodes[r["source"]]["type"] != "Source":
                    raise RegistryError(f"Claim {eid}: source_refs points to missing Source {r['source']!r}")
        if ent["type"] == "Evidence" and ent["artifact_ref"] is not None:
            ref = ent["artifact_ref"]
            if ref not in nodes or nodes[ref]["type"] != "Artifact":
                raise RegistryError(f"Evidence {eid}: artifact_ref points to missing Artifact {ref!r}")
        if ent["type"] == "Decision":
            for ref in ent.get("resolves", []):
                if ref not in nodes or nodes[ref]["type"] != "Claim":
                    raise RegistryError(f"Decision {eid}: resolves points to missing Claim {ref!r}")
    return nodes, edges


# --------------------------------------------------------------------------
# Graph analysis and assessment
# --------------------------------------------------------------------------
def find_cycles(node_ids, edges, types):
    graph = defaultdict(list)
    for e in edges:
        if e["type"] in types:
            graph[e["from"]].append(e["to"])
    for k in graph:
        graph[k].sort()
    color, cycles = {}, []
    # Iterative DFS: deep dependency chains must not hit Python's recursion limit.
    for start in sorted(node_ids):
        if color.get(start) is not None:
            continue
        path, frames = [start], [iter(graph.get(start, []))]
        color[start] = 1
        while frames:
            m = next(frames[-1], None)
            if m is None:
                color[path.pop()] = 2
                frames.pop()
                continue
            if color.get(m) == 1:
                cycles.append(path[path.index(m):] + [m])
            elif color.get(m) is None:
                color[m] = 1
                path.append(m)
                frames.append(iter(graph.get(m, [])))
    unique, seen = [], set()
    for c in cycles:
        body = c[:-1]
        i = body.index(min(body))
        canon = tuple(body[i:] + body[:i])
        if canon not in seen:
            seen.add(canon)
            unique.append(list(canon) + [canon[0]])
    return sorted(unique)


def topological_order(node_ids, edges):
    """Order for depends_on: prerequisites first. None if cyclic."""
    deps = defaultdict(set)
    involved = set()
    for e in edges:
        if e["type"] == "depends_on":
            deps[e["from"]].add(e["to"])
            involved |= {e["from"], e["to"]}
    remaining = {n: set(deps[n]) for n in involved}
    order = []
    while remaining:
        ready = sorted(n for n, d in remaining.items() if not d)
        if not ready:
            return None
        for n in ready:
            order.append(n)
            del remaining[n]
        for d in remaining.values():
            d.difference_update(ready)
    return order


@_structural
def load_catalog(path):
    raw = read_bytes(Path(path))
    data = load_json(Path(path))
    if not isinstance(data, dict) or data.get("schema_version") != 1 \
            or isinstance(data.get("schema_version"), bool) or not isinstance(data.get("components"), list):
        raise RegistryError("catalog: expected COMPONENT_REQUIREMENTS schema_version 1 with components[]")
    reqs, comps = {}, {}
    for c in data["components"]:
        cid = c.get("id") if isinstance(c, dict) else None
        if not isinstance(cid, str) or not COMPONENT_ID.fullmatch(cid) or cid in comps:
            raise RegistryError(f"catalog: invalid or duplicate component id {cid!r}")
        comps[cid] = c
        reqlist = c.get("requirements", [])
        if not isinstance(reqlist, list):
            raise RegistryError(f"catalog: {cid}.requirements must be an array")
        for r in reqlist:
            rid = r.get("id") if isinstance(r, dict) else None
            if not isinstance(rid, str) or not REQ_ID.fullmatch(rid) or rid in reqs \
                    or not rid.startswith(cid + "-"):
                raise RegistryError(f"catalog: invalid or duplicate requirement id {rid!r}")
            reqs[rid] = {"component": cid, "gate": r.get("gate"), "level": r.get("level"),
                         "status": r.get("status")}
    return {"components": sorted(comps), "requirements": reqs, "sha256": sha256_bytes(raw),
            "sha256_lf": sha256_lf(raw), "date": data.get("date")}


def _finding(code, severity, subject, detail):
    return {"code": code, "severity": severity, "subject": subject, "detail": detail}


def node_unusable(nid, nodes, ctx):
    """Reasons why a record cannot serve as a current basis (status, invalidation, supersession)."""
    reasons = []
    status = nodes[nid].get("record_status", "active")
    if status in {"stale", "superseded", "withdrawn", "draft"}:
        reasons.append(f"{nid}:record_status={status}")
    if nid in ctx["invalidated"]:
        reasons.append(f"{nid}:invalidated_by=" + ",".join(sorted(ctx["invalidated"][nid])))
    if nid in ctx["superseded"]:
        reasons.append(f"{nid}:superseded_by=" + ",".join(sorted(ctx["superseded"][nid])))
    return reasons


def same_person(a, b):
    return isinstance(a, str) and isinstance(b, str) and a.strip().casefold() == b.strip().casefold()


def evidence_current(ev, nodes, ctx, as_of, registry_synthetic):
    """Checks shared by requirement verdicts, promises and artifact acceptance."""
    reasons = []
    art = nodes.get(ev["artifact_ref"]) if ev["artifact_ref"] else None
    if registry_synthetic or ev.get("synthetic") or (art and art.get("synthetic")):
        reasons.append("synthetic")
    status = ev["evidence_status"]
    if status == "historical_import":
        reasons.append("historical_import_not_rerun")
    elif status != "executed":
        reasons.append(f"evidence_status={status}")
    if ev["evidence_class"] in NEVER_CLOSES:
        reasons.append(f"{ev['evidence_class']}_never_closes_acceptance")
    reasons += node_unusable(ev["id"], nodes, ctx)
    if date.fromisoformat(ev["date"]) > as_of:
        reasons.append("future_dated")
    if ev["valid_until"] is None:
        reasons.append("no_validity_limit_recorded")
    elif date.fromisoformat(ev["valid_until"]) < as_of:
        reasons.append("expired")
    if art is None:
        reasons.append("no_artifact_ref")
    else:
        reasons += node_unusable(art["id"], nodes, ctx)
        if ev["artifact_revision"] != art["revision"]:
            reasons.append(f"artifact_revision_mismatch({ev['artifact_revision']}!={art['revision']})")
    return reasons


def evidence_qualification(ev, req, nodes, ctx, as_of, registry_synthetic):
    """Return (qualifies, reasons). Reasons explain why evidence cannot close req."""
    reasons = evidence_current(ev, nodes, ctx, as_of, registry_synthetic)
    cls = ev["evidence_class"]
    if cls not in req["accepted_evidence_classes"]:
        reasons.append(f"class_{cls}_not_accepted")
    target = req["target_revision"]
    if target is None:
        reasons.append("target_revision_unknown")
    elif ev["artifact_revision"] != target:
        reasons.append(f"revision_mismatch({ev['artifact_revision']}!={target})")
    if req.get("requires_independent_review") and (not ev["reviewer"]
                                                     or same_person(ev["reviewer"], ev["producer"])):
        reasons.append("no_independent_reviewer")
    return (not reasons), reasons


@_structural
def assess(data, catalog=None, as_of=None, artifact_root=None):
    nodes, edges = validate_registry(data)
    as_of = as_of or date.fromisoformat(data["as_of"])
    synthetic = data["synthetic"]
    findings = []
    out_edges, in_edges = defaultdict(list), defaultdict(list)
    for e in edges:
        out_edges[e["from"]].append(e)
        in_edges[e["to"]].append(e)
    invalidated, superseded = defaultdict(set), defaultdict(set)
    for e in edges:
        if e["type"] == "invalidates":
            invalidated[e["to"]].add(e["from"])
        elif e["type"] == "supersedes":
            superseded[e["to"]].add(e["from"])
    ctx = {"invalidated": invalidated, "superseded": superseded}

    dep_cycles = find_cycles(nodes, edges, EXECUTION_EDGES)
    for c in dep_cycles:
        findings.append(_finding("DEPENDENCY_CYCLE", "error", c[0], " -> ".join(c)))
    for c in find_cycles(nodes, edges, PROVENANCE_EDGES):
        findings.append(_finding("PROVENANCE_CYCLE", "error", c[0], " -> ".join(c)))
    order = topological_order(nodes, edges)

    for e in edges:
        if e["type"] == "supersedes" and nodes[e["to"]].get("record_status", "active") == "active":
            findings.append(_finding("SUPERSEDED_NOT_MARKED", "warning", e["to"],
                                     f"superseded by {e['from']} but record_status is active"))

    # Requirements
    requirements = {}
    for rid, req in sorted((k, v) for k, v in nodes.items() if v["type"] == "RequirementRef"):
        entry = {"gate": req["gate"], "component": req["component"],
                 "target_revision": req["target_revision"], "evidence": [], "historical_pass": False}
        if catalog is None:
            findings.append(_finding("CATALOG_NOT_SUPPLIED", "open", rid,
                                     "existence of requirement id not checked"))
        else:
            if rid not in catalog["requirements"]:
                findings.append(_finding("REQ_UNKNOWN_IN_CATALOG", "error", rid,
                                         "id not present in supplied catalog"))
            elif catalog["requirements"][rid]["gate"] != req["gate"]:
                findings.append(_finding("REQ_UNKNOWN_IN_CATALOG", "error", rid,
                                         f"gate {req['gate']} != catalog {catalog['requirements'][rid]['gate']}"))
            if req["catalog"]["sha256_lf"] != catalog["sha256_lf"]:
                findings.append(_finding("CATALOG_HASH_MISMATCH", "open", rid,
                                         "reference recorded against another catalog revision"))
        qualifying = []
        for e in sorted(out_edges[rid], key=lambda x: x["to"]):
            if e["type"] != "verified_by":
                continue
            ev = nodes[e["to"]]
            ok, reasons = evidence_qualification(ev, req, nodes, ctx, as_of, synthetic)
            if ev["evidence_status"] == "historical_import" and ev["result"] == "PASS":
                entry["historical_pass"] = True
            entry["evidence"].append({"id": ev["id"], "result": ev["result"], "qualifies": ok,
                                      "reasons": reasons})
            if ok:
                qualifying.append(ev["result"])
        if "FAIL" in qualifying:
            verdict = "FAIL"
        elif "PASS" in qualifying:
            verdict = "PASS"
        else:
            verdict = "OPEN"
        entry["verdict"] = verdict
        code = {"PASS": "REQ_PASS", "FAIL": "REQ_FAIL", "OPEN": "REQ_OPEN"}[verdict]
        detail = "no linked evidence" if not entry["evidence"] else \
            "; ".join(f"{x['id']}:{'/'.join(x['reasons']) or x['result']}" for x in entry["evidence"])
        findings.append(_finding(code, "info" if verdict == "PASS" else "open", rid, detail))
        requirements[rid] = entry

    # Contradictions
    accepted_resolutions = defaultdict(set)
    for d in nodes.values():
        if d["type"] == "Decision" and d["decision_status"] == "accepted" \
                and not node_unusable(d["id"], nodes, ctx):
            for c in d.get("resolves", []):
                accepted_resolutions[c].add(d["id"])
    contradictions = []
    for e in sorted(edges, key=lambda x: (x["from"], x["to"])):
        if e["type"] != "contradicts":
            continue
        a, b = e["from"], e["to"]
        resolvers = sorted(accepted_resolutions[a] & accepted_resolutions[b]) \
            if nodes[a]["type"] == "Claim" else sorted(accepted_resolutions[b])
        contradictions.append({"from": a, "to": b, "resolved_by": resolvers})
        findings.append(_finding("CONTRADICTION_RESOLVED" if resolvers else "CONTRADICTION_OPEN",
                                 "info" if resolvers else "open", f"{a}<>{b}",
                                 ("resolved by " + ",".join(resolvers)) if resolvers
                                 else "both claims retained; decision required"))

    # Unknown values are reported, never coerced.
    unknowns = []
    for eid, n in sorted(nodes.items()):
        for key, value in sorted(n.items()):
            if value is None:
                unknowns.append({"id": eid, "field": key, "value": None})
            elif isinstance(value, dict):
                for sub, sv in sorted(value.items()):
                    if sv is None:
                        unknowns.append({"id": eid, "field": f"{key}.{sub}", "value": None})
        if n["type"] == "Experiment":
            if n["experiment_status"] == "completed":
                fixed = n["criterion_fixed_on"]
                if fixed is None or date.fromisoformat(fixed) > date.fromisoformat(n["completed_on"]):
                    findings.append(_finding("EXPERIMENT_CRITERION_LATE", "open", eid,
                                             "success criterion not fixed before completion"))
    for u in unknowns:
        findings.append(_finding("UNKNOWN_VALUE", "info", u["id"], u["field"]))

    # Rights and acceptance of artifacts
    for eid, n in sorted(nodes.items()):
        if n["type"] != "Artifact":
            continue
        lic = n["license"]["status"]
        if lic == "unknown":
            findings.append(_finding("RIGHTS_UNKNOWN", "open", eid, "license status unknown"))
        if n["lifecycle"] == "accepted":
            if lic in {"unknown", "restricted"}:
                findings.append(_finding("RIGHTS_BLOCK_ACCEPTANCE", "open", eid,
                                         f"accepted while license is {lic}"))
            if synthetic or n.get("synthetic"):
                findings.append(_finding("SYNTHETIC_ACCEPTED", "error", eid,
                                         "synthetic artifact cannot be accepted"))
            human = [nodes[e["to"]] for e in out_edges[eid] if e["type"] == "verified_by"]
            real = [h for h in human if h["evidence_class"] in HUMAN_ACCEPTANCE
                    and h["result"] == "PASS" and h["artifact_ref"] == eid
                    and h["reviewer"] and not same_person(h["reviewer"], h["producer"])
                    and not evidence_current(h, nodes, ctx, as_of, synthetic)]
            if not real:
                findings.append(_finding("ACCEPTANCE_UNSUPPORTED", "open", eid,
                                         "no executed, non-synthetic human acceptance evidence"))

    # Ownership: open work packets with different owners must not claim overlapping paths.
    open_wps = sorted((k, v) for k, v in nodes.items()
                      if v["type"] == "WorkPacket" and v["wp_status"] != "done"
                      and v.get("record_status", "active") == "active")

    def _norm(p):
        parts = [s for s in p.replace("\\", "/").split("/") if s not in ("", ".")]
        while parts and set(parts[-1]) == {"*"}:
            parts.pop()
        return "/".join(parts).casefold()  # "" means the whole repository

    for i, (a_id, a) in enumerate(open_wps):
        for b_id, b in open_wps[i + 1:]:
            if a["owner"] == b["owner"]:
                continue
            for pa in a["owned_paths"]:
                for pb in b["owned_paths"]:
                    x, y = _norm(pa), _norm(pb)
                    if x == y or not x or not y or x.startswith(y + "/") or y.startswith(x + "/"):
                        findings.append(_finding("OWNERSHIP_OVERLAP", "open", f"{a_id}<>{b_id}",
                                                 f"{pa} vs {pb}; owners {a['owner']} / {b['owner']}"))

    # Capabilities
    capabilities = []
    for eid, n in sorted(nodes.items()):
        if n["type"] != "Capability":
            continue
        state = ("unavailable" if n["available"] == "unavailable" else
                 "invocation_verified" if n["invocation_verified"] else
                 "declared_only" if n["declared"] else "undeclared")
        capabilities.append({"id": eid, "name": n["name"], "owner_agent": n["owner_agent"],
                             "declared": n["declared"], "available": n["available"],
                             "benchmarked": n["benchmarked"],
                             "invocation_verified": n["invocation_verified"], "state": state})
        if n["invocation_verified"] and n["available"] != "verified_today":
            findings.append(_finding("CAPABILITY_INCONSISTENT", "error", eid,
                                     "invocation_verified requires available=verified_today"))
        if state == "unavailable":
            findings.append(_finding("CAPABILITY_UNAVAILABLE", "open", eid, n["name"]))
        elif state == "declared_only":
            findings.append(_finding("CAPABILITY_DECLARED_ONLY", "open", eid,
                                     f"{n['name']} ({n['owner_agent']}): available={n['available']}"))
        if n.get("external_action_authorized"):
            findings.append(_finding("AUTHORIZATION_UNVERIFIABLE", "warning", eid,
                                     "recorded authorization is not verified by this validator"))

    # Optional read-only artifact hash check under an explicit root.
    integrity = []
    for eid, n in sorted(nodes.items()):
        if n["type"] != "Artifact":
            continue
        is_path = not urlparse(n["locator"]).scheme
        if artifact_root is None or not is_path or n["sha256"] is None:
            integrity.append({"id": eid, "status": "ARTIFACT_NOT_CHECKED"})
            continue
        root = Path(artifact_root).resolve()
        target = (root / n["locator"]).resolve()
        if target != root and root not in target.parents:
            raise RegistryError(f"Artifact {eid}: resolved path escapes artifact root")
        if not target.is_file():
            integrity.append({"id": eid, "status": "ARTIFACT_MISSING"})
            findings.append(_finding("ARTIFACT_MISSING", "open", eid, n["locator"]))
            continue
        digest = sha256_bytes(read_bytes(target))
        status = "ARTIFACT_HASH_MATCH" if digest == n["sha256"] else "ARTIFACT_HASH_MISMATCH"
        integrity.append({"id": eid, "status": status})
        findings.append(_finding(status, "info" if status.endswith("MATCH") and "MIS" not in status
                                 else "error", eid, n["locator"]))

    findings.sort(key=lambda f: ({"error": 0, "open": 1, "warning": 2, "info": 3}[f["severity"]],
                                 f["code"], f["subject"], f["detail"]))
    counts = defaultdict(int)
    for f in findings:
        counts[f["severity"]] += 1
    verdicts = defaultdict(int)
    for r in requirements.values():
        verdicts[r["verdict"]] += 1
    return {
        "report_schema": "ags.research-registry-report/1",
        "validator_version": VALIDATOR_VERSION,
        "registry_id": data["registry_id"],
        "mode": "synthetic_fixture" if synthetic else "registry",
        "as_of": as_of.isoformat(),
        "catalog": None if catalog is None else {"sha256_lf": catalog["sha256_lf"],
                                                 "requirements": len(catalog["requirements"]),
                                                 "components": len(catalog["components"])},
        "entity_counts": dict(sorted(_count_types(nodes).items())),
        "edge_counts": dict(sorted(_count_edges(edges).items())),
        "execution_order": order,
        "requirements": requirements,
        "requirement_verdicts": dict(sorted(verdicts.items())),
        "contradictions": contradictions,
        "capabilities": capabilities,
        "unknown_values": unknowns,
        "artifact_integrity": integrity,
        "findings": findings,
        "finding_counts": dict(sorted(counts.items())),
        "is_authorization": False,
        "authenticates_evidence": False,
        "limitations": [
            "Trusted-input consistency check; identities, receipts, URLs and rights are not authenticated.",
            "A matching sha256 proves the file is unchanged, not that its content is true.",
            "Requirement PASS only means linked records satisfy the rules for the stated revision.",
        ],
    }


def _count_types(nodes):
    c = defaultdict(int)
    for n in nodes.values():
        c[n["type"]] += 1
    return c


def _count_edges(edges):
    c = defaultdict(int)
    for e in edges:
        c[e["type"]] += 1
    return c


# --------------------------------------------------------------------------
# Queries
# --------------------------------------------------------------------------
@_structural
def query_why(nodes, edges, target):
    """Trace the basis of a node back through supports/derived_from/verified_by."""
    if target not in nodes:
        raise RegistryError(f"Unknown id {target!r}")
    basis = defaultdict(list)
    for e in edges:
        if e["type"] in {"supports", "contradicts"}:
            basis[e["to"]].append((e["type"], e["from"]))
        elif e["type"] in {"derived_from", "verified_by"}:
            basis[e["from"]].append((e["type"], e["to"]))
    for n in nodes.values():
        if n["type"] == "Claim":
            for r in n["source_refs"]:
                basis[n["id"]].append(("cites", r["source"]))
    rows, seen, queue = [], {target}, deque([(target, 0)])
    while queue:
        cur, depth = queue.popleft()
        for kind, other in sorted(basis.get(cur, [])):
            rows.append({"depth": depth + 1, "node": cur, "relation": kind, "basis": other,
                         "basis_type": nodes[other]["type"],
                         "record_status": nodes[other].get("record_status", "active")})
            if other not in seen:
                seen.add(other)
                queue.append((other, depth + 1))
    return rows


@_structural
def query_impact(nodes, edges, changed):
    """Everything that should be rechecked if `changed` changes (model, license, build...)."""
    if changed not in nodes:
        raise RegistryError(f"Unknown id {changed!r}")
    affected_by = defaultdict(set)
    for e in edges:
        if e["type"] in IMPACT_INCOMING:
            affected_by[e["to"]].add(e["from"])
        if e["type"] in IMPACT_OUTGOING:
            affected_by[e["from"]].add(e["to"])
    # Reference fields are implicit dependencies too.
    for n in nodes.values():
        if n["type"] == "Evidence" and n["artifact_ref"]:
            affected_by[n["artifact_ref"]].add(n["id"])
        if n["type"] == "Claim":
            for r in n["source_refs"]:
                affected_by[r["source"]].add(n["id"])
    result, queue = {}, deque([(changed, 0)])
    while queue:
        cur, depth = queue.popleft()
        for other in sorted(affected_by.get(cur, ())):
            if other not in result and other != changed:
                result[other] = depth + 1
                queue.append((other, depth + 1))
    return [{"id": k, "type": nodes[k]["type"], "distance": v}
            for k, v in sorted(result.items(), key=lambda kv: (kv[1], kv[0]))]


@_structural
def query_promises(nodes, edges, report, as_of=None):
    as_of = as_of or date.fromisoformat(report["as_of"])
    ctx = {"invalidated": defaultdict(set), "superseded": defaultdict(set)}
    for e in edges:
        if e["type"] == "invalidates":
            ctx["invalidated"][e["to"]].add(e["from"])
        elif e["type"] == "supersedes":
            ctx["superseded"][e["to"]].add(e["from"])
    registry_synthetic = report["mode"] == "synthetic_fixture"
    rows = []
    for cid, n in sorted(nodes.items()):
        if n["type"] != "Claim" or n["claim_kind"] != "client_promise":
            continue
        support = [e["from"] for e in edges if e["type"] == "supports" and e["to"] == cid]
        checks = [e["to"] for e in edges if e["type"] == "verified_by" and e["from"] == cid]
        ev = [nodes[x] for x in support + checks if nodes[x]["type"] == "Evidence"]
        # Only behaviour-level checks support a client promise; source reading or hashes do not.
        current = [x["id"] for x in ev if x["result"] == "PASS"
                   and x["evidence_class"] in PROMISE_SUPPORT_CLASSES
                   and not evidence_current(x, nodes, ctx, as_of, registry_synthetic)]
        historical = [x["id"] for x in ev if x["evidence_status"] == "historical_import"]
        contra = sorted(e["from"] for e in edges if e["type"] == "contradicts" and e["to"] == cid)
        rows.append({"claim": cid, "verification": n["verification"], "limitations": n["limitations"],
                     "current_executed_pass": sorted(current), "historical_only": sorted(historical),
                     "contradicted_by": contra,
                     "status": "supported_by_current_checks" if current else
                     "historical_support_only" if historical else "unsupported"})
    return rows


# --------------------------------------------------------------------------
# Traceability matrix (COMPONENT_TRACEABILITY.json)
# --------------------------------------------------------------------------
COVERAGE = {"module_check", "interface_only", "out_of_scope", "no_data"}


@_structural
def validate_traceability(trace, catalog):
    if not isinstance(trace, dict) or trace.get("schema") != TRACE_SCHEMA:
        raise RegistryError(f"Unsupported traceability schema {trace.get('schema') if isinstance(trace, dict) else None!r}")
    allowed = {"schema", "catalog", "generated_note", "coverage_legend", "rows"}
    if set(trace) - allowed or not {"schema", "catalog", "rows"} <= set(trace):
        raise RegistryError(f"traceability: keys must be within {sorted(allowed)}")
    cat = trace["catalog"]
    if not isinstance(cat, dict) or set(cat) != {"path", "revision", "sha256_lf", "components", "requirements"}:
        raise RegistryError("traceability.catalog: needs path, revision, sha256_lf, components, requirements")
    check_locator(cat["path"], "traceability.catalog.path")
    problems = []
    if cat["sha256_lf"] != catalog["sha256_lf"]:
        problems.append("catalog sha256_lf differs from supplied catalog: re-map required")
    for key in ("requirements", "components"):
        if isinstance(cat[key], bool) or not isinstance(cat[key], int):
            raise RegistryError(f"traceability.catalog.{key}: integer required")
    if not isinstance(trace["rows"], list):
        raise RegistryError("traceability.rows: array required")
    if cat["requirements"] != len(catalog["requirements"]) or cat["components"] != len(catalog["components"]):
        problems.append("recorded component/requirement counts differ from supplied catalog")
    seen = {}
    row_keys = {"requirement", "component", "gate", "r_blocks", "coverage", "module_checks",
                "input", "output", "check", "next_consumer", "note"}
    for i, row in enumerate(trace["rows"]):
        w = f"rows[{i}]"
        if not isinstance(row, dict) or set(row) != row_keys:
            raise RegistryError(f"{w}: keys must be exactly {sorted(row_keys)}")
        rid = row["requirement"]
        if not isinstance(rid, str) or not REQ_ID.fullmatch(rid):
            raise RegistryError(f"{w}: invalid requirement id {rid!r}")
        if rid in seen:
            raise RegistryError(f"{w}: duplicate requirement {rid}")
        seen[rid] = row
        if not isinstance(row["coverage"], str) or row["coverage"] not in COVERAGE:
            raise RegistryError(f"{w}: coverage must be one of {sorted(COVERAGE)}")
        if not isinstance(row["r_blocks"], list) or not row["r_blocks"] \
                or any(not isinstance(b, str) or b not in R_BLOCKS for b in row["r_blocks"]):
            raise RegistryError(f"{w}: r_blocks must be nonempty subset of R00..R11")
        str_list(row["module_checks"], f"{w}.module_checks")
        unknown_checks = set(row["module_checks"]) - set(CHECK_CODES)
        if unknown_checks:
            raise RegistryError(f"{w}: unknown module check codes {sorted(unknown_checks)}")
        if row["coverage"] == "module_check" and not row["module_checks"]:
            raise RegistryError(f"{w}: module_check coverage must name executed check codes")
        if row["coverage"] != "module_check" and row["module_checks"]:
            raise RegistryError(f"{w}: only module_check rows may claim module checks")
        for k in ("input", "output", "check", "next_consumer", "note"):
            if not nonempty(row[k]):
                raise RegistryError(f"{w}.{k} required")
        if rid in catalog["requirements"]:
            exp = catalog["requirements"][rid]
            if row["component"] != exp["component"] or row["gate"] != exp["gate"]:
                problems.append(f"{rid}: component/gate differ from catalog")
    missing = sorted(set(catalog["requirements"]) - set(seen))
    extra = sorted(set(seen) - set(catalog["requirements"]))
    if missing:
        problems.append(f"requirements missing from matrix: {missing}")
    if extra:
        problems.append(f"matrix ids absent from catalog: {extra}")
    coverage = defaultdict(int)
    for row in seen.values():
        coverage[row["coverage"]] += 1
    comps_in_rows = sorted({r["component"] for r in seen.values()})
    return {"report_schema": "ags.traceability-report/1", "status": "CONSISTENT" if not problems else "INCONSISTENT",
            "catalog_sha256_lf": catalog["sha256_lf"], "catalog_requirements": len(catalog["requirements"]),
            "catalog_components": len(catalog["components"]), "matrix_rows": len(seen),
            "components_covered": comps_in_rows, "coverage_counts": dict(sorted(coverage.items())),
            "problems": problems,
            "note": "module_check means this validator enforces the stated data-level property on supplied "
                    "records; it does not mean the real-world requirement is satisfied."}


def render_traceability_md(trace):
    lines = ["# COMPONENT_TRACEABILITY (generated)", "",
             "Generated by `python validator.py render-trace` from COMPONENT_TRACEABILITY.json. "
             "Edit the JSON, not this file.", "",
             f"Catalog: `{trace['catalog']['path']}` at `{trace['catalog']['revision']}`, "
             f"sha256 (LF-normalised) `{trace['catalog']['sha256_lf']}`; "
             f"{trace['catalog']['components']} components, {trace['catalog']['requirements']} requirements.", "",
             "Coverage: `module_check` = executed data-level check in validator.py; `interface_only` = contract "
             "described, not implemented; `out_of_scope` = outside this work packet; `no_data` = cannot be "
             "assessed from available inputs.", ""]
    counts = defaultdict(int)
    for r in trace["rows"]:
        counts[r["coverage"]] += 1
    lines.append("Totals: " + ", ".join(f"{k} {v}" for k, v in sorted(counts.items())) + ".")
    lines.append("")
    lines.append("| Requirement | Gate | R-blocks | Coverage | Module checks | Input → Output | Check | Next consumer | Note |")
    lines.append("|---|---|---|---|---|---|---|---|---|")
    esc = lambda s: str(s).replace("|", "\\|").replace("\n", " ")
    for r in sorted(trace["rows"], key=lambda x: x["requirement"]):
        lines.append(f"| {r['requirement']} | {r['gate']} | {', '.join(r['r_blocks'])} | {r['coverage']} | "
                     f"{', '.join(r['module_checks']) or '—'} | {esc(r['input'])} → {esc(r['output'])} | "
                     f"{esc(r['check'])} | {esc(r['next_consumer'])} | {esc(r['note'])} |")
    return "\n".join(lines) + "\n"


# --------------------------------------------------------------------------
# Output containment and CLI
# --------------------------------------------------------------------------
def dump(value):
    return json.dumps(value, ensure_ascii=False, indent=2, sort_keys=True,
                      default=lambda o: str(o) if isinstance(o, Decimal) else None) + "\n"


def safe_output(path, inputs, overwrite):
    """Output containment: resolved target must lie inside MODULE_ROOT and not be an input."""
    target = Path(path).resolve()
    if MODULE_ROOT not in target.parents:
        raise RegistryError(f"Refusing to write outside module directory: {Path(path).name}")
    if any(":" in part for part in target.relative_to(MODULE_ROOT).parts):
        raise RegistryError("':' is not allowed in output names (alternate data streams)")
    if target.suffix not in {".json", ".md"}:
        raise RegistryError("Report output must be .json or .md")
    for p in inputs:
        if p is not None and Path(p).resolve() == target:
            raise RegistryError("Refusing to overwrite an input file")
    if target.exists() and not overwrite:
        raise RegistryError(f"Output exists; pass --overwrite to replace: {target.name}")
    if target.exists() and not (target.parent == MODULE_ROOT / "Evidence"
                                or target == MODULE_ROOT / "COMPONENT_TRACEABILITY.md"):
        raise RegistryError("Overwrite is limited to Evidence/ reports and the generated COMPONENT_TRACEABILITY.md")
    if target.parent != MODULE_ROOT and not target.parent.is_dir():
        raise RegistryError(f"Output directory does not exist: {target.parent}")
    return target


def write_output(path, text, inputs, overwrite):
    target = safe_output(path, inputs, overwrite)
    with target.open("w", encoding="utf-8", newline="\n") as fh:
        fh.write(text)
    return target


def _as_of(value):
    return iso_day(value, "--as-of", nullable=False) if value else None


def emit(text):
    """Write UTF-8 regardless of the console code page (e.g. cp1251)."""
    buffer = getattr(sys.stdout, "buffer", None)
    if buffer is not None:
        sys.stdout.flush()
        buffer.write(text.encode("utf-8"))
        buffer.flush()
    else:
        sys.stdout.write(text)


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    sub = parser.add_subparsers(dest="command", required=True)
    chk = sub.add_parser("check", help="validate a registry and report traceability/gaps")
    chk.add_argument("registry", type=Path)
    chk.add_argument("--catalog", type=Path)
    chk.add_argument("--as-of")
    chk.add_argument("--artifact-root", type=Path, help="explicit read-only root for sha256 checks")
    chk.add_argument("--output", type=Path, help="write JSON report inside module directory")
    chk.add_argument("--overwrite", action="store_true")
    q = sub.add_parser("query", help="why | impact | requirement-gaps | declared-only | promises")
    q.add_argument("registry", type=Path)
    q.add_argument("kind", choices=["why", "impact", "requirement-gaps", "declared-only", "promises"])
    q.add_argument("target", nargs="?")
    q.add_argument("--catalog", type=Path)
    q.add_argument("--as-of")
    tr = sub.add_parser("trace", help="check COMPONENT_TRACEABILITY.json against the catalog")
    tr.add_argument("traceability", type=Path)
    tr.add_argument("--catalog", type=Path, required=True)
    rt = sub.add_parser("render-trace", help="generate COMPONENT_TRACEABILITY.md")
    rt.add_argument("traceability", type=Path)
    rt.add_argument("--catalog", type=Path, required=True)
    rt.add_argument("--output", type=Path, required=True)
    rt.add_argument("--overwrite", action="store_true")
    sr = sub.add_parser("sources", help="validate SOURCE_REGISTER.json (Source entities only)")
    sr.add_argument("register", type=Path)
    args = parser.parse_args(argv)
    try:
        if args.command == "check":
            catalog = load_catalog(args.catalog) if args.catalog else None
            report = assess(load_json(args.registry), catalog, _as_of(args.as_of), args.artifact_root)
            text = dump(report)
            if args.output:
                target = write_output(args.output, text, [args.registry, args.catalog], args.overwrite)
                emit(dump({"status": "WRITTEN", "path": target.name,
                            "finding_counts": report["finding_counts"]}))
            else:
                emit(text)
            return 1 if report["finding_counts"].get("error") else 0
        if args.command == "query":
            data = load_json(args.registry)
            nodes, edges = validate_registry(data)
            catalog = load_catalog(args.catalog) if args.catalog else None
            if args.kind in {"why", "impact"} and not args.target:
                raise RegistryError(f"query {args.kind} needs a target id")
            if args.kind == "why":
                out = query_why(nodes, edges, args.target)
            elif args.kind == "impact":
                out = query_impact(nodes, edges, args.target)
            else:
                report = assess(data, catalog, _as_of(args.as_of))
                if args.kind == "requirement-gaps":
                    out = {k: v for k, v in report["requirements"].items() if v["verdict"] != "PASS"}
                elif args.kind == "declared-only":
                    out = [c for c in report["capabilities"] if c["state"] != "invocation_verified"]
                else:
                    out = query_promises(nodes, edges, report)
            emit(dump({"query": args.kind, "target": args.target, "result": out}))
            return 0
        if args.command in {"trace", "render-trace"}:
            catalog = load_catalog(args.catalog)
            trace = load_json(args.traceability)
            result = validate_traceability(trace, catalog)
            if args.command == "trace":
                emit(dump(result))
                return 0 if result["status"] == "CONSISTENT" else 1
            if result["status"] != "CONSISTENT":
                emit(dump(result))
                return 1
            target = write_output(args.output, render_traceability_md(trace),
                                  [args.traceability, args.catalog], args.overwrite)
            emit(dump({"status": "WRITTEN", "path": target.name}))
            return 0
        if args.command == "sources":
            reg = load_json(args.register)
            if not isinstance(reg, dict) or reg.get("schema") != SOURCE_REGISTER_SCHEMA:
                raise RegistryError(f"Unsupported source register schema; supported: {SOURCE_REGISTER_SCHEMA}")
            if set(reg) - {"schema", "as_of", "note", "sources"} or not isinstance(reg.get("sources"), list):
                raise RegistryError("source register: keys schema, as_of, note, sources[]")
            iso_day(reg.get("as_of"), "as_of", nullable=False)
            ids, depth = set(), defaultdict(int)
            for i, s in enumerate(reg["sources"]):
                if not isinstance(s, dict) or s.get("type") != "Source":
                    raise RegistryError(f"sources[{i}]: Source entities only")
                _, sid = _check_entity(s, f"sources[{i}]")
                if sid in ids:
                    raise RegistryError(f"Duplicate source id {sid}")
                ids.add(sid)
                depth[s["study_depth"]] += 1
            emit(dump({"status": "VALID", "sources": len(ids), "study_depth": dict(sorted(depth.items()))}))
            return 0
    except RegistryError as exc:
        emit(dump({"status": "INVALID", "error": str(exc)}))
        return 2
    except (TypeError, KeyError, AttributeError, ValueError, RecursionError, IndexError) as exc:
        emit(dump({"status": "INVALID", "error": f"Malformed input structure: {type(exc).__name__}: {exc}"}))
        return 2
    return 2


if __name__ == "__main__":
    sys.exit(main())
