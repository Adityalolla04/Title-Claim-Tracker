"""Render a README-ready dashboard from the validated synthetic CSV fixture."""

from __future__ import annotations

import argparse
import csv
import json
from collections import Counter
from datetime import datetime
from pathlib import Path


def parse_timestamp(value: str | None) -> datetime | None:
    if not value:
        return None
    return datetime.fromisoformat(value.replace("Z", "+00:00"))


def read_csv(path: Path) -> list[dict[str, str]]:
    with path.open(encoding="utf-8-sig", newline="") as source:
        return list(csv.DictReader(source))


def summarize(data_dir: Path) -> dict[str, object]:
    quality = json.loads((data_dir.parent / "quality-report.json").read_text(encoding="utf-8"))
    if quality.get("Status") != "Passed" or quality.get("IsSynthetic") is not True:
        raise ValueError("Input quality report must pass and identify synthetic data.")

    claims = read_csv(data_dir / "claims.csv")
    attempts = read_csv(data_dir / "triage_attempts.csv")
    model_metrics = read_csv(data_dir / "model_metrics.csv")
    if any(row.get("IsSynthetic", "").lower() != "true" for row in claims + attempts):
        raise ValueError("Every claim and triage row must be marked synthetic.")

    filed_by_month: Counter[str] = Counter()
    solved_by_month: Counter[str] = Counter()
    for claim in claims:
        filed = parse_timestamp(claim.get("CreatedUtc"))
        solved = parse_timestamp(claim.get("ResolvedUtc"))
        if filed:
            filed_by_month[filed.strftime("%Y-%m")] += 1
        if solved and claim.get("CurrentStatus") in {"Resolved", "Closed"}:
            solved_by_month[solved.strftime("%Y-%m")] += 1

    statuses = Counter(row.get("CurrentStatus", "Unknown") for row in claims)
    outcomes = Counter(row.get("Outcome", "Unknown") for row in attempts)
    f1_scores = {
        row["ClassName"]: float(row["MetricValue"])
        for row in model_metrics
        if row.get("MetricName") == "F1"
        and row.get("ClassName") not in {"", "All"}
        and row.get("MetricValue")
    }
    return {
        "snapshot": quality.get("SnapshotUtc", "Unknown snapshot"),
        "claims_filed": len(claims),
        "claims_solved": sum(solved_by_month.values()),
        "open_backlog": sum(count for status, count in statuses.items() if status not in {"Resolved", "Closed"}),
        "triage_attempts": len(attempts),
        "months": sorted(set(filed_by_month) | set(solved_by_month)),
        "filed_by_month": filed_by_month,
        "solved_by_month": solved_by_month,
        "statuses": statuses,
        "outcomes": outcomes,
        "f1_scores": f1_scores,
    }


def render_dashboard(summary: dict[str, object], output: Path) -> None:
    import matplotlib

    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
    import numpy as np

    colors = {
        "ink": "#19333d", "muted": "#62777b", "teal": "#17877f",
        "blue": "#5588a8", "amber": "#d88a3d", "pale": "#e9f0ef", "paper": "#f7f8f5",
    }
    plt.rcParams.update({"font.family": "DejaVu Sans", "axes.titleweight": "bold", "text.color": colors["ink"]})
    figure = plt.figure(figsize=(14, 9), facecolor=colors["paper"])
    figure.suptitle("TITLE CLAIM TRACKER", x=0.07, y=0.965, ha="left", fontsize=21, fontweight="bold", color=colors["ink"])
    figure.text(0.07, 0.928, "SYNTHETIC OPERATIONS SNAPSHOT  |  Filed, solved, and triage comparisons", fontsize=10, color=colors["muted"])

    kpis = [
        ("CLAIMS FILED", summary["claims_filed"], colors["teal"]),
        ("SOLVED", summary["claims_solved"], colors["blue"]),
        ("OPEN BACKLOG", summary["open_backlog"], colors["amber"]),
        ("TRIAGE ATTEMPTS", summary["triage_attempts"], colors["ink"]),
    ]
    for index, (label, value, color) in enumerate(kpis):
        x = 0.07 + index * 0.225
        figure.text(x, 0.84, label, fontsize=9, fontweight="bold", color=colors["muted"])
        figure.text(x, 0.785, str(value), fontsize=25, fontweight="bold", color=color)
        figure.add_artist(plt.Line2D([x, x + 0.18], [0.76, 0.76], color=color, linewidth=3, transform=figure.transFigure))

    grid = figure.add_gridspec(2, 2, left=0.07, right=0.95, bottom=0.10, top=0.70, hspace=0.48, wspace=0.30)
    filed_ax, status_ax, triage_ax, model_ax = (figure.add_subplot(grid[row, col]) for row, col in ((0, 0), (0, 1), (1, 0), (1, 1)))
    for axis in (filed_ax, status_ax, triage_ax, model_ax):
        axis.set_facecolor("white")
        axis.spines[["top", "right", "left"]].set_visible(False)
        axis.spines["bottom"].set_color("#d9e2e0")
        axis.tick_params(colors=colors["muted"], length=0, pad=6)
        axis.grid(axis="y", color=colors["pale"], linewidth=0.8)
        axis.set_axisbelow(True)

    months = summary["months"]
    positions = np.arange(len(months))
    filed = [summary["filed_by_month"][month] for month in months]
    solved = [summary["solved_by_month"][month] for month in months]
    filed_ax.bar(positions - 0.19, filed, width=0.36, color=colors["teal"], label="Filed")
    filed_ax.bar(positions + 0.19, solved, width=0.36, color=colors["amber"], label="Solved")
    filed_ax.set_xticks(positions, [datetime.strptime(month, "%Y-%m").strftime("%b %Y") for month in months])
    filed_ax.set_title("Filed vs solved by month", loc="left", fontsize=12, pad=12)
    filed_ax.set_ylabel("Cases")
    filed_ax.legend(frameon=False, ncol=2, loc="upper right")

    statuses = summary["statuses"]
    status_names = list(statuses)
    status_ax.barh(status_names, [statuses[name] for name in status_names], color=[colors["amber"] if name in {"Resolved", "Closed"} else colors["blue"] for name in status_names])
    status_ax.set_title("Current claim status", loc="left", fontsize=12, pad=12)
    status_ax.set_xlabel("Cases")
    status_ax.invert_yaxis()

    outcomes = summary["outcomes"]
    outcome_names = list(outcomes)
    triage_ax.bar(outcome_names, [outcomes[name] for name in outcome_names], color=[colors["teal"], colors["amber"], colors["blue"]][:len(outcome_names)])
    triage_ax.set_title("Triage outcomes", loc="left", fontsize=12, pad=12)
    triage_ax.set_ylabel("Attempts")
    triage_ax.tick_params(axis="x", rotation=12)

    f1_scores = summary["f1_scores"]
    if f1_scores:
        model_ax.barh(list(f1_scores), [score * 100 for score in f1_scores.values()], color=colors["blue"])
        model_ax.set_xlim(0, 100)
        model_ax.set_xlabel("F1 score (percent)")
        model_ax.invert_yaxis()
    else:
        model_ax.text(0.5, 0.5, "No class-level F1 metrics", ha="center", va="center", transform=model_ax.transAxes, color=colors["muted"])
        model_ax.set_xticks([])
        model_ax.set_yticks([])
    model_ax.set_title("Model F1 by category", loc="left", fontsize=12, pad=12)

    figure.text(0.07, 0.045, "Synthetic demonstration only. Model metrics are not legal correctness probabilities.", fontsize=9, color=colors["muted"])
    figure.text(0.95, 0.045, f"Snapshot: {summary['snapshot']}", ha="right", fontsize=8, color=colors["muted"])
    output.parent.mkdir(parents=True, exist_ok=True)
    figure.savefig(output, dpi=170, bbox_inches="tight", facecolor=figure.get_facecolor())
    plt.close(figure)


def main() -> None:
    repository_root = Path(__file__).resolve().parents[2]
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--data-dir", type=Path, default=repository_root / "outputs" / "analytics-prepared" / "data")
    parser.add_argument("--output", type=Path, default=repository_root / "TitleClaimTracker" / "Analytics" / "dashboard-preview.png")
    arguments = parser.parse_args()
    render_dashboard(summarize(arguments.data_dir.resolve()), arguments.output.resolve())
    print(f"Synthetic dashboard saved to {arguments.output.resolve()}")


if __name__ == "__main__":
    main()