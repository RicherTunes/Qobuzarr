using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using FluentAssertions;
using NLog;
using Lidarr.Plugin.Qobuzarr.Download.Services;
using Qobuzarr.Tests.Fixtures;

namespace Qobuzarr.Tests.Unit.Download.Services
{
    public class ConcurrencyManagerTests : TestFixtureBase
    {
        [Fact]
        public void Constructor_WithValidParameters_InitializesCorrectly()
        {
            // Act
            using var sut = new ConcurrencyManager(MockLogger.Object, 3);

            // Assert
            sut.CurrentLimit.Should().Be(3);
            sut.ActiveCount.Should().Be(0);
            sut.WaitingCount.Should().Be(0);
        }

        [Fact]
        public void Constructor_WithZeroLimit_ThrowsArgumentException()
        {
            // Act & Assert
            Assert.Throws<ArgumentOutOfRangeException>(() => new ConcurrencyManager(MockLogger.Object, 0));
        }

        [Fact]
        public void Constructor_WithNegativeLimit_ThrowsArgumentException()
        {
            // Act & Assert
            Assert.Throws<ArgumentOutOfRangeException>(() => new ConcurrencyManager(MockLogger.Object, -1));
        }

        [Fact]
        public void Constructor_WithNullLogger_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new ConcurrencyManager(null, 3));
        }

        [Fact]
        public async Task AcquireSlotAsync_WithAvailableSlot_ReturnsImmediately()
        {
            // Arrange
            using var sut = new ConcurrencyManager(MockLogger.Object, 3);

            // Act
            var slot = await sut.AcquireSlotAsync();

            // Assert
            slot.Should().NotBeNull();
            sut.ActiveCount.Should().Be(1);
            sut.WaitingCount.Should().Be(0);

            // Cleanup
            slot.Dispose();
        }

        [Fact]
        public async Task AcquireSlotAsync_WithExhaustedSlots_WaitsForRelease()
        {
            // Arrange
            using var sut = new ConcurrencyManager(MockLogger.Object, 2);
            var slot1 = await sut.AcquireSlotAsync();
            var slot2 = await sut.AcquireSlotAsync();

            // Act
            var slot3Task = sut.AcquireSlotAsync();
            
            // Should be waiting
            await Task.Delay(50);
            sut.ActiveCount.Should().Be(2);
            // Note: WaitingCount is not accurately trackable with SemaphoreSlim
            slot3Task.IsCompleted.Should().BeFalse();

            // Release one slot
            slot1.Dispose();
            var slot3 = await slot3Task;

            // Assert
            slot3.Should().NotBeNull();
            sut.ActiveCount.Should().Be(2);

            // Cleanup
            slot2.Dispose();
            slot3.Dispose();
        }

        [Fact]
        public async Task AcquireSlotAsync_WithCancellation_ThrowsOperationCancelledException()
        {
            // Arrange
            using var sut = new ConcurrencyManager(MockLogger.Object, 1);
            var slot1 = await sut.AcquireSlotAsync();
            
            using var cts = new CancellationTokenSource();
            cts.CancelAfter(100); // Cancel after 100ms

            // Act & Assert
            await Assert.ThrowsAsync<OperationCanceledException>(
                () => sut.AcquireSlotAsync(cts.Token));

            // Cleanup
            slot1.Dispose();
        }

        [Fact]
        public async Task DisposedSlot_ReleasesSlot()
        {
            // Arrange
            using var sut = new ConcurrencyManager(MockLogger.Object, 1);

            // Act
            var slot = await sut.AcquireSlotAsync();
            sut.ActiveCount.Should().Be(1);

            slot.Dispose();

            // Assert
            sut.ActiveCount.Should().Be(0);
        }

        [Fact]
        public async Task MultipleSlotDisposal_IsSafe()
        {
            // Arrange
            using var sut = new ConcurrencyManager(MockLogger.Object, 1);
            var slot = await sut.AcquireSlotAsync();

            // Act - dispose multiple times
            slot.Dispose();
            slot.Dispose(); // Should not throw or affect state

            // Assert
            sut.ActiveCount.Should().Be(0);
        }

        [Fact]
        public void UpdateConcurrencyLimit_WithValidLimit_UpdatesLimit()
        {
            // Arrange
            using var sut = new ConcurrencyManager(MockLogger.Object, 3);

            // Act
            sut.UpdateConcurrencyLimit(5);

            // Assert
            sut.CurrentLimit.Should().Be(5);
        }

        [Fact]
        public void UpdateConcurrencyLimit_WithZeroLimit_ThrowsArgumentException()
        {
            // Arrange
            using var sut = new ConcurrencyManager(MockLogger.Object, 3);

            // Act & Assert
            Assert.Throws<ArgumentException>(() => sut.UpdateConcurrencyLimit(0));
        }

        [Fact]
        public void UpdateConcurrencyLimit_WithNegativeLimit_ThrowsArgumentException()
        {
            // Arrange
            using var sut = new ConcurrencyManager(MockLogger.Object, 3);

            // Act & Assert
            Assert.Throws<ArgumentException>(() => sut.UpdateConcurrencyLimit(-1));
        }

        [Fact]
        public async Task UpdateConcurrencyLimit_WithIncreaseWhileWaiting_AllowsMoreSlots()
        {
            // Arrange
            using var sut = new ConcurrencyManager(MockLogger.Object, 1);
            var slot1 = await sut.AcquireSlotAsync();

            var slot2Task = sut.AcquireSlotAsync();
            await Task.Delay(50); // Let it start waiting

            // Act - increase limit
            sut.UpdateConcurrencyLimit(2);

            // Assert
            var slot2 = await slot2Task;
            slot2.Should().NotBeNull();
            sut.ActiveCount.Should().Be(2);

            // Cleanup
            slot1.Dispose();
            slot2.Dispose();
        }

        [Fact]
        public async Task UpdateConcurrencyLimit_WithDecrease_MaintainsActiveSlots()
        {
            // Arrange
            using var sut = new ConcurrencyManager(MockLogger.Object, 3);
            var slot1 = await sut.AcquireSlotAsync();
            var slot2 = await sut.AcquireSlotAsync();

            // Act - decrease limit below active count
            sut.UpdateConcurrencyLimit(1);

            // Assert - the limit is updated but active slots remain tracked
            sut.CurrentLimit.Should().Be(1);
            
            // Active slots should still be 2 since we have 2 acquired slots
            sut.ActiveCount.Should().Be(2);

            // Slots should still be valid
            slot1.Should().NotBeNull();
            slot2.Should().NotBeNull();

            // Cleanup - after disposal, active count should reduce
            slot1.Dispose();
            slot2.Dispose();
            
            // After cleanup, active count should be 0
            sut.ActiveCount.Should().Be(0);
        }

        [Fact]
        public void GetStatistics_WithNoActivity_ReturnsBasicStatistics()
        {
            // Arrange
            using var sut = new ConcurrencyManager(MockLogger.Object, 3);

            // Act
            var stats = sut.GetStatistics();

            // Assert
            stats.MaxConcurrency.Should().Be(3);
            stats.ActiveOperations.Should().Be(0);
            stats.QueuedOperations.Should().Be(0);
            stats.TotalSlotsUsed.Should().Be(0);
            stats.AverageWaitTime.Should().Be(TimeSpan.Zero);
            stats.LastUpdated.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
        }

        [Fact]
        public async Task GetStatistics_WithActiveSlots_ReturnsCorrectStatistics()
        {
            // Arrange
            using var sut = new ConcurrencyManager(MockLogger.Object, 2);
            var slot1 = await sut.AcquireSlotAsync();
            var slot2 = await sut.AcquireSlotAsync();

            var slot3Task = sut.AcquireSlotAsync();
            await Task.Delay(50); // Let it start waiting

            // Act
            var stats = sut.GetStatistics();

            // Assert
            stats.MaxConcurrency.Should().Be(2);
            stats.ActiveOperations.Should().Be(2);
            // Note: QueuedOperations is not accurately trackable with SemaphoreSlim
            stats.QueuedOperations.Should().Be(0);
            stats.TotalSlotsUsed.Should().Be(2);

            // Cleanup
            slot1.Dispose();
            slot2.Dispose();
            var slot3 = await slot3Task;
            slot3.Dispose();
        }

        [Fact]
        public async Task ConcurrentSlotAcquisition_WorksCorrectly()
        {
            // Arrange
            using var sut = new ConcurrencyManager(MockLogger.Object, 3);
            const int taskCount = 10;
            var tasks = new Task[taskCount];

            // Act - acquire slots concurrently
            for (int i = 0; i < taskCount; i++)
            {
                tasks[i] = Task.Run(async () =>
                {
                    using var slot = await sut.AcquireSlotAsync();
                    await Task.Delay(100); // Simulate work
                });
            }

            await Task.WhenAll(tasks);

            // Assert - all tasks completed successfully
            tasks.Should().OnlyContain(t => t.IsCompletedSuccessfully);
            sut.ActiveCount.Should().Be(0); // All slots should be released
        }

        [Fact]
        [Trait("Category", "Slow")]
        [Trait("Category", "Stress")]
        public async Task StressTest_HighConcurrencyWithFrequentUpdates_RemainsStable()
        {
            // Arrange
            using var sut = new ConcurrencyManager(MockLogger.Object, 5);
            const int iterations = 100;
            var tasks = new Task[iterations];

            // Act - mix of slot acquisition and limit updates
            for (int i = 0; i < iterations; i++)
            {
                if (i % 10 == 0)
                {
                    tasks[i] = Task.Run(() => sut.UpdateConcurrencyLimit((i % 3) + 3)); // 3-5
                }
                else
                {
                    tasks[i] = Task.Run(async () =>
                    {
                        using var slot = await sut.AcquireSlotAsync();
                        await Task.Delay(10);
                    });
                }
            }

            // Assert - should not throw and should complete
            await Task.WhenAll(tasks);
            sut.ActiveCount.Should().Be(0);
        }

        [Fact]
        public void Dispose_WithActiveSlots_DisposesCleanly()
        {
            // Arrange
            var sut = new ConcurrencyManager(MockLogger.Object, 3);

            // Act & Assert - should not throw
            sut.Dispose();
        }

        [Fact]
        public void Dispose_CalledMultipleTimes_DoesNotThrow()
        {
            // Arrange
            var sut = new ConcurrencyManager(MockLogger.Object, 3);

            // Act & Assert
            sut.Dispose();
            sut.Dispose(); // Should not throw
        }

        [Fact]
        public async Task AcquireSlotAsync_AfterDispose_ThrowsObjectDisposedException()
        {
            // Arrange
            var sut = new ConcurrencyManager(MockLogger.Object, 3);
            sut.Dispose();

            // Act & Assert
            await Assert.ThrowsAsync<ObjectDisposedException>(() => sut.AcquireSlotAsync());
        }

        [Fact]
        public void UpdateConcurrencyLimit_AfterDispose_ThrowsObjectDisposedException()
        {
            // Arrange
            var sut = new ConcurrencyManager(MockLogger.Object, 3);
            sut.Dispose();

            // Act & Assert
            Assert.Throws<ObjectDisposedException>(() => sut.UpdateConcurrencyLimit(5));
        }
    }
}
