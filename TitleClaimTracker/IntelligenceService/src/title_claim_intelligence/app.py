import logging
import os
from pathlib import Path
from typing import Any

import joblib
from fastapi import FastAPI, HTTPException

from .classifier import ProbabilityModel, classify
from .contracts import ClassificationResponse, ExtractionResponse, IntakeRequest
from .extraction import extract

logger = logging.getLogger(__name__)


def _load_configured_model() -> tuple[ProbabilityModel | None, str | None]:
    artifact = os.getenv("TCT_MODEL_ARTIFACT")
    version = os.getenv("TCT_MODEL_VERSION")
    if not artifact:
        return None, version

    artifact_path = Path(artifact)
    if not artifact_path.is_file():
        logger.warning("Configured classification artifact is unavailable.")
        return None, version

    try:
        return joblib.load(artifact_path), version
    except (OSError, ValueError, EOFError):
        logger.exception("Configured classification artifact could not be loaded.")
        return None, version


def create_app(model: ProbabilityModel | None = None, model_version: str | None = None) -> FastAPI:
    service = FastAPI(title="Title Claim Intelligence", version="0.1.0")
    configured_model, configured_version = _load_configured_model() if model is None else (model, model_version)
    service.state.classifier = configured_model
    service.state.model_version = configured_version

    @service.get("/health")
    def health() -> dict[str, Any]:
        return {
            "status": "ready" if service.state.classifier is not None else "degraded",
            "classification": "ready" if service.state.classifier is not None else "unavailable",
            "extraction": "ready",
        }

    @service.post("/api/v1/classify", response_model=ClassificationResponse)
    def classify_intake(request: IntakeRequest) -> ClassificationResponse:
        if service.state.classifier is None:
            raise HTTPException(status_code=503, detail="No trusted model artifact is configured.")
        try:
            threshold = float(os.getenv("TCT_MINIMUM_SCORE", "0.65"))
            if not 0 <= threshold <= 1:
                raise ValueError("Classification threshold must be between zero and one.")
            return classify(request.text, service.state.classifier, service.state.model_version, threshold)
        except (ValueError, TypeError, IndexError) as error:
            logger.warning("Classification failed validation: %s", type(error).__name__)
            raise HTTPException(status_code=503, detail="The classification model is unavailable.") from error

    @service.post("/api/v1/extract", response_model=ExtractionResponse)
    def extract_intake(request: IntakeRequest) -> ExtractionResponse:
        return extract(request.text)

    return service


app = create_app()
