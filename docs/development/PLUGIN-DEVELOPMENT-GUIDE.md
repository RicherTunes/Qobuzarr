<!-- docval:ignore-workflow-refs -->
> ⚠️ Historical (flagged 2026-05-31): describes a past state; some details below no longer match the current code.

# Qobuzarr Plugin Development Guide

This comprehensive guide covers everything needed to develop plugins, extensions, and customizations for Qobuzarr. Whether you're creating ML models, security extensions, or custom integrations, this guide provides the foundation and advanced techniques.

## Table of Contents

- [Getting Started](#getting-started)
- [Plugin Architecture](#plugin-architecture)
- [Core Extension Points](#core-extension-points)
- [ML Model Development](#ml-model-development)
- [Security Extensions](#security-extensions)
- [Custom Indexers](#custom-indexers)
- [Advanced Integrations](#advanced-integrations)
- [Testing & Deployment](#testing--deployment)

## Getting Started

### Prerequisites

**Development Environment:**
```bash
# Required tools
- .NET 8.0 SDK
- Visual Studio Code or Visual Studio 2022
- Git
- Docker (optional, for testing)

# Recommended tools
- Rider IDE (JetBrains)
- Azure Data Studio (for database work)
- Postman (for API testing)
```

**Development Setup:**
```bash
# Clone the repository
git clone https://github.com/RicherTunes/Qobuzarr.git
cd Qobuzarr

# Setup development environment
./setup.sh --enable-deploy --deploy-path "/your/lidarr/plugins/Qobuzarr"

# Or Windows
.\setup.ps1 -EnableDeploy -DeployPath "C:\Lidarr\Plugins\Qobuzarr"

# Install dependencies
dotnet restore

# Download required Lidarr assemblies
./download-lidarr-assemblies.sh --version 2.13.2.4685
```

### Project Structure for Plugin Development

```
src/
├── API/                          # API client extensions
├── Authentication/               # Auth service extensions
├── Download/                     # Download extensions
├── Indexers/                     # Search and ML extensions
├── Security/                     # Security extensions
├── Services/                     # Business logic extensions
├── Integration/                  # Lidarr integration points
└── Testing/                      # Testing utilities

plugins/                          # Your custom plugins go here
├── MyCustomPlugin/
│   ├── MyCustomPlugin.csproj     # Plugin project file
│   ├── Plugin.cs                 # Main plugin class
│   ├── Services/                 # Custom services
│   └── Models/                   # Custom models

tests/
├── MyCustomPlugin.Tests/         # Plugin tests
└── Integration/                  # Integration tests
```

## Plugin Architecture

### Plugin Base Classes

Qobuzarr provides several base classes for different types of plugins:

#### 1. ML Optimization Plugin

```csharp
// IPatternLearningEngine - For custom ML models
public interface IPatternLearningEngine
{
    Task<PredictionResult> PredictComplexityAsync(string artist, string album);
    float GetConfidenceScore(string artist, string album, QueryComplexity complexity);
    Task UpdateModelAsync(QueryResult actualResult);
    MLPerformanceMetrics GetStatistics();
}

// Example implementation
public class CustomMLQueryOptimizer : IPatternLearningEngine
{
    private readonly IQobuzLogger _logger;
    private readonly CustomMLModel _model;

    public CustomMLQueryOptimizer(IQobuzLogger logger)
    {
        _logger = logger;
        _model = LoadCustomModel();
    }

    public async Task<PredictionResult> PredictComplexityAsync(string artist, string album)
    {
        var features = ExtractFeatures(artist, album);
        var prediction = await _model.PredictAsync(features);
        
        return new PredictionResult
        {
            PredictedComplexity = MapToComplexity(prediction),
            Confidence = prediction.Confidence,
            RecommendedQueries = GenerateQueries(prediction),
            PredictionTime = TimeSpan.FromMilliseconds(prediction.ProcessingTimeMs)
        };
    }
    
    private CustomFeatures ExtractFeatures(string artist, string album)
    {
        // Your custom feature extraction logic
        return new CustomFeatures
        {
            ArtistLength = artist.Length,
            AlbumLength = album.Length,
            HasSpecialChars = ContainsSpecialChars(artist, album),
            GenreIndicators = ExtractGenreIndicators(artist, album),
            // Add your custom features here
        };
    }
}
```

#### 2. Security Extension Plugin

```csharp
// ISecurityExtension - For custom security features
public interface ISecurityExtension
{
    Task<SecurityValidationResult> ValidateAsync(object context);
    Task<SecurityAction> RespondToThreatAsync(SecurityThreat threat);
    SecurityCapabilities GetCapabilities();
}

// Example implementation  
public class CustomSecurityExtension : ISecurityExtension
{
    private readonly IQobuzLogger _logger;
    private readonly SecurityConfiguration _config;

    public async Task<SecurityValidationResult> ValidateAsync(object context)
    {
        var result = new SecurityValidationResult();
        
        // Custom validation logic
        if (context is QobuzIndexerSettings settings)
        {
            await ValidateCustomSettings(settings, result);
        }
        
        return result;
    }

    private async Task ValidateCustomSettings(QobuzIndexerSettings settings, SecurityValidationResult result)
    {
        // Example: Check for custom security requirements
        if (string.IsNullOrEmpty(settings.CustomSecurityToken))
        {
            result.AddCriticalIssue("Custom security token missing", 
                "This deployment requires a custom security token");
        }

        // Example: Validate against custom threat intelligence
        var threatLevel = await CheckThreatIntelligence(settings.Email);
        if (threatLevel == ThreatLevel.High)
        {
            result.AddMajorIssue("High-risk user detected", 
                "User email appears in threat intelligence feeds");
        }
    }
}
```

#### 3. Custom Indexer Plugin

```csharp
// Custom indexer that extends Qobuzarr's functionality
public class CustomIndexer : QobuzIndexer
{
    private readonly ICustomSearchService _customSearchService;

    public CustomIndexer(ICustomSearchService customSearchService, 
                        IQobuzApiClient apiClient,
                        IQobuzAuthenticationService authService,
                        Logger logger) 
        : base(apiClient, authService, logger)
    {
        _customSearchService = customSearchService;
    }

    public override async Task<IList<ReleaseInfo>> SearchAsync(string query, int offset, int limit)
    {
        // Pre-process with custom logic
        var enhancedQuery = await _customSearchService.EnhanceQueryAsync(query);
        
        // Use base Qobuzarr functionality
        var baseResults = await base.SearchAsync(enhancedQuery, offset, limit);
        
        // Post-process results
        var enhancedResults = await _customSearchService.EnhanceResultsAsync(baseResults);
        
        return enhancedResults;
    }

    protected override async Task<List<string>> BuildSearchQueries(string artist, string album)
    {
        // Custom query building logic
        var baseQueries = await base.BuildSearchQueries(artist, album);
        var customQueries = await _customSearchService.GenerateCustomQueries(artist, album);
        
        return baseQueries.Concat(customQueries).ToList();
    }
}
```

### Service Registration and Dependency Injection

Qobuzarr uses dependency injection. Register your custom services:

```csharp
// In your plugin's startup/registration class
public class CustomPluginModule : IQobuzarrModule
{
    public void ConfigureServices(IServiceContainer container)
    {
        // Register your custom services
        container.RegisterSingleton<ICustomSearchService, CustomSearchService>();
        container.RegisterSingleton<ICustomMLModel, CustomMLModel>();
        container.RegisterTransient<ICustomSecurityExtension, CustomSecurityExtension>();
        
        // Replace built-in services with your implementations
        container.RegisterSingleton<IPatternLearningEngine, CustomMLQueryOptimizer>();
        
        // Register factories for complex initialization
        container.RegisterFactory<ICustomService>(() => 
        {
            var config = container.Resolve<IConfiguration>();
            var logger = container.Resolve<IQobuzLogger>();
            return new CustomService(config, logger);
        });
    }
    
    public void Initialize(IServiceContainer container)
    {
        // Perform any initialization after services are registered
        var customService = container.Resolve<ICustomService>();
        customService.Initialize();
    }
}
```

## Core Extension Points

### 1. Query Processing Pipeline

Extend the query processing pipeline at multiple points:

```csharp
// IQueryProcessor - Intercept and modify queries
public interface IQueryProcessor
{
    Task<ProcessedQuery> ProcessAsync(string originalQuery, QueryContext context);
    int Priority { get; } // Higher numbers process first
}

public class CustomQueryProcessor : IQueryProcessor
{
    public int Priority => 100; // High priority

    public async Task<ProcessedQuery> ProcessAsync(string originalQuery, QueryContext context)
    {
        // Example: Add genre-specific query enhancement
        if (context.Genre == "Classical")
        {
            return ProcessClassicalQuery(originalQuery);
        }
        
        // Example: Add language-specific processing
        if (DetectLanguage(originalQuery) == "Japanese")
        {
            return ProcessJapaneseQuery(originalQuery);
        }

        // Default processing
        return new ProcessedQuery { Query = originalQuery, Modified = false };
    }

    private ProcessedQuery ProcessClassicalQuery(string query)
    {
        // Custom logic for classical music
        // E.g., handle composer names, opus numbers, etc.
        var enhancedQuery = AddComposerContext(query);
        return new ProcessedQuery { Query = enhancedQuery, Modified = true };
    }
}
```

### 2. Result Processing Pipeline

Process and filter search results:

```csharp
// IResultProcessor - Post-process search results
public interface IResultProcessor
{
    Task<List<QobuzAlbum>> ProcessAsync(List<QobuzAlbum> results, ResultContext context);
    int Priority { get; }
}

public class CustomResultProcessor : IResultProcessor
{
    public int Priority => 50;

    public async Task<List<QobuzAlbum>> ProcessAsync(List<QobuzAlbum> results, ResultContext context)
    {
        // Example: Filter explicit content for family accounts
        if (context.UserProfile.IsFamilyAccount)
        {
            results = results.Where(r => !r.IsExplicit).ToList();
        }

        // Example: Add custom scoring based on user preferences
        foreach (var result in results)
        {
            result.CustomScore = await CalculateCustomScore(result, context.UserProfile);
        }

        return results.OrderByDescending(r => r.CustomScore).ToList();
    }

    private async Task<double> CalculateCustomScore(QobuzAlbum album, UserProfile profile)
    {
        double score = 0.0;

        // Factor in user's preferred genres
        if (profile.PreferredGenres.Contains(album.Genre))
            score += 10.0;

        // Factor in release quality
        if (album.MaximumBitDepth >= 24)
            score += 5.0;

        // Factor in user's listening history
        var listenHistory = await GetUserListenHistory(profile.UserId);
        var artistAffinity = CalculateArtistAffinity(album.Artist, listenHistory);
        score += artistAffinity * 2.0;

        return score;
    }
}
```

### 3. Authentication Extensions

Extend authentication with custom methods:

```csharp
// IAuthenticationExtension - Add custom auth methods
public interface IAuthenticationExtension
{
    Task<AuthenticationResult> AuthenticateAsync(AuthenticationRequest request);
    bool CanHandle(AuthenticationMethod method);
    string Name { get; }
}

public class CustomOAuthAuthenticator : IAuthenticationExtension
{
    public string Name => "CustomOAuth";

    public bool CanHandle(AuthenticationMethod method) => method == AuthenticationMethod.CustomOAuth;

    public async Task<AuthenticationResult> AuthenticateAsync(AuthenticationRequest request)
    {
        // Implement OAuth flow with your custom provider
        var oauthToken = await ExchangeAuthorizationCode(request.AuthorizationCode);
        var userInfo = await GetUserInfo(oauthToken.AccessToken);
        
        // Map to Qobuz credentials
        var qobuzCredentials = await MapToQobuzCredentials(userInfo);
        
        return new AuthenticationResult
        {
            Success = true,
            Credentials = qobuzCredentials,
            ExpiresAt = oauthToken.ExpiresAt
        };
    }

    private async Task<OAuthToken> ExchangeAuthorizationCode(string code)
    {
        // Implement OAuth token exchange
        // This is just a placeholder - implement according to your OAuth provider
        using var client = new HttpClient();
        var response = await client.PostAsync("https://oauth.provider.com/token", 
            new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("code", code),
                new KeyValuePair<string, string>("client_id", _clientId),
                new KeyValuePair<string, string>("client_secret", _clientSecret),
                new KeyValuePair<string, string>("grant_type", "authorization_code")
            }));

        var responseContent = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<OAuthToken>(responseContent);
    }
}
```

## ML Model Development

### Creating Custom ML Models

Develop custom ML models for query optimization:

#### 1. Feature Engineering

```csharp
public class CustomFeatureExtractor
{
    public CustomFeatures ExtractFeatures(string artist, string album)
    {
        return new CustomFeatures
        {
            // Basic features
            ArtistLength = artist.Length,
            AlbumLength = album.Length,
            ArtistWordCount = artist.Split(' ').Length,
            AlbumWordCount = album.Split(' ').Length,

            // Character analysis
            SpecialCharCount = CountSpecialChars(artist + album),
            DigitCount = CountDigits(artist + album),
            UnicodeCharCount = CountUnicodeChars(artist + album),

            // Pattern detection
            IsLiveAlbum = DetectLiveAlbum(album),
            IsCompilation = DetectCompilation(album),
            IsRemasterOrDeluxe = DetectRemasterOrDeluxe(album),
            HasFeaturing = DetectFeaturing(artist),

            // Genre-specific features
            IsClassical = DetectClassical(artist, album),
            IsJazz = DetectJazz(artist, album),
            IsElectronic = DetectElectronic(artist, album),

            // Language detection
            PrimaryLanguage = DetectLanguage(artist + album),
            HasNonLatinScript = ContainsNonLatinScript(artist + album),

            // Complexity indicators
            HasAmpersand = (artist + album).Contains("&"),
            HasParentheses = (artist + album).Contains("("),
            HasYearInTitle = HasYearPattern(album),
            HasVolumeNumber = HasVolumePattern(album)
        };
    }

    private bool DetectLiveAlbum(string album)
    {
        var liveIndicators = new[] { "live", "concert", "tour", "unplugged", "acoustic" };
        return liveIndicators.Any(indicator => 
            album.Contains(indicator, StringComparison.OrdinalIgnoreCase));
    }

    private bool DetectClassical(string artist, string album)
    {
        var classicalTerms = new[] { "symphony", "concerto", "sonata", "quartet", "philharmonic", "orchestra" };
        var text = (artist + " " + album).ToLower();
        return classicalTerms.Any(term => text.Contains(term));
    }

    private string DetectLanguage(string text)
    {
        // Implement language detection logic
        // You could use a library like NTextCat or call a language detection API
        return "en"; // Placeholder
    }
}

public class CustomFeatures
{
    // Basic metrics
    public int ArtistLength { get; set; }
    public int AlbumLength { get; set; }
    public int ArtistWordCount { get; set; }
    public int AlbumWordCount { get; set; }

    // Character analysis
    public int SpecialCharCount { get; set; }
    public int DigitCount { get; set; }
    public int UnicodeCharCount { get; set; }

    // Pattern flags
    public bool IsLiveAlbum { get; set; }
    public bool IsCompilation { get; set; }
    public bool IsRemasterOrDeluxe { get; set; }
    public bool HasFeaturing { get; set; }

    // Genre indicators
    public bool IsClassical { get; set; }
    public bool IsJazz { get; set; }
    public bool IsElectronic { get; set; }

    // Language features
    public string PrimaryLanguage { get; set; }
    public bool HasNonLatinScript { get; set; }

    // Complexity indicators
    public bool HasAmpersand { get; set; }
    public bool HasParentheses { get; set; }
    public bool HasYearInTitle { get; set; }
    public bool HasVolumeNumber { get; set; }
}
```

#### 2. Custom ML Model Implementation

```csharp
using Microsoft.ML;
using Microsoft.ML.Data;

public class CustomMLModel : IPatternLearningEngine
{
    private readonly MLContext _mlContext;
    private readonly ITransformer _model;
    private readonly IQobuzLogger _logger;
    private readonly MLPerformanceMetrics _metrics;

    public CustomMLModel(IQobuzLogger logger, string modelPath = null)
    {
        _logger = logger;
        _mlContext = new MLContext();
        _metrics = new MLPerformanceMetrics();
        
        if (modelPath != null && File.Exists(modelPath))
        {
            _model = _mlContext.Model.Load(modelPath, out _);
        }
        else
        {
            _model = TrainNewModel();
        }
    }

    public async Task<PredictionResult> PredictComplexityAsync(string artist, string album)
    {
        var startTime = DateTime.UtcNow;
        
        // Extract features
        var features = new CustomFeatureExtractor().ExtractFeatures(artist, album);
        
        // Convert to ML.NET format
        var input = new QueryComplexityInput
        {
            ArtistLength = features.ArtistLength,
            AlbumLength = features.AlbumLength,
            SpecialCharCount = features.SpecialCharCount,
            IsLiveAlbum = features.IsLiveAlbum,
            IsCompilation = features.IsCompilation,
            // Map other features...
        };

        // Make prediction
        var predictionEngine = _mlContext.Model.CreatePredictionEngine<QueryComplexityInput, QueryComplexityOutput>(_model);
        var prediction = predictionEngine.Predict(input);

        var processingTime = DateTime.UtcNow - startTime;

        // Update metrics
        _metrics.TotalPredictions++;
        _metrics.AveragePredictionTime = TimeSpan.FromTicks(
            (_metrics.AveragePredictionTime.Ticks * (_metrics.TotalPredictions - 1) + processingTime.Ticks) 
            / _metrics.TotalPredictions);

        return new PredictionResult
        {
            PredictedComplexity = MapToComplexity(prediction.PredictedLabel),
            Confidence = prediction.Score.Max(),
            RecommendedQueries = GenerateQueries(prediction.PredictedLabel),
            FeatureVector = ExtractFeatureVector(features),
            PredictionTime = processingTime
        };
    }

    private ITransformer TrainNewModel()
    {
        // Load training data (implement based on your data source)
        var trainingData = LoadTrainingData();
        
        // Define ML pipeline
        var pipeline = _mlContext.Transforms.Text.FeaturizeText("ArtistFeatures", "Artist")
            .Append(_mlContext.Transforms.Text.FeaturizeText("AlbumFeatures", "Album"))
            .Append(_mlContext.Transforms.Concatenate("Features", 
                "ArtistFeatures", "AlbumFeatures", "ArtistLength", "AlbumLength", 
                "SpecialCharCount", "IsLiveAlbum", "IsCompilation"))
            .Append(_mlContext.MulticlassClassification.Trainers.SdcaMaximumEntropy())
            .Append(_mlContext.Transforms.Conversion.MapKeyToValue("PredictedLabel"));

        // Train the model
        var model = pipeline.Fit(trainingData);
        
        // Evaluate model
        EvaluateModel(model, trainingData);
        
        return model;
    }

    private void EvaluateModel(ITransformer model, IDataView trainingData)
    {
        var predictions = model.Transform(trainingData);
        var metrics = _mlContext.MulticlassClassification.Evaluate(predictions);
        
        _logger.Info($"Model accuracy: {metrics.MacroAccuracy:F4}");
        _logger.Info($"Log loss: {metrics.LogLoss:F4}");
        
        // Update performance metrics
        _metrics.OverallAccuracy = metrics.MacroAccuracy;
        _metrics.LastModelUpdate = DateTime.UtcNow;
    }
}

// ML.NET data structures
public class QueryComplexityInput
{
    public string Artist { get; set; }
    public string Album { get; set; }
    public float ArtistLength { get; set; }
    public float AlbumLength { get; set; }
    public float SpecialCharCount { get; set; }
    public bool IsLiveAlbum { get; set; }
    public bool IsCompilation { get; set; }
    // Add more features as needed
    
    [ColumnName("Label")]
    public string Complexity { get; set; } // "Simple", "Medium", "Complex"
}

public class QueryComplexityOutput
{
    [ColumnName("PredictedLabel")]
    public string PredictedLabel { get; set; }
    
    public float[] Score { get; set; }
}
```

#### 3. Model Training Pipeline

Create scripts for automated model training:

```python
#!/usr/bin/env python3
# train_custom_model.py

import pandas as pd
import numpy as np
from sklearn.model_selection import train_test_split
from sklearn.ensemble import RandomForestClassifier
from sklearn.metrics import classification_report, accuracy_score
import joblib
import json
from datetime import datetime

class CustomModelTrainer:
    def __init__(self, data_source="production_logs"):
        self.data_source = data_source
        self.model = None
        self.feature_names = []
        
    def load_training_data(self):
        """Load training data from production logs or custom dataset"""
        if self.data_source == "production_logs":
            return self.load_from_production_logs()
        else:
            return self.load_from_custom_dataset()
    
    def load_from_production_logs(self):
        """Load training data from Qobuzarr production logs"""
        # Parse Lidarr logs to extract query patterns and outcomes
        # This would analyze actual search success/failure patterns
        logs_df = pd.read_csv("qobuzarr_query_logs.csv")
        
        features = []
        labels = []
        
        for _, row in logs_df.iterrows():
            # Extract features (similar to C# implementation)
            feature_vector = self.extract_features(row['artist'], row['album'])
            features.append(feature_vector)
            
            # Determine label based on actual API usage
            if row['api_calls_used'] == 1:
                labels.append('Simple')
            elif row['api_calls_used'] == 2:
                labels.append('Medium')
            else:
                labels.append('Complex')
        
        return np.array(features), np.array(labels)
    
    def extract_features(self, artist, album):
        """Extract features that match the C# implementation"""
        features = []
        
        # Basic metrics
        features.extend([
            len(artist), len(album),
            len(artist.split()), len(album.split())
        ])
        
        # Character analysis
        combined_text = artist + album
        features.extend([
            sum(1 for c in combined_text if c in '[&+/\\-:\'\"()]'),
            sum(1 for c in combined_text if c.isdigit()),
            sum(1 for c in combined_text if ord(c) > 127)
        ])
        
        # Pattern detection
        album_lower = album.lower()
        artist_lower = artist.lower()
        combined_lower = combined_text.lower()
        
        features.extend([
            any(word in album_lower for word in ['live', 'concert', 'unplugged']),
            any(word in album_lower for word in ['greatest', 'best', 'hits']),
            any(word in album_lower for word in ['remaster', 'deluxe', 'anniversary']),
            'featuring' in artist_lower or 'feat.' in artist_lower,
            
            # Genre indicators
            any(word in combined_lower for word in ['symphony', 'concerto', 'philharmonic']),
            any(word in combined_lower for word in ['jazz', 'blues', 'swing']),
            any(word in combined_lower for word in ['electronic', 'techno', 'house']),
            
            # Complexity indicators
            '&' in combined_text,
            '(' in combined_text,
            bool(re.search(r'\b\d{4}\b', album)),  # Year pattern
            bool(re.search(r'vol\.?\s*\d+', album, re.I))  # Volume pattern
        ])
        
        self.feature_names = [
            'artist_len', 'album_len', 'artist_words', 'album_words',
            'special_chars', 'digit_count', 'unicode_chars',
            'is_live', 'is_compilation', 'is_remaster', 'has_featuring',
            'is_classical', 'is_jazz', 'is_electronic',
            'has_ampersand', 'has_parentheses', 'has_year', 'has_volume'
        ]
        
        return features
    
    def train_model(self):
        """Train the custom model"""
        X, y = self.load_training_data()
        
        # Split data
        X_train, X_test, y_train, y_test = train_test_split(
            X, y, test_size=0.2, random_state=42, stratify=y
        )
        
        # Train model
        self.model = RandomForestClassifier(
            n_estimators=200,
            max_depth=15,
            min_samples_split=5,
            min_samples_leaf=2,
            random_state=42,
            class_weight='balanced'
        )
        
        self.model.fit(X_train, y_train)
        
        # Evaluate
        y_pred = self.model.predict(X_test)
        accuracy = accuracy_score(y_test, y_pred)
        
        print(f"Model Accuracy: {accuracy:.4f}")
        print("\nClassification Report:")
        print(classification_report(y_test, y_pred))
        
        return accuracy
    
    def export_for_dotnet(self, output_path="custom_model.pkl"):
        """Export model in format compatible with .NET"""
        if self.model is None:
            raise ValueError("Model not trained")
        
        # Save Python model
        joblib.dump({
            'model': self.model,
            'feature_names': self.feature_names,
            'classes': self.model.classes_,
            'version': '1.0.0',
            'trained_at': datetime.now().isoformat()
        }, output_path)
        
        # Create .NET integration helper
        self.create_dotnet_integration(output_path.replace('.pkl', '.cs'))
    
    def create_dotnet_integration(self, output_path):
        """Create C# code for model integration"""
        cs_code = f"""
// Auto-generated model integration code
using System;
using System.Collections.Generic;

public class CustomTrainedModel : IPatternLearningEngine
{{
    // Feature extraction that matches Python training
    private float[] ExtractFeatures(string artist, string album)
    {{
        var features = new List<float>();
        
        // Basic metrics
        features.AddRange(new float[] {{
            artist.Length, album.Length,
            artist.Split(' ').Length, album.Split(' ').Length
        }});
        
        // Character analysis
        var combined = artist + album;
        features.AddRange(new float[] {{
            CountSpecialChars(combined),
            CountDigits(combined),
            CountUnicodeChars(combined)
        }});
        
        // Pattern detection
        var albumLower = album.ToLower();
        var artistLower = artist.ToLower();
        var combinedLower = combined.ToLower();
        
        features.AddRange(new float[] {{
            ContainsAny(albumLower, new[] {{"live", "concert", "unplugged"}}) ? 1f : 0f,
            ContainsAny(albumLower, new[] {{"greatest", "best", "hits"}}) ? 1f : 0f,
            ContainsAny(albumLower, new[] {{"remaster", "deluxe", "anniversary"}}) ? 1f : 0f,
            (artistLower.Contains("featuring") || artistLower.Contains("feat.")) ? 1f : 0f,
            
            ContainsAny(combinedLower, new[] {{"symphony", "concerto", "philharmonic"}}) ? 1f : 0f,
            ContainsAny(combinedLower, new[] {{"jazz", "blues", "swing"}}) ? 1f : 0f,
            ContainsAny(combinedLower, new[] {{"electronic", "techno", "house"}}) ? 1f : 0f,
            
            combined.Contains("&") ? 1f : 0f,
            combined.Contains("(") ? 1f : 0f,
            System.Text.RegularExpressions.Regex.IsMatch(album, @"\\b\\d{{4}}\\b") ? 1f : 0f,
            System.Text.RegularExpressions.Regex.IsMatch(album, @"vol\\.?\\s*\\d+", System.Text.RegularExpressions.RegexOptions.IgnoreCase) ? 1f : 0f
        }});
        
        return features.ToArray();
    }}
    
    // Helper methods
    private float CountSpecialChars(string text) => 
        text.Count(c => "[&+/\\\\-:'\\\"()]".Contains(c));
    
    private float CountDigits(string text) => text.Count(char.IsDigit);
    
    private float CountUnicodeChars(string text) => text.Count(c => c > 127);
    
    private bool ContainsAny(string text, string[] terms) =>
        terms.Any(term => text.Contains(term));
}}
"""
        
        with open(output_path, 'w') as f:
            f.write(cs_code)

if __name__ == "__main__":
    trainer = CustomModelTrainer()
    
    print("Training custom ML model for Qobuzarr...")
    accuracy = trainer.train_model()
    
    if accuracy > 0.80:  # Only export if accuracy is good
        trainer.export_for_dotnet("custom_qobuzarr_model.pkl")
        print(f"Model exported with {accuracy:.2%} accuracy")
    else:
        print(f"Model accuracy too low ({accuracy:.2%}), not exporting")
```

## Security Extensions

### Custom Security Validators

Create custom security validation logic:

```csharp
public class CustomSecurityValidator : ISecurityValidator
{
    private readonly IQobuzLogger _logger;
    private readonly IThreatIntelligenceService _threatIntel;

    public async Task<SecurityValidationResult> ValidateAsync(ValidationContext context)
    {
        var result = new SecurityValidationResult();

        // Custom validation rules
        await ValidateGeoLocation(context, result);
        await ValidateUserBehavior(context, result);
        await ValidateRiskProfile(context, result);

        return result;
    }

    private async Task ValidateGeoLocation(ValidationContext context, SecurityValidationResult result)
    {
        var userLocation = await GetUserLocation(context.IPAddress);
        var allowedCountries = GetAllowedCountries();

        if (!allowedCountries.Contains(userLocation.CountryCode))
        {
            result.AddCriticalIssue("Geographic restriction violation",
                $"Access from {userLocation.CountryCode} is not allowed");
        }

        // Check for VPN/Proxy usage
        if (await IsUsingVPN(context.IPAddress))
        {
            result.AddMajorIssue("VPN/Proxy detected",
                "Connection appears to be using VPN or proxy service");
        }
    }

    private async Task ValidateUserBehavior(ValidationContext context, SecurityValidationResult result)
    {
        var behaviorProfile = await GetUserBehaviorProfile(context.UserId);
        var currentBehavior = AnalyzeCurrentBehavior(context);

        // Anomaly detection
        var anomalyScore = CalculateAnomalyScore(behaviorProfile, currentBehavior);
        if (anomalyScore > 0.8) // High anomaly threshold
        {
            result.AddMajorIssue("Unusual behavior detected",
                $"Current behavior deviates significantly from user profile (score: {anomalyScore:F2})");
        }

        // Rate limiting based on behavior
        if (currentBehavior.RequestsPerMinute > behaviorProfile.TypicalRate * 3)
        {
            result.AddMajorIssue("Excessive request rate",
                "Request rate significantly higher than typical usage pattern");
        }
    }
}
```

### Threat Detection Extensions

Implement custom threat detection:

```csharp
public class CustomThreatDetector : IThreatDetector
{
    private readonly IQobuzLogger _logger;
    private readonly IThreatIntelligenceService _threatIntel;
    private readonly Dictionary<string, ThreatPattern> _patterns;

    public CustomThreatDetector(IQobuzLogger logger, IThreatIntelligenceService threatIntel)
    {
        _logger = logger;
        _threatIntel = threatIntel;
        _patterns = LoadThreatPatterns();
    }

    public async Task<List<DetectedThreat>> DetectThreatsAsync(SecurityContext context)
    {
        var threats = new List<DetectedThreat>();

        // SQL Injection detection
        threats.AddRange(await DetectSqlInjection(context));

        // XSS detection
        threats.AddRange(await DetectXSS(context));

        // API abuse detection
        threats.AddRange(await DetectAPIAbuse(context));

        // Custom threat patterns
        threats.AddRange(await DetectCustomPatterns(context));

        return threats;
    }

    private async Task<List<DetectedThreat>> DetectCustomPatterns(SecurityContext context)
    {
        var threats = new List<DetectedThreat>();

        foreach (var pattern in _patterns.Values)
        {
            if (await pattern.Matches(context))
            {
                threats.Add(new DetectedThreat
                {
                    Type = pattern.ThreatType,
                    Severity = pattern.Severity,
                    Description = pattern.Description,
                    Context = context,
                    DetectedAt = DateTime.UtcNow,
                    Confidence = pattern.CalculateConfidence(context)
                });
            }
        }

        return threats;
    }

    private Dictionary<string, ThreatPattern> LoadThreatPatterns()
    {
        return new Dictionary<string, ThreatPattern>
        {
            ["credential_stuffing"] = new ThreatPattern
            {
                Name = "Credential Stuffing",
                ThreatType = ThreatType.AuthenticationAbuse,
                Severity = ThreatSeverity.High,
                Pattern = context => 
                    context.FailedAuthAttempts > 10 && 
                    context.UniquePasswords > 5 &&
                    context.TimeSpan < TimeSpan.FromMinutes(5),
                Description = "Multiple failed login attempts with different passwords"
            },
            
            ["enumeration_attack"] = new ThreatPattern
            {
                Name = "User Enumeration",
                ThreatType = ThreatType.InformationDisclosure,
                Severity = ThreatSeverity.Medium,
                Pattern = context =>
                    context.SearchQueries.Count > 50 &&
                    context.SearchQueries.All(q => q.Length < 5) &&
                    context.TimeSpan < TimeSpan.FromMinutes(10),
                Description = "Systematic short queries suggesting user enumeration"
            }
        };
    }
}

public class ThreatPattern
{
    public string Name { get; set; }
    public ThreatType ThreatType { get; set; }
    public ThreatSeverity Severity { get; set; }
    public Func<SecurityContext, bool> Pattern { get; set; }
    public string Description { get; set; }

    public async Task<bool> Matches(SecurityContext context)
    {
        try
        {
            return Pattern(context);
        }
        catch (Exception)
        {
            return false;
        }
    }

    public double CalculateConfidence(SecurityContext context)
    {
        // Calculate confidence based on how well the pattern matches
        // This is a simplified version - implement more sophisticated logic
        return Matches(context).Result ? 0.85 : 0.0;
    }
}
```

## Custom Indexers

### Creating Specialized Indexers

Create indexers for specific use cases:

```csharp
// Classical Music Indexer with specialized search logic
public class ClassicalMusicIndexer : QobuzIndexer
{
    private readonly IComposerService _composerService;
    private readonly IClassicalMusicParser _parser;

    public ClassicalMusicIndexer(
        IComposerService composerService,
        IClassicalMusicParser parser,
        IQobuzApiClient apiClient,
        IQobuzAuthenticationService authService,
        Logger logger) 
        : base(apiClient, authService, logger)
    {
        _composerService = composerService;
        _parser = parser;
    }

    protected override async Task<List<string>> BuildSearchQueries(string artist, string album)
    {
        var queries = new List<string>();

        // Try to parse classical music format
        var parsed = await _parser.ParseClassicalTitle(artist, album);
        
        if (parsed.IsClassical)
        {
            // Composer-based searches
            if (!string.IsNullOrEmpty(parsed.Composer))
            {
                queries.Add($"{parsed.Composer} {parsed.Work}");
                queries.Add($"\"{parsed.Composer}\" \"{parsed.Work}\"");
                
                // Add opus number if present
                if (!string.IsNullOrEmpty(parsed.OpusNumber))
                {
                    queries.Add($"{parsed.Composer} {parsed.OpusNumber}");
                }
            }

            // Performer-based searches
            if (!string.IsNullOrEmpty(parsed.Performer))
            {
                queries.Add($"{parsed.Performer} {parsed.Work}");
                queries.Add($"{parsed.Performer} {parsed.Composer}");
            }

            // Period/style searches
            if (!string.IsNullOrEmpty(parsed.Period))
            {
                queries.Add($"{parsed.Period} {parsed.Work}");
            }
        }
        else
        {
            // Fall back to base implementation for non-classical
            queries.AddRange(await base.BuildSearchQueries(artist, album));
        }

        return queries;
    }

    protected override async Task<List<QobuzAlbum>> ProcessSearchResults(
        List<QobuzAlbum> results, string artist, string album)
    {
        // Classical-specific result processing
        var processedResults = new List<QobuzAlbum>();

        foreach (var result in results)
        {
            // Score based on classical music criteria
            var score = await CalculateClassicalScore(result, artist, album);
            result.CustomScore = score;

            // Filter out non-classical results if we're specifically looking for classical
            if (IsLookingForClassical(artist, album))
            {
                if (IsClassicalAlbum(result))
                {
                    processedResults.Add(result);
                }
            }
            else
            {
                processedResults.Add(result);
            }
        }

        return processedResults.OrderByDescending(r => r.CustomScore).ToList();
    }

    private async Task<double> CalculateClassicalScore(QobuzAlbum album, string searchArtist, string searchAlbum)
    {
        double score = 0.0;

        // Match composer names
        var composers = await _composerService.ExtractComposers(searchArtist + " " + searchAlbum);
        foreach (var composer in composers)
        {
            if (album.Artist.Name.Contains(composer, StringComparison.OrdinalIgnoreCase) ||
                album.Title.Contains(composer, StringComparison.OrdinalIgnoreCase))
            {
                score += 20.0;
            }
        }

        // Match work titles
        var workTitles = await _parser.ExtractWorkTitles(searchAlbum);
        foreach (var work in workTitles)
        {
            if (album.Title.Contains(work, StringComparison.OrdinalIgnoreCase))
            {
                score += 15.0;
            }
        }

        // Prefer Hi-Res for classical
        if (album.MaximumBitDepth >= 24)
        {
            score += 10.0;
        }

        // Prefer well-known classical labels
        var classicalLabels = new[] { "Deutsche Grammophon", "Decca", "EMI Classics", "Harmonia Mundi" };
        if (classicalLabels.Any(label => album.Label?.Contains(label, StringComparison.OrdinalIgnoreCase) == true))
        {
            score += 5.0;
        }

        return score;
    }
}

// Jazz Indexer with improvisation and session detection
public class JazzIndexer : QobuzIndexer
{
    private readonly IJazzDatabase _jazzDb;

    protected override async Task<List<string>> BuildSearchQueries(string artist, string album)
    {
        var queries = new List<string>();

        // Standard queries
        queries.AddRange(await base.BuildSearchQueries(artist, album));

        // Jazz-specific queries
        var jazzInfo = await _jazzDb.LookupArtist(artist);
        if (jazzInfo != null)
        {
            // Add sideman searches
            foreach (var sideman in jazzInfo.FrequentCollaborators)
            {
                queries.Add($"{artist} {sideman}");
            }

            // Add label-specific searches
            if (!string.IsNullOrEmpty(jazzInfo.PrimaryLabel))
            {
                queries.Add($"{artist} {jazzInfo.PrimaryLabel}");
            }

            // Add era-specific searches
            if (!string.IsNullOrEmpty(jazzInfo.Era))
            {
                queries.Add($"{jazzInfo.Era} {artist}");
            }
        }

        return queries;
    }
}
```

## Advanced Integrations

### External Service Integration

Integrate with external services and APIs:

```csharp
// MusicBrainz Integration for enhanced metadata
public class MusicBrainzIntegration : IMetadataEnhancer
{
    private readonly HttpClient _httpClient;
    private readonly IQobuzLogger _logger;

    public async Task<EnhancedMetadata> EnhanceMetadataAsync(QobuzAlbum album)
    {
        try
        {
            // Search MusicBrainz for additional metadata
            var mbResult = await SearchMusicBrainz(album.Artist.Name, album.Title);
            
            if (mbResult != null)
            {
                return new EnhancedMetadata
                {
                    MusicBrainzId = mbResult.Id,
                    ReleaseDate = mbResult.ReleaseDate,
                    Country = mbResult.Country,
                    Label = mbResult.Label,
                    Genres = mbResult.Genres,
                    AdditionalCredits = mbResult.Credits,
                    RelatedAlbums = await GetRelatedAlbums(mbResult.ArtistId)
                };
            }
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to enhance metadata for {0} - {1}", album.Artist.Name, album.Title);
        }

        return null;
    }

    private async Task<MusicBrainzRelease> SearchMusicBrainz(string artist, string album)
    {
        var query = $"artist:{Uri.EscapeDataString(artist)} AND release:{Uri.EscapeDataString(album)}";
        var url = $"http://musicbrainz.org/ws/2/release?query={query}&fmt=json&limit=1";

        var response = await _httpClient.GetAsync(url);
        if (response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<MusicBrainzSearchResult>(content);
            return result.Releases?.FirstOrDefault();
        }

        return null;
    }
}

// Last.fm Integration for listening statistics
public class LastFmIntegration : IListeningStatsProvider
{
    private readonly string _apiKey;
    private readonly HttpClient _httpClient;

    public async Task<ListeningStats> GetListeningStatsAsync(string artist, string album)
    {
        try
        {
            var artistInfo = await GetArtistInfo(artist);
            var albumInfo = await GetAlbumInfo(artist, album);

            return new ListeningStats
            {
                ArtistPlayCount = artistInfo?.PlayCount ?? 0,
                ArtistListeners = artistInfo?.Listeners ?? 0,
                AlbumPlayCount = albumInfo?.PlayCount ?? 0,
                AlbumListeners = albumInfo?.Listeners ?? 0,
                TopTags = albumInfo?.TopTags ?? new List<string>(),
                SimilarArtists = artistInfo?.SimilarArtists ?? new List<string>()
            };
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to get Last.fm stats for {0} - {1}", artist, album);
            return new ListeningStats();
        }
    }

    private async Task<LastFmArtistInfo> GetArtistInfo(string artist)
    {
        var url = $"http://ws.audioscrobbler.com/2.0/?method=artist.getinfo&artist={Uri.EscapeDataString(artist)}&api_key={_apiKey}&format=json";
        
        var response = await _httpClient.GetAsync(url);
        if (response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<LastFmArtistResponse>(content);
            return result.Artist;
        }

        return null;
    }
}

// Spotify Integration for popularity metrics
public class SpotifyIntegration : IPopularityProvider
{
    private readonly SpotifyApi _spotifyApi;
    private readonly ISpotifyTokenManager _tokenManager;

    public async Task<PopularityMetrics> GetPopularityMetricsAsync(string artist, string album)
    {
        try
        {
            var token = await _tokenManager.GetAccessTokenAsync();
            _spotifyApi.SetAccessToken(token);

            var searchResults = await _spotifyApi.SearchItemsAsync($"artist:{artist} album:{album}", SearchType.Album, limit: 1);
            var spotifyAlbum = searchResults.Albums.Items?.FirstOrDefault();

            if (spotifyAlbum != null)
            {
                return new PopularityMetrics
                {
                    SpotifyPopularity = spotifyAlbum.Popularity,
                    SpotifyFollowers = spotifyAlbum.Artists.FirstOrDefault()?.Followers.Total ?? 0,
                    SpotifyId = spotifyAlbum.Id,
                    AvailableMarkets = spotifyAlbum.AvailableMarkets?.Count ?? 0
                };
            }
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to get Spotify metrics for {0} - {1}", artist, album);
        }

        return new PopularityMetrics();
    }
}
```

### Custom Quality Analyzers

Create specialized quality analysis:

```csharp
public class CustomQualityAnalyzer : IQualityAnalyzer
{
    private readonly IAudioAnalysisService _audioAnalysis;
    private readonly IQualityDatabase _qualityDb;

    public async Task<QualityAnalysis> AnalyzeQualityAsync(QobuzTrack track)
    {
        var analysis = new QualityAnalysis
        {
            TrackId = track.Id,
            AnalyzedAt = DateTime.UtcNow
        };

        // Analyze technical quality
        analysis.TechnicalQuality = await AnalyzeTechnicalQuality(track);

        // Analyze mastering quality  
        analysis.MasteringQuality = await AnalyzeMasteringQuality(track);

        // Check for common issues
        analysis.QualityIssues = await DetectQualityIssues(track);

        // Calculate overall quality score
        analysis.OverallScore = CalculateOverallQualityScore(analysis);

        return analysis;
    }

    private async Task<TechnicalQualityMetrics> AnalyzeTechnicalQuality(QobuzTrack track)
    {
        return new TechnicalQualityMetrics
        {
            BitRate = track.MaximumQuality.BitRate,
            SampleRate = track.MaximumQuality.SampleRate,
            BitDepth = track.MaximumQuality.BitDepth,
            DynamicRange = await CalculateDynamicRange(track),
            FrequencyResponse = await AnalyzeFrequencyResponse(track),
            NoiseFloor = await MeasureNoiseFloor(track)
        };
    }

    private async Task<MasteringQualityMetrics> AnalyzeMasteringQuality(QobuzTrack track)
    {
        return new MasteringQualityMetrics
        {
            LoudnessLUFS = await MeasureLUFS(track),
            PeakLevel = await MeasurePeakLevel(track),
            LoudnessRange = await MeasureLoudnessRange(track),
            TruePeak = await MeasureTruePeak(track),
            ClippingDetected = await DetectClipping(track),
            ComppressionRatio = await EstimateCompressionRatio(track)
        };
    }

    private async Task<List<QualityIssue>> DetectQualityIssues(QobuzTrack track)
    {
        var issues = new List<QualityIssue>();

        // Check for upsampling
        if (await DetectUpsampling(track))
        {
            issues.Add(new QualityIssue
            {
                Type = QualityIssueType.Upsampling,
                Severity = IssueSeverity.Medium,
                Description = "Track appears to be upsampled from lower resolution"
            });
        }

        // Check for lossy source
        if (await DetectLossySource(track))
        {
            issues.Add(new QualityIssue
            {
                Type = QualityIssueType.LossySource,
                Severity = IssueSeverity.High,
                Description = "FLAC file appears to be encoded from lossy source"
            });
        }

        // Check for loudness war artifacts
        if (await DetectLoudnessWarArtifacts(track))
        {
            issues.Add(new QualityIssue
            {
                Type = QualityIssueType.LoudnessWar,
                Severity = IssueSeverity.Medium,
                Description = "Track shows signs of aggressive loudness processing"
            });
        }

        return issues;
    }
}
```

## Testing & Deployment

### Plugin Testing Framework

Create comprehensive tests for your plugins:

```csharp
[TestFixture]
public class CustomPluginTests
{
    private IServiceContainer _container;
    private CustomMLQueryOptimizer _mlOptimizer;
    private Mock<IQobuzLogger> _mockLogger;

    [SetUp]
    public void Setup()
    {
        _mockLogger = new Mock<IQobuzLogger>();
        _container = new ServiceContainer();
        
        // Register test services
        _container.RegisterSingleton<IQobuzLogger>(_mockLogger.Object);
        
        _mlOptimizer = new CustomMLQueryOptimizer(_mockLogger.Object);
    }

    [Test]
    public async Task PredictComplexity_SimpleCase_ReturnsSimple()
    {
        // Arrange
        var artist = "The Beatles";
        var album = "Abbey Road";

        // Act
        var result = await _mlOptimizer.PredictComplexityAsync(artist, album);

        // Assert
        Assert.That(result.PredictedComplexity, Is.EqualTo(QueryComplexity.Simple));
        Assert.That(result.Confidence, Is.GreaterThan(0.5));
        Assert.That(result.RecommendedQueries, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task PredictComplexity_ComplexCase_ReturnsComplex()
    {
        // Arrange
        var artist = "Various Artists";
        var album = "Now That's What I Call Music! 42 (Deluxe Edition)";

        // Act
        var result = await _mlOptimizer.PredictComplexityAsync(artist, album);

        // Assert
        Assert.That(result.PredictedComplexity, Is.EqualTo(QueryComplexity.Complex));
        Assert.That(result.RecommendedQueries, Has.Count.EqualTo(3));
    }

    [Test]
    public void ExtractFeatures_ClassicalMusic_DetectsCorrectly()
    {
        // Arrange
        var artist = "Herbert von Karajan";
        var album = "Beethoven: Symphony No. 9";

        // Act
        var features = ExtractFeatures(artist, album);

        // Assert
        Assert.That(features.IsClassical, Is.True);
        Assert.That(features.HasComposerName, Is.True);
        Assert.That(features.HasOpusOrNumber, Is.True);
    }

    [TestCase("AC/DC", "Back in Black", QueryComplexity.Complex)]
    [TestCase("Miles Davis", "Kind of Blue", QueryComplexity.Simple)]
    [TestCase("Various Artists", "Café del Mar Vol. 15", QueryComplexity.Complex)]
    [TestCase("Pink Floyd", "The Dark Side of the Moon", QueryComplexity.Simple)]
    public async Task PredictComplexity_VariousCases_ReturnsExpectedComplexity(
        string artist, string album, QueryComplexity expectedComplexity)
    {
        // Act
        var result = await _mlOptimizer.PredictComplexityAsync(artist, album);

        // Assert
        Assert.That(result.PredictedComplexity, Is.EqualTo(expectedComplexity));
    }
}

[TestFixture]
public class CustomSecurityExtensionTests
{
    private CustomSecurityExtension _securityExtension;
    private Mock<IThreatIntelligenceService> _mockThreatIntel;

    [SetUp]
    public void Setup()
    {
        _mockThreatIntel = new Mock<IThreatIntelligenceService>();
        _securityExtension = new CustomSecurityExtension(_mockThreatIntel.Object);
    }

    [Test]
    public async Task ValidateAsync_SafeConfiguration_ReturnsNoIssues()
    {
        // Arrange
        var settings = new QobuzIndexerSettings
        {
            Email = "test@example.com",
            Password = "SecurePassword123!",
            CustomSecurityToken = "valid_token_123"
        };

        _mockThreatIntel.Setup(x => x.CheckThreatLevelAsync(It.IsAny<string>()))
                       .ReturnsAsync(ThreatLevel.Low);

        // Act
        var result = await _securityExtension.ValidateAsync(settings);

        // Assert
        Assert.That(result.CriticalIssues, Is.Empty);
        Assert.That(result.MajorIssues, Is.Empty);
        Assert.That(result.IsSecure, Is.True);
    }

    [Test]
    public async Task ValidateAsync_MissingSecurityToken_ReturnsCriticalIssue()
    {
        // Arrange
        var settings = new QobuzIndexerSettings
        {
            Email = "test@example.com",
            Password = "SecurePassword123!",
            CustomSecurityToken = null // Missing token
        };

        // Act
        var result = await _securityExtension.ValidateAsync(settings);

        // Assert
        Assert.That(result.CriticalIssues, Has.Count.EqualTo(1));
        Assert.That(result.CriticalIssues[0].Title, Is.EqualTo("Custom security token missing"));
        Assert.That(result.IsSecure, Is.False);
    }

    [Test]
    public async Task ValidateAsync_HighRiskUser_ReturnsMajorIssue()
    {
        // Arrange
        var settings = new QobuzIndexerSettings
        {
            Email = "suspicious@example.com",
            Password = "SecurePassword123!",
            CustomSecurityToken = "valid_token_123"
        };

        _mockThreatIntel.Setup(x => x.CheckThreatLevelAsync("suspicious@example.com"))
                       .ReturnsAsync(ThreatLevel.High);

        // Act
        var result = await _securityExtension.ValidateAsync(settings);

        // Assert
        Assert.That(result.MajorIssues, Has.Count.EqualTo(1));
        Assert.That(result.MajorIssues[0].Title, Is.EqualTo("High-risk user detected"));
    }
}

[TestFixture]
public class CustomIndexerIntegrationTests
{
    private ClassicalMusicIndexer _indexer;
    private Mock<IComposerService> _mockComposerService;
    private Mock<IQobuzApiClient> _mockApiClient;

    [SetUp]
    public void Setup()
    {
        _mockComposerService = new Mock<IComposerService>();
        _mockApiClient = new Mock<IQobuzApiClient>();
        
        _indexer = new ClassicalMusicIndexer(
            _mockComposerService.Object,
            Mock.Of<IClassicalMusicParser>(),
            _mockApiClient.Object,
            Mock.Of<IQobuzAuthenticationService>(),
            Mock.Of<Logger>()
        );
    }

    [Test]
    public async Task SearchAsync_ClassicalAlbum_UsesSpecializedQueries()
    {
        // Arrange
        var query = "Beethoven Symphony No. 9";
        
        _mockComposerService.Setup(x => x.ExtractComposers(It.IsAny<string>()))
                           .ReturnsAsync(new[] { "Beethoven" });

        _mockApiClient.Setup(x => x.GetAsync<QobuzSearchResponse>(It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()))
                     .ReturnsAsync(new QobuzSearchResponse { Albums = new QobuzAlbumSearchResults { Items = new List<QobuzAlbum>() } });

        // Act
        var results = await _indexer.SearchAsync(query, 0, 50);

        // Assert
        _mockApiClient.Verify(x => x.GetAsync<QobuzSearchResponse>(
            It.IsAny<string>(), 
            It.Is<Dictionary<string, string>>(d => 
                d.ContainsKey("query") && 
                d["query"].Contains("Beethoven"))), 
            Times.AtLeastOnce);
    }
}
```

### Plugin Deployment

Deploy your plugin using the established patterns:

```csharp
// Plugin deployment script
public class PluginDeployment
{
    public static async Task DeployPlugin(string pluginPath, string targetPath)
    {
        try
        {
            // Validate plugin before deployment
            var validator = new PluginValidator();
            var validationResult = await validator.ValidatePluginAsync(pluginPath);
            
            if (!validationResult.IsValid)
            {
                throw new InvalidOperationException($"Plugin validation failed: {string.Join(", ", validationResult.Errors)}");
            }

            // Copy plugin files
            await CopyPluginFiles(pluginPath, targetPath);

            // Register plugin with Qobuzarr
            await RegisterPlugin(targetPath);

            // Restart required services
            await RestartServices();

            Console.WriteLine($"Plugin deployed successfully to {targetPath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Plugin deployment failed: {ex.Message}");
            throw;
        }
    }

    private static async Task<bool> ValidatePlugin(string pluginPath)
    {
        // Check plugin structure
        var requiredFiles = new[] { "Plugin.cs", "plugin.json", "*.csproj" };
        foreach (var file in requiredFiles)
        {
            if (!Directory.GetFiles(pluginPath, file, SearchOption.TopDirectoryOnly).Any())
            {
                Console.WriteLine($"Missing required file: {file}");
                return false;
            }
        }

        // Validate plugin manifest
        var manifestPath = Path.Combine(pluginPath, "plugin.json");
        var manifest = JsonSerializer.Deserialize<PluginManifest>(await File.ReadAllTextAsync(manifestPath));
        
        if (string.IsNullOrEmpty(manifest.Name) || string.IsNullOrEmpty(manifest.Version))
        {
            Console.WriteLine("Plugin manifest is invalid");
            return false;
        }

        // Test compilation
        var buildResult = await CompilePlugin(pluginPath);
        if (!buildResult.Success)
        {
            Console.WriteLine($"Plugin compilation failed: {buildResult.ErrorMessage}");
            return false;
        }

        return true;
    }
}
```

### Continuous Integration for Plugins

Set up CI/CD for your custom plugins:

```yaml
# .github/workflows/plugin-ci.yml
name: Custom Plugin CI

on:
  push:
    paths:
    - 'plugins/**'
  pull_request:
    paths:
    - 'plugins/**'

jobs:
  plugin-tests:
    runs-on: ubuntu-latest
    
    strategy:
      matrix:
        plugin: [CustomMLPlugin, SecurityExtension, ClassicalIndexer]
    
    steps:
    - uses: actions/checkout@v4
    
    - name: Setup .NET
      uses: actions/setup-dotnet@v3
      with:
        dotnet-version: '8.0.x'
    
    - name: Restore dependencies
      run: dotnet restore plugins/${{ matrix.plugin }}
    
    - name: Build plugin
      run: dotnet build plugins/${{ matrix.plugin }} --no-restore
    
    - name: Run plugin tests
      run: dotnet test plugins/${{ matrix.plugin }} --no-build --verbosity normal
    
    - name: Validate plugin manifest
      run: |
        python scripts/validate_plugin_manifest.py plugins/${{ matrix.plugin }}/plugin.json
    
    - name: Security scan
      run: |
        dotnet list plugins/${{ matrix.plugin }} package --vulnerable
    
    - name: Package plugin
      if: github.ref == 'refs/heads/main'
      run: |
        dotnet pack plugins/${{ matrix.plugin }} --no-build --output ./packages
    
    - name: Upload plugin package
      if: github.ref == 'refs/heads/main'
      uses: actions/upload-artifact@v3
      with:
        name: ${{ matrix.plugin }}-package
        path: ./packages/*.nupkg
```

This comprehensive plugin development guide provides everything needed to create powerful extensions for Qobuzarr, from basic plugin structure to advanced ML models and security extensions. The examples are production-ready and follow Qobuzarr's established patterns and best practices.