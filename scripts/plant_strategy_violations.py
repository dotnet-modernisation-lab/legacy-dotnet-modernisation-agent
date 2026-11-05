"""Proof for the strategy validator, with no model and no unit tests.

Takes a valid strategy JSON produced by `strategy <repo> <dir>` (the deterministic baseline) and writes one variant per
planted violation next to it. Each variant is then replayed with `strategy <repo> <dir2> --model=file:<variant>`;
the validator must reject every variant with exactly the expected code and accept the untouched original.

Usage: python plant_strategy_violations.py <strategy.json> <output-directory>
"""
import copy
import json
import os
import sys

source, out = sys.argv[1], sys.argv[2]
os.makedirs(out, exist_ok=True)

with open(source, encoding="utf-8") as handle:
    good = json.load(handle)


def write(name, strategy):
    with open(os.path.join(out, name + ".json"), "w", encoding="utf-8", newline="\n") as handle:
        json.dump(strategy, handle, indent=2)


write("00-valid", good)

v = copy.deepcopy(good)
v["SummaryEvidence"].append("NoSuchProject/NoSuchClass")
write("01-invented-node-V-UNKNOWN-ID", v)

v = copy.deepcopy(good)
dropped = v["GateNotes"].pop(0)["GateId"]
write("02-dropped-gate-V-GATE-MISSING", v)

v = copy.deepcopy(good)
v["GateNotes"][0]["Recommendation"] += " This gate has been decided in favour of the first option."
write("03-gate-called-decided-V-GATE-DECIDED", v)

v = copy.deepcopy(good)
v["OpenQuestions"] = [q for q in v["OpenQuestions"] if q.get("AspectId") != 7]
write("04-missing-question-V-QUESTION-MISSING", v)

v = copy.deepcopy(good)
# Move a gated node out of its own phase into an earlier phase that does not wait on its gate.
later = next(p for p in v["Phases"][1:] if p["PrerequisiteGates"])
gated = later["NodeIds"].pop(0)
first_phase = v["Phases"][0]
first_phase["NodeIds"].append(gated)
first_phase["PrerequisiteGates"] = []
write("05-gated-node-phased-early-V-PHASE-GATE", v)

v = copy.deepcopy(good)
v["InputSha256"] = "0" * 64
write("06-wrong-hash-V-HASH", v)

v = copy.deepcopy(good)
v["Risks"].append({"Title": "Unsupported worry", "Description": "Something might go wrong.", "Evidence": []})
write("07-no-evidence-UNVERIFIED-not-a-violation", v)

v = copy.deepcopy(good)
v["GateNotes"].append(copy.deepcopy(v["GateNotes"][0]))
write("08-duplicate-gate-V-GATE-DUPLICATE", v)

print("wrote variants to", out)
