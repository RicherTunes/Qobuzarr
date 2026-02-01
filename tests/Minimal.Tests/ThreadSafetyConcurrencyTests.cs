using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;
using Lidarr.Plugin.Qobuzarr.Indexers;

namespace Minimal.Tests
{
    /// <summary>
    /// Thread safety and concurrency tests for the semantic query system.
    /// These tests ensure the system works correctly under concurrent load in production environments.
    /// CRITICAL: These tests prevent race conditions and state corruption in multi-user scenarios.
    /// </summary>
    public class ThreadSafetyConcurrencyTests
    {
        private readonly AlbumComponentClassifier _classifier;
        private readonly SemanticQueryStrategy _strategy;

        public ThreadSafetyConcurrencyTests()
        {
            _classifier = new AlbumComponentClassifier();
            _strategy = new SemanticQueryStrategy();
        }

        [Theory]
        [InlineData(2)]   // Dual-core simulation
        [InlineData(4)]   // Quad-core simulation
        [InlineData(8)]   // Server-class simulation
        [InlineData(16)]  // High-end server simulation
        public void ThreadSafety_ConcurrentClassification_SameInput(int threadCount)
        {
            // Arrange - Same album title processed by multiple threads
            var albumTitle = "MTV Unplugged Live Acoustic Sessions";
            var results = new ConcurrentBag<Dictionary<string, AlbumComponentType>>();
            var exceptions = new ConcurrentBag<Exception>();
            var barrier = new Barrier(threadCount);

            var threads = Enumerable.Range(0, threadCount).Select(i => new Thread(() =>
            {
                try
                {
                    // Synchronize thread start to maximize contention
                    barrier.SignalAndWait();

                    // Process classification multiple times per thread
                    for (int j = 0; j < 100; j++)
                    {
                        var components = _classifier.ClassifyComponents(albumTitle);
                        results.Add(components);
                    }
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            })).ToArray();

            // Act - Start all threads concurrently
            foreach (var thread in threads)
            {
                thread.Start();
            }

            // Wait for all threads to complete
            foreach (var thread in threads)
            {
                thread.Join(TimeSpan.FromSeconds(30)).Should().BeTrue("Thread should complete within timeout");
            }

            // Assert - No exceptions and consistent results
            exceptions.Should().BeEmpty("Concurrent classification should not throw exceptions");
            results.Should().HaveCount(threadCount * 100, "All classification operations should complete");

            // All results should be identical for the same input
            var firstResult = results.First();
            foreach (var result in results)
            {
                result.Should().BeEquivalentTo(firstResult,
                    "Concurrent classification of same input should produce identical results");
            }
        }

        [Fact]
        public void ThreadSafety_ConcurrentClassification_DifferentInputs()
        {
            // Arrange - Different album titles processed concurrently
            var albumTitles = new[]
            {
                "Simple Album",
                "Live Concert Recording",
                "Acoustic Sessions Demo",
                "Instrumental Compilation",
                "Studio Outtakes (Deluxe Edition)",
                "MTV Unplugged En Vivo",
                "Hi-Fi Audiophile 24-Bit",
                "Complete B-Sides Collection"
            };

            var results = new ConcurrentDictionary<string, List<Dictionary<string, AlbumComponentType>>>();
            var exceptions = new ConcurrentBag<Exception>();
            var threadCount = albumTitles.Length * 4; // 4 threads per album title

            var threads = new List<Thread>();

            // Create threads that process different albums
            for (int i = 0; i < threadCount; i++)
            {
                var albumTitle = albumTitles[i % albumTitles.Length];
                var thread = new Thread(() =>
                {
                    try
                    {
                        var localResults = new List<Dictionary<string, AlbumComponentType>>();

                        for (int j = 0; j < 50; j++) // 50 iterations per thread
                        {
                            var components = _classifier.ClassifyComponents(albumTitle);
                            localResults.Add(components);
                        }

                        results.AddOrUpdate(albumTitle,
                            localResults,
                            (key, existing) =>
                            {
                                lock (existing)
                                {
                                    existing.AddRange(localResults);
                                }
                                return existing;
                            });
                    }
                    catch (Exception ex)
                    {
                        exceptions.Add(ex);
                    }
                });

                threads.Add(thread);
            }

            // Act - Start all threads
            threads.ForEach(t => t.Start());

            // Wait for completion
            threads.ForEach(t => t.Join(TimeSpan.FromSeconds(30)).Should().BeTrue("Thread should complete"));

            // Assert - Verify thread safety
            exceptions.Should().BeEmpty("Concurrent processing should not throw exceptions");
            results.Should().HaveCount(albumTitles.Length, "Results should exist for all album titles");

            // Verify consistency within each album's results
            foreach (var kvp in results)
            {
                var albumResults = kvp.Value;
                albumResults.Should().NotBeEmpty($"Should have results for {kvp.Key}");

                var firstResult = albumResults.First();
                foreach (var result in albumResults)
                {
                    result.Should().BeEquivalentTo(firstResult,
                        $"All results for '{kvp.Key}' should be consistent across threads");
                }
            }
        }

        [Fact]
        public void ThreadSafety_ConcurrentStrategyGeneration_MixedOperations()
        {
            // Arrange - Mix of classification and strategy operations
            var testData = new[]
            {
                ("Artist1", "Album Live"),
                ("Artist2", "Songs Instrumental"),
                ("Artist3", "Music Acoustic"),
                ("Artist4", "Tracks (Deluxe Edition)"),
                ("Artist5", "Collection En Vivo")
            };

            var classificationResults = new ConcurrentBag<(string, Dictionary<string, AlbumComponentType>)>();
            var strategyResults = new ConcurrentBag<(string, QueryStrategy)>();
            var queryResults = new ConcurrentBag<(string, List<string>)>();
            var exceptions = new ConcurrentBag<Exception>();

            var tasks = testData.SelectMany(data => new[]
            {
                // Classification task
                Task.Run(() =>
                {
                    try
                    {
                        for (int i = 0; i < 100; i++)
                        {
                            var components = _classifier.ClassifyComponents(data.Item2);
                            classificationResults.Add((data.Item2, components));
                        }
                    }
                    catch (Exception ex) { exceptions.Add(ex); }
                }),
                
                // Strategy generation task
                Task.Run(() =>
                {
                    try
                    {
                        for (int i = 0; i < 100; i++)
                        {
                            var strategy = _strategy.DetermineStrategy(data.Item1, data.Item2);
                            strategyResults.Add((data.Item2, strategy));
                        }
                    }
                    catch (Exception ex) { exceptions.Add(ex); }
                }),
                
                // Query generation task  
                Task.Run(() =>
                {
                    try
                    {
                        for (int i = 0; i < 100; i++)
                        {
                            var queries = _strategy.BuildQueriesForBugCase(data.Item1, data.Item2);
                            queryResults.Add((data.Item2, queries));
                        }
                    }
                    catch (Exception ex) { exceptions.Add(ex); }
                })
            }).ToArray();

            // Act - Execute all tasks concurrently
            Task.WaitAll(tasks, TimeSpan.FromSeconds(60))
                .Should().BeTrue("All concurrent tasks should complete within timeout");

            // Assert - Verify thread safety across all operation types
            exceptions.Should().BeEmpty("Concurrent mixed operations should not throw exceptions");

            classificationResults.Should().HaveCount(testData.Length * 100,
                "All classification operations should complete");
            strategyResults.Should().HaveCount(testData.Length * 100,
                "All strategy operations should complete");
            queryResults.Should().HaveCount(testData.Length * 100,
                "All query generation operations should complete");

            // Verify consistency within each operation type
            foreach (var data in testData)
            {
                var albumClassifications = classificationResults
                    .Where(r => r.Item1 == data.Item2)
                    .Select(r => r.Item2)
                    .ToList();

                var albumStrategies = strategyResults
                    .Where(r => r.Item1 == data.Item2)
                    .Select(r => r.Item2)
                    .ToList();

                // All classifications for the same album should be identical
                if (albumClassifications.Count > 1)
                {
                    var firstClassification = albumClassifications.First();
                    albumClassifications.Should().OnlyContain(c => c.SequenceEqual(firstClassification),
                        $"Classifications for '{data.Item2}' should be consistent across threads");
                }

                // All strategies for the same album should be identical
                if (albumStrategies.Count > 1)
                {
                    var firstStrategy = albumStrategies.First();
                    albumStrategies.Should().OnlyContain(s =>
                        s.CleaningLevel == firstStrategy.CleaningLevel &&
                        s.PreserveTerms.SequenceEqual(firstStrategy.PreserveTerms),
                        $"Strategies for '{data.Item2}' should be consistent across threads");
                }
            }
        }

        [Fact]
        public void ThreadSafety_StaticCollectionAccess_VersionDescriptors()
        {
            // This test validates that our static readonly collections are thread-safe
            // The AlbumComponentClassifier uses static HashSets which should be thread-safe for reads

            var albumTitles = new[]
            {
                "Album Live", "Songs Instrumental", "Music Acoustic", "Tracks Demo",
                "Concert Unplugged", "Sessions Studio", "Compilation Remix", "Collection Mono"
            };

            var results = new ConcurrentBag<bool>();
            var exceptions = new ConcurrentBag<Exception>();
            var threadCount = 20;

            var threads = Enumerable.Range(0, threadCount).Select(i => new Thread(() =>
            {
                try
                {
                    var random = new Random(i); // Different seed per thread

                    for (int j = 0; j < 1000; j++) // Many iterations to stress test
                    {
                        var albumTitle = albumTitles[random.Next(albumTitles.Length)];
                        var components = _classifier.ClassifyComponents(albumTitle);

                        // Verify we get expected version descriptors
                        var hasVersionDescriptor = components.Values.Any(c => c == AlbumComponentType.VersionDescriptor);
                        results.Add(hasVersionDescriptor);

                        // Small delay to increase chance of race conditions if they exist
                        if (j % 100 == 0) Thread.Sleep(1);
                    }
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            })).ToArray();

            // Act - Start all threads
            foreach (var thread in threads)
            {
                thread.Start();
            }

            // Wait for completion
            foreach (var thread in threads)
            {
                thread.Join(TimeSpan.FromSeconds(30)).Should().BeTrue("Thread should complete within timeout");
            }

            // Assert - No exceptions and sensible results
            exceptions.Should().BeEmpty("Concurrent static collection access should not throw exceptions");
            results.Should().HaveCount(threadCount * 1000, "All operations should complete");
            results.Should().Contain(true, "At least some albums should have version descriptors");
        }

        [Fact]
        public void ThreadSafety_MemoryPressure_ConcurrentOperationsUnderLoad()
        {
            // This test simulates high memory pressure with concurrent operations

            var albumTitles = new[]
            {
                "The Complete Live MTV Unplugged Acoustic Sessions Hi-Fi 24-Bit (Deluxe Edition)",
                "Ultimate Collection Rarities B-Sides Demo Recordings Instrumental Versions",
                "International En Vivo Ao Vivo En Direct Concert Festival Tour Residency",
                "Studio Analog Digital Vinyl CD Mono Stereo Binaural Quadraphonic Versions"
            };

            var exceptions = new ConcurrentBag<Exception>();
            var completedOperations = new ConcurrentBag<int>();
            var maxMemoryUsed = 0L;
            var memoryLock = new object();

            var tasks = Enumerable.Range(0, Environment.ProcessorCount * 2).Select(taskId =>
                Task.Run(async () =>
                {
                    try
                    {
                        var operationsCompleted = 0;
                        var random = new Random(taskId);

                        for (int i = 0; i < 500; i++) // 500 operations per task
                        {
                            var albumTitle = albumTitles[random.Next(albumTitles.Length)];

                            // Mix of operations
                            switch (i % 4)
                            {
                                case 0:
                                    _classifier.ClassifyComponents(albumTitle);
                                    break;
                                case 1:
                                    _classifier.GetPreservedTerms(albumTitle);
                                    break;
                                case 2:
                                    _strategy.DetermineStrategy("Artist", albumTitle);
                                    break;
                                case 3:
                                    _strategy.BuildQueriesForBugCase("Artist", albumTitle);
                                    break;
                            }

                            operationsCompleted++;

                            // Periodic memory check
                            if (i % 50 == 0)
                            {
                                var currentMemory = GC.GetTotalMemory(false);
                                lock (memoryLock)
                                {
                                    if (currentMemory > maxMemoryUsed)
                                        maxMemoryUsed = currentMemory;
                                }

                                // Small delay to allow GC
                                await Task.Delay(1);
                            }
                        }

                        completedOperations.Add(operationsCompleted);
                    }
                    catch (Exception ex)
                    {
                        exceptions.Add(ex);
                    }
                })).ToArray();

            // Act - Execute under memory pressure
            var initialMemory = GC.GetTotalMemory(true);

            Task.WaitAll(tasks, TimeSpan.FromMinutes(2))
                .Should().BeTrue("All tasks should complete under memory pressure");

            // Force cleanup
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var finalMemory = GC.GetTotalMemory(false);

            // Assert - System should remain stable under pressure
            exceptions.Should().BeEmpty("Operations should not fail under memory pressure");

            var totalOperations = completedOperations.Sum();
            totalOperations.Should().Be(Environment.ProcessorCount * 2 * 500,
                "All operations should complete successfully");

            var memoryIncrease = finalMemory - initialMemory;
            memoryIncrease.Should().BeLessThan(maxMemoryUsed / 2,
                "Memory should be properly cleaned up after operations");
        }

        [Fact]
        public void ThreadSafety_DeadlockPrevention_NestedOperations()
        {
            // Test for potential deadlocks in nested semantic operations

            var barrier = new Barrier(4);
            var exceptions = new ConcurrentBag<Exception>();
            var completedTasks = new ConcurrentBag<bool>();

            var tasks = new[]
            {
                // Task 1: Classification -> Strategy -> Queries
                Task.Run(() =>
                {
                    try
                    {
                        barrier.SignalAndWait();
                        for (int i = 0; i < 100; i++)
                        {
                            var albumTitle = "Album Live Instrumental";
                            var components = _classifier.ClassifyComponents(albumTitle);
                            var strategy = _strategy.DetermineStrategy("Artist", albumTitle);
                            var queries = _strategy.BuildQueriesForStrategy("Artist", albumTitle, strategy);
                        }
                        completedTasks.Add(true);
                    }
                    catch (Exception ex) { exceptions.Add(ex); }
                }),

                // Task 2: Strategy -> Classification (reverse order)  
                Task.Run(() =>
                {
                    try
                    {
                        barrier.SignalAndWait();
                        for (int i = 0; i < 100; i++)
                        {
                            var albumTitle = "Songs Acoustic Demo";
                            var strategy = _strategy.DetermineStrategy("Artist", albumTitle);
                            var components = _classifier.ClassifyComponents(albumTitle);
                        }
                        completedTasks.Add(true);
                    }
                    catch (Exception ex) { exceptions.Add(ex); }
                }),

                // Task 3: Rapid mixed operations
                Task.Run(() =>
                {
                    try
                    {
                        barrier.SignalAndWait();
                        for (int i = 0; i < 200; i++)
                        {
                            var albumTitle = i % 2 == 0 ? "Music Unplugged" : "Tracks (Deluxe Edition)";
                            if (i % 3 == 0)
                                _classifier.ClassifyComponents(albumTitle);
                            else
                                _strategy.DetermineStrategy("Artist", albumTitle);
                        }
                        completedTasks.Add(true);
                    }
                    catch (Exception ex) { exceptions.Add(ex); }
                }),

                // Task 4: Stress operations with same input
                Task.Run(() =>
                {
                    try
                    {
                        barrier.SignalAndWait();
                        var albumTitle = "The Same Album Title For All";
                        for (int i = 0; i < 300; i++)
                        {
                            _classifier.ClassifyComponents(albumTitle);
                            _strategy.DetermineStrategy("Artist", albumTitle);
                        }
                        completedTasks.Add(true);
                    }
                    catch (Exception ex) { exceptions.Add(ex); }
                })
            };

            // Act - Wait for completion with generous timeout for deadlock detection
            var completed = Task.WaitAll(tasks, TimeSpan.FromMinutes(1));

            // Assert - No deadlocks should occur
            completed.Should().BeTrue("All tasks should complete without deadlocks");
            exceptions.Should().BeEmpty("No exceptions should occur during nested operations");
            completedTasks.Should().HaveCount(4, "All tasks should complete successfully");
        }

        [Fact]
        public void ThreadSafety_RapidStartStop_ThreadCreationDestruction()
        {
            // Test rapid thread creation and destruction to check for resource leaks

            var exceptions = new ConcurrentBag<Exception>();
            var successCount = new ConcurrentBag<int>();

            // Create and destroy many short-lived threads rapidly
            for (int batch = 0; batch < 10; batch++)
            {
                var threads = Enumerable.Range(0, 20).Select(i => new Thread(() =>
                {
                    try
                    {
                        var operations = 0;
                        // Very short-lived operations
                        for (int j = 0; j < 10; j++)
                        {
                            _classifier.ClassifyComponents($"Album {i}_{j}");
                            operations++;
                        }
                        successCount.Add(operations);
                    }
                    catch (Exception ex)
                    {
                        exceptions.Add(ex);
                    }
                })).ToArray();

                // Start all threads in batch
                foreach (var thread in threads)
                {
                    thread.Start();
                }

                // Wait for batch completion
                foreach (var thread in threads)
                {
                    thread.Join(TimeSpan.FromSeconds(5)).Should().BeTrue("Short-lived thread should complete quickly");
                }

                // Small delay between batches
                Thread.Sleep(10);
            }

            // Assert - No issues with rapid thread creation/destruction
            exceptions.Should().BeEmpty("Rapid thread creation should not cause exceptions");
            successCount.Should().HaveCount(200, "All threads should complete successfully"); // 10 batches * 20 threads
            successCount.Should().OnlyContain(count => count == 10, "Each thread should complete all operations");
        }
    }
}
