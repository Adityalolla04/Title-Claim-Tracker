# Analytics Data Dictionary

All public-demo files are synthetic. Identifiers are demo identifiers, not database keys. Timestamps are UTC and `SnapshotUtc` records preparation time.

| File | Grain | Required fields | Exclusions |
|---|---|---|---|
| `claims.csv` | One row per claim | DemoClaimId, PrimaryCategory, CurrentStatus, CreationSource, Geography, CreatedUtc, ResolvedUtc, Priority, ModelScore, ModelVersion, IsSynthetic, SnapshotUtc | User IDs, claimant names, notes, narratives |
| `status_history.csv` | One row per status event | DemoClaimId, PreviousStatus, NewStatus, EventUtc, EventKind, IsSynthetic | Actor IDs |
| `triage_attempts.csv` | One row per attempt | DemoAttemptId, Outcome, StartedUtc, CompletedUtc, PredictedCategory, Score, ModelVersion, CreatedDemoClaimId, ExtractionMode, IsSynthetic | Input text and extracted personal text |
| `model_metrics.csv` | One row per model/dataset/class/metric | ModelVersion, DatasetVersion, ClassName, MetricName, MetricValue, SampleCount, Provenance | Live activity presented as evaluation |
| `confusion_matrix.csv` | One row per actual/predicted pair | ModelVersion, DatasetVersion, ActualClass, PredictedClass, Count, Provenance | Fabricated counts |
| `public_document_summary.csv` | One row per geography/date/document type | Geography, RecordDate, SourceDocumentType, DocumentCount, SourceDesignation | Claim/dispute language |
| `data_quality_summary.csv` | One row per output/check | SnapshotUtc, DatasetName, InputRows, OutputRows, RejectedRows, DuplicateRows, NullRows, ValidationStatus | Private source details |

Permitted public fields are intentionally narrower than the application schema. `NULL` model scores remain NULL and are not converted to zero.
