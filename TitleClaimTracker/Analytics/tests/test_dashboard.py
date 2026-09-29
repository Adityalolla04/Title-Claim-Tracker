from collections import Counter
from pathlib import Path

from dashboard import summarize


def test_summarize_counts_filed_solved_backlog_and_triage(tmp_path: Path) -> None:
    data_dir = tmp_path / "data"
    data_dir.mkdir()
    (tmp_path / "quality-report.json").write_text(
        '{"Status":"Passed","IsSynthetic":true,"SnapshotUtc":"2026-09-01T00:00:00Z"}', encoding="utf-8"
    )
    (data_dir / "claims.csv").write_text(
        "DemoClaimId,CurrentStatus,CreatedUtc,ResolvedUtc,IsSynthetic\n"
        "A,Resolved,2026-01-02T00:00:00Z,2026-01-10T00:00:00Z,true\n"
        "B,New,2026-01-05T00:00:00Z,,true\n"
        "C,Closed,2026-02-02T00:00:00Z,2026-02-11T00:00:00Z,true\n", encoding="utf-8"
    )
    (data_dir / "triage_attempts.csv").write_text(
        "DemoAttemptId,Outcome,IsSynthetic\nT1,Created,true\nT2,NeedsReview,true\n", encoding="utf-8"
    )
    (data_dir / "model_metrics.csv").write_text(
        "ClassName,MetricName,MetricValue\nLien,F1,0.75\nAll,Accuracy,0.5\n", encoding="utf-8"
    )

    summary = summarize(data_dir)

    assert summary["claims_filed"] == 3
    assert summary["claims_solved"] == 2
    assert summary["open_backlog"] == 1
    assert summary["triage_attempts"] == 2
    assert summary["filed_by_month"] == Counter({"2026-01": 2, "2026-02": 1})
    assert summary["solved_by_month"] == Counter({"2026-01": 1, "2026-02": 1})
    assert summary["f1_scores"] == {"Lien": 0.75}


def test_summarize_rejects_non_synthetic_quality_report(tmp_path: Path) -> None:
    data_dir = tmp_path / "data"
    data_dir.mkdir()
    (tmp_path / "quality-report.json").write_text(
        '{"Status":"Passed","IsSynthetic":false}', encoding="utf-8"
    )

    try:
        summarize(data_dir)
    except ValueError as error:
        assert "synthetic" in str(error)
    else:
        raise AssertionError("Non-synthetic inputs should be rejected.")