# Intake Classification Labeling Guide

This is a separate narrative-intake dataset. ACRIS public-record rows are never used as labeled dispute narratives.

## Labels

- **Title Claim**: a stated dispute about ownership, chain of title, title validity, or a request to correct title.
- **Lien**: a stated debt, mortgage, construction, tax, or other encumbrance attached to property.
- **Easement**: a stated access, utility, right-of-way, or use-right issue. A neutral recorded easement is not automatically disputed.
- **Deed Dispute**: a stated challenge to a deed, conveyance, signature, forgery, execution, or transfer. If the text is only about ownership history with no deed/conveyance issue, prefer Title Claim.
- **Out-of-scope/uncertain**: insufficient facts, a general question, a category outside the four supported labels, or overlapping language that cannot be resolved from the text.

## Decision rules

1. Label what the intake text explicitly supports; do not infer a legal conclusion.
2. Prefer the most specific stated issue. A forged deed is Deed Dispute even when ownership is also mentioned.
3. A request to cross land or maintain a utility path is Easement unless the text clearly centers on a debt/encumbrance.
4. Use Out-of-scope/uncertain when required facts are missing or the text is only a request for legal advice.
5. Keep `SourceType`, `SourceReference`, `IsSynthetic`, and `TemplateOrSourceGroup` intact. They are provenance fields, not model features.
6. Do not put a paraphrase from the same template family in both training and evaluation. Split by `TemplateOrSourceGroup` before model training.

## Starter fixture

`Samples/intake-examples-v1.csv` is a small synthetic demonstration fixture. It is suitable for pipeline tests and examples only. It does not establish production accuracy, calibrated probabilities, or real-world reliability. Review status is explicit so unreviewed rows can be excluded from evaluation.
