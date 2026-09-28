import argparse
import csv
import json
from datetime import UTC, datetime
from pathlib import Path
from typing import Any
from uuid import uuid4

import joblib
from sklearn.feature_extraction.text import TfidfVectorizer
from sklearn.linear_model import LogisticRegression
from sklearn.metrics import accuracy_score, classification_report, confusion_matrix
from sklearn.model_selection import GroupShuffleSplit
from sklearn.pipeline import Pipeline

REQUIRED_COLUMNS = {"ExampleId", "Text", "PrimaryLabel", "IsSynthetic", "ReviewStatus", "TemplateOrSourceGroup"}


def default_dataset_path() -> Path:
    project_root = Path(__file__).resolve().parents[3]
    return project_root / "Data" / "ML" / "Samples" / "intake-examples-v1.csv"


def load_reviewed_fixture(path: Path) -> list[dict[str, str]]:
    with path.open(newline="", encoding="utf-8-sig") as source:
        reader = csv.DictReader(source)
        if not REQUIRED_COLUMNS.issubset(reader.fieldnames or []):
            raise ValueError("Dataset is missing required provenance or label columns.")
        rows = [
            row for row in reader
            if row["ReviewStatus"].strip().casefold() == "reviewed"
            and row["IsSynthetic"].strip().casefold() == "true"
        ]

    if len(rows) < 5:
        raise ValueError("At least five reviewed synthetic examples are required.")
    if any(not row["Text"].strip() or not row["TemplateOrSourceGroup"].strip() for row in rows):
        raise ValueError("Every included example must have text and a source group.")
    if len({row["ExampleId"] for row in rows}) != len(rows):
        raise ValueError("ExampleId values must be unique.")
    return rows


def split_by_source_group(
    rows: list[dict[str, str]], test_fraction: float = 0.5, seed: int = 42
) -> tuple[list[int], list[int]]:
    labels = [row["PrimaryLabel"] for row in rows]
    groups = [row["TemplateOrSourceGroup"] for row in rows]
    classes = set(labels)
    splitter = GroupShuffleSplit(n_splits=256, test_size=test_fraction, random_state=seed)
    for train_indices, test_indices in splitter.split(rows, labels, groups):
        if {labels[index] for index in train_indices} == classes and {labels[index] for index in test_indices} == classes:
            train_groups = {groups[index] for index in train_indices}
            test_groups = {groups[index] for index in test_indices}
            if train_groups.isdisjoint(test_groups):
                return train_indices.tolist(), test_indices.tolist()
    raise ValueError("Could not produce a source-group holdout containing every label on both sides.")


def train(dataset_path: Path, output_dir: Path, seed: int = 42) -> dict[str, Any]:
    rows = load_reviewed_fixture(dataset_path)
    train_indices, test_indices = split_by_source_group(rows, seed=seed)
    labels = [row["PrimaryLabel"] for row in rows]
    texts = [row["Text"] for row in rows]
    classes = sorted(set(labels))
    model = Pipeline(
        [
            ("features", TfidfVectorizer(ngram_range=(1, 2), sublinear_tf=True)),
            ("classifier", LogisticRegression(max_iter=1000, class_weight="balanced", random_state=seed)),
        ]
    )
    model.fit([texts[index] for index in train_indices], [labels[index] for index in train_indices])
    predictions = model.predict([texts[index] for index in test_indices])
    output_dir.mkdir(parents=True, exist_ok=True)
    model_path = output_dir / "classifier.joblib"
    joblib.dump(model, model_path)

    evaluation = {
        "modelVersion": f"synthetic-{datetime.now(UTC).strftime('%Y%m%dT%H%M%SZ')}-{uuid4().hex[:8]}",
        "dataset": dataset_path.name,
        "provenance": "Synthetic reviewed examples only; development evaluation, not production evidence.",
        "seed": seed,
        "trainingSamples": len(train_indices),
        "evaluationSamples": len(test_indices),
        "trainingGroups": sorted({rows[index]["TemplateOrSourceGroup"] for index in train_indices}),
        "evaluationGroups": sorted({rows[index]["TemplateOrSourceGroup"] for index in test_indices}),
        "accuracy": accuracy_score([labels[index] for index in test_indices], predictions),
        "classificationReport": classification_report(
            [labels[index] for index in test_indices], predictions, labels=classes, output_dict=True, zero_division=0
        ),
        "confusionMatrix": confusion_matrix(
            [labels[index] for index in test_indices], predictions, labels=classes
        ).tolist(),
        "classes": classes,
        "limitations": [
            "The fixture is small and synthetic; metrics do not estimate real-world performance.",
            "All predictions require human review and are not legal conclusions.",
            "Input narratives and identifiers are not included in the evaluation artifact.",
        ],
    }
    (output_dir / "evaluation.json").write_text(json.dumps(evaluation, indent=2), encoding="utf-8")
    return evaluation


def main() -> None:
    parser = argparse.ArgumentParser(description="Train a development classifier using source-group holdout.")
    parser.add_argument("--dataset", type=Path, default=default_dataset_path())
    parser.add_argument("--output", type=Path, default=Path("artifacts"))
    parser.add_argument("--seed", type=int, default=42)
    args = parser.parse_args()
    evaluation = train(args.dataset, args.output, args.seed)
    print(json.dumps({key: evaluation[key] for key in ("modelVersion", "trainingSamples", "evaluationSamples", "accuracy")}, indent=2))


if __name__ == "__main__":
    main()
