from collections.abc import Sequence
from typing import Any, Protocol

from .contracts import ClassificationResponse

SUPPORTED_LABELS = {"Title Claim", "Lien", "Easement", "Deed Dispute"}
ABSTENTION_LABEL = "Out-of-scope/uncertain"


class ProbabilityModel(Protocol):
    classes_: Sequence[str]

    def predict_proba(self, samples: list[str]) -> Any: ...


def classify(
    text: str,
    model: ProbabilityModel,
    model_version: str | None,
    minimum_score: float = 0.65,
) -> ClassificationResponse:
    probabilities = model.predict_proba([text])[0]
    if len(probabilities) != len(model.classes_):
        raise ValueError("Model output does not match its declared classes.")

    best_index = max(range(len(probabilities)), key=probabilities.__getitem__)
    label = str(model.classes_[best_index])
    score = float(probabilities[best_index])
    if not 0 <= score <= 1:
        raise ValueError("Model confidence must be between zero and one.")

    if label == ABSTENTION_LABEL:
        return ClassificationResponse(
            category=None,
            score=score,
            model_version=model_version,
            abstention_reason="The model marked this intake as out of scope or uncertain.",
        )
    if label not in SUPPORTED_LABELS:
        raise ValueError("Model contains an unsupported classification label.")
    if score < minimum_score:
        return ClassificationResponse(
            category=None,
            score=score,
            model_version=model_version,
            abstention_reason="The model score is below the configured review threshold.",
        )

    return ClassificationResponse(
        category=label,
        score=score,
        model_version=model_version,
        abstention_reason="Synthetic development model; administrator review is required.",
    )
