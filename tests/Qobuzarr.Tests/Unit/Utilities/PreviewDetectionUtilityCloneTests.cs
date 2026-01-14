using System;
using System.IO;
using Xunit;

namespace Qobuzarr.Tests.Unit.Utilities
{
    public sealed class PreviewDetectionUtilityCloneTests
    {
        [Fact]
        public void Repo_Should_Not_Contain_Local_PreviewDetectionUtility_Clone()
        {
            var repoRoot = FindRepoRoot();
            var clonePath = Path.Combine(repoRoot, "src", "Utilities", "PreviewDetectionUtility.cs");
            Assert.False(File.Exists(clonePath), $"Local PreviewDetectionUtility clone must not exist: {clonePath}");
        }

        private static string FindRepoRoot()
        {
            var dir = AppContext.BaseDirectory;
            for (var i = 0; i < 12; i++)
            {
                if (File.Exists(Path.Combine(dir, "Qobuzarr.csproj"))) return dir;
                var parent = Directory.GetParent(dir)?.FullName;
                if (parent == null) break;
                dir = parent;
            }

            throw new DirectoryNotFoundException("Could not locate repo root containing Qobuzarr.csproj");
        }
    }
}

