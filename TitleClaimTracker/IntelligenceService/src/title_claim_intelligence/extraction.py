import re

from .contracts import ExtractionResponse

_FIELD_PATTERNS = {
    "claimant_name": re.compile(r"(?:claimant|owner)\s*[:=]\s*([^,;]+)", re.IGNORECASE),
    "address": re.compile(r"(?:address|property)\s*[:=]\s*([^,;]+)", re.IGNORECASE),
    "city": re.compile(r"city\s*[:=]\s*([^,;]+)", re.IGNORECASE),
    "state": re.compile(r"state\s*[:=]\s*([a-z]{2})", re.IGNORECASE),
}
_REQUIRED_FIELDS = ("address", "city", "state")


def extract(text: str) -> ExtractionResponse:
    values: dict[str, str | None] = {}
    for field, pattern in _FIELD_PATTERNS.items():
        match = pattern.search(text)
        values[field] = match.group(1).strip()[:255] if match else None

    state = values["state"]
    if state is not None:
        values["state"] = state.upper()

    missing = [field for field in _REQUIRED_FIELDS if values[field] is None]
    return ExtractionResponse(
        claimant_name=values["claimant_name"],
        address=values["address"],
        city=values["city"],
        state=values["state"],
        issue_description=text,
        summary=text if len(text) <= 300 else text[:300] + "…",
        missing_fields=missing,
        follow_up_questions=[f"Please provide the property {field}." for field in missing],
    )
