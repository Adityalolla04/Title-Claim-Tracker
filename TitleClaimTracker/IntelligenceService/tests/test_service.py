from pathlib import Path
from typing import ClassVar

from fastapi.testclient import TestClient

from title_claim_intelligence.app import create_app
from title_claim_intelligence.classifier import classify
from title_claim_intelligence.training import (
    default_dataset_path,
    load_reviewed_fixture,
    split_by_source_group,
    train,
)


class FakeModel:
    classes_: ClassVar[list[str]] = ["Title Claim", "Out-of-scope/uncertain"]

    def __init__(self, probabilities: list[float]) -> None:
        self.probabilities = probabilities

    def predict_proba(self, samples: list[str]) -> list[list[float]]:
        assert len(samples) == 1
        return [self.probabilities]


def test_health_reports_degraded_when_no_trusted_model_is_configured() -> None:
    client = TestClient(create_app())

    response = client.get("/health")

    assert response.status_code == 200
    assert response.json() == {
        "status": "degraded",
        "classification": "unavailable",
        "extraction": "ready",
    }


def test_classification_fails_closed_without_model() -> None:
    client = TestClient(create_app())

    response = client.post("/api/v1/classify", json={"text": "A fictional deed signature is disputed."})

    assert response.status_code == 503
    assert response.json()["detail"] == "No trusted model artifact is configured."


def test_classification_never_marks_prediction_as_accepted() -> None:
    client = TestClient(create_app(FakeModel([0.91, 0.09]), "test-model"))

    response = client.post("/api/v1/classify", json={"text": "A fictional deed signature is disputed."})

    assert response.status_code == 200
    assert response.json()["category"] == "Title Claim"
    assert response.json()["status"] == "needs_review"
    assert response.json()["provenance"] == "synthetic_development"


def test_extraction_returns_missing_fields_without_persisting_input() -> None:
    client = TestClient(create_app())

    response = client.post("/api/v1/extract", json={"text": "owner=Fictional Person; state=ny"})

    assert response.status_code == 200
    assert response.json()["claimantName"] == "Fictional Person"
    assert response.json()["state"] == "NY"
    assert response.json()["missingFields"] == ["address", "city"]
    assert response.json()["followUpQuestions"] == [
        "Please provide the property address.",
        "Please provide the property city.",
    ]


def test_intake_contract_rejects_empty_and_oversized_text() -> None:
    client = TestClient(create_app())

    assert client.post("/api/v1/extract", json={"text": " "}).status_code == 422
    assert client.post("/api/v1/extract", json={"text": "x" * 5001}).status_code == 422


def test_training_holdout_keeps_source_groups_disjoint_and_all_labels_present() -> None:
    rows = load_reviewed_fixture(default_dataset_path())
    train_indices, test_indices = split_by_source_group(rows)
    train_groups = {rows[index]["TemplateOrSourceGroup"] for index in train_indices}
    test_groups = {rows[index]["TemplateOrSourceGroup"] for index in test_indices}

    assert train_groups.isdisjoint(test_groups)
    assert {rows[index]["PrimaryLabel"] for index in train_indices} == {
        row["PrimaryLabel"] for row in rows
    }
    assert {rows[index]["PrimaryLabel"] for index in test_indices} == {
        row["PrimaryLabel"] for row in rows
    }


def test_training_writes_synthetic_only_artifacts_without_narratives(tmp_path: Path) -> None:
    evaluation = train(default_dataset_path(), tmp_path, seed=42)
    evaluation_text = (tmp_path / "evaluation.json").read_text(encoding="utf-8")

    assert (tmp_path / "classifier.joblib").is_file()
    assert evaluation["provenance"].startswith("Synthetic reviewed examples only")
    assert set(evaluation["trainingGroups"]).isdisjoint(evaluation["evaluationGroups"])
    assert "fictional parcel" not in evaluation_text


def test_classifier_abstains_on_uncertain_and_low_confidence_results() -> None:
    uncertain = classify("uncertain", FakeModel([0.01, 0.99]), "test")
    low_confidence = classify("weak signal", FakeModel([0.60, 0.40]), "test")

    assert uncertain.category is None
    assert low_confidence.category is None
    assert uncertain.status == low_confidence.status == "needs_review"
