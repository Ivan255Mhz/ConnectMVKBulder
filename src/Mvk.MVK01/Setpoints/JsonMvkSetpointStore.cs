using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Mvk.MVK01.Setpoints
{
    /// <summary>
    /// JSON-хранилище уставок. Поддерживает объект с полем <c>setpoints</c>
    /// и просто массив уставок.
    /// </summary>
    public sealed class JsonMvkSetpointStore : IMvkSetpointStore
    {
        private static readonly JsonSerializerOptions SerializerOptions = CreateOptions();

        private readonly string path;

        /// <summary>
        /// Создаёт хранилище.
        /// </summary>
        /// <param name="path">Путь к JSON-файлу.</param>
        public JsonMvkSetpointStore(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Не задан путь к файлу уставок.", nameof(path));
            }

            this.path = System.IO.Path.GetFullPath(path);
        }

        /// <summary>
        /// Путь к файлу.
        /// </summary>
        public string Path => path;

        /// <inheritdoc/>
        public async ValueTask<IReadOnlyList<MvkSetpoint>> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"Файл уставок не найден: '{path}'.", path);
            }

            string json = await File
                .ReadAllTextAsync(path, cancellationToken)
                .ConfigureAwait(false);

            return Parse(json);
        }

        /// <inheritdoc/>
        public async ValueTask SaveAsync(
            IReadOnlyCollection<MvkSetpoint> setpoints,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(setpoints);
            MvkSetpointValidator.ThrowIfInvalid(setpoints);

            SetpointFile file = new() { Setpoints = [.. setpoints] };
            string json = JsonSerializer.Serialize(file, SerializerOptions);

            string? directory = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string tempPath = path + ".tmp";

            try
            {
                await File
                    .WriteAllTextAsync(tempPath, json, cancellationToken)
                    .ConfigureAwait(false);

                File.Move(tempPath, path, overwrite: true);
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }

        private static IReadOnlyList<MvkSetpoint> Parse(string json)
        {
            using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });

            JsonElement root = document.RootElement;
            JsonElement array;

            if (root.ValueKind == JsonValueKind.Array)
            {
                array = root;
            }
            else if (root.ValueKind == JsonValueKind.Object
                && TryGetPropertyIgnoreCase(root, "setpoints", out JsonElement setpoints)
                && setpoints.ValueKind == JsonValueKind.Array)
            {
                array = setpoints;
            }
            else
            {
                throw new MvkSetpointValidationException(
                [
                    "Ожидается JSON-массив уставок или объект с полем 'setpoints'."
                ]);
            }

            List<MvkSetpoint>? items = array.Deserialize<List<MvkSetpoint>>(SerializerOptions);
            if (items is null)
            {
                throw new MvkSetpointValidationException(["Не удалось прочитать массив уставок."]);
            }

            MvkSetpointValidator.ThrowIfInvalid(items);
            return items;
        }

        private static bool TryGetPropertyIgnoreCase(
            JsonElement element,
            string name,
            out JsonElement value)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }

            value = default;
            return false;
        }

        private static JsonSerializerOptions CreateOptions()
        {
            JsonSerializerOptions options = new()
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };

            options.Converters.Add(new JsonStringEnumConverter());
            return options;
        }

        private sealed class SetpointFile
        {
            [JsonPropertyName("setpoints")]
            public List<MvkSetpoint> Setpoints { get; set; } = [];
        }
    }
}
