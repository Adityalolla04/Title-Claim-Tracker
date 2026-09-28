import pytest
from acris_spark_pipeline import (
    CODES_REQUIRED,
    FORBIDDEN_MODEL_FIELDS,
    LEGALS_REQUIRED,
    MASTER_REQUIRED,
    MODEL_FEATURES,
    MODEL_LABEL,
    validate_input_contract,
)


def test_acris_contract_accepts_expected_public_api_fields() -> None:
    validate_input_contract(MASTER_REQUIRED, LEGALS_REQUIRED, CODES_REQUIRED)


def test_document_type_model_excludes_label_and_identifiers_from_features() -> None:
    assert MODEL_LABEL not in MODEL_FEATURES
    assert not FORBIDDEN_MODEL_FIELDS.intersection(MODEL_FEATURES)


def test_acris_contract_reports_missing_fields() -> None:
    with pytest.raises(ValueError, match="master.*doc_type"):
        validate_input_contract(MASTER_REQUIRED - {"doc_type"}, LEGALS_REQUIRED, CODES_REQUIRED)