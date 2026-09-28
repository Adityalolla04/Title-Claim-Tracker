# Metrics Definitions

- **Total claims:** count of rows in `claims.csv` at the claim grain.
- **Current open backlog:** claims whose current status is not `Resolved` or `Closed` at the snapshot.
- **Claims created in period:** claims whose `CreatedUtc` falls inside the selected period.
- **Claims resolved in period:** claims with a non-null `ResolvedUtc` in the selected period.
- **Resolution duration:** `ResolvedUtc - CreatedUtc` only when both timestamps exist and the result is non-negative. Unresolved duration is unavailable, not zero.
- **Automatic creation rate:** completed attempts with outcome `Created` and `CreationSource=Assisted`, divided by completed eligible attempts. Incomplete/failed attempts are excluded from the denominator.
- **Needs-information rate:** attempts with outcome `NeedsInformation` divided by completed eligible attempts.
- **Review correction rate:** reviewed attempts marked incorrect divided by reviewed attempts, not all live attempts.
- **Acceptance coverage:** accepted predictions divided by all evaluated examples. This is not accuracy and does not describe legal correctness.
- **Model score:** an uncalibrated classifier score. It is not a probability of correctness.
- **Public document volume:** count of recorded public documents by source document type. It is never labeled a claim count, dispute rate, or legal outcome.

Status history is a separate one-to-many subject. Aggregate it before relating it to claims so claim totals do not fan out.
