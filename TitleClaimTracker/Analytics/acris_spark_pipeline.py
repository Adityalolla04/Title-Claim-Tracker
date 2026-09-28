from __future__ import annotations

import argparse
import json
from pathlib import Path
from typing import Any

MASTER_REQUIRED = {
    "document_id",
    "doc_type",
    "recorded_borough",
    "document_amt",
    "percent_trans",
    "reel_yr",
    "recorded_datetime",
}
LEGALS_REQUIRED = {
    "document_id",
    "borough",
    "block",
    "lot",
    "property_type",
    "easement",
    "partial_lot",
    "air_rights",
    "subterranean_rights",
}
CODES_REQUIRED = {"doc__type", "doc__type_description", "class_code_description"}
MODEL_LABEL = "doc_type"
MODEL_FEATURES = (
    "recorded_borough",
    "document_amt",
    "percent_trans",
    "reel_yr",
    "recorded_month",
)
FORBIDDEN_MODEL_FIELDS = {
    "document_id",
    "doc_type",
    "doc__type_description",
    "class_code_description",
    "street_number",
    "street_name",
    "unit",
    "rawpayload",
}


def validate_input_contract(
    master_columns: set[str], legals_columns: set[str], codes_columns: set[str]
) -> None:
    missing = {
        "master": sorted(MASTER_REQUIRED - master_columns),
        "legals": sorted(LEGALS_REQUIRED - legals_columns),
        "codes": sorted(CODES_REQUIRED - codes_columns),
    }
    missing = {name: columns for name, columns in missing.items() if columns}
    if missing:
        raise ValueError(f"ACRIS input is missing required fields: {missing}")

    if MODEL_LABEL in MODEL_FEATURES or FORBIDDEN_MODEL_FIELDS.intersection(MODEL_FEATURES):
        raise ValueError("The document-type model feature list contains its label or a forbidden field.")


def run_pipeline(
    master_path: Path,
    legals_path: Path,
    codes_path: Path,
    output_directory: Path,
) -> dict[str, Any]:
    for path in (master_path, legals_path, codes_path):
        if not path.is_file():
            raise FileNotFoundError(f"Missing ACRIS JSON snapshot: {path}")

    try:
        from pyspark.ml import Pipeline
        from pyspark.ml.classification import LogisticRegression
        from pyspark.ml.evaluation import MulticlassClassificationEvaluator
        from pyspark.ml.feature import (
            Imputer,
            OneHotEncoder,
            StandardScaler,
            StringIndexer,
            VectorAssembler,
        )
        from pyspark.sql import SparkSession
        from pyspark.sql import functions as F
    except ImportError as error:
        raise RuntimeError("PySpark is required; install the analytics project dependencies.") from error

    output_directory.mkdir(parents=True, exist_ok=False)
    spark = (
        SparkSession.builder.appName("TitleClaimTrackerAcrisPortfolioPipeline")
        .master("local[*]")
        .getOrCreate()
    )
    spark.sparkContext.setLogLevel("ERROR")

    try:
        master_source = spark.read.option("multiLine", True).json(str(master_path)).cache()
        legal_source = spark.read.option("multiLine", True).json(str(legals_path)).cache()
        codes_source = spark.read.option("multiLine", True).json(str(codes_path)).cache()
        validate_input_contract(
            set(master_source.columns), set(legal_source.columns), set(codes_source.columns)
        )

        codes = codes_source.select(
            F.col("doc__type").cast("string").alias("code_doc_type"),
            F.col("doc__type_description").alias("doc_type_description"),
            F.col("class_code_description").alias("class_code_description"),
        ).dropDuplicates(["code_doc_type"])

        documents = (
            master_source
            .select(
                F.col("document_id").cast("string").alias("document_id"),
                F.col("doc_type").cast("string").alias("doc_type"),
                F.col("recorded_borough").cast("string").alias("recorded_borough"),
                F.col("document_amt").cast("double").alias("document_amt"),
                F.col("percent_trans").cast("double").alias("percent_trans"),
                F.col("reel_yr").cast("double").alias("reel_yr"),
                F.to_date("recorded_datetime").alias("recorded_date"),
            )
            .withColumn("recorded_month", F.month("recorded_date").cast("double"))
            .join(codes, F.col("doc_type") == F.col("code_doc_type"), "left")
            .drop("code_doc_type")
            .cache()
        )

        legal_rows = legal_source.select(
            F.col("document_id").cast("string").alias("document_id"),
            F.col("borough").cast("string").alias("borough"),
            F.col("block").cast("string").alias("block"),
            F.col("lot").cast("string").alias("lot"),
            "property_type",
            "easement",
            "partial_lot",
            "air_rights",
            "subterranean_rights",
        )
        legal_rows = legal_rows.withColumn(
            "parcel_key", F.concat_ws("-", "borough", "block", "lot")
        )
        legal_summary = legal_rows.groupBy("document_id").agg(
            F.count(F.lit(1)).alias("legal_reference_count"),
            F.countDistinct("parcel_key").alias("distinct_parcel_count"),
            F.countDistinct("property_type").alias("property_type_count"),
            F.max(F.when(F.col("easement") == "Y", 1).otherwise(0)).alias("has_easement"),
            F.max(F.when(F.col("air_rights") == "Y", 1).otherwise(0)).alias("has_air_rights"),
            F.max(F.when(F.col("subterranean_rights") == "Y", 1).otherwise(0)).alias("has_subterranean_rights"),
        )
        enriched_documents = documents.join(legal_summary, "document_id", "left").fillna(
            0,
            subset=[
                "legal_reference_count",
                "distinct_parcel_count",
                "property_type_count",
                "has_easement",
                "has_air_rights",
                "has_subterranean_rights",
            ],
        )

        document_count = master_source.count()
        unique_documents = master_source.select("document_id").distinct().count()
        legal_count = legal_source.count()
        orphan_legal_rows = legal_rows.join(
            documents.select("document_id"), "document_id", "left_anti"
        ).count()
        missing_document_ids = master_source.filter(
            F.col("document_id").isNull() | (F.trim(F.col("document_id")) == "")
        ).count()
        missing_legal_keys = legal_rows.filter(
            F.col("document_id").isNull()
            | F.col("borough").isNull()
            | F.col("block").isNull()
            | F.col("lot").isNull()
        ).count()
        unmatched_codes = documents.filter(F.col("doc_type_description").isNull()).count()
        duplicate_document_ids = document_count - unique_documents

        enriched_documents.write.mode("errorifexists").parquet(
            str(output_directory / "documents")
        )
        legal_summary.write.mode("errorifexists").parquet(
            str(output_directory / "legal-summary")
        )
        codes.write.mode("errorifexists").parquet(str(output_directory / "document-codes"))

        labeled = documents.filter(
            F.col(MODEL_LABEL).isNotNull()
            & F.col("recorded_borough").isNotNull()
            & F.col("document_id").isNotNull()
        )
        training = labeled.filter(F.pmod(F.xxhash64("document_id"), F.lit(5)) != 0)
        evaluation = labeled.filter(F.pmod(F.xxhash64("document_id"), F.lit(5)) == 0)
        training_labels = training.select(MODEL_LABEL).distinct()
        known_evaluation = evaluation.join(training_labels, MODEL_LABEL, "inner")
        total_evaluation = evaluation.count()
        known_evaluation_count = known_evaluation.count()
        excluded_unseen_labels = total_evaluation - known_evaluation_count
        if training_labels.count() < 2 or known_evaluation_count == 0:
            raise ValueError("The bounded snapshot does not support a train/evaluation split with 2+ labels.")

        numeric_features = ["document_amt", "percent_trans", "reel_yr", "recorded_month"]
        imputed_features = [f"{name}_imputed" for name in numeric_features]
        feature_pipeline = Pipeline(
            stages=[
                StringIndexer(inputCol=MODEL_LABEL, outputCol="label"),
                StringIndexer(
                    inputCol="recorded_borough", outputCol="borough_index", handleInvalid="keep"
                ),
                OneHotEncoder(inputCols=["borough_index"], outputCols=["borough_vector"]),
                Imputer(inputCols=numeric_features, outputCols=imputed_features),
                VectorAssembler(
                    inputCols=[*imputed_features, "borough_vector"], outputCol="raw_features"
                ),
                StandardScaler(
                    inputCol="raw_features", outputCol="features", withMean=False, withStd=True
                ),
                LogisticRegression(
                    labelCol="label",
                    featuresCol="features",
                    predictionCol="prediction",
                    maxIter=40,
                    regParam=0.1,
                    family="multinomial",
                ),
            ]
        )
        model = feature_pipeline.fit(training)
        predictions = model.transform(known_evaluation).cache()
        evaluator = MulticlassClassificationEvaluator(
            labelCol="label", predictionCol="prediction"
        )
        accuracy = evaluator.setMetricName("accuracy").evaluate(predictions)
        weighted_f1 = evaluator.setMetricName("f1").evaluate(predictions)
        label_indexer = model.stages[0]
        confusion = [
            {
                "actual": label_indexer.labels[int(row["label"])],
                "predicted": label_indexer.labels[int(row["prediction"])],
                "count": row["count"],
            }
            for row in predictions.groupBy("label", "prediction").count().collect()
        ]
        model_directory = output_directory / "models" / "document-type"
        model.write().save(str(model_directory))
        report = {
            "status": "Passed" if not (missing_document_ids or duplicate_document_ids or orphan_legal_rows) else "Failed",
            "source": "NYC Open Data ACRIS API snapshot",
            "modelTask": "Suggest ACRIS document type from non-label structured fields",
            "labelColumn": MODEL_LABEL,
            "featureColumns": list(MODEL_FEATURES),
            "split": "deterministic document_id hash; 20% evaluation bucket",
            "masterRows": document_count,
            "uniqueDocuments": unique_documents,
            "legalRows": legal_count,
            "documentCodeRows": codes.count(),
            "orphanLegalRows": orphan_legal_rows,
            "missingDocumentIds": missing_document_ids,
            "duplicateDocumentIds": duplicate_document_ids,
            "missingLegalParcelKeys": missing_legal_keys,
            "unmatchedDocumentCodes": unmatched_codes,
            "trainingRows": training.count(),
            "evaluationRows": total_evaluation,
            "evaluationRowsWithUnseenLabelsExcluded": excluded_unseen_labels,
            "evaluatedRows": known_evaluation_count,
            "accuracy": accuracy,
            "weightedF1": weighted_f1,
            "classes": label_indexer.labels,
            "confusionMatrix": confusion,
            "modelPath": str(model_directory),
            "limitations": [
                "This is a bounded 100,000-Master-row development sample, not the complete ACRIS archive.",
                "The target is a source document code, not a claim category or legal outcome.",
                "Public-record structured fields may be incomplete; predictions require human review.",
                "The deterministic hash split is reproducible but does not establish future temporal performance.",
                "Curated outputs are local Parquet files; this pipeline does not write to SQL Server.",
            ],
        }
        (output_directory / "quality-report.json").write_text(
            json.dumps(report, indent=2), encoding="utf-8"
        )
        if report["status"] != "Passed":
            raise ValueError(f"ACRIS Spark quality checks failed; see {output_directory / 'quality-report.json'}.")
        return report
    finally:
        spark.stop()


def main() -> None:
    repository_data = Path(__file__).resolve().parents[1] / "Data" / "Public" / "Raw"
    repository_output = Path(__file__).resolve().parents[2] / "outputs" / "acris-spark-run"
    parser = argparse.ArgumentParser(
        description="Build local Spark Parquet products and evaluate a bounded ACRIS document-type model."
    )
    parser.add_argument("--master", type=Path, default=repository_data / "acris-master.json")
    parser.add_argument("--legals", type=Path, default=repository_data / "acris-legals.json")
    parser.add_argument("--codes", type=Path, default=repository_data / "acris-document-codes.json")
    parser.add_argument("--output", type=Path, default=repository_output)
    parser.add_argument(
        "--confirm-local-processing",
        action="store_true",
        help="Confirm local processing of the downloaded public-record snapshot.",
    )
    args = parser.parse_args()
    if not args.confirm_local_processing:
        parser.error("--confirm-local-processing is required; no Spark job was started.")
    report = run_pipeline(args.master, args.legals, args.codes, args.output)
    print(json.dumps({key: report[key] for key in ("status", "masterRows", "legalRows", "accuracy", "weightedF1")}, indent=2))


if __name__ == "__main__":
    main()