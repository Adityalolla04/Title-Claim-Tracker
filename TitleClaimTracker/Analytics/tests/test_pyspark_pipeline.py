from pathlib import Path

import pytest
from pyspark_pipeline import (
    SQL_QUERIES,
    VALID_STATUSES,
    pseudonym,
    read_jdbc_settings,
    validate_query_allowlist,
)


def test_sql_queries_are_read_only_and_exclude_sensitive_fields() -> None:
    validate_query_allowlist()

    combined = " ".join(SQL_QUERIES.values()).upper()
    for sensitive_name in (
        "CLAIMANTNAME",
        "SUBMITTEDBYUSERID",
        "ACTORUSERID",
        "INPUTTEXT",
        "EXTRACTEDADDRESS",
        "NOTES",
        "RAWPAYLOAD",
        "CORRECTIONNOTES",
        "OLDVALUE",
        "NEWVALUE",
        "DATASETPROVENANCE",
    ):
        assert sensitive_name not in combined

    assert "ISDELETED = 0" in SQL_QUERIES["claims"].upper()
    assert "Pending Review" in VALID_STATUSES
    assert "Active Investigation" in VALID_STATUSES
    assert "REGISTERED MODEL EVALUATION" in SQL_QUERIES["model_metrics"].upper()


def test_query_allowlist_rejects_future_dml_and_forbidden_columns(monkeypatch: pytest.MonkeyPatch) -> None:
    import pyspark_pipeline

    monkeypatch.setitem(pyspark_pipeline.SQL_QUERIES, "unsafe", "SELECT InputText FROM dbo.TriageAttempts")

    with pytest.raises(ValueError, match="forbidden sensitive column"):
        validate_query_allowlist()


def test_jdbc_configuration_requires_separate_credentials(tmp_path: Path) -> None:
    driver_jar = tmp_path / "mssql-jdbc.jar"
    driver_jar.touch()
    settings = read_jdbc_settings(
        {
            "TCT_ANALYTICS_JDBC_URL": "jdbc:sqlserver://localhost;databaseName=TitleClaimTracker",
            "TCT_ANALYTICS_JDBC_USER": "analytics_reader",
            "TCT_ANALYTICS_JDBC_PASSWORD": "provided-outside-source",
            "TCT_SQLSERVER_JDBC_JAR": str(driver_jar),
        }
    )

    assert settings.username == "analytics_reader"
    assert "provided-outside-source" not in settings.url


def test_jdbc_configuration_rejects_password_embedded_in_url(tmp_path: Path) -> None:
    driver_jar = tmp_path / "mssql-jdbc.jar"
    driver_jar.touch()
    with pytest.raises(ValueError, match="Do not embed credentials"):
        read_jdbc_settings(
            {
                "TCT_ANALYTICS_JDBC_URL": "jdbc:sqlserver://localhost;password=inline-secret",
                "TCT_ANALYTICS_JDBC_USER": "analytics_reader",
                "TCT_ANALYTICS_JDBC_PASSWORD": "external-secret",
                "TCT_SQLSERVER_JDBC_JAR": str(driver_jar),
            }
        )


def test_pseudonyms_are_namespaced_and_run_scoped() -> None:
    first_run = pseudonym(123, "CLM", b"first-run-key")
    same_run = pseudonym(123, "CLM", b"first-run-key")
    next_run = pseudonym(123, "CLM", b"next-run-key")

    assert first_run.startswith("CLM-")
    assert first_run == same_run
    assert first_run != next_run
    assert pseudonym(None, "CLM", b"first-run-key") is None
