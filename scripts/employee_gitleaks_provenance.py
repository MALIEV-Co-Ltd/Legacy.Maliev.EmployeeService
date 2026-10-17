"""Temporary hosted diagnostic: never emit Match, Secret, Entropy, message or raw report."""
import json
import os
from pathlib import Path
import re
import sys
from verify_employee_metadata_proof import CUSTODY, GRANTS, bounded_read, object_json

def reviewed_scope(commit):
    if not isinstance(commit, str) or not re.fullmatch(r"[0-9a-f]{40}", commit):
        raise ValueError("Reviewed scan commit refused")
    manifest = object_json(bounded_read(CUSTODY))
    if manifest["schemaVersion"] != 1 or manifest["grants"] != GRANTS:
        raise ValueError("Reviewed inventory refused")
    paths = {row["path"] for row in manifest["candidate"]}
    baseline = {row["path"] for row in manifest["baseline"]}
    if not baseline.issubset(paths):
        raise ValueError("Historical-only inventory path refused")
    # The externally bound self-manifest is deliberately not an emitted File scope.
    # Unknown/deleted/self paths fixed-refuse; no inferred filesystem additions.
    for name in paths:
        if (not isinstance(name, str) or len(name) > 512
                or not re.fullmatch(r"[A-Za-z0-9_./ -]+", name)
                or any(part in ("", ".", "..") for part in name.split("/"))):
            raise ValueError("Reviewed inventory path refused")
    return frozenset(paths), frozenset((commit,))

def safe_provenance(data, allowed_paths, allowed_commits):
    findings = object_json(data)
    if not isinstance(findings, list) or len(findings) > 1000:
        raise ValueError("Finding count refused")
    rows = []
    for finding in findings:
        if not isinstance(finding, dict):
            raise ValueError("Finding shape refused")
        rule, name, line, commit = (finding.get(key) for key in ("RuleID", "File", "StartLine", "Commit"))
        if not isinstance(rule, str) or not re.fullmatch(r"[A-Za-z0-9_-]{1,128}", rule):
            raise ValueError("Rule identity refused")
        if (not isinstance(name, str) or len(name) > 512
                or not re.fullmatch(r"[A-Za-z0-9_./ -]+", name)
                or any(part in ("", ".", "..") for part in name.split("/"))):
            raise ValueError("Relative path refused")
        if name not in allowed_paths:
            raise ValueError("Finding path outside reviewed inventory")
        if type(line) is not int or not 1 <= line <= 10000000:
            raise ValueError("Line identity refused")
        if not isinstance(commit, str) or not re.fullmatch(r"[0-9a-f]{40}", commit):
            raise ValueError("Commit identity refused")
        if commit not in allowed_commits:
            raise ValueError("Finding commit outside reviewed scan")
        rows.append({"RuleID": rule, "File": name, "StartLine": line, "Commit": commit})
    return rows

if __name__ == "__main__":
    try:
        if len(sys.argv) != 2:
            raise ValueError("One report required")
        paths, commits = reviewed_scope(os.environ.get("GITHUB_SHA"))
        rows = safe_provenance(bounded_read(Path(sys.argv[1])), paths, commits)
        for row in rows:
            print(json.dumps(row, sort_keys=True))
    except (ValueError, OSError, UnicodeError, TypeError, KeyError):
        print("Gitleaks provenance diagnostic refused.", file=sys.stderr)
        sys.exit(2)
