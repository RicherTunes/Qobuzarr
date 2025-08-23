using System.Threading.Tasks;

namespace QobuzCLI.Commands.Handlers
{
    /// <summary>
    /// Base interface for Lidarr command handlers.
    /// Part of the command handler pattern to separate responsibilities.
    /// </summary>
    public interface ILidarrCommandHandler
    {
        /// <summary>
        /// Executes the command handler.
        /// </summary>
        Task ExecuteAsync();
    }
}