from __future__ import annotations

import argparse
import hashlib
import hmac
import json
import os
import re
import secrets
import shutil
import tempfile
from dataclasses import dataclass
from datetime import UTC, datetime
from pathlib import Path
from typing import Any

VALID_STATUSES = (
    "New",
    "Under Review",
    "Escalated",
    "Resolved",
    "Closed",
    "Pending Review",
    "Active Investigation",
)

SQL_QUERIES = {
    "claims": """
        SELECT lf.FilingID AS SourceClaimKey,
               ft.TypeName AS PrimaryCategory,
               lf.Status AS CurrentStatus,
               lf.CreationSource,
               p.State AS Geography,
               lf.CreatedUtc,
               resolved.ResolvedUtc,
               CAST(lf.TriagePriority AS int) AS Priority,
               CAST(lf.ClassificationScore AS float) AS ModelScore,
               mv.Version AS ModelVersion,
               lf.IsSyntheticDemoData AS IsSynthetic
        FROM dbo.LegalFilings AS lf
        INNER JOIN dbo.FilingTypes AS ft ON ft.FilingTypeID = lf.FilingTypeID
        INNER JOIN dbo.Properties AS p ON p.PropertyID = lf.PropertyID
        LEFT JOIN dbo.ModelVersions AS mv ON mv.ModelVersionID = lf.ModelVersionID
        OUTER APPLY (
            SELECT MIN(h.TransitionedUtc) AS ResolvedUtc
            FROM dbo.ClaimStatusHistory AS h
            WHERE h.FilingID = lf.FilingID
              AND h.ToStatus IN (N'Resolved', N'Closed')
        ) AS resolved
        WHERE lf.IsDeleted = 0
    """,
    "status_history": """
        SELECT h.FilingID AS SourceClaimKey,
               h.FromStatus AS PreviousStatus,
               h.ToStatus AS NewStatus,
               h.TransitionedUtc AS EventUtc,
               CAST(CASE WHEN h.FromStatus IS NULL THEN N'Legacy status baseline'
                         ELSE N'Application event' END AS nvarchar(40)) AS EventKind,
               lf.IsSyntheticDemoData AS IsSynthetic
        FROM dbo.ClaimStatusHistory AS h
        INNER JOIN dbo.LegalFilings AS lf ON lf.FilingID = h.FilingID
        WHERE lf.IsDeleted = 0
    """,
    "triage_attempts": """
        SELECT ta.TriageAttemptID AS SourceAttemptKey,
               ta.CreatedFilingID AS SourceCreatedClaimKey,
               ta.Outcome,
               ta.CreatedUtc AS StartedUtc,
               ta.CompletedUtc,
               ft.TypeName AS PredictedCategory,
               CAST(ta.ClassificationScore AS float) AS Score,
               mv.Version AS ModelVersion
        FROM dbo.TriageAttempts AS ta
        LEFT JOIN dbo.FilingTypes AS ft ON ft.FilingTypeID = ta.PredictedFilingTypeID
        LEFT JOIN dbo.ModelVersions AS mv ON mv.ModelVersionID = ta.ModelVersionID
    """,
    "model_metrics": """
        SELECT mv.Version AS ModelVersion,
               er.DatasetName,
               er.DatasetVersion,
               CAST(N'Registered model evaluation' AS nvarchar(40)) AS Provenance,
               er.SampleCount,
               er.Accuracy,
               er.MacroF1,
               er.WeightedF1,
               er.PrecisionScore,
               er.RecallScore,
               er.EvaluatedUtc
        FROM dbo.ModelEvaluationResults AS er
        INNER JOIN dbo.ModelVersions AS mv ON mv.ModelVersionID = er.ModelVersionID
    """,
    "public_document_summary": """
        SELECT ep.State AS Geography,
               epd.RecordedDate AS RecordDate,
               epd.SourceDocumentType,
               COUNT(DISTINCT epd.ExternalPropertyDocumentID) AS DocumentCount,
               ds.SourceName AS SourceDesignation
        FROM dbo.ExternalPropertyDocuments AS epd
        INNER JOIN dbo.DataSources AS ds ON ds.DataSourceID = epd.DataSourceID
        INNER JOIN dbo.ExternalDocumentProperties AS edp
            ON edp.ExternalPropertyDocumentID = epd.ExternalPropertyDocumentID
        INNER JOIN dbo.ExternalProperties AS ep ON ep.ExternalPropertyID = edp.ExternalPropertyID
        GROUP BY ep.State, epd.RecordedDate, epd.SourceDocumentType, ds.SourceName
    """,
}

SENSITIVE_COLUMN_NAMES = (
    "CLAIMANTNAME",
    "SUBMITTEDBYUSERID",
    "DELETEDBYUSERID",
    "ACTORUSERID",
    "INPUTTEXT",
    "EXTRACTEDCLAIMANTNAME",
    "EXTRACTEDADDRESS",
    "EXTRACTEDNOTES",
    "CORRECTIONNOTES",
    "NOTES",
    "REASON",
    "OLDVALUE",
    "NEWVALUE",
    "FAILUREMESSAGE",
    "RAWPAYLOAD",
    "IDEMPOTENCYKEY",
)


@dataclass(frozen=True)
class JdbcSettings:
    url: str
    username: str
    password: str
    driver_jar: str


def read_jdbc_settings(environment: dict[str, str] | None = None) -> JdbcSettings:
    values = os.environ if environment is None else environment
    url = values.get("TCT_ANALYTICS_JDBC_URL", "").strip()
    username = values.get("TCT_ANALYTICS_JDBC_USER", "").strip()
    password = values.get("TCT_ANALYTICS_JDBC_PASSWORD", "")
    driver_jar = values.get("TCT_SQLSERVER_JDBC_JAR", "").strip()
    if not url or not username or not password or not driver_jar:
        raise ValueError("Set the read-only JDBC URL, credentials, and driver JAR in environment variables.")
    if re.search(r"(?i)(?:password|pwd)\s*=", url):
        raise ValueError("Do not embed credentials in the JDBC URL.")
    if not Path(driver_jar).is_file():
        raise ValueError("TCT_SQLSERVER_JDBC_JAR must point to an existing Microsoft JDBC driver JAR.")
    return JdbcSettings(url=url, username=username, password=password, driver_jar=driver_jar)


def pseudonym(source_key: Any, namespace: str, run_key: bytes) -> str | None:
    if source_key is None:
        return None
    message = f"{namespace}:{source_key}".encode()
    digest = hmac.new(run_key, message, hashlib.sha256).hexdigest()[:24]
    return f"{namespace.upper()}-{digest}"


def validate_query_allowlist() -> None:
    for name, query in SQL_QUERIES.items():
        upper_query = query.upper()
        if any(column in upper_query for column in SENSITIVE_COLUMN_NAMES):
            raise ValueError(f"Query {name} contains a forbidden sensitive column.")
        if re.search(r"\b(?:INSERT|UPDATE|DELETE|MERGE|EXEC|EXECUTE|TRUNCATE|DROP|ALTER|CREATE)\b", upper_query):
            raise ValueError(f"Query {name} contains a non-read-only statement.")


def _write_single_csv(frame: Any, destination: Path) -> None:
    destination.parent.mkdir(parents=True, exist_ok=True)
    staging_root = destination.parent / ".staging"
    staging_root.mkdir(parents=True, exist_ok=True)
    staging_directory = Path(tempfile.mkdtemp(prefix=f"{destination.stem}-", dir=staging_root))
    try:
        frame.coalesce(1).write.mode("errorifexists").option("header", True).csv(str(staging_directory))
        part_files = list(staging_directory.glob("part-*.csv"))
        if len(part_files) != 1:
            raise RuntimeError(f"Expected one Spark CSV part for {destination.name}.")
        os.replace(part_files[0], destination)
    finally:
        shutil.rmtree(staging_directory, ignore_errors=True)


def run_pipeline(settings: JdbcSettings, output_directory: Path) -> dict[str, Any]:
    validate_query_allowlist()
    try:
        from pyspark.sql import SparkSession
        from pyspark.sql import functions as spark_functions
        from pyspark.sql import types as spark_types
    except ImportError as error:
        raise RuntimeError("PySpark is required; install the analytics project dependencies.") from error

    spark = (
        SparkSession.builder.appName("TitleClaimTrackerReadOnlyAnalytics")
        .master(os.getenv("TCT_SPARK_MASTER", "local[*]"))
        .config("spark.jars", settings.driver_jar)
        .getOrCreate()
    )
    spark.sparkContext.setLogLevel("ERROR")
    run_key = secrets.token_bytes(32)
    hmac_udf = spark_functions.udf(
        lambda value, namespace: pseudonym(value, namespace, run_key), spark_types.StringType()
    )

    def read_query(query: str) -> Any:
        return (
            spark.read.format("jdbc")
            .option("url", settings.url)
            .option("user", settings.username)
            .option("password", settings.password)
            .option("driver", "com.microsoft.sqlserver.jdbc.SQLServerDriver")
            .option("query", query)
            .load()
        )

    try:
        raw_claims = read_query(SQL_QUERIES["claims"])
        claim_map = raw_claims.select("SourceClaimKey").withColumn(
            "ClaimKey", hmac_udf("SourceClaimKey", spark_functions.lit("CLM"))
        )
        snapshot_utc = datetime.now(UTC).isoformat()
        claims = (
            raw_claims.join(claim_map, "SourceClaimKey")
            .withColumn("SnapshotUtc", spark_functions.lit(snapshot_utc))
            .select(
                "ClaimKey", "PrimaryCategory", "CurrentStatus", "CreationSource", "Geography",
                "CreatedUtc", "ResolvedUtc", "Priority", "ModelScore", "ModelVersion",
                "IsSynthetic", "SnapshotUtc",
            )
        )

        raw_history = read_query(SQL_QUERIES["status_history"])
        history = (
            raw_history.join(claim_map, "SourceClaimKey")
            .select("ClaimKey", "PreviousStatus", "NewStatus", "EventUtc", "EventKind", "IsSynthetic")
        )

        raw_attempts = read_query(SQL_QUERIES["triage_attempts"])
        attempts = raw_attempts.withColumn(
            "AttemptKey", hmac_udf("SourceAttemptKey", spark_functions.lit("ATT"))
        )
        created_claim_map = claim_map.select(
            spark_functions.col("SourceClaimKey").alias("SourceCreatedClaimKey"),
            spark_functions.col("ClaimKey").alias("CreatedClaimKey"),
        )
        attempts = (
            attempts.join(created_claim_map, "SourceCreatedClaimKey", "left")
            .select(
                "AttemptKey", "Outcome", "StartedUtc", "CompletedUtc", "PredictedCategory",
                "Score", "ModelVersion", "CreatedClaimKey",
            )
        )
        metrics = read_query(SQL_QUERIES["model_metrics"])
        documents = read_query(SQL_QUERIES["public_document_summary"])

        claim_count = claims.count()
        history_count = history.count()
        attempt_count = attempts.count()
        invalid_statuses = claims.filter(~spark_functions.col("CurrentStatus").isin(*VALID_STATUSES)).count()
        invalid_claim_scores = claims.filter(
            spark_functions.col("ModelScore").isNotNull()
            & ((spark_functions.col("ModelScore") < 0) | (spark_functions.col("ModelScore") > 1))
        ).count()
        invalid_attempt_scores = attempts.filter(
            spark_functions.col("Score").isNotNull()
            & ((spark_functions.col("Score") < 0) | (spark_functions.col("Score") > 1))
        ).count()
        invalid_metric_scores = metrics.filter(
            (spark_functions.col("Accuracy") < 0)
            | (spark_functions.col("Accuracy") > 1)
            | (spark_functions.col("MacroF1") < 0)
            | (spark_functions.col("MacroF1") > 1)
            | (spark_functions.col("WeightedF1") < 0)
            | (spark_functions.col("WeightedF1") > 1)
            | (spark_functions.col("PrecisionScore").isNotNull() & (spark_functions.col("PrecisionScore") > 1))
            | (spark_functions.col("PrecisionScore").isNotNull() & (spark_functions.col("PrecisionScore") < 0))
            | (spark_functions.col("RecallScore").isNotNull() & (spark_functions.col("RecallScore") > 1))
            | (spark_functions.col("RecallScore").isNotNull() & (spark_functions.col("RecallScore") < 0))
        ).count()
        orphan_events = history.join(claims.select("ClaimKey"), "ClaimKey", "left_anti").count()
        orphan_attempts = raw_attempts.filter(
            spark_functions.col("SourceCreatedClaimKey").isNotNull()
        ).join(
            claim_map.select(spark_functions.col("SourceClaimKey").alias("SourceCreatedClaimKey")),
            "SourceCreatedClaimKey",
            "left_anti",
        ).count()
        duplicate_claim_ids = claim_count - claims.select("ClaimKey").distinct().count()
        rejected_rows = (
            invalid_statuses + invalid_claim_scores + invalid_attempt_scores + invalid_metric_scores
            + orphan_events + orphan_attempts + duplicate_claim_ids
        )
        has_synthetic_rows = claims.filter(spark_functions.col("IsSynthetic")).limit(1).count() > 0
        has_operational_rows = claims.filter(~spark_functions.col("IsSynthetic")).limit(1).count() > 0
        report = {
            "snapshotUtc": snapshot_utc,
            "mode": "read-only SQL Server extract",
            "status": "Passed" if rejected_rows == 0 else "Failed",
            "isSynthetic": has_synthetic_rows and not has_operational_rows,
            "containsSynthetic": has_synthetic_rows,
            "containsOperational": has_operational_rows,
            "claims": claim_count,
            "statusHistory": history_count,
            "triageAttempts": attempt_count,
            "modelEvaluationRows": metrics.count(),
            "publicDocumentGroups": documents.count(),
            "invalidStatusRows": invalid_statuses,
            "invalidScoreRows": invalid_claim_scores + invalid_attempt_scores + invalid_metric_scores,
            "orphanStatusRows": orphan_events,
            "orphanCreatedClaimReferences": orphan_attempts,
            "duplicateClaimIds": duplicate_claim_ids,
            "rejectedRows": rejected_rows,
            "exportedColumnsExcludeNarrativeAndUserIdentifiers": True,
            "limitations": [
                "Operational extract; not approved for public publication.",
                "Claim and attempt identifiers are run-scoped HMAC pseudonyms and cannot be linked across runs.",
                "No claim narratives, claimant names, addresses, user IDs, or raw payloads are selected.",
            ],
        }
        output_directory.mkdir(parents=True, exist_ok=True)
        report_path = output_directory / "quality-report.json"
        report_path.write_text(json.dumps(report, indent=2), encoding="utf-8")
        if rejected_rows:
            raise ValueError(f"SQL analytics validation failed; see {report_path}.")

        for name, frame in (
            ("claims.csv", claims),
            ("status_history.csv", history),
            ("triage_attempts.csv", attempts),
            ("model_metrics.csv", metrics),
            ("public_document_summary.csv", documents),
        ):
            _write_single_csv(frame, output_directory / name)
        return report
    finally:
        spark.stop()


def main() -> None:
    parser = argparse.ArgumentParser(description="Export approved fields from SQL Server using read-only PySpark.")
    parser.add_argument(
        "--confirm-read-only-export",
        action="store_true",
        help="Confirm that the configured SQL account is read-only and the local export is authorized.",
    )
    args = parser.parse_args()
    if not args.confirm_read_only_export:
        parser.error("--confirm-read-only-export is required; no SQL connection was attempted.")

    settings = read_jdbc_settings()
    repository_root = Path(__file__).resolve().parents[2]
    output_directory = repository_root / "outputs" / "analytics-sql"
    report = run_pipeline(settings, output_directory)
    print(json.dumps({key: report[key] for key in ("snapshotUtc", "status", "claims", "statusHistory", "triageAttempts")}, indent=2))


if __name__ == "__main__":
    main()
