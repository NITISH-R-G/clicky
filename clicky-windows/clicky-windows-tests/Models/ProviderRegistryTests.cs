using System.Linq;
using clicky_windows.Models;
using Xunit;

namespace clicky_windows_tests.Models
{
    /// <summary>
    /// Tests for the provider preset registry. These guard the contract that the
    /// Settings UI and GenericAiClient rely on: the registry must always contain
    /// a "Custom Provider" fallback, presets must resolve by name, and every
    /// preset must carry a non-empty default model and endpoint.
    /// </summary>
    public class ProviderRegistryTests
    {
        [Fact]
        public void GetPresets_ReturnsNonEmptyList()
        {
            var presets = ProviderRegistry.GetPresets();

            Assert.NotEmpty(presets);
        }

        [Fact]
        public void GetPresets_AlwaysContainsCustomProviderFallback()
        {
            var presets = ProviderRegistry.GetPresets();

            // "Custom Provider" is the fallback returned by GetPreset when a name
            // does not match — it must always be present or resolution breaks.
            Assert.Contains(presets, preset => preset.Name == "Custom Provider");
        }

        [Theory]
        [InlineData("Anthropic (Claude)", "Anthropic", "ApiKey")]
        [InlineData("OpenAI", "OpenAI", "Bearer")]
        [InlineData("Ollama (Local)", "OpenAI", "None")]
        public void GetPreset_ResolvesKnownPresetsWithExpectedSchema(
            string name, string expectedFormat, string expectedAuthType)
        {
            var preset = ProviderRegistry.GetPreset(name);

            Assert.Equal(name, preset.Name);
            Assert.Equal(expectedFormat, preset.Format);
            Assert.Equal(expectedAuthType, preset.AuthType);
        }

        [Fact]
        public void GetPreset_UnknownNameFallsBackToCustomProvider()
        {
            var preset = ProviderRegistry.GetPreset("This provider does not exist");

            // The registry's contract is to never return null — it falls back to
            // the last preset (Custom Provider) for anything unrecognized.
            Assert.Equal("Custom Provider", preset.Name);
        }

        [Fact]
        public void EveryPreset_HasNonEmptyDefaultModelAndEndpoint()
        {
            // Custom Provider is intentionally empty (user fills it in); all
            // named providers must ship a usable default model + endpoint.
            var presets = ProviderRegistry.GetPresets()
                .Where(preset => preset.Name != "Custom Provider");

            Assert.All(presets, preset =>
            {
                Assert.False(string.IsNullOrWhiteSpace(preset.DefaultModel),
                    $"Preset '{preset.Name}' is missing a DefaultModel.");
                Assert.False(string.IsNullOrWhiteSpace(preset.DefaultEndpoint),
                    $"Preset '{preset.Name}' is missing a DefaultEndpoint.");
            });
        }

        [Fact]
        public void EveryPreset_FormatIsOpenAiOrAnthropic()
        {
            var presets = ProviderRegistry.GetPresets();

            Assert.All(presets, preset =>
            {
                Assert.True(
                    preset.Format == "OpenAI" || preset.Format == "Anthropic",
                    $"Preset '{preset.Name}' has unsupported Format '{preset.Format}'.");
            });
        }
    }
}
