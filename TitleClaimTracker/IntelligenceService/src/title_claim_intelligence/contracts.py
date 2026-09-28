from typing import Literal

from pydantic import BaseModel, ConfigDict, Field
from pydantic.alias_generators import to_camel


class ApiModel(BaseModel):
    model_config = ConfigDict(alias_generator=to_camel, populate_by_name=True)


class IntakeRequest(ApiModel):
    model_config = ConfigDict(str_strip_whitespace=True)

    text: str = Field(min_length=1, max_length=5000)


class ClassificationResponse(ApiModel):
    category: str | None
    score: float | None = Field(default=None, ge=0, le=1)
    status: Literal["needs_review"] = "needs_review"
    model_version: str | None = None
    provenance: Literal["synthetic_development"] = "synthetic_development"
    abstention_reason: str | None = None


class ExtractionResponse(ApiModel):
    mode: Literal["deterministic"] = "deterministic"
    claimant_name: str | None = None
    address: str | None = None
    city: str | None = None
    state: str | None = None
    issue_description: str
    summary: str
    missing_fields: list[str]
    follow_up_questions: list[str]
