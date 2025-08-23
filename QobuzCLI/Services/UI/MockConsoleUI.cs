using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace QobuzCLI.Services.UI
{
    /// <summary>
    /// Mock implementation of IConsoleUI for unit testing.
    /// Captures UI interactions and allows pre-configured responses.
    /// </summary>
    public class MockConsoleUI : IConsoleUI
    {
        private readonly List<string> _outputCapture;
        private readonly Queue<string> _inputResponses;
        private readonly Queue<bool> _confirmationResponses;
        private readonly Queue<string> _selectionResponses;

        public MockConsoleUI()
        {
            _outputCapture = new List<string>();
            _inputResponses = new Queue<string>();
            _confirmationResponses = new Queue<bool>();
            _selectionResponses = new Queue<string>();
            Title = "Test Console";
        }

        /// <summary>
        /// Gets all captured output from console operations.
        /// </summary>
        public IReadOnlyList<string> CapturedOutput => _outputCapture.AsReadOnly();

        /// <summary>
        /// Gets the last line written to the console.
        /// </summary>
        public string LastOutput => _outputCapture.LastOrDefault() ?? string.Empty;

        /// <summary>
        /// Gets the number of lines written to the console.
        /// </summary>
        public int OutputCount => _outputCapture.Count;

        public void MarkupLine(string markup)
        {
            _outputCapture.Add($"[MARKUP] {markup}");
        }

        public void WriteLine(string text = "")
        {
            _outputCapture.Add($"[TEXT] {text}");
        }

        public void Write(Table table)
        {
            _outputCapture.Add($"[TABLE] {table.Rows.Count} rows, {table.Columns.Count} columns");
        }

        public string Prompt(SelectionPrompt<string> prompt)
        {
            _outputCapture.Add($"[SELECTION_PROMPT] {prompt.Title}");
            
            if (_selectionResponses.Count > 0)
            {
                var response = _selectionResponses.Dequeue();
                _outputCapture.Add($"[SELECTION_RESPONSE] {response}");
                return response;
            }
            
            // Default response for testing
            var defaultChoice = "1";
            _outputCapture.Add($"[SELECTION_RESPONSE] {defaultChoice} (default)");
            return defaultChoice;
        }

        public string Ask(string prompt)
        {
            _outputCapture.Add($"[INPUT_PROMPT] {prompt}");
            
            if (_inputResponses.Count > 0)
            {
                var response = _inputResponses.Dequeue();
                _outputCapture.Add($"[INPUT_RESPONSE] {response}");
                return response;
            }
            
            return "test-input";
        }

        public bool Confirm(string prompt, bool defaultValue = false)
        {
            _outputCapture.Add($"[CONFIRM_PROMPT] {prompt} (default: {defaultValue})");
            
            if (_confirmationResponses.Count > 0)
            {
                var response = _confirmationResponses.Dequeue();
                _outputCapture.Add($"[CONFIRM_RESPONSE] {response}");
                return response;
            }
            
            _outputCapture.Add($"[CONFIRM_RESPONSE] {defaultValue} (default)");
            return defaultValue;
        }

        public Table CreateTable()
        {
            var table = new Table();
            table.Border = TableBorder.Rounded;
            return table;
        }

        public SelectionPrompt<string> CreateSelectionPrompt()
        {
            return new SelectionPrompt<string>()
                .PageSize(15);
        }

        public Progress CreateProgress()
        {
            // Return a minimal progress instance for testing
            return AnsiConsole.Progress();
        }

        public T WithSpinner<T>(string message, Func<T> operation)
        {
            _outputCapture.Add($"[SPINNER] {message}");
            return operation();
        }

        public async Task<T> WithSpinnerAsync<T>(string message, Func<Task<T>> operation)
        {
            _outputCapture.Add($"[SPINNER_ASYNC] {message}");
            return await operation().ConfigureAwait(false);
        }

        public void Clear()
        {
            _outputCapture.Add("[CLEAR]");
        }

        public string Title { get; set; }

        // Test helper methods

        /// <summary>
        /// Queues a response for the next Ask() call.
        /// </summary>
        /// <param name="response">The response to return</param>
        public void QueueInputResponse(string response)
        {
            _inputResponses.Enqueue(response);
        }

        /// <summary>
        /// Queues a response for the next Confirm() call.
        /// </summary>
        /// <param name="response">The response to return</param>
        public void QueueConfirmationResponse(bool response)
        {
            _confirmationResponses.Enqueue(response);
        }

        /// <summary>
        /// Queues a response for the next Prompt() call.
        /// </summary>
        /// <param name="response">The response to return</param>
        public void QueueSelectionResponse(string response)
        {
            _selectionResponses.Enqueue(response);
        }

        /// <summary>
        /// Clears all captured output.
        /// </summary>
        public void ClearCapture()
        {
            _outputCapture.Clear();
        }

        /// <summary>
        /// Checks if the captured output contains the specified text.
        /// </summary>
        /// <param name="text">Text to search for</param>
        /// <returns>True if the text was found in any output</returns>
        public bool ContainsOutput(string text)
        {
            return _outputCapture.Any(output => output.Contains(text, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Gets all output lines that match the specified type.
        /// </summary>
        /// <param name="type">Type to filter by (e.g., "MARKUP", "TEXT", "TABLE")</param>
        /// <returns>Filtered output lines</returns>
        public IEnumerable<string> GetOutputByType(string type)
        {
            return _outputCapture.Where(output => output.StartsWith($"[{type}]"));
        }
    }
}