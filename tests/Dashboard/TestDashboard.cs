using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using QobuzCLI.Services;
using QobuzCLI.Services.Logging;

namespace QobuzCLI
{
    public class TestDashboard
    {
        public static async Task Main(string[] args)
        {
            Console.WriteLine($"Testing Full Screen Dashboard");
            Console.WriteLine($"Console Size: {Console.WindowWidth} x {Console.WindowHeight}");
            Console.WriteLine("Press any key to start dashboard test...");
            Console.ReadKey(true);

            // Set up services
            var services = new ServiceCollection();
            services.AddLogging(builder =>
            {
                builder.SetMinimumLevel(LogLevel.Information);
            });
            
            // Add dashboard state provider
            services.AddSingleton<IDashboardStateProvider, DashboardStateProvider>();
            services.AddSingleton<Dashboard>();
            
            var serviceProvider = services.BuildServiceProvider();
            var dashboard = serviceProvider.GetRequiredService<Dashboard>();

            // Start dashboard with test data
            dashboard.Start("Testing Full Screen Layout", 100);

            // Simulate progress updates
            var random = new Random();
            for (int i = 0; i <= 100; i++)
            {
                dashboard.UpdateProgress(i, i - random.Next(0, 5), random.Next(0, 5), 
                    $"Processing item {i} with a longer description to test column width",
                    i > 0 ? $"Last successful: Item {i - 1}" : "");
                
                // Add some log messages
                if (i % 10 == 0)
                {
                    dashboard.AddLogMessage($"Milestone reached: {i}% complete");
                }
                
                await Task.Delay(100); // 100ms delay for visibility
            }

            // Keep dashboard visible for a moment
            await Task.Delay(2000);
            
            dashboard.StopOperation();
            
            Console.WriteLine("\nDashboard test completed!");
            Console.WriteLine("The dashboard should have:");
            Console.WriteLine("- Used the full width of your console window");
            Console.WriteLine("- Dynamically adjusted column widths");
            Console.WriteLine("- Showed a progress bar spanning the console width");
            Console.WriteLine("- Utilized available vertical space");
        }
    }
    
    // Simple implementation of IDashboardStateProvider for testing
    public class DashboardStateProvider : IDashboardStateProvider
    {
        private bool _isActive;
        
        public bool IsDashboardActive => _isActive;
        
        public event EventHandler<bool>? DashboardStateChanged;
        
        public void SetDashboardActive(bool active)
        {
            _isActive = active;
            DashboardStateChanged?.Invoke(this, active);
        }
    }
}