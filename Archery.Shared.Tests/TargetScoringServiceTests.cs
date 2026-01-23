// Copyright (c) GoldArrowTracker. Licensed under the GNU AFFERO GENERAL PUBLIC LICENSE v3.0

namespace Archery.Shared.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using Archery.Shared.Models;
using Archery.Shared.Services;
using Xunit;

/// <summary>
/// Unit tests for the TargetScoringService scoring logic.
/// </summary>
public class TargetScoringServiceTests
{
    // Dummy IObjectDetectionService for tests that don't need actual inference
    private class DummyObjectDetectionService : IObjectDetectionService
    {
        public ObjectDetectionConfig Config => new ObjectDetectionConfig();
        public List<ObjectDetectionResult> Predict(byte[] imageBytes) => new();
        public void Dispose() { }
    }

    [Fact]
    public void CalculateArrowScore_ArrowAtCenter_Returns10Points()
    {
        // Arrange
        var service = new TargetScoringService(new DummyObjectDetectionService());
        const float distanceFromCenter = 0;
        const float targetRadius = 100;

        // Act
        var score = service.CalculateArrowScore(distanceFromCenter, targetRadius);

        // Assert
        Assert.Equal(10, score);
    }

    [Fact]
    public void CalculateArrowScore_ArrowAtInnerRing_Returns9Points()
    {
        // Arrange: 15% of radius (ring 2)
        var service = new TargetScoringService(new DummyObjectDetectionService());
        const float distanceFromCenter = 15;
        const float targetRadius = 100;

        // Act
        var score = service.CalculateArrowScore(distanceFromCenter, targetRadius);

        // Assert
        Assert.Equal(9, score);
    }

    [Fact]
    public void CalculateArrowScore_ArrowAtOuterRing_Returns1Point()
    {
        // Arrange: 95% of radius (ring 10)
        var service = new TargetScoringService(new DummyObjectDetectionService());
        const float distanceFromCenter = 95;
        const float targetRadius = 100;

        // Act
        var score = service.CalculateArrowScore(distanceFromCenter, targetRadius);

        // Assert
        Assert.Equal(1, score);
    }

    [Fact]
    public void CalculateArrowScore_ArrowOutsideTarget_Returns0Points()
    {
        // Arrange
        var service = new TargetScoringService(new DummyObjectDetectionService());
        const float distanceFromCenter = 150;
        const float targetRadius = 100;

        // Act
        var score = service.CalculateArrowScore(distanceFromCenter, targetRadius);

        // Assert
        Assert.Equal(0, score);
    }

    [Fact]
    public void CalculateArrowScore_ZeroRadius_Returns0Points()
    {
        // Arrange
        var service = new TargetScoringService(new DummyObjectDetectionService());
        const float distanceFromCenter = 50;
        const float targetRadius = 0;

        // Act
        var score = service.CalculateArrowScore(distanceFromCenter, targetRadius);

        // Assert
        Assert.Equal(0, score);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(10, 9)]
    [InlineData(20, 8)]
    [InlineData(50, 5)]
    [InlineData(90, 1)]
    [InlineData(100, 0)]
    public void CalculateArrowScore_VariousDistances_ReturnsExpectedScore(float distance, int expectedScore)
    {
        // Arrange
        var service = new TargetScoringService(new DummyObjectDetectionService());
        const float targetRadius = 100;

        // Act
        var score = service.CalculateArrowScore(distance, targetRadius);

        // Assert
        Assert.Equal(expectedScore, score);
    }

    // Mock implementation of IObjectDetectionService for testing AnalyzeTargetImageAsync
    private class MockObjectDetectionService : IObjectDetectionService
    {
        private readonly List<ObjectDetectionResult> _mockDetections;

        public ObjectDetectionConfig Config => new ObjectDetectionConfig();

        public MockObjectDetectionService(List<ObjectDetectionResult> mockDetections)
        {
            _mockDetections = mockDetections;
        }

        public List<ObjectDetectionResult> Predict(byte[] imageBytes)
        {
            return _mockDetections;
        }

        public void Dispose() { }
    }

    [Fact]
    public async Task AnalyzeTargetImageAsync_WithValidDetections_ReturnsSuccessAndCorrectScore()
    {
        // Arrange
        var targetDetection = new ObjectDetectionResult
        {
            ClassId = 9,       // Mapped to "target" in Config
            ClassName = "target",
            Confidence = 0.95f,
            X = 100,
            Y = 100,
            Width = 200,
            Height = 200
        };

        var arrowDetection1 = new ObjectDetectionResult
        {
            ClassId = 2,       // Mapped to "10" in Config (inner ring)
            ClassName = "10",
            Confidence = 0.85f,
            X = 100,
            Y = 100,
            Width = 10,
            Height = 10
        };
        
        var arrowDetection2 = new ObjectDetectionResult
        {
            ClassId = 7,       // Mapped to "6" in Config (mid ring)
            ClassName = "6",
            Confidence = 0.75f,
            X = 120,
            Y = 120,
            Width = 10,
            Height = 10
        };
        
        var arrowDetection3 = new ObjectDetectionResult
        {
            ClassId = 0,       // Mapped to "0" in Config (miss)
            ClassName = "0",
            Confidence = 0.60f,
            X = 250,
            Y = 250,
            Width = 10,
            Height = 10
        };

        var mockDetections = new List<ObjectDetectionResult>
        {
            targetDetection,
            arrowDetection1,
            arrowDetection2,
            arrowDetection3
        };

        var mockService = new MockObjectDetectionService(mockDetections);
        var service = new TargetScoringService(mockService);

        // Act
        var result = await service.AnalyzeTargetImageAsync(new byte[] { 0x01, 0x02, 0x03 }); // Dummy image data

        // Assert
        Assert.Equal(AnalysisStatus.Success, result.Status);
        Assert.Equal(100, result.TargetCenterX);
        Assert.Equal(100, result.TargetCenterY);
        Assert.Equal(100, result.TargetRadius); // Half of width/height
        Assert.Equal(2, result.DetectedArrows.Count);

        // Verify arrow scores
        var arrowScores = result.ArrowScores.OrderBy(a => a.Detection.CenterX).ToList(); // Order for consistent assertion

        Assert.Equal(10, arrowScores[0].Points); // Arrow 1 (at target center)
        Assert.Equal(8, arrowScores[1].Points); // Arrow 2 (closer to center)

        Assert.Equal(18, result.TotalScore); // 10 + 8 + 0
    }

    [Fact]
    public async Task AnalyzeTargetImageAsync_NoTargetDetection_ReturnsFailure()
    {
        // Arrange
        var arrowDetection = new ObjectDetectionResult
        {
            ClassId = 2,
            ClassName = "10",
            Confidence = 0.85f,
            X = 100,
            Y = 100,
            Width = 10,
            Height = 10
        };

        var mockDetections = new List<ObjectDetectionResult> { arrowDetection };
        var mockService = new MockObjectDetectionService(mockDetections);
        var service = new TargetScoringService(mockService);

        // Act
        var result = await service.AnalyzeTargetImageAsync(new byte[] { 0x01, 0x02, 0x03 });

        // Assert
        Assert.Equal(AnalysisStatus.Failure, result.Status);
        Assert.Contains("Could not detect archery target in image", result.ErrorMessage);
    }

    [Fact]
    public async Task AnalyzeTargetImageAsync_NoDetections_ReturnsFailure()
    {
        // Arrange
        var mockDetections = new List<ObjectDetectionResult>();
        var mockService = new MockObjectDetectionService(mockDetections);
        var service = new TargetScoringService(mockService);

        // Act
        var result = await service.AnalyzeTargetImageAsync(new byte[] { 0x01, 0x02, 0x03 });

        // Assert
        Assert.Equal(AnalysisStatus.Failure, result.Status);
        Assert.Contains("Object Detection model did not detect any objects in the image", result.ErrorMessage);
    }
}