using Spectre.Console;
using System;
using System.Threading.Tasks;

namespace QobuzCLI.Services.UI
{
    /// <summary>
    /// Production implementation of IConsoleUI using Spectre.Console.
    /// Provides the actual console interaction functionality.
    /// </summary>
    public class SpectreConsoleUI : IConsoleUI
    {
        public void MarkupLine(string markup)
        {
            AnsiConsole.MarkupLine(markup);
        }

        public void WriteLine(string text = "")
        {
            AnsiConsole.WriteLine(text);
        }

        public void Write(Table table)
        {
            AnsiConsole.Write(table);
        }

        public string Prompt(SelectionPrompt<string> prompt)
        {
            return AnsiConsole.Prompt(prompt);
        }

        public string Ask(string prompt)
        {
            return AnsiConsole.Ask<string>(prompt);
        }

        public bool Confirm(string prompt, bool defaultValue = false)
        {
            return AnsiConsole.Confirm(prompt, defaultValue);
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
            return AnsiConsole.Progress();
        }

        public T WithSpinner<T>(string message, Func<T> operation)
        {
            return AnsiConsole.Status()
                .Spinner(Spinner.Known.Arc)
                .Start(message, ctx => operation());
        }

        public async Task<T> WithSpinnerAsync<T>(string message, Func<Task<T>> operation)
        {
            return await AnsiConsole.Status()
                .Spinner(Spinner.Known.Arc)
                .StartAsync(message, async ctx => await operation().ConfigureAwait(false))
                .ConfigureAwait(false);
        }

        public void Clear()
        {
            AnsiConsole.Clear();
        }

        public string Title
        {
            get => Console.Title;
            set => Console.Title = value;
        }
    }
}
