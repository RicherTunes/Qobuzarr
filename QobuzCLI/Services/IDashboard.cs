using System;

namespace QobuzCLI.Services;

/// <summary>
/// Interface for Dashboard to enable mocking in tests
/// </summary>
public interface IDashboard : IDisposable
{
    bool IsActive { get; }
    void AddLogMessage(string message);
    void UpdateProgress(int processed, int success, int failed, string currentItem = "", string lastSuccessful = "");
    void Start(string operation, int totalItems);
    void Stop();
}