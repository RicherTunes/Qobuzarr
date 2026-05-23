# Experimental Features

This document lists features that are hidden behind opt-in flags and are NOT production-ready.
Enabling these features requires explicit configuration and may produce incorrect or incomplete
results. All experimental features are tracked here and in `QobuzarrConstants.Experimental`.

---

## HybridFeatureExtraction

| Field | Value |
|-------|-------|
| Constant | `QobuzarrConstants.Experimental.HybridFeatureExtraction` |
| Flag property | `HybridConfiguration.EnableHybridFeatureExtraction` |
| Default | `false` (disabled) |
| Status | Placeholder implementation — not functional |
| Owner | RicherTunes |

### Description

`HybridMLQueryOptimizer.ExtractCombinedFeatures()` returns a 25-element float vector that
combines features from the baseline and personal models. The combined vector is used when the
optimizer is in hybrid mode (both baseline and personal models loaded).

**When disabled (default):** `ExtractCombinedFeatures` returns a zero-vector `new float[25]`.
All other hybrid logic (confidence-based routing, model selection, statistics) works normally.

**When enabled:** `ExtractCombinedFeatures` attempts to call `IFeatureExtractor.ExtractFeatures()`
on the baseline model if it implements the `IFeatureExtractor` interface. Falls back to the
zero-vector if the baseline model does not implement `IFeatureExtractor`.

### How to Enable

Set `EnableHybridFeatureExtraction = true` in the `HybridConfiguration` passed to
`HybridMLQueryOptimizer`:

```csharp
var config = new HybridConfiguration
{
    EnableHybridFeatureExtraction = true  // [Experimental]
};
var optimizer = new HybridMLQueryOptimizer(logger, baselineModel, personalModel, config);
```

### Risks

- The baseline `MLQueryOptimizer` does not currently implement `IFeatureExtractor`.
  Enabling this flag with the default baseline model will log a warning and return a
  zero-vector — identical to the disabled path.
- Feature vector compatibility between baseline and personal models is not validated.
  Enabling hybrid feature extraction with mismatched models may produce nonsensical vectors.

### Removal Condition

This flag will be removed (and the feature enabled unconditionally) once:
1. `MLQueryOptimizer` (or a successor) implements `IFeatureExtractor`.
2. The combined feature vector is validated to improve prediction accuracy on the test dataset.
3. Existing tests confirm no regression in `PredictComplexity` accuracy.
