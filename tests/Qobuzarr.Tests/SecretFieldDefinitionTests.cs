using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Lidarr.Plugin.Qobuzarr.Indexers;
using NzbDrone.Core.Annotations;
using Xunit;

namespace Qobuzarr.Tests
{
    /// <summary>
    /// LOOP-005: a settings field that holds a secret (password / token / app secret / api key) must render as a
    /// masked <see cref="FieldType.Password"/> with <see cref="PrivacyLevel.Password"/>, never a plaintext
    /// Textbox — otherwise the value is shown in clear in the Lidarr UI and copied into UI state. This gate fails
    /// any secret-labelled field that regresses to a non-Password type.
    /// </summary>
    [Trait("Category", "Unit")]
    public class SecretFieldDefinitionTests
    {
        private static readonly string[] SecretKeywords =
            { "secret", "token", "password", "apikey", "api key", "private key" };

        [Fact]
        public void SecretLabelledIndexerSettingsFields_AreMaskedPasswordFields()
        {
            var offenders = new List<string>();

            foreach (var prop in typeof(QobuzIndexerSettings).GetProperties())
            {
                var fd = prop.GetCustomAttribute<FieldDefinitionAttribute>();
                if (fd is null)
                {
                    continue;
                }

                var haystack = ((fd.Label ?? string.Empty) + " " + prop.Name).ToLowerInvariant();
                var isSecret = SecretKeywords.Any(k => haystack.Contains(k));
                if (!isSecret)
                {
                    continue;
                }

                if (fd.Type != FieldType.Password || fd.Privacy != PrivacyLevel.Password)
                {
                    offenders.Add($"{prop.Name} (label '{fd.Label}') is Type={fd.Type}, Privacy={fd.Privacy} — expected Password/Password");
                }
            }

            offenders.Should().BeEmpty(
                "secret settings fields must be masked Password fields: " + string.Join("; ", offenders));
        }
    }
}
