using System.CommandLine;
using Microsoft.Extensions.Logging;
using QobuzCLI.Models;
using QobuzCLI.Services;
using Spectre.Console;

namespace QobuzCLI.Commands;

public class ConfigCommand
{
    private readonly IConfigService _configService;
    private readonly ILogger<ConfigCommand> _logger;

    public Command Command { get; }

    public ConfigCommand(IConfigService configService, ILogger<ConfigCommand> logger)
    {
        _configService = configService;
        _logger = logger;
        Command = CreateCommand();
    }

    private Command CreateCommand()
    {
        var configCommand = new Command("config", "Manage configuration settings");

        // qobuz config list
        var listCommand = new Command("list", "List all configuration settings");
        var categoryOption = new Option<string?>("--category", "Filter by category");
        listCommand.AddOption(categoryOption);
        listCommand.SetHandler(async (string? category) => await HandleListAsync(category), categoryOption);

        // qobuz config get <key>
        var getCommand = new Command("get", "Get a specific configuration value");
        var getKeyArg = new Argument<string>("key", "Configuration key to retrieve");
        getCommand.AddArgument(getKeyArg);
        getCommand.SetHandler(async (string key) => await HandleGetAsync(key), getKeyArg);

        // qobuz config set <key> <value>
        var setCommand = new Command("set", "Set a configuration value");
        var setKeyArg = new Argument<string>("key", "Configuration key to set");
        var setValueArg = new Argument<string>("value", "Value to set");
        setCommand.AddArgument(setKeyArg);
        setCommand.AddArgument(setValueArg);
        setCommand.SetHandler(async (string key, string value) => await HandleSetAsync(key, value), setKeyArg, setValueArg);

        // qobuz config reset <key>
        var resetCommand = new Command("reset", "Reset a configuration value to default");
        var resetKeyArg = new Argument<string>("key", "Configuration key to reset");
        resetCommand.AddArgument(resetKeyArg);
        resetCommand.SetHandler(async (string key) => await HandleResetAsync(key), resetKeyArg);

        // qobuz config path
        var pathCommand = new Command("path", "Show configuration file location");
        pathCommand.SetHandler(HandlePath);

        configCommand.AddCommand(listCommand);
        configCommand.AddCommand(getCommand);
        configCommand.AddCommand(setCommand);
        configCommand.AddCommand(resetCommand);
        configCommand.AddCommand(pathCommand);

        return configCommand;
    }

    private async Task HandleListAsync(string? category)
    {
        try
        {
            var parameters = await _configService.GetParametersAsync();
            var config = await _configService.GetAllValuesAsync();

            if (!string.IsNullOrEmpty(category))
            {
                parameters = parameters.Where(p => 
                    p.Category.Equals(category, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            if (!parameters.Any())
            {
                AnsiConsole.MarkupLine("[yellow]No configuration parameters found.[/]");
                return;
            }

            var table = new Table();
            table.AddColumn("Setting");
            table.AddColumn("Value");
            table.AddColumn("Description");

            var groupedParams = parameters.GroupBy(p => p.Category);

            foreach (var group in groupedParams)
            {
                // Add category header
                table.AddRow($"[bold blue]{group.Key}[/]", "", "");
                
                foreach (var param in group)
                {
                    var currentValue = config.TryGetValue(param.Key, out var value) ? value.ToString() : "not set";
                    var description = param.Description;
                    
                    // Mask sensitive values with stars
                    if (param.IsSensitive && currentValue != "not set" && !string.IsNullOrEmpty(currentValue))
                    {
                        currentValue = "***set***";
                    }
                    
                    if (param.AllowedValues != null && param.AllowedValues.Any())
                    {
                        description += $" (options: {string.Join(", ", param.AllowedValues)})";
                    }
                    
                    table.AddRow($"  {param.Key}", $"[green]{currentValue}[/]", description);
                }
                
                table.AddEmptyRow();
            }

            AnsiConsole.Write(table);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error listing configuration: {ex.Message}[/]");
            _logger.LogError(ex, "Failed to list configuration");
        }
    }

    private async Task HandleGetAsync(string key)
    {
        try
        {
            var parameters = await _configService.GetParametersAsync();
            var param = parameters.FirstOrDefault(p => p.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
            
            if (param == null)
            {
                AnsiConsole.MarkupLine($"[red]Unknown configuration key: {key}[/]");
                AnsiConsole.MarkupLine("[dim]Use 'qobuz config list' to see available keys.[/]");
                return;
            }

            var value = await _configService.GetValueAsync<object>(key);
            
            AnsiConsole.MarkupLine($"[blue]{key}[/]: [green]{value ?? "not set"}[/]");
            AnsiConsole.MarkupLine($"[dim]{param.Description}[/]");
            
            if (param.AllowedValues != null && param.AllowedValues.Any())
            {
                AnsiConsole.MarkupLine($"[dim]Allowed values: {string.Join(", ", param.AllowedValues)}[/]");
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error getting configuration: {ex.Message}[/]");
            _logger.LogError(ex, "Failed to get configuration value for key: {Key}", key);
        }
    }

    private async Task HandleSetAsync(string key, string value)
    {
        try
        {
            var parameters = await _configService.GetParametersAsync();
            var param = parameters.FirstOrDefault(p => p.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
            
            if (param == null)
            {
                AnsiConsole.MarkupLine($"[red]Unknown configuration key: {key}[/]");
                AnsiConsole.MarkupLine("[dim]Use 'qobuz config list' to see available keys.[/]");
                return;
            }

            // Convert value to the appropriate type
            object typedValue = param.Type.Name switch
            {
                nameof(Boolean) => bool.Parse(value),
                nameof(Int32) => int.Parse(value),
                nameof(Double) => double.Parse(value),
                _ => value
            };

            await _configService.SetValueAsync(key, typedValue);
            AnsiConsole.MarkupLine($"[green]Set {key} = {value}[/]");
        }
        catch (FormatException)
        {
            AnsiConsole.MarkupLine($"[red]Invalid value format for {key}. Expected type: {await GetParameterTypeAsync(key)}[/]");
        }
        catch (ArgumentException ex)
        {
            AnsiConsole.MarkupLine($"[red]{ex.Message}[/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error setting configuration: {ex.Message}[/]");
            _logger.LogError(ex, "Failed to set configuration value for key: {Key}", key);
        }
    }

    private async Task HandleResetAsync(string key)
    {
        try
        {
            var parameters = await _configService.GetParametersAsync();
            var param = parameters.FirstOrDefault(p => p.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
            
            if (param == null)
            {
                AnsiConsole.MarkupLine($"[red]Unknown configuration key: {key}[/]");
                return;
            }

            if (param.DefaultValue != null)
            {
                await _configService.SetValueAsync(key, param.DefaultValue);
                AnsiConsole.MarkupLine($"[green]Reset {key} to default value: {param.DefaultValue}[/]");
            }
            else
            {
                AnsiConsole.MarkupLine($"[yellow]No default value available for {key}[/]");
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error resetting configuration: {ex.Message}[/]");
            _logger.LogError(ex, "Failed to reset configuration value for key: {Key}", key);
        }
    }

    private void HandlePath()
    {
        var path = _configService.GetConfigPath();
        AnsiConsole.MarkupLine($"Configuration file: [blue]{path}[/]");
        
        if (File.Exists(path))
        {
            var info = new FileInfo(path);
            AnsiConsole.MarkupLine($"Last modified: [dim]{info.LastWriteTime:yyyy-MM-dd HH:mm:ss}[/]");
            AnsiConsole.MarkupLine($"Size: [dim]{info.Length} bytes[/]");
        }
        else
        {
            AnsiConsole.MarkupLine("[yellow]Configuration file does not exist yet.[/]");
        }
    }
    
    private async Task<string> GetParameterTypeAsync(string key)
    {
        var parameters = await _configService.GetParametersAsync();
        var param = parameters.FirstOrDefault(p => p.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        return param?.Type.Name ?? "string";
    }
}