using Spectre.Console;
using System.Collections.Generic;

namespace QobuzCLI.Services.UI
{
    /// <summary>
    /// Abstraction over Spectre.Console for testable UI interactions.
    /// Enables unit testing of command UI logic by providing mockable interface.
    /// </summary>
    public interface IConsoleUI
    {
        /// <summary>
        /// Writes a markup line to the console.
        /// </summary>
        /// <param name="markup">The markup text to write</param>
        void MarkupLine(string markup);

        /// <summary>
        /// Writes plain text to the console.
        /// </summary>
        /// <param name="text">The text to write</param>
        void WriteLine(string text = "");

        /// <summary>
        /// Writes a table to the console.
        /// </summary>
        /// <param name="table">The table to write</param>
        void Write(Table table);

        /// <summary>
        /// Prompts the user for selection from a list of choices.
        /// </summary>
        /// <param name="prompt">The selection prompt to display</param>
        /// <returns>The selected choice</returns>
        string Prompt(SelectionPrompt<string> prompt);

        /// <summary>
        /// Prompts the user for text input.
        /// </summary>
        /// <param name="prompt">The text prompt to display</param>
        /// <returns>The user input</returns>
        string Ask(string prompt);

        /// <summary>
        /// Prompts the user for confirmation (yes/no).
        /// </summary>
        /// <param name="prompt">The confirmation prompt</param>
        /// <param name="defaultValue">Default value if user just presses enter</param>
        /// <returns>True if user confirms, false otherwise</returns>
        bool Confirm(string prompt, bool defaultValue = false);

        /// <summary>
        /// Creates a new table for display.
        /// </summary>
        /// <returns>A new table instance</returns>
        Table CreateTable();

        /// <summary>
        /// Creates a new selection prompt.
        /// </summary>
        /// <returns>A new selection prompt instance</returns>
        SelectionPrompt<string> CreateSelectionPrompt();

        /// <summary>
        /// Creates a progress display for long-running operations.
        /// </summary>
        /// <returns>A new progress instance</returns>
        Progress CreateProgress();

        /// <summary>
        /// Shows a spinner while executing an operation.
        /// </summary>
        /// <param name="message">Message to show with the spinner</param>
        /// <param name="operation">Operation to execute</param>
        /// <returns>Result of the operation</returns>
        T WithSpinner<T>(string message, Func<T> operation);

        /// <summary>
        /// Shows a spinner while executing an async operation.
        /// </summary>
        /// <param name="message">Message to show with the spinner</param>
        /// <param name="operation">Async operation to execute</param>
        /// <returns>Result of the operation</returns>
        Task<T> WithSpinnerAsync<T>(string message, Func<Task<T>> operation);

        /// <summary>
        /// Clears the console screen.
        /// </summary>
        void Clear();

        /// <summary>
        /// Gets or sets the console title.
        /// </summary>
        string Title { get; set; }
    }
}
