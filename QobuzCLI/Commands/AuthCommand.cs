using System.CommandLine;
using Microsoft.Extensions.Logging;
using QobuzCLI.Services;
using Spectre.Console;

namespace QobuzCLI.Commands;

public class AuthCommand
{
    private readonly IConfigService _configService;
    private readonly IPluginHost _pluginHost;
    private readonly ILogger<AuthCommand> _logger;

    public Command Command { get; }

    public AuthCommand(IConfigService configService, IPluginHost pluginHost, ILogger<AuthCommand> logger)
    {
        _configService = configService;
        _pluginHost = pluginHost;
        _logger = logger;
        Command = CreateCommand();
    }

    private Command CreateCommand()
    {
        var authCommand = new Command("auth", "Manage authentication credentials");

        // qobuz auth login
        var loginCommand = new Command("login", "Login to Qobuz account");
        var emailOption = new Option<string?>("--email", "Email address for login");
        var passwordOption = new Option<string?>("--password", "Password for login");
        var userIdOption = new Option<string?>("--user-id", "User ID for token authentication");
        var tokenOption = new Option<string?>("--token", "Authentication token");

        loginCommand.AddOption(emailOption);
        loginCommand.AddOption(passwordOption);
        loginCommand.AddOption(userIdOption);
        loginCommand.AddOption(tokenOption);

        loginCommand.SetHandler(async (string? email, string? password, string? userId, string? token) =>
            await HandleLoginAsync(email, password, userId, token).ConfigureAwait(false),
            emailOption, passwordOption, userIdOption, tokenOption);

        // qobuz auth status
        var statusCommand = new Command("status", "Check authentication status");
        statusCommand.SetHandler(async () => await HandleStatusAsync().ConfigureAwait(false));

        // qobuz auth logout
        var logoutCommand = new Command("logout", "Clear stored credentials");
        logoutCommand.SetHandler(async () => await HandleLogoutAsync().ConfigureAwait(false));

        authCommand.AddCommand(loginCommand);
        authCommand.AddCommand(statusCommand);
        authCommand.AddCommand(logoutCommand);

        return authCommand;
    }

    private async Task HandleLoginAsync(string? email, string? password, string? userId, string? token)
    {
        try
        {
            bool useTokenAuth = !string.IsNullOrEmpty(userId) || !string.IsNullOrEmpty(token);
            bool useEmailAuth = !string.IsNullOrEmpty(email) || !string.IsNullOrEmpty(password);

            if (useTokenAuth && useEmailAuth)
            {
                AnsiConsole.MarkupLine("[red]Cannot use both email and token authentication simultaneously.[/]");
                return;
            }

            if (!useTokenAuth && !useEmailAuth)
            {
                // Interactive login
                await HandleInteractiveLoginAsync().ConfigureAwait(false);
                return;
            }

            if (useEmailAuth)
            {
                await HandleEmailLoginAsync(email, password).ConfigureAwait(false);
            }
            else if (useTokenAuth)
            {
                await HandleTokenLoginAsync(userId, token).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Login failed: {ex.Message}[/]");
            _logger.LogError(ex, "Authentication failed");
        }
    }

    private async Task HandleInteractiveLoginAsync()
    {
        AnsiConsole.MarkupLine("[blue]Qobuz Authentication Setup[/]");
        AnsiConsole.WriteLine();

        var authMethod = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Choose authentication method:")
                .AddChoices("Email/Password", "User ID/Token"));

        if (authMethod == "Email/Password")
        {
            var email = AnsiConsole.Ask<string>("Email address:");
            var password = AnsiConsole.Prompt(
                new TextPrompt<string>("Password:")
                    .Secret());

            await HandleEmailLoginAsync(email, password).ConfigureAwait(false);
        }
        else
        {
            var userId = AnsiConsole.Ask<string>("User ID:");
            var token = AnsiConsole.Prompt(
                new TextPrompt<string>("Authentication Token:")
                    .Secret());

            await HandleTokenLoginAsync(userId, token).ConfigureAwait(false);
        }
    }

    private async Task HandleEmailLoginAsync(string? email, string? password)
    {
        if (string.IsNullOrEmpty(email))
            email = AnsiConsole.Ask<string>("Email address:");

        if (string.IsNullOrEmpty(password))
            password = AnsiConsole.Prompt(new TextPrompt<string>("Password:").Secret());

        // Save credentials
        await _configService.SetValueAsync("auth-method", "email").ConfigureAwait(false);
        await _configService.SetValueAsync("email", email).ConfigureAwait(false);
        await _configService.SetValueAsync("password", password).ConfigureAwait(false);

        // Clear token auth fields
        await _configService.SetValueAsync("user-id", (string?)null).ConfigureAwait(false);
        await _configService.SetValueAsync("auth-token", (string?)null).ConfigureAwait(false);

        // Test authentication
        await TestAndReportAuthAsync().ConfigureAwait(false);
    }

    private async Task HandleTokenLoginAsync(string? userId, string? token)
    {
        if (string.IsNullOrEmpty(userId))
            userId = AnsiConsole.Ask<string>("User ID:");

        if (string.IsNullOrEmpty(token))
            token = AnsiConsole.Prompt(new TextPrompt<string>("Authentication Token:").Secret());

        // Save credentials
        await _configService.SetValueAsync("auth-method", "token").ConfigureAwait(false);
        await _configService.SetValueAsync("user-id", userId).ConfigureAwait(false);
        await _configService.SetValueAsync("auth-token", token).ConfigureAwait(false);

        // Clear email auth fields
        await _configService.SetValueAsync("email", (string?)null).ConfigureAwait(false);
        await _configService.SetValueAsync("password", (string?)null).ConfigureAwait(false);

        // Test authentication
        await TestAndReportAuthAsync().ConfigureAwait(false);
    }

    private async Task TestAndReportAuthAsync()
    {
        AnsiConsole.WriteLine();
        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .StartAsync("Testing authentication...", async ctx =>
            {
                var config = await _configService.LoadConfigAsync().ConfigureAwait(false);
                await _pluginHost.InitializeAsync(config).ConfigureAwait(false);

                var success = await _pluginHost.TestAuthenticationAsync().ConfigureAwait(false);

                if (success)
                {
                    AnsiConsole.MarkupLine("[green]✓ Authentication successful![/]");
                }
                else
                {
                    AnsiConsole.MarkupLine("[red]✗ Authentication failed. Please check your credentials.[/]");
                }
            });
    }

    private async Task HandleStatusAsync()
    {
        try
        {
            var config = await _configService.LoadConfigAsync().ConfigureAwait(false);

            if (!config.HasValidAuth())
            {
                AnsiConsole.MarkupLine("[yellow]⚠ No authentication configured[/]");
                AnsiConsole.MarkupLine("[dim]Use 'qobuz auth login' to set up authentication.[/]");
                return;
            }

            var table = new Table();
            table.AddColumn("Property");
            table.AddColumn("Value");

            table.AddRow("Authentication Method", config.AuthMethod);

            if (config.IsEmailAuth())
            {
                table.AddRow("Email", config.Email ?? "not set");
                table.AddRow("Password", config.Password != null ? "***set***" : "not set");
            }
            else if (config.IsTokenAuth())
            {
                table.AddRow("User ID", config.UserId ?? "not set");
                table.AddRow("Auth Token", config.AuthToken != null ? "***set***" : "not set");
            }

            AnsiConsole.Write(table);

            // Test current authentication
            if (_pluginHost.IsInitialized || config.HasValidAuth())
            {
                AnsiConsole.WriteLine();
                await AnsiConsole.Status()
                    .Spinner(Spinner.Known.Dots)
                    .StartAsync("Testing current session...", async ctx =>
                    {
                        try
                        {
                            if (!_pluginHost.IsInitialized)
                                await _pluginHost.InitializeAsync(config).ConfigureAwait(false);

                            var success = await _pluginHost.TestAuthenticationAsync().ConfigureAwait(false);

                            if (success)
                            {
                                AnsiConsole.MarkupLine("[green]✓ Session is valid[/]");
                            }
                            else
                            {
                                AnsiConsole.MarkupLine("[red]✗ Session is invalid or expired[/]");
                            }
                        }
                        catch (Exception ex)
                        {
                            AnsiConsole.MarkupLine($"[red]✗ Authentication test failed: {ex.Message}[/]");
                        }
                    });
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error checking authentication status: {ex.Message}[/]");
            _logger.LogError(ex, "Failed to check authentication status");
        }
    }

    private async Task HandleLogoutAsync()
    {
        try
        {
            var confirm = AnsiConsole.Confirm("Are you sure you want to clear all stored credentials?");

            if (!confirm)
            {
                AnsiConsole.MarkupLine("[yellow]Operation cancelled.[/]");
                return;
            }

            // Clear all authentication fields
            await _configService.SetValueAsync("email", (string?)null).ConfigureAwait(false);
            await _configService.SetValueAsync("password", (string?)null).ConfigureAwait(false);
            await _configService.SetValueAsync("user-id", (string?)null).ConfigureAwait(false);
            await _configService.SetValueAsync("auth-token", (string?)null).ConfigureAwait(false);
            await _configService.SetValueAsync("auth-method", "email").ConfigureAwait(false);

            AnsiConsole.MarkupLine("[green]✓ All credentials cleared.[/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error during logout: {ex.Message}[/]");
            _logger.LogError(ex, "Failed to clear credentials");
        }
    }
}
